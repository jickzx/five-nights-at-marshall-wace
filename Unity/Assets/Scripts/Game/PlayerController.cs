using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Game
{
    /// <summary>
    /// First-person survivor. WASD + mouse, Shift to run, E to use, F for the (weak) flashlight.
    /// Head bob and footstep events follow the stride; wading in the blood slows you down.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public float WalkSpeed = 3.4f, RunSpeed = 5.6f, LookSensitivity = 0.12f;
        public const float EyeHeight = 1.6f;

        CharacterController _cc;
        Transform _cam;
        Camera _camera;
        Light _flashlight;
        public GameObject Body { get; private set; }
        float _pitch, _vy, _bobPhase, _bobAmp, _lastStepSign, _flashTimer;
        Interactable _focus;
        readonly Queue<Vector3> _autopilot = new Queue<Vector3>();
        bool _autoRun;

        public Camera Camera => _camera;
        public float Speed { get; private set; }
        public bool Wading { get; private set; }
        public float Pitch => _pitch;
        /// <summary>Which way the player looks, in degrees.</summary>
        public float ViewYaw => transform.eulerAngles.y;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f; _cc.radius = 0.35f; _cc.center = new Vector3(0, 0.9f, 0);
            _cc.stepOffset = 0.45f; _cc.slopeLimit = 50f;
            Body = ModelLoader.Load("player", transform);
            Body.name = "PlayerBody";
            foreach (var r in Body.GetComponentsInChildren<Renderer>()) r.enabled = false; // first person
            foreach (var c in Body.GetComponentsInChildren<Collider>()) Destroy(c);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            _camera = cam.AddComponent<Camera>();
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 1600f;
            _camera.fieldOfView = 68f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = RenderSettings.fogColor;
            cam.AddComponent<AudioListener>();
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0, EyeHeight, 0);
            _cam = cam.transform;

            var fl = new GameObject("Flashlight");
            fl.transform.SetParent(_cam, false);
            fl.transform.localPosition = new Vector3(0.18f, -0.15f, 0.1f);
            _flashlight = fl.AddComponent<Light>();
            _flashlight.type = LightType.Spot;
            _flashlight.color = new Color(1f, 0.9f, 0.72f);
            _flashlight.range = 17f;
            _flashlight.spotAngle = 52f;
            _flashlight.innerSpotAngle = 18f;
            _flashlight.intensity = 9f;
            _flashlight.shadows = LightShadows.None;

            Services.Player = transform;
        }

        void Start()
        {
            var spawn = GameObject.FindWithTag("Respawn");
            if (spawn) { Warp(spawn.transform.position + Vector3.up * 0.05f); transform.rotation = Quaternion.Euler(0, spawn.transform.eulerAngles.y, 0); }
        }

        bool CanMove
        {
            get { var p = Services.State.Phase; return p == GamePhase.Explore || p == GamePhase.Chase; }
        }

        /// <summary>Walk (or run) through these points as if a player were steering. Used for demo recordings.</summary>
        public void WalkTo(bool run, params Vector3[] points)
        {
            _autopilot.Clear();
            foreach (var p in points) _autopilot.Enqueue(p);
            _autoRun = run;
        }

        public bool AutopilotBusy => _autopilot.Count > 0;

        void Update()
        {
            var phase = Services.State.Phase;
            if (phase == GamePhase.Dead || phase == GamePhase.Escaped) { Services.UI.ShowPrompt(null); return; } // a cutscene owns the camera
            bool move = CanMove;
            bool menu = phase == GamePhase.Title || phase == GamePhase.Puzzle || phase == GamePhase.Reveal || phase == GamePhase.Quiz;
            Cursor.lockState = move ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = menu;
            var kb = Keyboard.current; var mouse = Mouse.current;

            Vector3 dir = Vector3.zero;
            bool run = false;
            if (move && kb != null)
            {
                if (kb.wKey.isPressed) dir += Vector3.forward;
                if (kb.sKey.isPressed) dir += Vector3.back;
                if (kb.aKey.isPressed) dir += Vector3.left;
                if (kb.dKey.isPressed) dir += Vector3.right;
                run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (kb.fKey.wasPressedThisFrame) _flashlight.enabled = !_flashlight.enabled;
            }
            if (move && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue() * LookSensitivity;
                transform.Rotate(0, d.x, 0);
                _pitch = Mathf.Clamp(_pitch - d.y, -80, 80);
            }
            Vector3 worldDir = transform.TransformDirection(dir.normalized);
            if (move && dir == Vector3.zero && _autopilot.Count > 0)
            {
                var target = _autopilot.Peek();
                var to = target - transform.position; to.y = 0;
                if (to.magnitude < 0.6f) _autopilot.Dequeue();
                else
                {
                    var want = Quaternion.LookRotation(to);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 140f * Time.deltaTime);
                    worldDir = transform.forward;
                    run = _autoRun;
                    _pitch = Mathf.MoveTowards(_pitch, 4f, 20f * Time.deltaTime);
                }
            }

            // Wading: the deeper the blood, the slower you go.
            float depth = Services.State.SeaLevel - transform.position.y;
            Wading = depth > 0.04f;
            float speed = run ? RunSpeed : WalkSpeed;
            if (Wading) speed *= Mathf.Lerp(0.85f, 0.5f, Mathf.Clamp01(depth / 0.7f));
            _vy = _cc.isGrounded ? -2f : _vy - 9.81f * Time.deltaTime;
            Vector3 vel = worldDir * speed;
            _cc.Move((vel + Vector3.up * _vy) * Time.deltaTime);
            var flat = _cc.velocity; flat.y = 0;
            Speed = move ? flat.magnitude : 0f;

            HeadBob(run);
            FlashlightFlicker();

            _focus = null;
            if (move)
            {
                float best = float.MaxValue;
                foreach (var it in Interactable.All)
                {
                    if (!it || !it.Enabled) continue;
                    var a = it.transform.position; a.y = 0; var b = transform.position; b.y = 0;
                    float dd = Vector3.Distance(a, b);
                    if (dd < it.Range && dd < best) { best = dd; _focus = it; }
                }
            }
            Services.UI.ShowPrompt(_focus ? _focus.Prompt : null);
            if (_focus && kb != null && kb.eKey.wasPressedThisFrame) Use(_focus);
            if (phase == GamePhase.Puzzle && kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame))
                GameDirector.Instance?.AbandonPuzzle();
        }

        public void Use(Interactable it)
        {
            if (!it || !it.Enabled) return;
            if (it.Type == Interactable.Kind.Ship) GameDirector.Instance?.BoardShip();
            else it.Interact();
        }

        void HeadBob(bool run)
        {
            float target = Speed > 0.2f ? Mathf.Clamp01(Speed / WalkSpeed) : 0f;
            _bobAmp = Mathf.MoveTowards(_bobAmp, target, Time.deltaTime * 4f);
            _bobPhase += Time.deltaTime * Speed * (run ? 1.55f : 1.85f);
            float s = Mathf.Sin(_bobPhase * 2f);
            float y = s * 0.05f * _bobAmp * (Wading ? 0.6f : 1f);
            float x = Mathf.Cos(_bobPhase) * 0.035f * _bobAmp;
            float breathe = Mathf.Sin(Time.time * 1.6f) * 0.008f;
            _cam.localPosition = new Vector3(x, EyeHeight + y + breathe, 0);
            _cam.localRotation = Quaternion.Euler(_pitch, 0, -x * 25f);
            // A footfall at the bottom of each bob.
            float sign = Mathf.Sign(s);
            if (_bobAmp > 0.3f && sign < 0 && _lastStepSign > 0)
            {
                GameEvents.RaiseFootstep(new FootstepEvent
                {
                    Surface = Wading ? "blood" : "shore", Speed = Speed, Position = transform.position,
                });
            }
            _lastStepSign = sign;
        }

        void FlashlightFlicker()
        {
            if (!_flashlight.enabled) return;
            // A tired cell: an occasional stutter, more often when the beast is close.
            _flashTimer -= Time.deltaTime;
            float threat = Services.State.Threat;
            if (_flashTimer <= 0)
            {
                _flashTimer = Random.Range(1.5f, 7f) * (1.2f - threat);
                StartCoroutine(Stutter(Random.Range(2, 5)));
            }
        }

        System.Collections.IEnumerator Stutter(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _flashlight.intensity = Random.Range(1f, 4f);
                yield return new WaitForSeconds(Random.Range(0.03f, 0.09f));
                _flashlight.intensity = 9f;
                yield return new WaitForSeconds(Random.Range(0.03f, 0.12f));
            }
        }

        /// <summary>Teleport helper for tests, skip keys and automated play.</summary>
        public void Warp(Vector3 pos) { _cc.enabled = false; transform.position = pos; _cc.enabled = true; _vy = 0; }

        public void Face(Vector3 worldPoint)
        {
            var to = worldPoint - transform.position; to.y = 0;
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        }

        public void SetFlashlight(bool on) => _flashlight.enabled = on;

        /// <summary>Set the view direction (degrees). For scripted shots and tests.</summary>
        public void Look(float yaw, float pitch) { transform.rotation = Quaternion.Euler(0, yaw, 0); _pitch = Mathf.Clamp(pitch, -80, 80); }
    }
}

using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Game
{
    /// <summary>Placeholder first-person controller: WASD + mouse look, E to interact.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public float Speed = 4f, LookSensitivity = 0.12f;
        CharacterController _cc;
        Transform _cam;
        float _pitch, _vy;
        Interactable _focus;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f; _cc.radius = 0.35f; _cc.center = new Vector3(0, 0.9f, 0);
            var body = ModelLoader.Load("player", transform);
            foreach (var r in body.GetComponentsInChildren<Renderer>()) r.enabled = false; // first person
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cam.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;
            cam.AddComponent<AudioListener>();
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0, 1.6f, 0);
            _cam = cam.transform;
            Services.Player = transform;
        }

        bool CanMove
        {
            get { var p = Services.State.Phase; return p == GamePhase.Explore || p == GamePhase.Chase; }
        }

        void Update()
        {
            bool move = CanMove;
            Cursor.lockState = move ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !move;
            var kb = Keyboard.current; var mouse = Mouse.current;

            Vector3 dir = Vector3.zero;
            if (move && kb != null)
            {
                if (kb.wKey.isPressed) dir += Vector3.forward;
                if (kb.sKey.isPressed) dir += Vector3.back;
                if (kb.aKey.isPressed) dir += Vector3.left;
                if (kb.dKey.isPressed) dir += Vector3.right;
            }
            if (move && mouse != null)
            {
                var d = mouse.delta.ReadValue() * LookSensitivity;
                transform.Rotate(0, d.x, 0);
                _pitch = Mathf.Clamp(_pitch - d.y, -80, 80);
                _cam.localEulerAngles = new Vector3(_pitch, 0, 0);
            }
            _vy = _cc.isGrounded ? -1f : _vy - 9.81f * Time.deltaTime;
            _cc.Move((transform.TransformDirection(dir.normalized) * Speed + Vector3.up * _vy) * Time.deltaTime);

            _focus = null;
            if (move)
            {
                float best = float.MaxValue;
                foreach (var it in Interactable.All)
                {
                    if (!it.Enabled) continue;
                    float dd = Vector3.Distance(transform.position, it.transform.position);
                    if (dd < it.Range && dd < best) { best = dd; _focus = it; }
                }
            }
            Services.UI.ShowPrompt(_focus ? _focus.Prompt : null);
            if (_focus && kb != null && kb.eKey.wasPressedThisFrame) _focus.Interact();
        }

        /// <summary>Teleport helper for tests and automated play.</summary>
        public void Warp(Vector3 pos) { _cc.enabled = false; transform.position = pos; _cc.enabled = true; }
    }
}

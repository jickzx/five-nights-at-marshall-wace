using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace Fnmw.XR
{
    /// <summary>
    /// Everything the headset adds, built around the game's own player at runtime:
    /// <list type="bullet">
    /// <item>head tracking on the player's camera (under a CameraOffset that keeps the eyes at the game's eye
    /// height, standing or seated) and room-scale: walking in the room pushes the character capsule;</item>
    /// <item>Touch controllers read every frame into <see cref="XrBridge"/>, mapped onto the keys the game
    /// already understands (see <see cref="ReadControls"/>);</item>
    /// <item>snap or smooth turning (right stick, click to switch), a comfort vignette while moving;</item>
    /// <item>a laser from the right controller onto the UI glass, the trigger clicks;</item>
    /// <item>a theater: cutscenes that fly a camera (the intro, both endings) play on a big screen in a dark
    /// room instead of moving the player's head for them;</item>
    /// <item>a control card on the left wrist, and a pause while the Quest system menu has focus.</item>
    /// </list>
    /// It lives on its own root object (the escape ending switches the player off).
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class XrRig : MonoBehaviour
    {
        public const int TheaterLayer = 29;
        public const float EyeHeight = 1.6f;          // PlayerController.EyeHeight
        const string SmoothTurnPref = "fnmw.xr.smoothTurn";
        static readonly Vector3 TheaterOrigin = new Vector3(0, -2000f, 0);

        Transform _player, _origin, _offset, _eye, _left, _right, _aim;
        Camera _eyeCam, _theaterCam;
        TrackedPoseDriver _eyePose;
        CharacterController _cc;
        Transform _theaterRoot, _theaterOffset, _theaterHead;
        RenderTexture _theaterRt;
        Renderer _screen;
        LineRenderer _laser;
        Transform _dot;
        Renderer _vignette;
        Material _vignetteMat;
        Text _card;
        bool _theater, _calibrated, _recalibrate, _paused;
        int _trackedFrames;
        float _savedTimeScale = 1f, _vignetteA;

        readonly Turner _turner = new Turner();
        readonly StickRepeater _arrows = new StickRepeater();
        readonly List<InputAction> _actions = new List<InputAction>();
        readonly List<XRInputSubsystem> _inputs = new List<XRInputSubsystem>();
        InputAction _move, _turn, _run, _a, _b, _x, _y, _rTrigger, _lTrigger, _menu, _lClick, _rClick;

        // ------------------------------------------------------------------ build

        public void Build(Transform player, Camera cam)
        {
            _player = player;
            _cc = player.GetComponent<CharacterController>();
            _eyeCam = cam;
            _eye = cam.transform;

            _origin = new GameObject("XrOrigin").transform;
            _origin.SetParent(player, false);
            _offset = new GameObject("CameraOffset").transform;
            _offset.SetParent(_origin, false);
            _offset.localPosition = new Vector3(0, EyeHeight, 0);   // until the first calibration
            _eye.SetParent(_offset, false);
            _eye.localPosition = Vector3.zero; _eye.localRotation = Quaternion.identity;
            _eyeCam.stereoTargetEye = StereoTargetEyeMask.Both;
            _eyeCam.cullingMask &= ~(1 << TheaterLayer);
            _eyePose = AddPose(_eye.gameObject, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

            _left = Controller("LeftController", "{LeftHand}", out _);
            _right = Controller("RightController", "{RightHand}", out _);
            _aim = new GameObject("RightAim").transform;
            _aim.SetParent(_offset, false);
            AddPose(_aim.gameObject, "<XRController>{RightHand}/pointerPosition", "<XRController>{RightHand}/pointerRotation");

            BuildLaser();
            BuildVignette();
            BuildCard();
            BuildTheater();
            BindActions();

            _turner.Smooth = PlayerPrefs.GetInt(SmoothTurnPref, 0) == 1;
            SubsystemManager.GetSubsystems(_inputs);
            foreach (var s in _inputs)
            {
                s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                s.trackingOriginUpdated += OnOriginUpdated;
            }
            XrBridge.Head = _eye;
            XrBridge.GlassLayer = 0;
            RefreshCard();
        }

        static TrackedPoseDriver AddPose(GameObject go, string position, string rotation)
        {
            var tpd = go.AddComponent<TrackedPoseDriver>();
            tpd.enabled = false;
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            tpd.ignoreTrackingState = true;
            tpd.positionInput = new InputActionProperty(new InputAction(go.name + "Pos", InputActionType.Value, position, expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction(go.name + "Rot", InputActionType.Value, rotation, expectedControlType: "Quaternion"));
            tpd.enabled = true;
            return tpd;
        }

        Transform Controller(string name, string hand, out TrackedPoseDriver pose)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_offset, false);
            pose = AddPose(root.gameObject, "<XRController>" + hand + "/devicePosition", "<XRController>" + hand + "/deviceRotation");
            // a plain dark grip, so the player can see their hands in the dark
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(body.GetComponent<Collider>());
            body.name = "Grip";
            body.transform.SetParent(root, false);
            body.transform.localPosition = new Vector3(0, -0.01f, -0.03f);
            body.transform.localScale = new Vector3(0.035f, 0.035f, 0.11f);
            var mr = body.GetComponent<MeshRenderer>();
            mr.sharedMaterial = OverlayMaterial(new Color(0.16f, 0.13f, 0.12f, 1f), UnityEngine.Rendering.CompareFunction.LessEqual, 2000);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        static Shader _overlay;
        static Material OverlayMaterial(Color c, UnityEngine.Rendering.CompareFunction ztest, int queue)
        {
            if (!_overlay) _overlay = Resources.Load<Shader>("XR/XrOverlay");
            var m = new Material(_overlay ? _overlay : Shader.Find("Sprites/Default")) { name = "xr_overlay", color = c };
            m.SetFloat("_ZTest", (float)ztest);
            m.renderQueue = queue;
            return m;
        }

        void BuildLaser()
        {
            var go = new GameObject("Laser");
            go.transform.SetParent(_aim, false);
            _laser = go.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.widthMultiplier = 0.004f;
            _laser.numCapVertices = 2;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laser.sharedMaterial = OverlayMaterial(Color.white, UnityEngine.Rendering.CompareFunction.Always, 4010);
            _laser.startColor = new Color(0.95f, 0.75f, 0.55f, 0.0f);
            _laser.endColor = new Color(0.95f, 0.75f, 0.55f, 0.75f);
            _laser.enabled = false;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "LaserDot";
            _dot = dot.transform;
            _dot.SetParent(transform, false);
            _dot.localScale = Vector3.one * 0.022f;
            var mr = dot.GetComponent<MeshRenderer>();
            mr.sharedMaterial = OverlayMaterial(new Color(1f, 0.85f, 0.65f, 0.95f), UnityEngine.Rendering.CompareFunction.Always, 4011);
            mr.sharedMaterial.mainTexture = RadialTexture(32, hard: true);
            dot.SetActive(false);
        }

        void BuildVignette()
        {
            // A head-locked ring that darkens the edge of view while the stick moves you (comfort).
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "ComfortVignette";
            go.transform.SetParent(_eye, false);
            go.transform.localPosition = new Vector3(0, 0, 0.12f);
            go.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
            _vignette = go.GetComponent<MeshRenderer>();
            _vignetteMat = OverlayMaterial(new Color(0, 0, 0, 0), UnityEngine.Rendering.CompareFunction.Always, 4005);
            _vignetteMat.mainTexture = RadialTexture(64, hard: false);
            _vignette.sharedMaterial = _vignetteMat;
            _vignette.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _vignette.enabled = false;
        }

        /// <summary>Alpha ramp: a dot (hard) or a ring that is clear in the middle and dark at the edge.</summary>
        static Texture2D RadialTexture(int n, bool hard)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = hard ? "xr_dot" : "xr_vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = hard ? Mathf.Clamp01((1f - r) * 6f) : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void BuildCard()
        {
            var go = new GameObject("ControlCard", typeof(RectTransform));
            go.transform.SetParent(_left, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0.02f);
            go.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.00045f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(400, 300);
            var bg = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.03f, 0.02f, 0.02f, 0.82f);
            Stretch(bg.rectTransform);
            _card = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            _card.transform.SetParent(go.transform, false);
            _card.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _card.fontSize = 17;
            _card.lineSpacing = 1.05f;
            _card.color = new Color(0.9f, 0.84f, 0.76f);
            _card.alignment = TextAnchor.UpperLeft;
            Stretch(_card.rectTransform);
            _card.rectTransform.offsetMin = new Vector2(12, 10); _card.rectTransform.offsetMax = new Vector2(-12, -10);
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        void RefreshCard()
        {
            if (!_card) return;
            _card.text = ControlCardText(_turner.Smooth);
        }

        /// <summary>The wrist card. Public so tests can check every action is listed.</summary>
        public static string ControlCardText(bool smooth) =>
            "CONTROLS\n" +
            "Walk: left stick     Run: hold left grip\n" +
            "Turn: right stick (" + (smooth ? "smooth" : "snap") + ")  click R-stick: switch\n" +
            "Use / E: A or right trigger    Light / F: B\n" +
            "Screens: aim + right trigger, or stick + A\n" +
            "Place tile / Space: A     Send / Enter: X\n" +
            "Undo / Bksp: B     Clear / Del: Y\n" +
            "Title: A starts, Y replays tutorial (T)\n" +
            "Leave console, skip intro / Esc: menu\n" +
            "Skip tutorial / hold Q: hold left stick";

        void BuildTheater()
        {
            _theaterRoot = new GameObject("XrTheater").transform;
            _theaterRoot.SetParent(transform, false);
            _theaterRoot.position = TheaterOrigin;
            _theaterOffset = new GameObject("CameraOffset").transform;
            _theaterOffset.SetParent(_theaterRoot, false);
            _theaterOffset.localPosition = new Vector3(0, EyeHeight, 0);
            var head = new GameObject("TheaterCamera");
            head.layer = TheaterLayer;
            _theaterHead = head.transform;
            _theaterHead.SetParent(_theaterOffset, false);
            _theaterCam = head.AddComponent<Camera>();
            _theaterCam.cullingMask = 1 << TheaterLayer;
            _theaterCam.clearFlags = CameraClearFlags.SolidColor;
            _theaterCam.backgroundColor = Color.black;
            _theaterCam.nearClipPlane = 0.05f; _theaterCam.farClipPlane = 40f;
            _theaterCam.depth = 100f;
            _theaterCam.stereoTargetEye = StereoTargetEyeMask.Both;
            AddPose(head, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");
            head.SetActive(false);

            _theaterRt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "xr_theater", antiAliasing = 2 };
            _theaterRt.Create();
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(screen.GetComponent<Collider>());
            screen.name = "TheaterScreen";
            screen.layer = TheaterLayer;
            screen.transform.SetParent(_theaterRoot, false);
            screen.transform.localPosition = new Vector3(0, EyeHeight, 3.4f);
            screen.transform.localScale = new Vector3(4.8f, 2.7f, 1f);
            _screen = screen.GetComponent<MeshRenderer>();
            var m = OverlayMaterial(Color.white, UnityEngine.Rendering.CompareFunction.LessEqual, 2000);
            m.mainTexture = _theaterRt;
            _screen.sharedMaterial = m;
            screen.SetActive(false);
        }

        void BindActions()
        {
            _move = Act("Move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
            _turn = Act("Turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");
            _run = Act("Run", InputActionType.Button, "<XRController>{LeftHand}/{GripButton}");
            _a = Act("A", InputActionType.Button, "<XRController>{RightHand}/{PrimaryButton}");
            _b = Act("B", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}");
            _x = Act("X", InputActionType.Button, "<XRController>{LeftHand}/{PrimaryButton}");
            _y = Act("Y", InputActionType.Button, "<XRController>{LeftHand}/{SecondaryButton}");
            _rTrigger = Act("RTrigger", InputActionType.Button, "<XRController>{RightHand}/{TriggerButton}");
            _lTrigger = Act("LTrigger", InputActionType.Button, "<XRController>{LeftHand}/{TriggerButton}");
            _menu = Act("Menu", InputActionType.Button, "<XRController>{LeftHand}/{MenuButton}");
            _lClick = Act("LStickClick", InputActionType.Button, "<XRController>{LeftHand}/{Primary2DAxisClick}");
            _rClick = Act("RStickClick", InputActionType.Button, "<XRController>{RightHand}/{Primary2DAxisClick}");
        }

        InputAction Act(string name, InputActionType type, string binding)
        {
            var a = new InputAction(name, type, binding);
            a.Enable();
            _actions.Add(a);
            return a;
        }

        void OnOriginUpdated(XRInputSubsystem s) => _recalibrate = true;

        void OnDestroy()
        {
            foreach (var a in _actions) { a.Disable(); a.Dispose(); }
            _actions.Clear();
            foreach (var s in _inputs) if (s != null) s.trackingOriginUpdated -= OnOriginUpdated;
            if (_theaterRt) { if (_eyeCam && _eyeCam.targetTexture == _theaterRt) _eyeCam.targetTexture = null; _theaterRt.Release(); Destroy(_theaterRt); }
            if (_paused) { Time.timeScale = _savedTimeScale; AudioListener.pause = false; }
            XrBridge.ClearFrame();
        }

        // ------------------------------------------------------------------ frame

        static bool Roaming(GamePhase p) => p == GamePhase.Explore || p == GamePhase.Chase;

        void Update()
        {
            if (!XrBridge.Active) return;
            float dt = Time.unscaledDeltaTime;
            var phase = Services.State.Phase;
            ReadControls(dt);
            if (_paused) { XrBridge.ClearFrame(); return; }

            Calibrate();
            if (_eye && !_theater && Roaming(phase) && _player && _player.gameObject.activeInHierarchy)
            {
                FollowHead();
                float yaw = _turner.Tick(_turn.ReadValue<Vector2>().x, Time.deltaTime);
                if (yaw != 0f) _player.Rotate(0f, yaw, 0f, Space.World);
                bool moving = XrBridge.Move.sqrMagnitude > 0.04f || (_turner.Smooth && Mathf.Abs(_turn.ReadValue<Vector2>().x) > 0.15f);
                _vignetteA = Mathf.MoveTowards(_vignetteA, moving ? 0.85f : 0f, dt * 4f);
            }
            else _vignetteA = Mathf.MoveTowards(_vignetteA, 0f, dt * 4f);
            if (_vignette)
            {
                _vignette.enabled = _vignetteA > 0.01f && !_theater;
                _vignetteMat.color = new Color(0, 0, 0, _vignetteA);
            }
            if (_rClick.WasPressedThisFrame())
            {
                _turner.Smooth = !_turner.Smooth;
                PlayerPrefs.SetInt(SmoothTurnPref, _turner.Smooth ? 1 : 0);
                RefreshCard();
            }
            Laser(phase);
        }

        /// <summary>
        /// Touch controllers onto the game's keys. Every field is written every frame; each reader only looks
        /// at the ones its phase uses (the roaming player vs. the screens), as with the keyboard.
        /// </summary>
        void ReadControls(float dt)
        {
            XrBridge.ClearFrame();
            var move = _move.ReadValue<Vector2>();
            var turn = _turn.ReadValue<Vector2>();
            bool a = _a.WasPressedThisFrame(), b = _b.WasPressedThisFrame(), x = _x.WasPressedThisFrame(), y = _y.WasPressedThisFrame();
            bool trig = _rTrigger.WasPressedThisFrame(), menu = _menu.WasPressedThisFrame();

            // roaming
            XrBridge.Move = move.sqrMagnitude < 0.02f ? Vector2.zero : move;
            XrBridge.Run = _run.IsPressed();
            XrBridge.Interact = a || trig;
            XrBridge.Flashlight = b;
            XrBridge.Leave = menu;
            XrBridge.SkipHeld = _lClick.IsPressed();

            // screens
            XrBridge.Space = a;
            XrBridge.Enter = x || _lTrigger.WasPressedThisFrame();
            XrBridge.Back = b;
            XrBridge.Clear = y;
            XrBridge.Replay = y;
            // either stick steps the screen cursor (tiles, quiz answers, worked-example pages)
            var stick = turn.sqrMagnitude >= move.sqrMagnitude ? turn : move;
            _arrows.Tick(stick, dt);
            XrBridge.Left = _arrows.Left; XrBridge.Right = _arrows.Right; XrBridge.Up = _arrows.Up; XrBridge.Down = _arrows.Down;
            XrBridge.Click = trig;
        }

        void Calibrate()
        {
            if (!_eye || _theater) return;
            float y = _eye.localPosition.y;
            if (_eye.localPosition == Vector3.zero) return;      // no pose yet
            if (++_trackedFrames < 10 && !_recalibrate) return;
            if (_calibrated && !_recalibrate) return;
            // Put the eyes at the game's eye height whatever the player's real height or posture,
            // so standing and seated play see the same game.
            float off = EyeHeight - y;
            _offset.localPosition = new Vector3(_offset.localPosition.x, off, _offset.localPosition.z);
            _theaterOffset.localPosition = new Vector3(0, off, 0);
            _calibrated = true; _recalibrate = false;
        }

        /// <summary>Room-scale: when the head walks away from the capsule, move the capsule under it (it still collides).</summary>
        void FollowHead()
        {
            if (!_cc || !_cc.enabled) return;
            Vector3 local = _origin.InverseTransformPoint(_eye.position);
            var flat = new Vector3(local.x, 0, local.z);
            if (flat.sqrMagnitude < 0.0004f) return;
            Vector3 before = _player.position;
            _cc.Move(_origin.TransformVector(flat));
            Vector3 movedLocal = _origin.InverseTransformVector(_player.position - before);
            movedLocal.y = 0;
            _offset.localPosition -= movedLocal;
        }

        void Laser(GamePhase phase)
        {
            bool screens = !_theater && !Roaming(phase) && !Services.State.IsOver && _aim;
            bool hit = false;
            Vector3 point = default;
            if (screens && XrBridge.Glass && GlassHit.Raycast(XrBridge.Glass, _aim.position, _aim.forward, out var uv, out point))
            {
                hit = true;
                XrBridge.HasPointer = true;
                XrBridge.PointerUv = uv;
            }
            if (_laser)
            {
                _laser.enabled = screens;
                if (screens)
                {
                    _laser.SetPosition(0, _aim.position);
                    _laser.SetPosition(1, hit ? point : _aim.position + _aim.forward * 3f);
                }
            }
            if (_dot)
            {
                _dot.gameObject.SetActive(hit);
                if (hit) _dot.SetPositionAndRotation(point - XrBridge.Glass.forward * 0.005f, XrBridge.Glass.rotation);
            }
        }

        void LateUpdate()
        {
            if (!XrBridge.Active) return;
            var s = Services.State;
            bool want = XrBridge.CutscenePlaying || s.IsOver;
            if (want != _theater) SetTheater(want);
            if (_theater && _eyeCam)
            {
                // The cutscene may have replaced, re-parented or re-targeted the camera; keep it on the screen.
                if (_eyeCam.targetTexture != _theaterRt) _eyeCam.targetTexture = _theaterRt;
                _eyeCam.stereoTargetEye = StereoTargetEyeMask.None;
                _eyeCam.cullingMask &= ~(1 << TheaterLayer);
            }
        }

        void SetTheater(bool on)
        {
            _theater = on;
            if (_eyePose) _eyePose.enabled = !on;          // the cutscene drives the camera, not the head
            if (_eyeCam)
            {
                _eyeCam.targetTexture = on ? _theaterRt : null;
                _eyeCam.stereoTargetEye = on ? StereoTargetEyeMask.None : StereoTargetEyeMask.Both;
                if (on) _eyeCam.aspect = 16f / 9f; else _eyeCam.ResetAspect();
            }
            _theaterCam.gameObject.SetActive(on);
            _screen.gameObject.SetActive(on);
            if (_left) _left.gameObject.SetActive(!on);
            if (_right) _right.gameObject.SetActive(!on);
            XrBridge.Head = on ? _theaterHead : _eye;
            XrBridge.GlassLayer = on ? TheaterLayer : 0;
            if (!on && _eye && _offset && _eye.parent != _offset)
            {
                _eye.SetParent(_offset, false);
                _eye.localPosition = Vector3.zero; _eye.localRotation = Quaternion.identity;
            }
        }

        // ------------------------------------------------------------------ focus

        // Meta requires the app to pause while the system menu (or the guardian) takes focus.
        void OnApplicationFocus(bool focus) => SetPaused(!focus);
        void OnApplicationPause(bool pause) => SetPaused(pause);

        void SetPaused(bool pause)
        {
            if (!XrBridge.Active || pause == _paused) return;
            _paused = pause;
            if (pause) { _savedTimeScale = Time.timeScale; Time.timeScale = 0f; AudioListener.pause = true; }
            else { Time.timeScale = _savedTimeScale; AudioListener.pause = false; }
        }
    }
}

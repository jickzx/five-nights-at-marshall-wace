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
    /// already understands (the layout lives in <see cref="TouchMapper"/>);</item>
    /// <item>Touch-like controller bodies whose buttons light and move when pressed, with a label beside each
    /// button saying what it does right now, and a wrist card for the current context (<see cref="XrHelp"/>);</item>
    /// <item>a dim chevron on the ground towards the current objective (<see cref="XrGuide"/>);</item>
    /// <item>snap or smooth turning (right stick, click to switch), a comfort vignette while moving;</item>
    /// <item>a laser from the right controller onto the UI glass, the trigger clicks;</item>
    /// <item>a theater: cutscenes that fly a camera (the intro, both endings) play on a big screen in a dark
    /// room instead of moving the player's head for them. The screen comes up straight ahead of wherever the
    /// headset faces when the cutscene starts, however the player stands or has turned in their room
    /// (<see cref="TheaterLayout"/>). The death scare first plays in the world, in the player's own view
    /// (<see cref="XrBridge.HoldTheater"/>), then the ending moves to the theater;</item>
    /// <item>a pause while the Quest system menu has focus.</item>
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
        XrControllerModel _leftModel, _rightModel;
        XrGuide _guide;
        Vector3 _lastHeadPos = new Vector3(0, EyeHeight, 0);
        Quaternion _lastHeadRot = Quaternion.identity;
        bool _realignTheater;
        int _helpKey = -1;
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
        readonly TouchMapper _mapper = new TouchMapper();
        readonly List<InputAction> _actions = new List<InputAction>();
        readonly List<XRInputSubsystem> _inputs = new List<XRInputSubsystem>();
        InputAction _move, _turn, _run, _rGrip, _a, _b, _x, _y, _rTrigger, _lTrigger, _rTriggerAxis, _lTriggerAxis, _menu, _lClick, _rClick, _headPos, _headRot;

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
            _leftModel = new XrControllerModel(_left, left: true);
            _rightModel = new XrControllerModel(_right, left: false);
            _aim = new GameObject("RightAim").transform;
            _aim.SetParent(_offset, false);
            AddPose(_aim.gameObject, "<XRController>{RightHand}/pointerPosition", "<XRController>{RightHand}/pointerRotation");

            BuildLaser();
            BuildVignette();
            BuildCard();
            BuildTheater();
            BindActions();
            var guide = new GameObject("XrGuide");
            guide.transform.SetParent(transform, false);
            _guide = guide.AddComponent<XrGuide>();
            _guide.Build(player);

            _turner.Smooth = PlayerPrefs.GetInt(SmoothTurnPref, 0) == 1;
            SubsystemManager.GetSubsystems(_inputs);
            foreach (var s in _inputs)
            {
                s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                s.trackingOriginUpdated += OnOriginUpdated;
            }
            XrBridge.Head = _eye;
            XrBridge.GlassLayer = 0;
            RefreshHelp();
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
            Kill(dot.GetComponent<Collider>());
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
            Kill(go.GetComponent<Collider>());
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
            // over the back of the left wrist: turn the wrist up to read it
            var go = new GameObject("ControlCard", typeof(RectTransform));
            go.transform.SetParent(_left, false);
            go.transform.localPosition = new Vector3(0f, 0.085f, -0.075f);
            go.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.0004f;    // 30 px text = 12 mm
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(540, 300);
            var bg = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.03f, 0.02f, 0.02f, 0.88f);
            Stretch(bg.rectTransform);
            _card = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            _card.transform.SetParent(go.transform, false);
            _card.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _card.fontSize = 30;
            _card.lineSpacing = 1.1f;
            _card.supportRichText = true;
            _card.color = new Color(0.94f, 0.88f, 0.8f);
            _card.alignment = TextAnchor.UpperLeft;
            _card.horizontalOverflow = HorizontalWrapMode.Wrap;
            _card.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch(_card.rectTransform);
            _card.rectTransform.offsetMin = new Vector2(16, 12); _card.rectTransform.offsetMax = new Vector2(-16, -12);
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        XrContext Context => XrHelp.ContextFor(Services.State.Phase, _theater, XrBridge.LessonStage);

        /// <summary>The wrist card and the button labels for what the player is doing now. Cheap when nothing changed.</summary>
        void RefreshHelp()
        {
            var c = Context;
            bool tut = XrBridge.TutorialActive;
            bool hint = XrBridge.HintOffered, speak = XrBridge.SpeakOffered;
            int key = (int)c * 16 + (_turner.Smooth ? 8 : 0) + (tut ? 4 : 0) + (hint ? 2 : 0) + (speak ? 1 : 0);
            if (key == _helpKey) return;      // nothing changed: no strings built, no text touched
            _helpKey = key;
            if (_card) _card.text = XrHelp.Card(c, _turner.Smooth, tut, hint, speak);
            if (_rightModel != null)
            {
                XrHelp.RightLabels(c, _turner.Smooth, out var a, out var b, out var st, out var tr, out var gr);
                _rightModel.Label(XrControllerModel.Button.Face1, a);
                _rightModel.Label(XrControllerModel.Button.Face2, b);
                _rightModel.Label(XrControllerModel.Button.Stick, st);
                _rightModel.Label(XrControllerModel.Button.Trigger, tr);
                _rightModel.Label(XrControllerModel.Button.Grip, gr);
            }
            if (_leftModel != null)
            {
                XrHelp.LeftLabels(c, tut, out var x, out var y, out var st, out var tr, out var gr, hint, speak);
                _leftModel.Label(XrControllerModel.Button.Face1, x);
                _leftModel.Label(XrControllerModel.Button.Face2, y);
                _leftModel.Label(XrControllerModel.Button.Stick, st);
                _leftModel.Label(XrControllerModel.Button.Trigger, tr);
                _leftModel.Label(XrControllerModel.Button.Grip, gr);
            }
        }

        /// <summary>Buttons light and move with the hands.</summary>
        void ShowButtons()
        {
            if (_rightModel != null)
            {
                _rightModel.Show(XrControllerModel.Button.Face1, _a.IsPressed(), 0f, 0f);
                _rightModel.Show(XrControllerModel.Button.Face2, _b.IsPressed(), 0f, _mapper.B.Progress);
                _rightModel.Show(XrControllerModel.Button.Trigger, _rTrigger.IsPressed(), _rTriggerAxis.ReadValue<float>(), 0f);
                _rightModel.Show(XrControllerModel.Button.Grip, _rGrip.IsPressed(), 0f, 0f);
                _rightModel.Stick(_turn.ReadValue<Vector2>(), _rClick.IsPressed());
            }
            if (_leftModel != null)
            {
                _leftModel.Show(XrControllerModel.Button.Face1, _x.IsPressed(), 0f, 0f);
                _leftModel.Show(XrControllerModel.Button.Face2, _y.IsPressed(), 0f, _mapper.Y.Progress);
                _leftModel.Show(XrControllerModel.Button.Trigger, _lTrigger.IsPressed(), _lTriggerAxis.ReadValue<float>(), 0f);
                _leftModel.Show(XrControllerModel.Button.Grip, _run.IsPressed(), 0f, 0f);
                _leftModel.Stick(_move.ReadValue<Vector2>(), _lClick.IsPressed());
            }
        }

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
            Kill(screen.GetComponent<Collider>());
            screen.name = "TheaterScreen";
            screen.layer = TheaterLayer;
            // under the CameraOffset, in the headset's tracking space: AlignTheater puts it in front of the face
            screen.transform.SetParent(_theaterOffset, false);
            screen.transform.localPosition = new Vector3(0, EyeHeight, TheaterLayout.Distance);
            screen.transform.localScale = new Vector3(TheaterLayout.Size.x, TheaterLayout.Size.y, 1f);
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
            _rGrip = Act("RGrip", InputActionType.Button, "<XRController>{RightHand}/{GripButton}");
            _a = Act("A", InputActionType.Button, "<XRController>{RightHand}/{PrimaryButton}");
            _b = Act("B", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}");
            _x = Act("X", InputActionType.Button, "<XRController>{LeftHand}/{PrimaryButton}");
            _y = Act("Y", InputActionType.Button, "<XRController>{LeftHand}/{SecondaryButton}");
            _rTrigger = Act("RTrigger", InputActionType.Button, "<XRController>{RightHand}/{TriggerButton}");
            _lTrigger = Act("LTrigger", InputActionType.Button, "<XRController>{LeftHand}/{TriggerButton}");   // shown, mapped to nothing
            _rTriggerAxis = Act("RTriggerAxis", InputActionType.Value, "<XRController>{RightHand}/{Trigger}");
            _lTriggerAxis = Act("LTriggerAxis", InputActionType.Value, "<XRController>{LeftHand}/{Trigger}");
            // the head in tracking space, whatever a cutscene does to the camera
            _headPos = new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            _headRot = new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            _headPos.Enable(); _headRot.Enable();
            _actions.Add(_headPos); _actions.Add(_headRot);
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

        void OnOriginUpdated(XRInputSubsystem s) { _recalibrate = true; _realignTheater = true; }

        /// <summary>Destroy that also works when tests build the rig in edit mode.</summary>
        internal static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy() => Shutdown();

        /// <summary>Release the input actions, the theater texture and any pause (OnDestroy; tests call it directly).</summary>
        public void Shutdown()
        {
            foreach (var a in _actions) { if (a == null) continue; a.Disable(); a.Dispose(); }
            _actions.Clear();
            foreach (var s in _inputs) if (s != null) s.trackingOriginUpdated -= OnOriginUpdated;
            if (_theaterRt) { if (_eyeCam && _eyeCam.targetTexture == _theaterRt) _eyeCam.targetTexture = null; _theaterRt.Release(); Kill(_theaterRt); _theaterRt = null; }
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
            RememberHead();
            bool onGlass = Laser(phase);
            ReadControls(dt, onGlass);
            if (_paused) { XrBridge.ClearFrame(); return; }

            Calibrate();
            Vector2 turnStick = _turn.ReadValue<Vector2>();
            if (_eye && !_theater && Roaming(phase) && _player && _player.gameObject.activeInHierarchy)
            {
                FollowHead();
                float yaw = _turner.Tick(turnStick, Time.deltaTime);
                if (yaw != 0f) { _player.Rotate(0f, yaw, 0f, Space.World); XrBridge.StickTurnDegrees += Mathf.Abs(yaw); }
                bool moving = XrBridge.Move.sqrMagnitude > 0.04f || (_turner.Smooth && Mathf.Abs(turnStick.x) > 0.15f);
                _vignetteA = Mathf.MoveTowards(_vignetteA, moving ? 0.85f : 0f, dt * 4f);
            }
            else
            {
                // On a screen the right stick steps the cursor: a stick still held when roaming resumes must not turn.
                _turner.Rest(turnStick);
                _vignetteA = Mathf.MoveTowards(_vignetteA, 0f, dt * 4f);
            }
            if (_vignette)
            {
                _vignette.enabled = _vignetteA > 0.01f && !_theater;
                _vignetteMat.color = new Color(0, 0, 0, _vignetteA);
            }
            if (_rClick.WasPressedThisFrame())
            {
                _turner.Smooth = !_turner.Smooth;
                _turner.Rest(turnStick);   // switching to snap with the stick pushed over must not snap at once
                PlayerPrefs.SetInt(SmoothTurnPref, _turner.Smooth ? 1 : 0);
            }
            if (!_theater) { ShowButtons(); RefreshHelp(); }
        }

        /// <summary>
        /// Touch controllers onto the game's keys (the layout is <see cref="TouchMapper"/>). Every field is written
        /// every frame; each reader only looks at the ones its phase uses (the roaming player vs. the screens), as
        /// with the keyboard.
        /// </summary>
        void ReadControls(float dt, bool onGlass)
        {
            XrBridge.ClearFrame();
            var f = new TouchFrame
            {
                LeftStick = _move.ReadValue<Vector2>(), RightStick = _turn.ReadValue<Vector2>(),
                A = _a.IsPressed(), B = _b.IsPressed(), X = _x.IsPressed(), Y = _y.IsPressed(),
                RightTrigger = _rTrigger.IsPressed(), LeftTrigger = _lTrigger.IsPressed(),
                LeftGrip = _run.IsPressed(), Menu = _menu.IsPressed(), PointerOnGlass = onGlass,
            };
            var m = _mapper.Tick(f, dt);
            XrBridge.Move = m.Move; XrBridge.Run = m.Run; XrBridge.Interact = m.Interact; XrBridge.Flashlight = m.Flashlight;
            XrBridge.Leave = m.Leave; XrBridge.SkipHeld = m.SkipHeld;
            XrBridge.Enter = m.Enter; XrBridge.Space = m.Space; XrBridge.Back = m.Back; XrBridge.Clear = m.Clear;
            XrBridge.Replay = m.Replay; XrBridge.Click = m.Click; XrBridge.Hint = m.Hint; XrBridge.Speak = m.Speak;
            XrBridge.Left = m.Left; XrBridge.Right = m.Right; XrBridge.Up = m.Up; XrBridge.Down = m.Down;
            if (onGlass) { XrBridge.HasPointer = true; XrBridge.PointerUv = _pointerUv; }
        }

        /// <summary>The head's pose in tracking space, kept fresh while the player's own view is live (a cutscene
        /// may re-parent the camera the same frame it starts).</summary>
        void RememberHead()
        {
            if (TrackedHead(out var p, out var r)) { _lastHeadPos = p; _lastHeadRot = r; return; }
            if (_eye && !_theater && _eye.parent == _offset && _eye.localPosition != Vector3.zero)
            {
                _lastHeadPos = _eye.localPosition; _lastHeadRot = _eye.localRotation;
            }
        }

        bool TrackedHead(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (_headPos == null || _headRot == null || _headPos.activeControl == null || _headRot.activeControl == null) return false;
            pos = _headPos.ReadValue<Vector3>();
            rot = _headRot.ReadValue<Quaternion>();
            float n = rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w;
            if (n < 0.5f) return false;                 // no pose yet
            return true;
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

        Vector2 _pointerUv;

        /// <summary>The laser and its dot; true when it meets the UI glass (the UV is kept for ReadControls).</summary>
        bool Laser(GamePhase phase)
        {
            bool screens = !_theater && !Roaming(phase) && !Services.State.IsOver && _aim && !_paused;
            bool hit = false;
            Vector3 point = default;
            if (screens && XrBridge.Glass && GlassHit.Raycast(XrBridge.Glass, _aim.position, _aim.forward, out var uv, out point))
            {
                hit = true;
                _pointerUv = uv;
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
                if (_dot.gameObject.activeSelf != hit) _dot.gameObject.SetActive(hit);
                if (hit) _dot.SetPositionAndRotation(point - XrBridge.Glass.forward * 0.005f, XrBridge.Glass.rotation);
            }
            return hit;
        }

        void LateUpdate()
        {
            if (!XrBridge.Active) return;
            var s = Services.State;
            // the death scare's first moment plays in the world, in the player's own view (HoldTheater)
            bool want = (XrBridge.CutscenePlaying || s.IsOver) && !XrBridge.HoldTheater;
            if (want != _theater) SetTheater(want);
            else if (_theater && _realignTheater) AlignTheater();
            _realignTheater = false;
            if (_theater && _eyeCam)
            {
                // The cutscene may have replaced, re-parented or re-targeted the camera; keep it on the screen.
                if (_eyeCam.targetTexture != _theaterRt) _eyeCam.targetTexture = _theaterRt;
                _eyeCam.stereoTargetEye = StereoTargetEyeMask.None;
                _eyeCam.cullingMask &= ~(1 << TheaterLayer);
            }
        }

        /// <summary>
        /// Put the theater screen straight ahead of where the headset faces now, at eye level, wherever the
        /// player stands or has turned in their room. Without this the screen sat down the room's +Z axis, so a
        /// player who had turned (physically or with the stick's snap turns counting for nothing here) saw the
        /// ending off to one side or behind them.
        /// </summary>
        void AlignTheater()
        {
            if (!_screen || !_theaterOffset) return;
            Vector3 p = _lastHeadPos; Quaternion r = _lastHeadRot;
            if (TrackedHead(out var tp, out var tr)) { p = tp; r = tr; }
            TheaterLayout.Screen(p, r, EyeHeight - _theaterOffset.localPosition.y, out var pos, out var rot);
            _screen.transform.localPosition = pos;
            _screen.transform.localRotation = rot;
        }

        void SetTheater(bool on)
        {
            if (on) AlignTheater();
            _theater = on;
            if (_guide) _guide.SetTheater(on);
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

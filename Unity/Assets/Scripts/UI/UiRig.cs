using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>One frame of input, already translated for the screens. Tests build these by hand.</summary>
    public struct UiInput
    {
        public int Digit;          // 0-based index of a number key pressed this frame, -1 for none
        public bool Enter, Space, Back, Clear, Left, Right, Up, Down, Click;
        public bool Replay;        // T: the title's "replay tutorial"
        public bool Hint;          // H: ask the ship computer for a hint (console only)
        public bool Speak;         // P: hear the decoded sentence again (quiz payoff only)
        public bool HasPointer;
        public Vector2 Pointer;    // stage space: origin at the centre of the stage, y up, in virtual pixels

        public static UiInput None => new UiInput { Digit = -1 };
        public bool Confirm => Enter || Space;
        public bool Any => Digit >= 0 || Enter || Space || Back || Clear || Left || Right || Up || Down || Click;

        public static UiInput Key(int digit) => new UiInput { Digit = digit };
        public static UiInput PressEnter => new UiInput { Digit = -1, Enter = true };
        public static UiInput PressBack => new UiInput { Digit = -1, Back = true };
        public static UiInput PressReplay => new UiInput { Digit = -1, Replay = true };
        public static UiInput PressHint => new UiInput { Digit = -1, Hint = true };
        public static UiInput PressSpeak => new UiInput { Digit = -1, Speak = true };
    }

    /// <summary>
    /// The UI's own little world: a camera that draws the canvas into a low-resolution texture, and an
    /// overlay that shows that texture through the CRT shader. Nothing here touches the scene.
    /// </summary>
    internal sealed class UiRig
    {
        public const int StageW = 640, StageH = 360;

        public GameObject Root;
        public Camera Cam;
        public Canvas Canvas;
        public RectTransform CanvasRt, Stage, Backdrop;
        public RenderTexture Rt;
        public Material Mat;
        public RawImage Presenter;
        public Canvas PresenterCanvas;
        public int W = StageW, H = StageH;
        int _screenW, _screenH;

        public static UiRig Build(Transform parent)
        {
            var rig = new UiRig();
            rig.Root = new GameObject("UiRig") { layer = Kit.UiLayer };
            rig.Root.transform.SetParent(parent, false);
            rig.Root.transform.position = new Vector3(0, -4000, 0);   // far from the level; only the UI camera looks here

            var camGo = new GameObject("UiCamera") { layer = Kit.UiLayer };
            camGo.transform.SetParent(rig.Root.transform, false);
            var cam = rig.Cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = StageH * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Kit.UiLayer;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.depth = -50;
            cam.useOcclusionCulling = false;

            var canvasGo = new GameObject("UiCanvas", typeof(RectTransform)) { layer = Kit.UiLayer };
            canvasGo.transform.SetParent(rig.Root.transform, false);
            var canvas = rig.Canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            canvas.pixelPerfect = true;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            rig.CanvasRt = (RectTransform)canvasGo.transform;

            rig.Backdrop = Kit.Stretch("Backdrop", rig.CanvasRt);
            var stage = rig.Stage = Kit.Node("Stage", rig.CanvasRt);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(StageW, StageH);
            stage.anchoredPosition = Vector2.zero;

            var shader = Resources.Load<Shader>("UI/CRT");
            if (!shader) { Debug.LogWarning("[UI] CRT shader missing; showing the interface without effects"); shader = Shader.Find("UI/Default"); }
            rig.Mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            var showGo = new GameObject("UiPresenter", typeof(RectTransform));
            showGo.transform.SetParent(parent, false);
            var showCanvas = rig.PresenterCanvas = showGo.AddComponent<Canvas>();
            showCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            showCanvas.sortingOrder = 5000;
            var imgRt = Kit.Stretch("Glass", showGo.transform);
            imgRt.gameObject.layer = 0;
            var img = rig.Presenter = imgRt.gameObject.AddComponent<RawImage>();
            img.material = rig.Mat;
            img.raycastTarget = false;

            rig.Resize(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            return rig;
        }

        /// <summary>Virtual resolution: 360 lines tall, at least 640 wide, following the window's aspect.</summary>
        public static Vector2Int VirtualSize(int screenW, int screenH)
        {
            float aspect = screenW / (float)Mathf.Max(1, screenH);
            int w = Mathf.RoundToInt(StageH * aspect / 2f) * 2, h = StageH;
            if (w < StageW) { w = StageW; h = Mathf.Clamp(Mathf.RoundToInt(StageW / aspect / 2f) * 2, StageH, 960); }
            return new Vector2Int(Mathf.Min(w, 1280), h);
        }

        public void Resize(int screenW, int screenH)
        {
            if (screenW == _screenW && screenH == _screenH && Rt) return;
            _screenW = screenW; _screenH = screenH;
            var v = VirtualSize(screenW, screenH);
            if (!Rt || Rt.width != v.x || Rt.height != v.y)
            {
                if (Rt) { Cam.targetTexture = null; Rt.Release(); Kit.Kill(Rt); }
                W = v.x; H = v.y;
                Rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32)
                {
                    name = "FnmwUi", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave,
                };
                Rt.Create();
                Cam.targetTexture = Rt;
                Presenter.texture = Rt;
                Mat.SetTexture("_UiTex", Rt);
            }
            Mat.SetVector("_UiSize", new Vector4(W, H, screenW / (float)W, screenH / (float)H));
            // One scanline per virtual row when the window is big enough to show the gap between rows;
            // otherwise one per k rows, and shallower, so small windows keep their text readable.
            float pxPerRow = screenH / (float)H;
            int k = Mathf.Max(1, Mathf.CeilToInt(2.5f / pxPerRow));
            Mat.SetFloat("_ScanLines", H / (float)k);
            Mat.SetFloat("_ScanDepth", k == 1 ? 1f : 0.45f);
            // Overscan the glass a few pixels past the window so no edge of the world can leak around it.
            const float over = 8f;
            var glass = Presenter.rectTransform;
            glass.offsetMin = new Vector2(-over, -over); glass.offsetMax = new Vector2(over, over);
            Presenter.uvRect = new Rect(-over / screenW, -over / screenH, 1f + 2f * over / screenW, 1f + 2f * over / screenH);
        }

        // ---- VR ---------------------------------------------------------------------------
        // A screen-space overlay never reaches a headset, so in VR the same glass (same texture, same CRT
        // material, same content) hangs in the world in front of the player and follows their head.

        /// <summary>Distance from the eyes to the glass in VR, metres.</summary>
        public const float XrDistance = 1.9f;
        /// <summary>How wide the glass is in VR, metres (about 64 degrees of view at XrDistance).</summary>
        public const float XrWidth = 2.4f;
        bool _xrPlaced;
        Vector3 _xrPos;
        Quaternion _xrRot = Quaternion.identity;

        /// <summary>Glass size in VR for a texture of w x h virtual pixels: XrWidth wide, the texture's aspect.</summary>
        public static Vector2 XrSize(int w, int h) => new Vector2(XrWidth, XrWidth * h / Mathf.Max(1f, w));

        /// <summary>Called every UI frame. Does nothing on desktop.</summary>
        public void FollowXr(float dt, bool roaming)
        {
            if (!XrBridge.Active || !PresenterCanvas) return;
            var head = XrBridge.Head;
            if (PresenterCanvas.renderMode != RenderMode.WorldSpace)
            {
                PresenterCanvas.renderMode = RenderMode.WorldSpace;
                PresenterCanvas.worldCamera = null;
                var crt = (RectTransform)PresenterCanvas.transform;
                crt.pivot = new Vector2(0.5f, 0.5f);
                _xrPlaced = false;
            }
            var rt = (RectTransform)PresenterCanvas.transform;
            // one virtual pixel = XrWidth / W metres; the glass rect is in canvas pixels
            rt.sizeDelta = new Vector2(W, H);
            float s = XrWidth / Mathf.Max(1, W);
            rt.localScale = new Vector3(s, s, s);
            var glass = Presenter.rectTransform;
            glass.offsetMin = glass.offsetMax = Vector2.zero;
            Presenter.uvRect = new Rect(0, 0, 1, 1);
            int layer = XrBridge.GlassLayer;
            if (PresenterCanvas.gameObject.layer != layer) { PresenterCanvas.gameObject.layer = layer; glass.gameObject.layer = layer; }
            if (!head) return;

            // Where it wants to be: straight ahead of the face at XrDistance, level-ish (pitch damped).
            Vector3 fwd = head.forward;
            Vector3 flat = fwd; flat.y = 0;
            if (flat.sqrMagnitude < 1e-4f) flat = head.up; flat.y = 0;
            flat.Normalize();
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 35f) * 0.6f;
            Quaternion want = Quaternion.LookRotation(flat) * Quaternion.Euler(pitch, 0, 0);
            Vector3 wantPos = head.position + want * Vector3.forward * XrDistance;
            if (!_xrPlaced) { _xrPlaced = true; _xrPos = wantPos; _xrRot = want; }
            else
            {
                // Roaming: the visor rides with the head (tight follow). On a screen: lazy follow, it only
                // slides back in front once the head has turned well away, so reading is steady.
                float off = Quaternion.Angle(_xrRot, want);
                float rate = roaming ? 10f : off > 28f ? 4f : (Vector3.Distance(_xrPos, wantPos) > 0.6f ? 3f : 0f);
                float k = 1f - Mathf.Exp(-rate * dt);
                _xrRot = Quaternion.Slerp(_xrRot, want, k);
                _xrPos = Vector3.Lerp(_xrPos, head.position + _xrRot * Vector3.forward * XrDistance, roaming ? k : Mathf.Max(k, 1f - Mathf.Exp(-8f * dt)));
            }
            rt.SetPositionAndRotation(_xrPos, _xrRot);
            XrBridge.Glass = rt;
        }

        /// <summary>A 0..1 point on the glass (x right, y up) to stage space, through the CRT curve.</summary>
        public Vector2 UvToStage(Vector2 uv, float curve)
        {
            var c = uv - new Vector2(0.5f, 0.5f);
            var s = c * (1f + curve * (c.sqrMagnitude - 0.25f));
            return new Vector2(s.x * W, s.y * H);
        }

        public void Dispose()
        {
            if (Cam) Cam.targetTexture = null;
            if (Rt) { Rt.Release(); Kit.Kill(Rt); }
            Kit.Kill(Mat);
        }

        /// <summary>Screen pixel to stage space, through the same curve the shader bends the glass with.</summary>
        public Vector2 ScreenToStage(Vector2 screenPx, float curve)
        {
            var uv = new Vector2(screenPx.x / Mathf.Max(1, _screenW), screenPx.y / Mathf.Max(1, _screenH));
            var c = uv - new Vector2(0.5f, 0.5f);
            var s = c * (1f + curve * (c.sqrMagnitude - 0.25f));
            return new Vector2(s.x * W, s.y * H);
        }

        /// <summary>Reads the keyboard and mouse through the Input System.</summary>
        public UiInput ReadInput(float curve)
        {
            var i = UiInput.None;
            var kb = Keyboard.current;
            if (kb != null)
            {
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key, kb.digit0Key };
                var pad = new[] { kb.numpad1Key, kb.numpad2Key, kb.numpad3Key, kb.numpad4Key, kb.numpad5Key, kb.numpad6Key, kb.numpad7Key, kb.numpad8Key, kb.numpad9Key, kb.numpad0Key };
                for (int d = 0; d < 10; d++)
                    if (digits[d].wasPressedThisFrame || pad[d].wasPressedThisFrame) { i.Digit = d; break; }
                i.Enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                i.Space = kb.spaceKey.wasPressedThisFrame;
                i.Back = kb.backspaceKey.wasPressedThisFrame;
                i.Clear = kb.deleteKey.wasPressedThisFrame;   // Esc walks away from the console (PlayerController); it no longer also wipes the tiles
                i.Left = kb.leftArrowKey.wasPressedThisFrame;
                i.Right = kb.rightArrowKey.wasPressedThisFrame;
                i.Up = kb.upArrowKey.wasPressedThisFrame;
                i.Down = kb.downArrowKey.wasPressedThisFrame;
                i.Replay = kb.tKey.wasPressedThisFrame;
                i.Hint = kb.hKey.wasPressedThisFrame;
                i.Speak = kb.pKey.wasPressedThisFrame;
            }
            if (XrBridge.Active)
            {
                // Touch controllers, translated by Fnmw.XR into the same keys the screens already know.
                if (i.Digit < 0) i.Digit = XrBridge.Digit;
                i.Enter |= XrBridge.Enter; i.Space |= XrBridge.Space; i.Back |= XrBridge.Back; i.Clear |= XrBridge.Clear;
                i.Left |= XrBridge.Left; i.Right |= XrBridge.Right; i.Up |= XrBridge.Up; i.Down |= XrBridge.Down;
                i.Replay |= XrBridge.Replay;
                if (XrBridge.HasPointer)
                {
                    i.HasPointer = true;
                    i.Pointer = UvToStage(XrBridge.PointerUv, curve);
                    i.Click = XrBridge.Click;
                }
                return i;
            }
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState != CursorLockMode.Locked)
            {
                i.HasPointer = true;
                i.Pointer = ScreenToStage(mouse.position.ReadValue(), curve);
                i.Click = mouse.leftButton.wasPressedThisFrame;
            }
            return i;
        }
    }
}

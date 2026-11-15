using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.XR
{
    /// <summary>
    /// A Touch-like controller body built from primitives under a tracked grip pose: the handle, the face with its
    /// tracking ring, two face buttons, a thumbstick, the trigger and the grip. Every button lights up and sinks
    /// while held (the trigger swings with how far it is pulled, the stick tilts), a hold fills the button
    /// towards amber, and a short label beside each button says what it does right now.
    ///
    /// Geometry is in the grip pose's space (Unity's OpenXR grip: +Z along the handle, pointing forward out of
    /// the fist, +Y up out of the back of the hand). It is an approximation of the real shape, not a scan.
    /// </summary>
    public sealed class XrControllerModel
    {
        public enum Button { Face1, Face2, Stick, Trigger, Grip }   // Face1 = A/X (lower), Face2 = B/Y (upper)

        static readonly Color Shell = new Color(0.10f, 0.10f, 0.11f);
        static readonly Color Face = new Color(0.16f, 0.16f, 0.17f);
        static readonly Color Ring = new Color(0.22f, 0.21f, 0.22f);
        static readonly Color Key = new Color(0.55f, 0.52f, 0.48f);
        static readonly Color Lit = new Color(1f, 0.78f, 0.52f);
        static readonly Color HoldLit = new Color(1f, 0.55f, 0.18f);

        public readonly Transform Root;
        public readonly bool Left;

        sealed class Part
        {
            public Transform T;
            public Material Mat;
            public Vector3 Rest;
            public Quaternion RestRot;
            public Color Base;
            public Text Label;
            public string LabelShown;
        }

        readonly Dictionary<Button, Part> _parts = new Dictionary<Button, Part>();
        readonly Transform _stickCap;
        readonly Canvas _labels;

        /// <summary>Build under <paramref name="pose"/> (the tracked grip). <paramref name="left"/>: X/Y, menu side.</summary>
        public XrControllerModel(Transform pose, bool left)
        {
            Left = left;
            Root = new GameObject("TouchModel").transform;
            Root.SetParent(pose, false);
            float side = left ? 1f : -1f;   // towards the thumb: the face buttons sit on the thumb side of centre

            // the handle, tilted the way a fist holds it
            Prim(PrimitiveType.Capsule, "Handle", Root, new Vector3(0, -0.005f, -0.025f), Quaternion.Euler(90f, 0, 0), new Vector3(0.034f, 0.055f, 0.03f), Shell);
            // the head: a flattened puck above the handle carrying the face
            var head = new GameObject("Head").transform;
            head.SetParent(Root, false);
            head.localPosition = new Vector3(0, 0.022f, 0.035f);
            head.localRotation = Quaternion.Euler(-28f, 0, 0);
            Prim(PrimitiveType.Cylinder, "Face", head, Vector3.zero, Quaternion.identity, new Vector3(0.06f, 0.008f, 0.056f), Face);
            // the tracking ring, the halo the player will recognise
            Prim(PrimitiveType.Cylinder, "Ring", head, new Vector3(-side * 0.012f, 0.002f, 0.018f), Quaternion.Euler(28f, 0, side * 12f), new Vector3(0.09f, 0.004f, 0.075f), Ring);

            // face buttons (A/B or X/Y), thumbstick
            _parts[Button.Face1] = Key3(PrimitiveType.Cylinder, "Face1", head, new Vector3(side * 0.012f, 0.007f, -0.004f), new Vector3(0.013f, 0.003f, 0.013f));
            _parts[Button.Face2] = Key3(PrimitiveType.Cylinder, "Face2", head, new Vector3(side * 0.006f, 0.007f, 0.014f), new Vector3(0.013f, 0.003f, 0.013f));
            var stick = Key3(PrimitiveType.Cylinder, "Stick", head, new Vector3(-side * 0.012f, 0.009f, 0.002f), new Vector3(0.006f, 0.006f, 0.006f));
            _parts[Button.Stick] = stick;
            _stickCap = Prim(PrimitiveType.Cylinder, "StickCap", stick.T, new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(3f, 0.45f, 3f), Key).transform;
            // the trigger under the index finger, the grip under the middle finger
            _parts[Button.Trigger] = Key3(PrimitiveType.Cube, "Trigger", Root, new Vector3(0, -0.006f, 0.05f), new Vector3(0.012f, 0.026f, 0.01f), Quaternion.Euler(30f, 0, 0));
            _parts[Button.Grip] = Key3(PrimitiveType.Cube, "Grip", Root, new Vector3(-side * 0.017f, -0.012f, -0.012f), new Vector3(0.006f, 0.03f, 0.04f), Quaternion.Euler(70f, 0, 0));

            // labels: one small world-space canvas, the text beside each button, angled up towards the eyes
            var cgo = new GameObject("Labels", typeof(RectTransform));
            cgo.transform.SetParent(Root, false);
            cgo.transform.localPosition = new Vector3(0, 0.035f, 0.035f);
            cgo.transform.localRotation = Quaternion.Euler(62f, 0, 0);
            cgo.transform.localScale = Vector3.one * 0.0004f;     // 1 canvas unit = 0.4 mm; 26 px text = ~1 cm tall
            _labels = cgo.AddComponent<Canvas>();
            _labels.renderMode = RenderMode.WorldSpace;
            ((RectTransform)cgo.transform).sizeDelta = new Vector2(400, 300);
            float outX = side * 125f;   // labels sit outboard of the controller, on the thumb's side
            AddLabel(Button.Face1, new Vector2(outX, -40f), left);
            AddLabel(Button.Face2, new Vector2(outX, 10f), left);
            AddLabel(Button.Stick, new Vector2(-outX, 30f), !left);
            AddLabel(Button.Trigger, new Vector2(-outX * 0.2f, 95f), !left);
            AddLabel(Button.Grip, new Vector2(-outX, -95f), !left);
        }

        void AddLabel(Button b, Vector2 at, bool alignLeft)
        {
            var t = new GameObject("Label" + b, typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(_labels.transform, false);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 26;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(0.95f, 0.88f, 0.78f, 0.95f);
            t.alignment = alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(220, 34);
            rt.pivot = new Vector2(alignLeft ? 0f : 1f, 0.5f);
            rt.anchoredPosition = at + new Vector2(alignLeft ? -20f : 20f, 0);
            var shadow = t.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0, 0, 0, 0.85f);
            shadow.effectDistance = new Vector2(2, -2);
            _parts[b].Label = t;
            t.text = "";
        }

        Part Key3(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var r = Prim(type, name, parent, pos, rot ?? Quaternion.identity, scale, Key);
            return new Part { T = r.transform, Mat = r.sharedMaterial, Rest = pos, RestRot = rot ?? Quaternion.identity, Base = Key };
        }

        static Shader _solid;
        static Renderer Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            XrRig.Kill(col);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            if (!_solid) _solid = Resources.Load<Shader>("XR/XrSolid");
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(_solid ? _solid : Shader.Find("Sprites/Default")) { name = "xr_" + name, color = c };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        /// <summary>Show one frame of input: <paramref name="hold"/> is 0..1 towards a hold (B, Y), 0 otherwise.</summary>
        public void Show(Button b, bool down, float analog, float hold)
        {
            if (!_parts.TryGetValue(b, out var p)) return;
            float lit = down ? 1f : 0f;
            Color c = Color.Lerp(p.Base, Lit, Mathf.Max(lit * 0.85f, analog * 0.7f));
            if (hold > 0f) c = Color.Lerp(c, HoldLit, hold);
            p.Mat.color = c;
            if (p.Mat.HasProperty("_Emit")) p.Mat.SetFloat("_Emit", Mathf.Max(lit, hold) * 0.6f);
            switch (b)
            {
                case Button.Trigger:
                    p.T.localRotation = p.RestRot * Quaternion.Euler(-28f * analog, 0, 0);
                    break;
                case Button.Grip:
                    p.T.localPosition = p.Rest + new Vector3((Left ? -1f : 1f) * 0.004f * lit, 0, 0);
                    break;
                case Button.Stick:
                    break;   // tilted by Stick()
                default:
                    p.T.localPosition = p.Rest - new Vector3(0, 0.0025f * lit, 0);
                    break;
            }
        }

        /// <summary>Tilt the thumbstick with its axis (clicked = sinks).</summary>
        public void Stick(Vector2 axis, bool clicked)
        {
            var p = _parts[Button.Stick];
            p.T.localRotation = Quaternion.Euler(axis.y * 22f, 0, -axis.x * 22f);
            p.T.localPosition = p.Rest - new Vector3(0, clicked ? 0.002f : 0f, 0);
            if (_stickCap)
            {
                var r = _stickCap.GetComponent<Renderer>();
                if (r) r.sharedMaterial.color = Color.Lerp(Key, Lit, Mathf.Clamp01(axis.magnitude) * 0.6f + (clicked ? 0.4f : 0f));
            }
        }

        /// <summary>What a button does right now (empty hides its label). Only touches the text when it changes.</summary>
        public void Label(Button b, string text)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Label) return;
            text ??= "";
            if (p.LabelShown == text) return;
            p.LabelShown = text;
            p.Label.text = text;
        }

        public string LabelOf(Button b) => _parts.TryGetValue(b, out var p) ? p.LabelShown ?? "" : "";
    }
}

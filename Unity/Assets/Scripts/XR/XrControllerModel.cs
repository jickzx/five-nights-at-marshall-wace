using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.XR
{
    /// <summary>
    /// A Touch-style controller under a tracked grip pose: the handle, the face with its tracking ring, two face
    /// buttons, a thumbstick, the trigger and the grip. The shape is the modelled one in Resources/Models/XR
    /// (touch_left / touch_right, made in assets/blender/touch_controllers.blend, one mesh per moving part, named
    /// as <see cref="ArtParts"/>); if that art is missing it falls back to the same layout built from primitives. Every button lights up and sinks
    /// while held (the trigger swings with how far it is pulled, the stick tilts), a hold fills the button
    /// towards amber, and a short label beside each button says what it does right now, led by the button's own
    /// name ("A  SEND"). The tutorial can mark buttons to press: they pulse, swell and their label turns bright.
    ///
    /// The body is a mid grey, not the real controller's black: the shore is dark and foggy, and a near-black
    /// controller read as no controller at all when the player looked down at their hands.
    ///
    /// Geometry is in the grip pose's space (Unity's OpenXR grip: +Z along the handle, pointing forward out of
    /// the fist, +Y up out of the back of the hand). As on the real controllers, the face buttons sit on the outer
    /// side of the face, the stick on the thumb side, and the grip on the inner side of the handle. It is an
    /// approximation of the real shape, not a scan.
    /// </summary>
    public sealed class XrControllerModel
    {
        public enum Button { Face1, Face2, Stick, Trigger, Grip }   // Face1 = A/X (lower), Face2 = B/Y (upper)

        static readonly Color Shell = new Color(0.34f, 0.34f, 0.36f);
        static readonly Color Face = new Color(0.24f, 0.24f, 0.26f);
        static readonly Color Ring = new Color(0.46f, 0.45f, 0.47f);
        static readonly Color Key = new Color(0.86f, 0.84f, 0.80f);
        static readonly Color GuideColor = new Color(0.55f, 0.95f, 1f);      // a button the tutorial wants pressed
        static readonly Color LabelColor = new Color(0.95f, 0.88f, 0.78f, 0.95f);
        public const int LabelSize = 30, GuideLabelSize = 38;
        static readonly Color Lit = new Color(1f, 0.78f, 0.52f);
        static readonly Color HoldLit = new Color(1f, 0.55f, 0.18f);

        public readonly Transform Root;
        public readonly bool Left;
        /// <summary>True when the modelled art was found and used; false for the primitive fallback.</summary>
        public readonly bool FromArt;

        /// <summary>The meshes the art must provide, by object name. StickCap is re-parented under Stick on load
        /// (the FBX export's axis baking misplaces nested children, so the file keeps every part a sibling).</summary>
        public static readonly string[] ArtParts = { "Body", "FacePlate", "Ring", "Face1", "Face2", "Stick", "StickCap", "Trigger", "Grip" };
        public const string ArtLeft = "Models/XR/touch_left", ArtRight = "Models/XR/touch_right";

        sealed class Part
        {
            public Transform T;
            public Material Mat;
            public Vector3 Rest, RestScale;
            public Vector3 Down;     // the direction a press moves it, in its parent's space
            public Quaternion RestRot;
            public string Name;      // "A", "TRIGGER": leads its label
            public bool Guided;
            public Color Base;
            public Text Label;
            public string LabelShown;
        }

        readonly Dictionary<Button, Part> _parts = new Dictionary<Button, Part>();
        Transform _stickCap;
        Vector3 _stickCapScale;
        readonly Canvas _labels;

        /// <summary>Build under <paramref name="pose"/> (the tracked grip). <paramref name="left"/>: X/Y, menu side.</summary>
        public XrControllerModel(Transform pose, bool left)
        {
            Left = left;
            Root = new GameObject("TouchModel").transform;
            Root.SetParent(pose, false);
            float side = left ? 1f : -1f;   // -1 for the right hand; labels go on the outer side, -side
            FromArt = BuildFromArt(left);
            if (!FromArt) BuildPrimitives(left);

            // labels: one small world-space canvas, the text beside each button, angled up towards the eyes
            var cgo = new GameObject("Labels", typeof(RectTransform));
            cgo.transform.SetParent(Root, false);
            cgo.transform.localPosition = new Vector3(0, 0.035f, 0.035f);
            cgo.transform.localRotation = Quaternion.Euler(62f, 0, 0);
            cgo.transform.localScale = Vector3.one * 0.0004f;     // 1 canvas unit = 0.4 mm; 26 px text = ~1 cm tall
            _labels = cgo.AddComponent<Canvas>();
            _labels.renderMode = RenderMode.WorldSpace;
            ((RectTransform)cgo.transform).sizeDelta = new Vector2(400, 300);
            // Every label in one column on the hand's outer side (right of the right hand, left of the left), each led by
            // its button's name. With the face labels on the thumb side, the two hands' labels met and overlapped in
            // the middle whenever the hands were held close together.
            float outX = -side * 105f;
            bool alignLeft = !left;
            _parts[Button.Face1].Name = left ? "X" : "A";
            _parts[Button.Face2].Name = left ? "Y" : "B";
            _parts[Button.Stick].Name = "STICK";
            _parts[Button.Trigger].Name = "TRIGGER";
            _parts[Button.Grip].Name = "GRIP";
            AddLabel(Button.Face2, new Vector2(outX, 92f), alignLeft);
            AddLabel(Button.Face1, new Vector2(outX, 46f), alignLeft);
            AddLabel(Button.Stick, new Vector2(outX, 0f), alignLeft);
            AddLabel(Button.Trigger, new Vector2(outX, -46f), alignLeft);
            AddLabel(Button.Grip, new Vector2(outX, -92f), alignLeft);
        }

        /// <summary>The modelled controller: instantiate it, recolour every part with the flat XR shader and pick out
        /// the moving parts. False (and nothing left behind) if the art is missing or lacks a part.</summary>
        bool BuildFromArt(bool left)
        {
            var prefab = Resources.Load<GameObject>(left ? ArtLeft : ArtRight);
            if (!prefab) return false;
            var art = Object.Instantiate(prefab, Root, false);
            art.name = "Art";
            // Blender's +Y forward / +Z up arrives in Unity facing -Z; half a turn puts it along the grip's +Z
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            art.transform.localScale = Vector3.one;
            var found = new Dictionary<string, Transform>();
            foreach (var t in art.GetComponentsInChildren<Transform>(true)) if (!found.ContainsKey(t.name)) found[t.name] = t;
            foreach (var n in ArtParts)
                if (!found.ContainsKey(n)) { Debug.LogWarning($"[XR] controller art {prefab.name} has no '{n}'; using primitives"); XrRig.Kill(art); return false; }

            found["StickCap"].SetParent(found["Stick"], true);
            Paint(found["Body"], Shell); Paint(found["FacePlate"], Face); Paint(found["Ring"], Ring); Paint(found["Stick"], Face);
            Paint(found["StickCap"], Key);
            _parts[Button.Face1] = ArtKey(found["Face1"]);
            _parts[Button.Face2] = ArtKey(found["Face2"]);
            _parts[Button.Stick] = ArtKey(found["Stick"], Face);
            _parts[Button.Trigger] = ArtKey(found["Trigger"]);
            _parts[Button.Grip] = ArtKey(found["Grip"]);
            _stickCap = found["StickCap"];
            _stickCapScale = _stickCap.localScale;
            foreach (var c in art.GetComponentsInChildren<Collider>(true)) XrRig.Kill(c);
            return true;
        }

        Part ArtKey(Transform t, Color? baseColor = null)
        {
            var c = baseColor ?? Key;
            var mat = Paint(t, c);
            // a face button's own up is the face normal (the art keeps each button's rotation), so it sinks along it
            return new Part { T = t, Mat = mat, Rest = t.localPosition, RestScale = t.localScale, RestRot = t.localRotation,
                Down = t.localRotation * Vector3.down, Base = c };
        }

        static Material Paint(Transform t, Color c)
        {
            var mr = t.GetComponent<Renderer>();
            if (!mr) return null;
            mr.sharedMaterial = Solid(t.name, c);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr.sharedMaterial;
        }

        /// <summary>The fallback: the same layout from primitives (used when the art is missing).</summary>
        void BuildPrimitives(bool left)
        {
            float outer = left ? -1f : 1f;   // the outer side of the face: right of the right hand, left of the left
            // the handle, tilted the way a fist holds it
            Prim(PrimitiveType.Capsule, "Handle", Root, new Vector3(0, -0.005f, -0.025f), Quaternion.Euler(90f, 0, 0), new Vector3(0.034f, 0.055f, 0.03f), Shell);
            // the head: a flattened puck above the handle carrying the face
            var head = new GameObject("Head").transform;
            head.SetParent(Root, false);
            head.localPosition = new Vector3(0, 0.022f, 0.035f);
            head.localRotation = Quaternion.Euler(-28f, 0, 0);
            Prim(PrimitiveType.Cylinder, "Face", head, Vector3.zero, Quaternion.identity, new Vector3(0.06f, 0.008f, 0.056f), Face);
            Prim(PrimitiveType.Cylinder, "Ring", head, new Vector3(outer * 0.012f, 0.002f, 0.018f), Quaternion.Euler(28f, 0, -outer * 12f), new Vector3(0.09f, 0.004f, 0.075f), Ring);

            // face buttons (A/B or X/Y) on the outer side, the thumbstick on the thumb side
            _parts[Button.Face1] = Key3(PrimitiveType.Cylinder, "Face1", head, new Vector3(outer * 0.012f, 0.007f, -0.004f), new Vector3(0.013f, 0.003f, 0.013f));
            _parts[Button.Face2] = Key3(PrimitiveType.Cylinder, "Face2", head, new Vector3(outer * 0.006f, 0.007f, 0.014f), new Vector3(0.013f, 0.003f, 0.013f));
            var stick = Key3(PrimitiveType.Cylinder, "Stick", head, new Vector3(-outer * 0.012f, 0.009f, 0.002f), new Vector3(0.006f, 0.006f, 0.006f));
            _parts[Button.Stick] = stick;
            _stickCapScale = new Vector3(3f, 0.45f, 3f);
            _stickCap = Prim(PrimitiveType.Cylinder, "StickCap", stick.T, new Vector3(0, 1.2f, 0), Quaternion.identity, _stickCapScale, Key).transform;
            // the trigger under the index finger, the grip on the inner side under the middle finger
            _parts[Button.Trigger] = Key3(PrimitiveType.Cube, "Trigger", Root, new Vector3(0, -0.006f, 0.05f), new Vector3(0.012f, 0.026f, 0.01f), Quaternion.Euler(30f, 0, 0));
            _parts[Button.Grip] = Key3(PrimitiveType.Cube, "Grip", Root, new Vector3(-outer * 0.017f, -0.012f, -0.012f), new Vector3(0.006f, 0.03f, 0.04f), Quaternion.Euler(70f, 0, 0));
        }

        void AddLabel(Button b, Vector2 at, bool alignLeft)
        {
            var t = new GameObject("Label" + b, typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(_labels.transform, false);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = LabelSize;
            t.fontStyle = FontStyle.Bold;
            t.color = LabelColor;
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
            return new Part { T = r.transform, Mat = r.sharedMaterial, Rest = pos, RestScale = scale, RestRot = rot ?? Quaternion.identity,
                Down = Vector3.down, Base = Key };
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
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Solid(name, c);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static Material Solid(string name, Color c)
        {
            if (!_solid) _solid = Resources.Load<Shader>("XR/XrSolid");
            return new Material(_solid ? _solid : Shader.Find("Sprites/Default")) { name = "xr_" + name, color = c };
        }

        /// <summary>Show one frame of input: <paramref name="hold"/> is 0..1 towards a hold (B, Y), 0 otherwise.</summary>
        public void Show(Button b, bool down, float analog, float hold)
        {
            if (!_parts.TryGetValue(b, out var p)) return;
            float lit = down ? 1f : 0f;
            float pulse = p.Guided && !down ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            Color c = Color.Lerp(p.Base, Lit, Mathf.Max(lit * 0.85f, analog * 0.7f));
            if (pulse > 0f) c = Color.Lerp(c, GuideColor, 0.45f + 0.55f * pulse);
            if (hold > 0f) c = Color.Lerp(c, HoldLit, hold);
            p.Mat.color = c;
            if (p.Mat.HasProperty("_Emit")) p.Mat.SetFloat("_Emit", Mathf.Max(Mathf.Max(lit, hold) * 0.6f, pulse * 0.5f));
            // a guided button swells a little in time with its pulse, so it is found at a glance
            if (b != Button.Stick) p.T.localScale = p.RestScale * (1f + (p.Guided ? 0.18f + 0.22f * pulse : 0f));
            switch (b)
            {
                case Button.Trigger:
                    p.T.localRotation = p.RestRot * Quaternion.Euler(-28f * analog, 0, 0);
                    break;
                case Button.Grip:
                    // squeezed in towards the handle's centre line
                    p.T.localPosition = p.Rest + new Vector3(-Mathf.Sign(p.Rest.x) * 0.0015f * lit, 0, 0);
                    break;
                case Button.Stick:
                    break;   // tilted by Stick()
                default:
                    p.T.localPosition = p.Rest + p.Down * (0.0022f * lit);
                    break;
            }
        }

        /// <summary>Tilt the thumbstick with its axis (clicked = sinks).</summary>
        public void Stick(Vector2 axis, bool clicked)
        {
            var p = _parts[Button.Stick];
            p.T.localRotation = p.RestRot * Quaternion.Euler(axis.y * 22f, 0, -axis.x * 22f);
            p.T.localPosition = p.Rest + p.Down * (clicked ? 0.002f : 0f);
            if (_stickCap)
            {
                var r = _stickCap.GetComponent<Renderer>();
                float pulse = p.Guided && axis.sqrMagnitude < 0.04f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
                Color c = Color.Lerp(Key, Lit, Mathf.Clamp01(axis.magnitude) * 0.6f + (clicked ? 0.4f : 0f));
                if (r) r.sharedMaterial.color = pulse > 0f ? Color.Lerp(c, GuideColor, 0.45f + 0.55f * pulse) : c;
                _stickCap.localScale = _stickCapScale * (p.Guided ? 1.25f + 0.3f * pulse : 1f);
            }
        }

        /// <summary>What a button does right now (empty hides its label). Only touches the text when it changes.</summary>
        public void Label(Button b, string text)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Label) return;
            text ??= "";
            if (p.LabelShown == text) return;
            p.LabelShown = text;
            Draw(p);
        }

        public string LabelOf(Button b) => _parts.TryGetValue(b, out var p) ? p.LabelShown ?? "" : "";

        /// <summary>The label as drawn: the button's name, then what it does ("A  SEND"); a guided button with no
        /// job right now still shows its name, marked.</summary>
        public string DrawnLabelOf(Button b) => _parts.TryGetValue(b, out var p) && p.Label ? p.Label.text : "";

        public bool IsGuided(Button b) => _parts.TryGetValue(b, out var p) && p.Guided;

        /// <summary>Mark the buttons the tutorial wants pressed (all others are cleared). Cheap when nothing changed.</summary>
        public void Guide(IReadOnlyList<XrHelp.Key> keys)
        {
            foreach (var kv in _parts)
            {
                bool on = false;
                if (keys != null)
                    for (int i = 0; i < keys.Count; i++)
                        if (keys[i].Left == Left && keys[i].Button == kv.Key) { on = true; break; }
                if (kv.Value.Guided == on) continue;
                kv.Value.Guided = on;
                Draw(kv.Value);
            }
        }

        static void Draw(Part p)
        {
            if (!p.Label) return;
            string job = p.LabelShown ?? "";
            string text = job.Length == 0 ? (p.Guided ? p.Name : "") : p.Name + "  " + job;
            if (p.Guided && text.Length > 0) text = "> " + text;
            p.Label.text = text;
            p.Label.fontSize = p.Guided ? GuideLabelSize : LabelSize;
            p.Label.color = p.Guided ? GuideColor : LabelColor;
        }

        /// <summary>Turn every label to face the eyes, so they read from any wrist angle, not edge-on.</summary>
        public void FaceLabels(Vector3 eye)
        {
            foreach (var p in _parts.Values)
            {
                if (!p.Label || p.Label.text.Length == 0) continue;
                var t = p.Label.transform;
                var d = t.position - eye;
                if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }
    }
}

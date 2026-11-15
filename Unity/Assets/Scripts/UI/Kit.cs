using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>Blacks, rust, bone and blood. Amber and ivory are kept for the reveal; dawn for the escape.</summary>
    internal static class Pal
    {
        public static readonly Color Void = Hex(0x070506);
        public static readonly Color Soot = Hex(0x150F0D);
        public static readonly Color Ash = Hex(0x2A1E19);
        public static readonly Color RustDeep = Hex(0x4A2214);
        public static readonly Color Rust = Hex(0x8F3F1F);
        public static readonly Color RustHot = Hex(0xCE682E);
        public static readonly Color Bone = Hex(0xE8DCC0);
        public static readonly Color BoneDim = Hex(0x9A8D76);
        public static readonly Color Blood = Hex(0xD0112B);
        public static readonly Color BloodDeep = Hex(0x5E0612);
        public static readonly Color Amber = Hex(0xFFC066);
        public static readonly Color Ivory = Hex(0xFFF4DA);
        public static readonly Color WarmDark = Hex(0x1C1109);
        public static readonly Color Dawn = Hex(0xC9D6E6);
        public static readonly Color Indigo = Hex(0x10152E);
        public static readonly Color Ink = Hex(0x4A423B);
        public static readonly Color Abyss = Hex(0x06050A);      // the title sky, past the wormhole
        public static readonly Color Bruise = Hex(0x2E0E1A);     // the title sky at the horizon of TrES-2b
        public static readonly Color Clear = new Color(0, 0, 0, 0);

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        public static Color A(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// Fonts shipped under Resources/UI/Fonts. DotGothic16 is the house face (a 16px bitmap design that
    /// covers Japanese, accented Latin and Cyrillic); Unifont backs it up for every other script.
    /// </summary>
    public static class FontBook
    {
        static Font _pixel, _wide, _os;
        static readonly HashSet<Font> Hooked = new HashSet<Font>();
        static bool _listening;

        public static Font Pixel => _pixel ? _pixel : (_pixel = Load("UI/Fonts/DotGothic16"));
        public static Font Wide => _wide ? _wide : (_wide = Load("UI/Fonts/Unifont"));

        static Font Load(string path)
        {
            var f = Resources.Load<Font>(path);
            if (!f) { Debug.LogWarning("[UI] missing font " + path + ", falling back to the built-in font"); f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            Hook(f);
            return f;
        }

        // Dynamic font atlases are rebuilt as new characters arrive; keep them unfiltered.
        static void Hook(Font f)
        {
            if (!f) return;
            Hooked.Add(f);
            if (!_listening) { Font.textureRebuilt += OnRebuilt; _listening = true; }
            Crisp(f);
        }

        static void OnRebuilt(Font f) { if (Hooked.Contains(f)) Crisp(f); }

        static void Crisp(Font f)
        {
            if (f && f.material && f.material.mainTexture) f.material.mainTexture.filterMode = FilterMode.Point;
        }

        // Unity quietly borrows missing glyphs from fonts installed on the machine, so Font.HasCharacter
        // says yes to nearly everything. To know what the shipped file itself can draw, each font has a
        // 64K-bit coverage map beside it, generated from its cmap.
        static byte[] _pixelMap, _wideMap;

        static byte[] Map(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            return asset ? asset.bytes : null;
        }

        static bool InMap(byte[] map, char ch) => map == null || (ch >> 3) >= map.Length || (map[ch >> 3] & (1 << (ch & 7))) != 0;

        /// <summary>True when the font file itself (not an operating-system stand-in) has every character.</summary>
        public static bool Covers(Font f, string s)
        {
            if (!f || string.IsNullOrEmpty(s)) return true;
            byte[] map = null;
            if (f == Pixel) map = _pixelMap ?? (_pixelMap = Map("UI/Fonts/DotGothic16.coverage"));
            else if (f == Wide) map = _wideMap ?? (_wideMap = Map("UI/Fonts/Unifont.coverage"));
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch) || char.IsSurrogate(ch)) continue;
                if (map != null ? !InMap(map, ch) : !f.HasCharacter(ch)) return false;
            }
            return true;
        }

        /// <summary>The first shipped font that can draw every character of <paramref name="s"/>.</summary>
        public static Font For(string s)
        {
            if (Covers(Pixel, s)) return Pixel;
            if (Covers(Wide, s)) return Wide;
            if (!_os)
            {
                try { _os = Font.CreateDynamicFontFromOSFont(new[] { "Arial Unicode MS", "Noto Sans", "Segoe UI", "Arial" }, 16); Hook(_os); }
                catch (Exception) { _os = null; }
            }
            return _os && Covers(_os, s) ? _os : Wide;
        }

        public static int Width(string s, int size, Font f = null)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            f = f ? f : For(s);
            f.RequestCharactersInTexture(s, size, FontStyle.Normal);
            int w = 0;
            foreach (char ch in s)
                if (f.GetCharacterInfo(ch, out var ci, size, FontStyle.Normal)) w += ci.advance;
            return w;
        }
    }

    /// <summary>A clickable region, in the coordinate space of the stage.</summary>
    internal sealed class Hit
    {
        public RectTransform Rt;
        public Action OnClick;
        public Action<bool> OnHover;
        public bool Hovered;
    }

    /// <summary>Tiny uGUI factory. Everything is placed in whole pixels from the top-left of its parent.</summary>
    internal static class Kit
    {
        public const int UiLayer = 5;
        static Sprite _frame, _frameThick;

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            return rt;
        }

        public static RectTransform Node(string name, Transform parent, float x, float y, float w, float h)
        {
            var rt = Node(name, parent);
            Place(rt, x, y, w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
            rt.sizeDelta = new Vector2(Mathf.Round(w), Mathf.Round(h));
        }

        /// <summary>Moves the pivot to the middle without moving the element, so it can scale in place.</summary>
        public static void Center(RectTransform rt)
        {
            var size = rt.sizeDelta;
            rt.anchoredPosition += new Vector2(size.x * (0.5f - rt.pivot.x), size.y * (0.5f - rt.pivot.y));
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        public static void Move(RectTransform rt, float x, float y) => rt.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Node(name, parent);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Box(string name, Transform parent, Color c, float x, float y, float w, float h)
        {
            var rt = Node(name, parent, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        /// <summary>One-pixel (or two-pixel) outline with clipped corners, nine-sliced.</summary>
        public static Image Frame(string name, Transform parent, Color c, float x, float y, float w, float h, bool thick = false)
        {
            var img = Box(name, parent, c, x, y, w, h);
            img.sprite = thick ? (_frameThick ? _frameThick : (_frameThick = MakeFrame(2))) : (_frame ? _frame : (_frame = MakeFrame(1)));
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            return img;
        }

        static Sprite MakeFrame(int t)
        {
            const int n = 12;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int ex = Mathf.Min(x, n - 1 - x), ey = Mathf.Min(y, n - 1 - y);
                    bool corner = ex + ey < 2;                       // clip the corners
                    bool border = ex < t || ey < t || ex + ey < 2 + t;
                    px[y * n + x] = border && !corner ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px); tex.Apply(false);
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color c, TextAnchor anchor,
            float x, float y, float w, float h, bool wrap = false)
        {
            text = Rtl.Visual(text ?? "");
            var rt = Node(name, parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = FontBook.For(text);
            t.fontSize = size;
            t.color = c;
            t.alignment = anchor;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            t.lineSpacing = 1f;
            t.text = text ?? "";
            return t;
        }

        public static void SetText(Text t, string s)
        {
            s = Rtl.Visual(s ?? "");
            if (t.text == s) return;
            var f = FontBook.For(s);
            if (t.font != f)
            {
                float px = t.lineSpacing * NaturalLine(t);
                t.font = f;
                if (t.horizontalOverflow == HorizontalWrapMode.Wrap) Pitch(t, px);
            }
            t.text = s;
        }

        static float NaturalLine(Text t) =>
            t.font && t.font.lineHeight > 0 ? t.font.lineHeight * (t.fontSize / (float)Mathf.Max(1, t.font.fontSize)) : t.fontSize * 1.4f;

        /// <summary>
        /// Line pitch of a wrapped label, in pixels. The two shipped fonts have different natural line
        /// heights, so paragraphs set this rather than a line-spacing factor.
        /// </summary>
        public static Text Pitch(Text t, float px = 18f)
        {
            t.lineSpacing = px / NaturalLine(t);
            return t;
        }

        public static RawImage Pic(string name, Transform parent, Texture tex, Color c, float x, float y, float w, float h)
        {
            var rt = Node(name, parent, x, y, w, h);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex; img.color = c; img.raycastTarget = false;
            return img;
        }

        public static CanvasGroup Group(RectTransform rt)
        {
            var g = rt.GetComponent<CanvasGroup>();
            if (!g) g = rt.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false; g.interactable = false;
            return g;
        }

        public static void Kill(UnityEngine.Object o)
        {
            if (!o) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var ch = parent.GetChild(i);
                ch.gameObject.SetActive(false);
                ch.SetParent(null, false);
                Kill(ch.gameObject);
            }
        }

        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static float EaseOut(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float Ramp(float t, float start, float dur) => Mathf.Clamp01((t - start) / Mathf.Max(dur, 1e-4f));
        public static bool Blink(float t, float hz = 2f) => Mathf.Repeat(t * hz, 1f) < 0.55f;

        /// <summary>Word-wrap helper that measures with the real font, for laying text out by hand.</summary>
        public static int Lines(string s, int size, float width)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var f = FontBook.For(s);
            int lines = 1; float x = 0; float space = FontBook.Width(" ", size, f);
            foreach (var word in s.Split(' '))
            {
                float w = FontBook.Width(word, size, f);
                if (x > 0 && x + space + w > width) { lines++; x = w; }
                else x += (x > 0 ? space : 0) + w;
            }
            return lines;
        }
    }
}

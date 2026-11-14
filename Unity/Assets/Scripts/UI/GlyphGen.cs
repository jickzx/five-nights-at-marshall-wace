using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.UI
{
    /// <summary>
    /// Stroke style of an alien script. Each real-language family gets its own hand, so a
    /// Japanese-based tongue looks built from sealed chambers and a French-based one flows
    /// under exaggerated accents.
    /// </summary>
    public enum ScriptFamily { Block, Flow, Stem, Rune, Hook, Bar, Knot }

    /// <summary>A glyph as polylines in the unit square (y up), ready to rasterise at any size.</summary>
    public sealed class GlyphShape
    {
        public readonly List<Vector2[]> Strokes = new List<Vector2[]>();
        public float Width = 0.085f;

        public float Length()
        {
            float len = 0;
            foreach (var s in Strokes)
                for (int i = 1; i < s.Length; i++) len += Vector2.Distance(s[i - 1], s[i]);
            return len;
        }
    }

    /// <summary>
    /// Procedural alien writing. A word's <c>GlyphSeed</c> always produces the same glyph, so the
    /// player can match a tile to the example it came from. Pure logic: no scene objects.
    /// </summary>
    public static class GlyphGen
    {
        struct Rng
        {
            uint _s;
            public Rng(int seed) { _s = (uint)seed * 2654435761u + 0x9E3779B9u; if (_s == 0) _s = 1; Next(); Next(); }
            public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
            public int Range(int n) => (int)(Next() % (uint)n);
            public float F() => (Next() & 0xFFFFFF) / (float)0x1000000;
            public float F(float a, float b) => a + (b - a) * F();
            public bool Chance(float p) => F() < p;
        }

        /// <summary>Which hand a real language's alien script is written in.</summary>
        public static ScriptFamily FamilyFor(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            if (code.Length > 2 && code[2] == '-') code = code.Substring(0, 2);
            switch (code)
            {
                case "ja": case "zh": case "ko": case "yue": case "japanese": case "chinese": case "mandarin": case "korean":
                    return ScriptFamily.Block;
                case "fr": case "es": case "it": case "pt": case "ro": case "ca": case "la":
                case "french": case "spanish": case "italian": case "portuguese": case "latin":
                    return ScriptFamily.Flow;
                case "fi": case "et": case "hu": case "tr": case "az": case "mn":
                case "finnish": case "hungarian": case "turkish": case "estonian":
                    return ScriptFamily.Stem;
                case "de": case "en": case "nl": case "sv": case "no": case "nb": case "da": case "is":
                case "german": case "english": case "dutch": case "swedish": case "norwegian": case "icelandic":
                    return ScriptFamily.Rune;
                case "ar": case "he": case "fa": case "ur": case "arabic": case "hebrew": case "persian":
                    return ScriptFamily.Hook;
                case "hi": case "bn": case "ta": case "th": case "ne": case "sa": case "hindi": case "thai": case "bengali":
                    return ScriptFamily.Bar;
                case "ru": case "uk": case "pl": case "cs": case "bg": case "sr": case "el":
                case "russian": case "polish": case "greek": case "czech": case "ukrainian":
                    return ScriptFamily.Knot;
            }
            int h = 17;
            foreach (char ch in code) h = h * 31 + ch;
            return (ScriptFamily)(Mathf.Abs(h) % 7);
        }

        public static GlyphShape Build(int seed, ScriptFamily family)
        {
            var r = new Rng(seed ^ ((int)family * 0x3C6EF372));
            var g = new GlyphShape();
            switch (family)
            {
                case ScriptFamily.Block: Block(ref r, g); break;
                case ScriptFamily.Flow: Flow(ref r, g); break;
                case ScriptFamily.Stem: Stem(ref r, g); break;
                case ScriptFamily.Rune: Rune(ref r, g); break;
                case ScriptFamily.Hook: Hook(ref r, g); break;
                case ScriptFamily.Bar: Bar(ref r, g); break;
                default: Knot(ref r, g); break;
            }
            return g;
        }

        // ---- stroke helpers -------------------------------------------------------------

        static Vector2 V(float x, float y) => new Vector2(x, y);
        static void Line(GlyphShape g, params Vector2[] pts) => g.Strokes.Add(pts);

        static void Quad(GlyphShape g, Vector2 a, Vector2 ctrl, Vector2 b, int steps = 8)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1 - t;
                pts[i] = u * u * a + 2 * u * t * ctrl + t * t * b;
            }
            g.Strokes.Add(pts);
        }

        static void Ring(GlyphShape g, Vector2 c, float rad, int steps = 12)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
            }
            g.Strokes.Add(pts);
        }

        static void Dot(GlyphShape g, Vector2 p) => g.Strokes.Add(new[] { p, p + new Vector2(0.001f, 0) });

        // ---- the seven hands ------------------------------------------------------------

        // Sealed chambers: frames, bars and falling sweeps on a square lattice.
        static void Block(ref Rng r, GlyphShape g)
        {
            g.Width = 0.09f;
            float[] L = { 0.14f, 0.38f, 0.62f, 0.86f };
            switch (r.Range(6))
            {
                case 0: Line(g, V(L[0], L[3]), V(L[3], L[3])); break;                                     // roof
                case 1: Line(g, V(L[0], L[3]), V(L[0], L[0])); break;                                     // wall
                case 2: Line(g, V(L[0], L[0]), V(L[0], L[3]), V(L[3], L[3]), V(L[3], L[0])); break;       // gate
                case 3: Line(g, V(L[0], L[3]), V(L[3], L[3]), V(L[3], L[0])); break;                      // corner
                case 4: Line(g, V(L[0], L[3]), V(L[3], L[3]), V(L[3], L[1]), V(L[0], L[1]), V(L[0], L[3])); break; // chamber
                default: Line(g, V(0.5f, L[3]), V(0.5f, L[0])); Line(g, V(L[0], L[2]), V(L[3], L[2])); break;      // cross
            }
            int inner = 2 + r.Range(2);
            for (int i = 0; i < inner; i++)
            {
                switch (r.Range(6))
                {
                    case 0: { float y = L[r.Range(3)]; Line(g, V(L[r.Range(2)], y), V(L[2 + r.Range(2)], y)); break; }
                    case 1: { float x = L[1 + r.Range(2)]; Line(g, V(x, L[2 + r.Range(2)]), V(x, L[r.Range(2)])); break; }
                    case 2: Quad(g, V(L[2], L[2]), V(L[1] + 0.06f, L[1]), V(L[0], L[0]), 5); break;       // left sweep
                    case 3: Quad(g, V(L[1], L[2]), V(L[2], L[1]), V(L[3], L[0]), 5); break;               // right sweep
                    case 4: { float x = L[1 + r.Range(2)], y = L[1 + r.Range(2)]; Line(g, V(x, y), V(x + 0.1f, y - 0.1f)); break; } // tick
                    default: { float x = L[1 + r.Range(2)]; Line(g, V(x, L[2]), V(x, L[0] + 0.02f), V(x - 0.12f, L[0] + 0.12f)); break; } // hook
                }
            }
        }

        // Cursive on a baseline, crowned with oversized accents.
        static void Flow(ref Rng r, GlyphShape g)
        {
            g.Width = 0.075f;
            const float baseY = 0.26f, xh = 0.60f;
            int arcs = 2 + r.Range(2);
            float x = 0.12f, step = 0.76f / arcs;
            var p = V(x, r.Chance(0.5f) ? baseY : xh);
            for (int i = 0; i < arcs; i++)
            {
                bool up = Mathf.Approximately(p.y, baseY);
                var q = V(x + step, up ? xh : baseY);
                if (r.Chance(0.3f)) q.y = p.y;
                float bulge = r.F(0.12f, 0.3f) * (up ? 1 : -1);
                Quad(g, p, V(x + step * (up ? 0.15f : 0.85f), (p.y + q.y) * 0.5f + bulge), q, 6);
                p = q; x += step;
            }
            if (r.Chance(0.45f)) Ring(g, V(r.F(0.3f, 0.7f), baseY + 0.13f), 0.11f, 9);
            if (r.Chance(0.35f)) Quad(g, V(0.5f, baseY), V(0.56f, 0.12f), V(0.42f, 0.08f), 4);            // cedilla
            float ax = r.F(0.32f, 0.68f);
            switch (r.Range(5))
            {
                case 0: Line(g, V(ax - 0.08f, 0.76f), V(ax + 0.08f, 0.92f)); break;                         // acute
                case 1: Line(g, V(ax - 0.08f, 0.92f), V(ax + 0.08f, 0.76f)); break;                         // grave
                case 2: Line(g, V(ax - 0.12f, 0.76f), V(ax, 0.92f), V(ax + 0.12f, 0.76f)); break;           // circumflex
                case 3: Line(g, V(ax - 0.15f, 0.80f), V(ax - 0.05f, 0.90f), V(ax + 0.05f, 0.80f), V(ax + 0.15f, 0.90f)); break; // tilde
                default: Dot(g, V(ax - 0.1f, 0.86f)); Dot(g, V(ax + 0.1f, 0.86f)); break;                   // diaeresis
            }
        }

        // Tall stems chained together and dotted: long words built by stacking endings.
        static void Stem(ref Rng r, GlyphShape g)
        {
            g.Width = 0.085f;
            int stems = 2 + r.Range(2);
            float gap = 0.72f / (stems - 1 + 0.0001f);
            var xs = new float[stems];
            for (int i = 0; i < stems; i++)
            {
                xs[i] = 0.14f + gap * i;
                float top = r.Chance(0.6f) ? 0.68f : 0.5f, bot = r.Chance(0.75f) ? 0.12f : 0.3f;
                Line(g, V(xs[i], top), V(xs[i], bot));
                if (r.Chance(0.55f)) { Dot(g, V(xs[i] - 0.07f, 0.86f)); Dot(g, V(xs[i] + 0.07f, 0.86f)); }
                else if (r.Chance(0.25f)) Ring(g, V(xs[i], 0.86f), 0.06f, 7);
            }
            for (int i = 1; i < stems; i++)
                if (r.Chance(0.8f)) { float y = 0.18f + 0.16f * r.Range(4); Line(g, V(xs[i - 1], y), V(xs[i], y)); }
            if (g.Strokes.Count == stems) Line(g, V(xs[0], 0.4f), V(xs[stems - 1], 0.4f));
        }

        // A stem with angular branches, cut rather than written.
        static void Rune(ref Rng r, GlyphShape g)
        {
            g.Width = 0.085f;
            float sx = r.Chance(0.5f) ? 0.3f : 0.5f;
            Line(g, V(sx, 0.88f), V(sx, 0.12f));
            int branches = 2 + r.Range(2);
            for (int i = 0; i < branches; i++)
            {
                float y = 0.2f + 0.2f * r.Range(4);
                float len = r.F(0.22f, 0.36f);
                float dir = r.Chance(0.75f) ? 1 : -1;
                float rise = r.Chance(0.5f) ? len : -len;
                var tip = V(Mathf.Clamp(sx + dir * len, 0.1f, 0.9f), Mathf.Clamp(y + rise, 0.1f, 0.9f));
                if (r.Chance(0.3f)) Line(g, V(sx, y), tip, V(sx, Mathf.Clamp(y + rise * 2, 0.1f, 0.9f)));
                else Line(g, V(sx, y), tip);
            }
            if (r.Chance(0.35f)) Line(g, V(0.76f, 0.88f), V(0.76f, 0.12f));
        }

        // A long sweep along the line with a bowl at its end, and dots that change the word.
        static void Hook(ref Rng r, GlyphShape g)
        {
            g.Width = 0.08f;
            float y = r.F(0.36f, 0.5f);
            Quad(g, V(0.88f, y + r.F(0f, 0.2f)), V(0.6f, y - 0.06f), V(0.34f, y), 6);
            Quad(g, V(0.34f, y), V(0.06f, y - 0.02f), V(0.14f, y - 0.26f), 6);
            if (r.Chance(0.6f)) Quad(g, V(0.14f, y - 0.26f), V(0.3f, y - 0.36f), V(0.44f, y - 0.24f), 4);
            if (r.Chance(0.5f)) Line(g, V(r.F(0.55f, 0.8f), 0.9f), V(r.F(0.55f, 0.8f), y + 0.02f));
            int dots = 1 + r.Range(3);
            bool above = r.Chance(0.6f);
            float dx = r.F(0.35f, 0.6f);
            for (int i = 0; i < dots; i++)
                Dot(g, V(dx + (i - (dots - 1) * 0.5f) * 0.14f, above ? y + 0.24f + (i == 1 && dots == 3 ? 0.1f : 0) : y - 0.3f));
        }

        // Everything hangs from a headline.
        static void Bar(ref Rng r, GlyphShape g)
        {
            g.Width = 0.085f;
            Line(g, V(0.1f, 0.84f), V(0.9f, 0.84f));
            float sx = r.F(0.5f, 0.74f);
            Line(g, V(sx, 0.84f), V(sx, r.Chance(0.7f) ? 0.12f : 0.34f));
            switch (r.Range(4))
            {
                case 0: Quad(g, V(sx, 0.62f), V(0.08f, 0.66f), V(0.2f, 0.36f), 6); Quad(g, V(0.2f, 0.36f), V(0.3f, 0.2f), V(sx, 0.36f), 5); break;
                case 1: Ring(g, V(sx - 0.24f, 0.46f), 0.14f, 10); Line(g, V(sx - 0.1f, 0.46f), V(sx, 0.46f)); break;
                case 2: Line(g, V(0.24f, 0.84f), V(0.24f, 0.5f)); Quad(g, V(0.24f, 0.5f), V(0.3f, 0.3f), V(sx, 0.5f), 5); break;
                default: Quad(g, V(sx, 0.56f), V(0.3f, 0.7f), V(0.16f, 0.5f), 5); Line(g, V(0.16f, 0.5f), V(0.34f, 0.2f)); break;
            }
            if (r.Chance(0.4f)) Quad(g, V(sx, 0.84f), V(sx + 0.14f, 0.99f), V(sx - 0.2f, 0.96f), 4);
            if (r.Chance(0.4f)) Line(g, V(sx + 0.1f, 0.3f), V(sx + 0.16f, 0.12f));
        }

        // Rings and wedges tied together.
        static void Knot(ref Rng r, GlyphShape g)
        {
            g.Width = 0.075f;
            int rings = 1 + r.Range(2);
            var c0 = V(r.F(0.28f, 0.4f), r.F(0.58f, 0.7f));
            Ring(g, c0, 0.15f, 10);
            var c1 = V(r.F(0.6f, 0.72f), r.F(0.26f, 0.4f));
            if (rings == 2) Ring(g, c1, 0.13f, 10);
            else Line(g, c1 + V(-0.14f, -0.12f), c1 + V(0f, 0.14f), c1 + V(0.14f, -0.12f), c1 + V(-0.14f, -0.12f));
            Line(g, c0 + V(0.1f, -0.11f), c1 + V(-0.09f, 0.1f));
            switch (r.Range(4))
            {
                case 0: Line(g, V(0.12f, 0.14f), V(c0.x, c0.y - 0.15f)); break;
                case 1: Line(g, V(0.88f, 0.86f), V(c1.x, c1.y + 0.14f)); break;
                case 2: Line(g, V(0.14f, 0.88f), V(0.86f, 0.88f)); break;
                default: Line(g, V(c0.x, c0.y + 0.15f), V(c0.x, 0.92f)); Line(g, V(c0.x - 0.1f, 0.84f), V(c0.x + 0.1f, 0.84f)); break;
            }
        }

        // ---- rasteriser -----------------------------------------------------------------

        /// <summary>
        /// Hard-edged alpha mask, row 0 at the bottom. <paramref name="progress"/> below 1 draws only the
        /// first part of the strokes, so a glyph can be written on stroke by stroke.
        /// </summary>
        public static byte[] Raster(GlyphShape g, int size, float progress = 1f)
        {
            var mask = new byte[size * size];
            float half = Mathf.Max(g.Width * 0.5f, 0.62f / size);
            float budget = g.Length() * Mathf.Clamp01(progress) + (progress >= 1f ? 1f : 0f);
            foreach (var s in g.Strokes)
            {
                for (int i = 1; i < s.Length; i++)
                {
                    var a = s[i - 1]; var b = s[i];
                    float len = Vector2.Distance(a, b);
                    if (budget <= 0) return mask;
                    if (len > budget) b = Vector2.Lerp(a, b, budget / Mathf.Max(len, 1e-5f));
                    budget -= len;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - half) * size));
                    int x1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + half) * size));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - half) * size));
                    int y1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + half) * size));
                    var ab = b - a; float abLen2 = Mathf.Max(ab.sqrMagnitude, 1e-8f);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLen2);
                            if ((a + ab * t - p).sqrMagnitude <= half * half) mask[y * size + x] = 255;
                        }
                }
            }
            return mask;
        }

        // ---- textures -------------------------------------------------------------------

        static readonly Dictionary<(int, ScriptFamily, int), Texture2D> Cache = new Dictionary<(int, ScriptFamily, int), Texture2D>();

        /// <summary>Cached white-on-clear texture for a glyph. Point filtered: these are pixels.</summary>
        public static Texture2D Texture(int seed, ScriptFamily family, int size)
        {
            var key = (seed, family, size);
            if (Cache.TryGetValue(key, out var tex) && tex) return tex;
            tex = NewTexture(size);
            Paint(tex, Build(seed, family), 1f);
            Cache[key] = tex;
            return tex;
        }

        public static Texture2D NewTexture(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
        };

        public static void Paint(Texture2D tex, GlyphShape shape, float progress)
        {
            int size = tex.width;
            var mask = Raster(shape, size, progress);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, mask[i]);
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        public static void ClearCache()
        {
            foreach (var t in Cache.Values) if (t) { if (Application.isPlaying) UnityEngine.Object.Destroy(t); else UnityEngine.Object.DestroyImmediate(t); }
            Cache.Clear();
        }

        /// <summary>Stable seed for a word that has no tile (so the same word always draws the same glyph).</summary>
        public static int SeedFor(string word)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char ch in word ?? "") h = (h ^ ch) * 16777619;
                return h;
            }
        }
    }
}

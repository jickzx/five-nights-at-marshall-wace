using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// Every line of the opening's copy, in one place, so the concept (ship, wormhole, TrES-2b under a
    /// black hole) can be reworded or reverted without touching layout.
    /// </summary>
    internal static class TitleCopy
    {
        public const string Over = "F I V E   N I G H T S   A T";
        public const string Name = "MARSHALL WACE";
        public const string Target = "TrES-2b  ·  BEYOND THE WORMHOLE  ·  UNCHARTED";
        public const string Line = "A hole in the sky. A dead tongue. Wake the ship before it wakes.";
        public const string Cue = "BEGIN TRANSIT";
        public const string EscapeLine = "The ship tears free of TrES-2b and its blood sea.";
    }

    /// <summary>
    /// Title: the far side of the wormhole. A black hole hangs over TrES-2b, its light bent into a ring;
    /// a ship falls toward a ridge of alien spires above the blood sea, and a line of writing nobody can
    /// read yet keeps rewriting itself. Everything is drawn from code at the stage's pixel scale.
    /// </summary>
    internal sealed class TitleScreen : UiScreen
    {
        Action _onStart;
        readonly List<RawImage> _glyphs = new List<RawImage>();
        readonly List<Image> _stars = new List<Image>();
        readonly List<Vector3> _orbits = new List<Vector3>();      // radius, angle, speed (orbiting stars)
        readonly List<Image> _trail = new List<Image>();
        RectTransform _cue, _sky, _ship, _replay;
        RawImage _hole;
        float _swapT;
        int _seed = 7;

        public const string ReplayText = "T  \u00B7  REPLAY TUTORIAL";
        public const string ReplayTextXr = "Y  \u00B7  REPLAY TUTORIAL";   // Y tapped on the title (Fnmw.XR)

        const int HoleTex = 112;
        static readonly Vector2 HoleAt = new Vector2(UiRig.StageW / 2f, 50f);   // centre, stage pixels from top-left
        static readonly Vector2 ShipFrom = new Vector2(372, 58), ShipTo = new Vector2(566, 284);
        static Texture2D _skyTex, _holeTex, _ridgeTex;

        public TitleScreen(GameUI ui) : base(ui, "Title") { }
        public override GlassMode Glass => GlassMode.Title;

        public void Show(Action onStart)
        {
            Open();
            _onStart = onStart;
            _glyphs.Clear(); _stars.Clear(); _orbits.Clear(); _trail.Clear();

            BuildBackdrop();

            // stars: a still field, plus a few caught in orbit close to the hole
            var rng = new System.Random(23);
            for (int i = 0; i < 70; i++)
            {
                float x = rng.Next(4, UiRig.StageW - 4), y = rng.Next(4, 280);
                if (Vector2.Distance(new Vector2(x, y), HoleAt) < 46) continue;
                int s = i % 11 == 0 ? 2 : 1;
                _stars.Add(Kit.Box("Star", Root, Pal.Bone, x, y, s, s));
                _orbits.Add(Vector3.zero);
            }
            for (int i = 0; i < 7; i++)
            {
                float r = 30f + i * 6f + (float)rng.NextDouble() * 4f;
                _stars.Add(Kit.Box("Orbit", Root, Pal.Amber, 0, 0, 1, 1));
                _orbits.Add(new Vector3(r, (float)rng.NextDouble() * Mathf.PI * 2f, 1400f / (r * Mathf.Sqrt(r))));
            }

            _hole = Kit.Pic("BlackHole", Root, HoleTexture(), Color.white, HoleAt.x - HoleTex / 2f, HoleAt.y - HoleTex / 2f, HoleTex, HoleTex);

            for (int i = 0; i < 5; i++) _trail.Add(Kit.Box("Trail", Root, Pal.Amber, 0, 0, 1, 1));
            _ship = Kit.Pic("Ship", Root, Pictograms.Get("ship"), Pal.Bone, ShipFrom.x - 6, ShipFrom.y - 6, 13, 13).rectTransform;
            Kit.Center(_ship);
            var dir = ShipTo - ShipFrom;
            _ship.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg - 90f);

            Kit.Label("Over", Root, TitleCopy.Over, 16, Pal.RustHot, TextAnchor.MiddleCenter, 20, 106, 600, 16);
            Kit.Label("Name", Root, TitleCopy.Name, 48, Pal.Bone, TextAnchor.MiddleCenter, 20, 122, 600, 48);
            Kit.Box("Rule", Root, Pal.Rust, 164, 176, 312, 1);
            Kit.Label("Target", Root, TitleCopy.Target, 16, Pal.BoneDim, TextAnchor.MiddleCenter, 20, 182, 600, 16);
            const int n = 9;
            float gx = (UiRig.StageW - (n * 24 + (n - 1) * 8)) / 2f;
            for (int i = 0; i < n; i++)
            {
                _glyphs.Add(Kit.Pic("Glyph", Root, GlyphGen.Texture(_seed++ * 7919, (ScriptFamily)(i % 7), 24), Pal.Rust, gx, 206, 24, 24));
                gx += 32;
            }
            Kit.Label("Line", Root, TitleCopy.Line, 16, Pal.BoneDim, TextAnchor.MiddleCenter, 20, 238, 600, 16);
            _cue = Widgets.Cue(Root, 264, TitleCopy.Cue, Pal.Bone);
            // once the tutorial has been seen (or skipped) this session, it can be asked for again
            _replay = null;
            if (TutorialSession.Seen)
            {
                // top right corner, clear of the black hole and the title
                _replay = Kit.Node("Replay", Root, 0, 14, UiRig.StageW, 18);
                string replay = XrBridge.Active ? ReplayTextXr : ReplayText;
                string rest = replay.Substring(1);              // the T (or Y) is drawn as a keycap
                float tw = FontBook.Width(rest, 16);
                float rx = UiRig.StageW - 24 - (18 + tw);
                Kit.Frame("Cap", _replay, Pal.BoneDim, rx, 0, 18, 18);
                Kit.Label("Key", _replay, replay.Substring(0, 1), 16, Pal.BoneDim, TextAnchor.MiddleCenter, rx, 1, 18, 16);
                Kit.Label("Text", _replay, rest, 16, Pal.BoneDim, TextAnchor.MiddleLeft, rx + 18, 1, tw + 4, 16);
            }
            Tick(0, UiInput.None);
        }

        /// <summary>Sky and horizon cover the whole glass, not just the 640x360 stage.</summary>
        void BuildBackdrop()
        {
            KillBackdrop();
            _sky = Kit.Stretch("TitleSky", Ui.Rig.Backdrop);
            var sky = _sky.gameObject.AddComponent<RawImage>();
            sky.texture = SkyTexture(); sky.raycastTarget = false;

            var ridge = Kit.Node("Ridge", _sky);
            ridge.anchorMin = new Vector2(0, 0); ridge.anchorMax = new Vector2(1, 0); ridge.pivot = new Vector2(0.5f, 0);
            ridge.sizeDelta = new Vector2(0, 84); ridge.anchoredPosition = Vector2.zero;
            var img = ridge.gameObject.AddComponent<RawImage>();
            img.texture = RidgeTexture(); img.raycastTarget = false;
        }

        void KillBackdrop()
        {
            if (_sky) { Kit.Kill(_sky.gameObject); _sky = null; }
        }

        public override void Close()
        {
            base.Close();
            KillBackdrop();
        }

        protected override void OnTick(float dt, UiInput input)
        {
            // the unreadable line keeps rewriting itself
            _swapT += dt;
            if (_swapT > 0.28f && _glyphs.Count > 0)
            {
                _swapT = 0;
                int i = UnityEngine.Random.Range(0, _glyphs.Count);
                _glyphs[i].texture = GlyphGen.Texture(_seed++ * 7919, (ScriptFamily)(_seed % 7), 24);
                _glyphs[i].color = UnityEngine.Random.value < 0.2f ? Pal.Blood : Pal.Rust;
            }

            for (int i = 0; i < _stars.Count; i++)
            {
                var o = _orbits[i];
                if (o.x > 0)
                {
                    float a = o.y + T * o.z * 0.05f;
                    // the orbit is a flattened ellipse, in the plane of the disk
                    Kit.Move(_stars[i].rectTransform, HoleAt.x + Mathf.Cos(a) * o.x, HoleAt.y + Mathf.Sin(a) * o.x * 0.3f);
                    _stars[i].enabled = Mathf.Sin(a) < 0 || Mathf.Abs(Mathf.Cos(a)) * o.x > 20f;   // hidden behind the hole
                    _stars[i].color = Pal.Amber.A(0.5f + 0.5f * Mathf.Abs(Mathf.Cos(a)));
                }
                else _stars[i].color = Pal.Bone.A(0.25f + 0.55f * Mathf.Abs(Mathf.Sin(T * (0.3f + (i % 7) * 0.13f) + i * 1.7f)));
            }

            // the bent light breathes, slowly
            if (_hole) { float b = 0.86f + 0.14f * Mathf.Sin(T * 0.9f); _hole.color = new Color(b, b, b, 1f); }

            // a ship falls toward the planet, again and again, gathering speed
            float k = Mathf.Repeat(T + 2f, 15f) / 10f;
            bool flying = k <= 1f;
            if (_ship) _ship.gameObject.SetActive(flying);
            float e = k * k;
            for (int i = 0; i < _trail.Count; i++)
            {
                float te = Mathf.Max(0, k - (i + 1) * 0.025f);
                var p = Vector2.Lerp(ShipFrom, ShipTo, te * te);
                Kit.Move(_trail[i].rectTransform, p.x, p.y);
                _trail[i].enabled = flying && k > (i + 1) * 0.025f;
                _trail[i].color = Pal.Amber.A(0.55f * (1f - i / (float)_trail.Count) * (0.6f + 0.4f * e));
            }
            if (flying && _ship)
            {
                var p = Vector2.Lerp(ShipFrom, ShipTo, e);
                _ship.anchoredPosition = new Vector2(Mathf.Round(p.x), -Mathf.Round(p.y));
            }

            if (_cue) _cue.gameObject.SetActive(Kit.Blink(T, 1.2f));
            if (_replay)
            {
                var g = Kit.Group(_replay);
                g.alpha = Kit.Ramp(T, 0.8f, 0.5f) * (0.7f + 0.3f * Mathf.PingPong(T * 0.8f, 1f));
            }
            if (T > 0.25f && _replay && input.Replay)
            {
                TutorialSession.ReplayRequested = true;
                var cb = _onStart; _onStart = null; cb?.Invoke();
                return;
            }
            if (T > 0.25f && (input.Confirm || input.Click)) { var cb = _onStart; _onStart = null; cb?.Invoke(); }
        }

        // ---- procedural art ---------------------------------------------------------------

        static Texture2D NewTex(int w, int h, FilterMode f) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = f, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        static Texture2D SkyTexture()
        {
            if (_skyTex) return _skyTex;
            const int n = 90;
            _skyTex = NewTex(1, n, FilterMode.Bilinear);
            var stops = new[] { (0f, Pal.Abyss), (0.55f, Pal.Hex(0x0C0913)), (0.82f, Pal.Hex(0x1E0C18)), (1f, Pal.Bruise) };
            for (int y = 0; y < n; y++)
            {
                float t = 1f - y / (float)(n - 1);     // 0 at the top of the screen
                Color c = stops[stops.Length - 1].Item2;
                for (int i = 1; i < stops.Length; i++)
                    if (t <= stops[i].Item1) { c = Color.Lerp(stops[i - 1].Item2, stops[i].Item2, Mathf.InverseLerp(stops[i - 1].Item1, stops[i].Item1, t)); break; }
                _skyTex.SetPixel(0, y, c);
            }
            _skyTex.Apply(false);
            return _skyTex;
        }

        /// <summary>
        /// The black hole: a lightless disk, a thin photon ring, the far side of the accretion disk bent up
        /// over the top, and the near side crossing in front. The approaching side (left) burns brighter.
        /// </summary>
        static Texture2D HoleTexture()
        {
            if (_holeTex) return _holeTex;
            const int n = HoleTex;
            const float R = 16.5f;
            _holeTex = NewTex(n, n, FilterMode.Point);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f - n / 2f) / R, v = (y + 0.5f - n / 2f) / R;   // v up
                    float r = Mathf.Sqrt(u * u + v * v);

                    // accretion disk, a thin tilted ellipse
                    float ed = Mathf.Sqrt(u * u + (v / 0.2f) * (v / 0.2f));
                    float disk = 0f;
                    if (ed > 1.45f && ed < 3.1f)
                    {
                        float d = (ed - 1.45f) / 1.65f;
                        disk = Mathf.Pow(1f - d, 1.6f) * Mathf.Clamp01((ed - 1.45f) / 0.12f) * Mathf.Clamp(1f - 0.5f * u / 3.1f, 0.4f, 1.5f);
                    }
                    Color diskC = DiskColor(ed);

                    if (r < 1f)
                    {
                        // event horizon: only the near half of the disk is in front of it
                        Color c = Color.black;
                        if (v < 0 && disk > 0) c = Color.Lerp(c, diskC, Mathf.Clamp01(disk * 1.3f));
                        px[y * n + x] = new Color(c.r, c.g, c.b, 1f);
                        continue;
                    }

                    float ring = Mathf.Exp(-Sq((r - 1.07f) / 0.05f));
                    float arc = Mathf.Exp(-Sq((r - 1.3f) / 0.14f)) * (v > 0 ? 0.85f : 0.3f) * Mathf.Clamp(1f - 0.35f * u, 0.5f, 1.4f);
                    float halo = Mathf.Exp(-Sq((r - 1f) / 0.8f)) * 0.28f;
                    if (v > 0 && r < 1.6f) disk *= Mathf.Clamp01((r - 1.0f) / 0.6f);   // the far side dives under the lensed arc

                    Color sum = Pal.Ivory * ring + Pal.Amber * arc + Pal.RustDeep * halo * 2f + diskC * disk;
                    float a = Mathf.Clamp01(Mathf.Max(ring, Mathf.Max(arc, Mathf.Max(halo, disk))));
                    // quantise alpha a little, so it reads as pixel art rather than a smooth glow
                    a = Mathf.Round(a * 8f) / 8f;
                    if (a <= 0f) { px[y * n + x] = Pal.Clear; continue; }
                    // keep the hue at full brightness and let alpha carry the falloff
                    float m = Mathf.Max(0.001f, Mathf.Max(sum.r, Mathf.Max(sum.g, sum.b)));
                    var col = sum / m;
                    px[y * n + x] = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), a);
                }
            _holeTex.SetPixels(px);
            _holeTex.Apply(false);
            return _holeTex;
        }

        static Color DiskColor(float ed)
        {
            float d = Mathf.InverseLerp(1.45f, 3.1f, ed);
            if (d < 0.2f) return Color.Lerp(Pal.Ivory, Pal.Amber, d / 0.2f);
            if (d < 0.55f) return Color.Lerp(Pal.Amber, Pal.RustHot, (d - 0.2f) / 0.35f);
            return Color.Lerp(Pal.RustHot, Pal.Blood, (d - 0.55f) / 0.45f);
        }

        static float Sq(float x) => x * x;

        /// <summary>The horizon of TrES-2b: a low broken ridge, thin spires leaning at the edges, rim-lit by the disk.</summary>
        static Texture2D RidgeTexture()
        {
            if (_ridgeTex) return _ridgeTex;
            const int w = 640, h = 84;
            _ridgeTex = NewTex(w, h, FilterMode.Point);
            var top = new int[w];
            var rng = new System.Random(5);
            for (int x = 0; x < w; x++)
                top[x] = Mathf.RoundToInt(48 + 7f * Mathf.Sin(x * 0.019f + 1f) + 5f * Mathf.Sin(x * 0.053f + 2.3f) + 2.5f * Mathf.Sin(x * 0.14f + 0.4f));
            // spires: thin, slightly leaning, mostly toward the edges so the middle stays clear for text
            for (int s = 0; s < 14; s++)
            {
                bool left = s % 2 == 0;
                int cx = left ? rng.Next(8, 150) : rng.Next(490, 632);
                int half = rng.Next(2, 6);
                int height = rng.Next(14, 40);
                float lean = (float)(rng.NextDouble() - 0.5) * 0.5f;
                int foot = top[Mathf.Clamp(cx, 0, w - 1)];
                for (int dy = 0; dy < height; dy++)
                {
                    float f = 1f - dy / (float)height;
                    int hw = Mathf.Max(0, Mathf.RoundToInt(half * f));
                    int c = cx + Mathf.RoundToInt(lean * dy);
                    for (int x = c - hw; x <= c + hw; x++)
                        if (x >= 0 && x < w) top[x] = Mathf.Min(top[x], foot - dy);
                }
            }
            var rock = Pal.Hex(0x0A0608);
            var px = new Color[w * h];
            for (int x = 0; x < w; x++)
                for (int row = 0; row < h; row++)          // row 0 is the top of the texture
                {
                    int y = h - 1 - row;
                    Color c = Pal.Clear;
                    int t = Mathf.Max(0, top[x]);
                    if (row == t) c = Pal.Rust.A(0.9f);
                    else if (row == t + 1) c = Color.Lerp(rock, Pal.RustDeep, 0.6f);
                    else if (row > t) c = rock;
                    px[y * w + x] = c;
                }
            _ridgeTex.SetPixels(px);
            _ridgeTex.Apply(false);
            return _ridgeTex;
        }
    }
}

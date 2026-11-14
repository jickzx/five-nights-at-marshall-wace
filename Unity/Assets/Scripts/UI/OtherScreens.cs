using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>Title: the name on dead glass, a line of writing nobody can read yet, and the tide at the bottom.</summary>
    internal sealed class TitleScreen : UiScreen
    {
        Action _onStart;
        readonly List<RawImage> _glyphs = new List<RawImage>();
        RectTransform _cue;
        float _swapT;
        int _seed = 7;

        public TitleScreen(GameUI ui) : base(ui, "Title") { }
        public override GlassMode Glass => GlassMode.Title;

        public void Show(Action onStart)
        {
            Open();
            _onStart = onStart;
            _glyphs.Clear();
            Kit.Label("Over", Root, "F I V E   N I G H T S   A T", 16, Pal.RustHot, TextAnchor.MiddleCenter, 20, 84, 600, 16);
            Kit.Label("Name", Root, "MARSHALL WACE", 48, Pal.Bone, TextAnchor.MiddleCenter, 20, 106, 600, 48);
            Kit.Box("Rule", Root, Pal.Rust, 164, 166, 312, 1);
            const int n = 9;
            float x = (UiRig.StageW - (n * 24 + (n - 1) * 8)) / 2f;
            for (int i = 0; i < n; i++)
            {
                _glyphs.Add(Kit.Pic("Glyph", Root, GlyphGen.Texture(_seed++ * 7919, (ScriptFamily)(i % 7), 24), Pal.Rust, x, 180, 24, 24));
                x += 32;
            }
            Kit.Label("Line", Root, "Read the dead tongue. Wake the ship. The tide is rising.", 16, Pal.BoneDim, TextAnchor.MiddleCenter, 20, 216, 600, 16);
            _cue = Widgets.Cue(Root, 258, "BEGIN", Pal.Bone);
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
            if (_cue) _cue.gameObject.SetActive(Kit.Blink(T, 1.2f));
            if (T > 0.25f && (input.Confirm || input.Click)) { var cb = _onStart; _onStart = null; cb?.Invoke(); }
        }
    }

    /// <summary>
    /// The visor while roaming. Almost nothing is written on it: a prompt when something can be used,
    /// the ship's power cells, and claw marks that appear as the beast changes. Threat itself is drawn
    /// by the glass (CrtFx), not here.
    /// </summary>
    internal sealed class HudLayer : UiScreen
    {
        RectTransform _prompt, _power;
        Text _promptText;
        Image _promptFrame, _promptFill;
        readonly List<Image> _cells = new List<Image>();
        readonly List<Image> _claws = new List<Image>();
        string _promptShown;
        int _solved = -1, _total = -1, _stage = 1;
        float _cellFlash, _clawT = 9f;

        public HudLayer(GameUI ui) : base(ui, "Hud") { }
        public override GlassMode Glass => GlassMode.Visor;
        public string PromptShown => _promptShown;

        public void Build()
        {
            Open();
            _prompt = Kit.Node("Prompt", Root, 0, 232, 100, 28);
            _promptFill = Kit.Box("Fill", _prompt, Pal.Void.A(0.72f), 0, 0, 100, 28);
            _promptFrame = Kit.Frame("Frame", _prompt, Pal.Bone, 0, 0, 100, 28);
            _promptText = Kit.Label("Text", _prompt, "", 16, Pal.Bone, TextAnchor.MiddleCenter, 0, 6, 100, 16);
            _prompt.gameObject.SetActive(false);
            _power = Kit.Node("Power", Root, 34, 316, 200, 16);
            for (int i = 0; i < 3; i++)
            {
                var claw = Kit.Box("Claw", Root, Pal.Blood, 548 + i * 16, 22, 3, 46);
                claw.rectTransform.localRotation = Quaternion.Euler(0, 0, -18);
                claw.enabled = false;
                _claws.Add(claw);
            }
            _promptShown = null; _solved = -1; _total = -1;
        }

        public void SetPrompt(string text)
        {
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToUpperInvariant();
            if (text == _promptShown) return;
            _promptShown = text;
            if (!_prompt) return;
            _prompt.gameObject.SetActive(text != null);
            if (text == null) return;
            float w = Mathf.Ceil((FontBook.Width(text, 16) + 28) / 2f) * 2f;
            Kit.Place(_prompt, (UiRig.StageW - w) / 2f, 232, w, 28);
            _promptFill.rectTransform.sizeDelta = new Vector2(w, 28);
            _promptFrame.rectTransform.sizeDelta = new Vector2(w, 28);
            _promptText.rectTransform.sizeDelta = new Vector2(w, 16);
            Kit.SetText(_promptText, text);
        }

        public void StageChanged(int stage) { _stage = stage; _clawT = 0; }

        protected override void OnTick(float dt, UiInput input)
        {
            var s = Ui.State ?? Services.State;
            if (s != null && (s.PuzzlesSolved != _solved || s.TotalPuzzles != _total))
            {
                if (_solved >= 0 && s.PuzzlesSolved > _solved) _cellFlash = 1f;
                _solved = s.PuzzlesSolved; _total = s.TotalPuzzles;
                Kit.Clear(_power); _cells.Clear();
                Widgets.Power(_power, 0, 0, _solved, _total, Pal.Bone, Pal.Rust.A(0.7f), _cells);
            }
            _cellFlash = Mathf.MoveTowards(_cellFlash, 0, dt * 0.6f);
            if (_solved > 0 && _solved <= _cells.Count)
                _cells[_solved - 1].color = Color.Lerp(Pal.Bone, Pal.Amber, _cellFlash * Mathf.PingPong(T * 6f, 1f));

            if (_promptFrame) _promptFrame.color = Pal.Bone.A(0.55f + 0.45f * Mathf.PingPong(T * 1.6f, 1f));

            _clawT += dt;
            for (int i = 0; i < _claws.Count; i++)
            {
                bool on = i < _stage - 1;
                _claws[i].enabled = on;
                if (!on) continue;
                bool fresh = i == _stage - 2;
                float a = fresh ? Mathf.Lerp(1f, 0.55f, Kit.Ramp(_clawT, 0.4f, 2f)) : 0.55f;
                float len = fresh ? 46f * Kit.EaseOut(Kit.Ramp(_clawT, i * 0.05f, 0.18f)) : 46f;
                _claws[i].color = Pal.Blood.A(a);
                _claws[i].rectTransform.sizeDelta = new Vector2(3, len);
            }
        }
    }

    /// <summary>Death: no gore, no score. The world goes slowly white and one line is left on it.</summary>
    internal sealed class DeathScreen : UiScreen
    {
        Action _onRestart;
        Text _line;
        RawImage _key;

        public DeathScreen(GameUI ui) : base(ui, "Death") { }
        public override GlassMode Glass => GlassMode.Death;
        public const string Line = "The tide takes you. Your words remain.";

        public void Show(Action onRestart)
        {
            Open();
            _onRestart = onRestart;
            _line = Kit.Label("Line", Root, Line, 16, Pal.Ivory, TextAnchor.MiddleCenter, 20, 170, 600, 16);
            _line.enabled = false;
            _key = Kit.Pic("Key", Root, Pictograms.Get("enter"), Pal.Ivory, 313, 204, 13, 13);
            _key.enabled = false;
            Ui.Fx.WhiteTarget = 1f;
            Ui.Fx.InkTarget = 0f;
        }

        protected override void OnTick(float dt, UiInput input)
        {
            Ui.Fx.WhiteTarget = 1f;
            if (T > 4.6f) { _line.enabled = true; Ui.Fx.InkTarget = 1f; }
            _key.enabled = T > 9f && Mathf.Repeat(T, 2.4f) < 1.2f;
            if (T > 3f && (input.Confirm || input.Click)) { var cb = _onRestart; _onRestart = null; cb?.Invoke(); }
        }
    }

    /// <summary>Escape: the first cool colours of the game. Dawn over the blood sea, the ship leaving, and what you carry out.</summary>
    internal sealed class EscapeScreen : UiScreen
    {
        Action _onRestart;
        RectTransform _sky, _cue, _ship;
        Image _trail;
        readonly List<Image> _stars = new List<Image>();
        readonly List<Graphic> _fadeIn = new List<Graphic>();
        readonly List<float> _fadeAt = new List<float>();
        static Texture2D _gradient;

        public EscapeScreen(GameUI ui) : base(ui, "Escape") { }
        public override GlassMode Glass => GlassMode.Escape;

        static Texture2D Gradient()
        {
            if (_gradient) return _gradient;
            const int n = 90;
            _gradient = new Texture2D(1, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var stops = new[] { (0f, Pal.Hex(0x0A0D1F)), (0.45f, Pal.Hex(0x1B1F45)), (0.68f, Pal.Hex(0x4A3460)), (0.77f, Pal.Hex(0xB86B62)), (0.80f, Pal.Hex(0xF2C79A)), (0.805f, Pal.Hex(0x3A0C16)), (1f, Pal.Hex(0x0E0408)) };
            for (int y = 0; y < n; y++)
            {
                float t = 1f - y / (float)(n - 1);       // 0 at the top of the screen
                Color c = stops[stops.Length - 1].Item2;
                for (int i = 1; i < stops.Length; i++)
                    if (t <= stops[i].Item1) { c = Color.Lerp(stops[i - 1].Item2, stops[i].Item2, Mathf.InverseLerp(stops[i - 1].Item1, stops[i].Item1, t)); break; }
                _gradient.SetPixel(0, y, c);
            }
            _gradient.Apply(false);
            return _gradient;
        }

        public void Show(GameState state, RunJournal journal, Action onRestart)
        {
            Open();
            _onRestart = onRestart;
            _stars.Clear(); _fadeIn.Clear(); _fadeAt.Clear();

            // sky across the whole glass, not just the stage
            Kit.Kill(_sky ? _sky.gameObject : null);
            _sky = Kit.Stretch("EscapeSky", Ui.Rig.Backdrop);
            var sky = _sky.gameObject.AddComponent<RawImage>();
            sky.texture = Gradient(); sky.raycastTarget = false;

            var rng = new System.Random(11);
            for (int i = 0; i < 46; i++)
            {
                var star = Kit.Box("Star", Root, Pal.Dawn, rng.Next(8, 632), rng.Next(8, 230), 1 + (i % 9 == 0 ? 1 : 0), 1 + (i % 9 == 0 ? 1 : 0));
                _stars.Add(star);
            }
            _trail = Kit.Box("Trail", Root, Pal.Amber.A(0.5f), 541, 200, 2, 60);
            _ship = Kit.Pic("Ship", Root, Pictograms.Get("ship"), Pal.Ivory, 529, 190, 26, 26).rectTransform;

            float y = 34;
            Fade(Kit.Label("Title", Root, "LIFT-OFF", 32, Pal.Ivory, TextAnchor.MiddleLeft, 44, y, 400, 32), 0.6f);
            y += 40;
            Fade(Kit.Label("Sub", Root, "The ship tears free of the blood sea.", 16, Pal.Dawn, TextAnchor.MiddleLeft, 44, y, 420, 16), 1.1f);
            y += 24;
            if (state != null)
            {
                string stats = "CONSOLES " + state.PuzzlesSolved + "/" + state.TotalPuzzles + "  ·  MISFIRES " + state.Mistakes + "  ·  " + Lore.Clock(state.Elapsed);
                Fade(Kit.Label("Stats", Root, stats, 16, Pal.BoneDim, TextAnchor.MiddleLeft, 44, y, 420, 16), 1.5f);
            }
            y += 30;
            Fade(Kit.Label("Tag", Root, journal != null && journal.Entries.Count > 0 ? "WHAT YOU CARRY OUT" : "NO TONGUE DECODED THIS TIME", 16, Pal.Amber, TextAnchor.MiddleLeft, 44, y, 420, 16), 2.0f);
            Fade(Kit.Box("Rule", Root, Pal.Amber.A(0.6f), 44, y + 19, 250, 1), 2.0f);
            y += 28;

            if (journal != null)
            {
                int count = Mathf.Min(journal.Entries.Count, 5);
                bool roomy = count <= 2;
                float at = 2.4f;
                for (int i = 0; i < count && y < 300; i++)
                {
                    var e = journal.Entries[i];
                    Fade(Kit.Label("Lang", Root, e.Language.ToUpperInvariant(), 16, Pal.Ivory, TextAnchor.MiddleLeft, 44, y, 300, 16), at);
                    y += 20;
                    int phrases = Mathf.Min(e.Phrases.Count, roomy ? 3 : 1);
                    for (int p = 0; p < phrases; p++)
                    {
                        var ph = e.Phrases[p];
                        float rw = FontBook.Width(ph.Real, 16);
                        Fade(Kit.Label("Real", Root, ph.Real, 16, Pal.Amber, TextAnchor.MiddleLeft, 56, y, rw + 4, 16), at + 0.15f);
                        Fade(Kit.Label("Means", Root, ph.Translation, 16, Pal.Dawn, TextAnchor.MiddleLeft, 56 + rw + 14, y, 300, 16), at + 0.15f);
                        y += 19;
                    }
                    if (roomy && !string.IsNullOrEmpty(e.Fact))
                    {
                        int lines = Mathf.Clamp(Kit.Lines(e.Fact, 16, 400), 1, 3);
                        var t = Kit.Label("Fact", Root, e.Fact, 16, Pal.BoneDim, TextAnchor.UpperLeft, 56, y + 2, 400, lines * 18, true);
                        t.lineSpacing = 0.8f;
                        Fade(t, at + 0.3f);
                        y += lines * 18 + 4;
                    }
                    y += 8; at += 0.5f;
                }
            }
            _cue = Widgets.Cue(Root, 330, "AGAIN", Pal.Ivory);
        }

        void Fade(Graphic g, float at)
        {
            _fadeIn.Add(g); _fadeAt.Add(at);
            g.canvasRenderer.SetAlpha(0);
        }

        public override void Close()
        {
            base.Close();
            if (_sky) { Kit.Kill(_sky.gameObject); _sky = null; }
        }

        protected override void OnTick(float dt, UiInput input)
        {
            for (int i = 0; i < _fadeIn.Count; i++)
                if (_fadeIn[i]) _fadeIn[i].canvasRenderer.SetAlpha(Kit.Ramp(T, _fadeAt[i], 0.6f));
            for (int i = 0; i < _stars.Count; i++)
                _stars[i].color = Pal.Dawn.A(0.35f + 0.65f * Mathf.Abs(Mathf.Sin(T * (0.4f + (i % 5) * 0.17f) + i)));
            // the ship climbs slowly, for as long as you watch
            float climb = Mathf.Min(150f, T * 9f);
            Kit.Move(_ship, 529 + Mathf.Sin(T * 0.7f) * 2f, 190 - climb);
            Kit.Place(_trail.rectTransform, 541, 216 - climb, 2, 30 + climb * 0.5f);
            _trail.color = Pal.Amber.A(0.25f + 0.25f * Mathf.PingPong(T * 5f, 1f));
            if (_cue) _cue.gameObject.SetActive(T > 3.5f && Kit.Blink(T, 1.2f));
            if (T > 2f && (input.Confirm || input.Click)) { var cb = _onRestart; _onRestart = null; cb?.Invoke(); }
        }
    }
}

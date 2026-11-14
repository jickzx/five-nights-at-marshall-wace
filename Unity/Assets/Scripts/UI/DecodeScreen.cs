using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The warm half of the game. The alien phrase decodes, glyph by glyph, into the real phrase it
    /// mirrors; the pattern is explained with the language's name blocked out; the quiz asks for the
    /// name; and the answer pays off with the name, the pattern in full and a "now you know" line.
    /// One screen, because the decoded phrase should stay in view the whole time.
    /// </summary>
    internal sealed class DecodeScreen : UiScreen
    {
        /// <summary>Hide the language's name during the reveal so the quiz is a real question.</summary>
        public static bool RedactUntilQuiz = true;

        enum Step { Morph, Read, Quiz, Verdict, Payoff }

        sealed class Cell { public RawImage Img; public int CharIndex; public float LockAt; public bool Locked; }
        sealed class Option { public RectTransform Rt; public Image Frame, Fill; public Text Label, Key; public RawImage Mark; }

        public DecodeScreen(GameUI ui) : base(ui, "Decode") { }
        public override GlassMode Glass => GlassMode.Decode;
        public string StepName => _step.ToString();

        Step _step;
        float _stepT;
        RevealInfo _reveal;
        LangPuzzle _puzzle;
        ScriptFamily _fam;
        Action _onContinue;
        Action<int> _onChoose;
        QuizQuestion _quiz;

        // top: the phrase
        RectTransform _top, _alienRow, _lower;
        Text _realText, _readingText, _translation;
        RectTransform _source;
        string _phrase = "", _reading = "", _alienText = "";
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<Texture2D> _pool = new List<Texture2D>();
        float _morphEnd, _shuffleT;
        int _shown;

        // bottom: pattern / quiz / payoff
        RectTransform _cue;
        readonly List<Option> _options = new List<Option>();
        int _cursor, _chosen = -1, _correct = -1;
        bool _wasRight;
        Text _stamp;

        // ---- reveal ---------------------------------------------------------------------

        public void ShowReveal(RevealInfo reveal, LangPuzzle puzzle, ScriptFamily fam, Action onContinue)
        {
            Open();
            _reveal = reveal ?? new RevealInfo();
            _puzzle = puzzle; _fam = fam; _onContinue = onContinue; _onChoose = null; _quiz = null;
            _chosen = -1; _correct = -1; _cells.Clear(); _options.Clear(); _pool.Clear(); _shown = 0; _shuffleT = 0;
            _top = Kit.Node("Top", Root, 0, 0, UiRig.StageW, 160);
            _lower = Kit.Node("Lower", Root, 0, 0, UiRig.StageW, UiRig.StageH);
            BuildPhrase();
            SetStep(Step.Morph);
        }

        void BuildPhrase()
        {
            Lore.SplitReal(_reveal.RealText, out _phrase, out _reading);
            _alienText = (_reveal.AlienText ?? "").Trim();
            var words = _alienText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // the alien phrase, large
            _alienRow = Kit.Node("Alien", _top, 0, 0, UiRig.StageW, 160);
            Kit.Group(_alienRow);
            const int g = 48, gap = 12;
            float total = words.Length * g + Mathf.Max(0, words.Length - 1) * gap;
            float x = (UiRig.StageW - total) / 2f;
            foreach (var word in words)
            {
                Kit.Pic("Glyph", _alienRow, GlyphGen.Texture(SeedOf(word), _fam, 24), Pal.Bone, x, 54, g, g);
                x += g + gap;
            }

            // the real phrase it becomes: as large as fits
            int size = 48;
            var font = FontBook.For(_phrase);
            while (size > 16 && FontBook.Width(_phrase, size, font) > 560) size -= 16;
            float pw = FontBook.Width(_phrase, size, font);
            float px = Mathf.Round((UiRig.StageW - pw) / 2f);
            int py = 54 + (48 - size) / 2;
            _realText = Kit.Label("Real", _top, "", size, Pal.Ivory, TextAnchor.MiddleLeft, px, py, pw + 8, size);
            _realText.font = font;

            // scramble cells sit where each real character will land
            for (int i = 0; i < 12; i++) _pool.Add(GlyphGen.Texture(SeedOf("pool" + i), _fam, size >= 32 ? size / 2 : 16));
            float cx = px;
            int visible = 0;
            for (int i = 0; i < _phrase.Length; i++)
            {
                float adv = FontBook.Width(_phrase[i].ToString(), size, font);
                if (!char.IsWhiteSpace(_phrase[i]) && _cells.Count < 48)
                {
                    float cell = Mathf.Min(adv, size);
                    var img = Kit.Pic("Cell", _top, _pool[i % _pool.Count], Pal.Amber, cx + (adv - cell) / 2f, py + (size - cell) / 2f, cell, cell);
                    img.enabled = false;
                    _cells.Add(new Cell { Img = img, CharIndex = i });
                    visible++;
                }
                cx += adv;
            }
            float per = Mathf.Min(0.11f, 1.5f / Mathf.Max(1, visible));
            for (int i = 0; i < _cells.Count; i++) _cells[i].LockAt = 1.0f + i * per;
            _morphEnd = 1.0f + visible * per + 0.25f;

            _readingText = Kit.Label("Reading", _top, _alienText, 16, Pal.RustHot, TextAnchor.MiddleCenter, 20, 108, 600, 16);

            // afterwards: where it came from, and what it means
            _source = Kit.Node("Source", _top, 0, 20, UiRig.StageW, 18);
            var world = (Ui.State ?? Services.State)?.World;
            string name = (world?.AlienName ?? "ALIEN").ToUpperInvariant();
            float sw = FontBook.Width(name, 16) + 10 + words.Length * 18 + 6 + FontBook.Width(_alienText, 16);
            float sx = Mathf.Round((UiRig.StageW - sw) / 2f);
            Kit.Label("Name", _source, name, 16, Pal.Rust, TextAnchor.MiddleLeft, sx, 0, 200, 16);
            sx += FontBook.Width(name, 16) + 10;
            foreach (var word in words) { Kit.Pic("Mini", _source, GlyphGen.Texture(SeedOf(word), _fam, 16), Pal.BoneDim, sx, 0, 16, 16); sx += 18; }
            Kit.Label("Sound", _source, _alienText, 16, Pal.BoneDim, TextAnchor.MiddleLeft, sx + 6, 0, 300, 16);
            Kit.Group(_source).alpha = 0;

            string meaning = string.IsNullOrWhiteSpace(_reveal.Translation) ? "" : "“" + _reveal.Translation.Trim() + "”";
            _translation = Kit.Label("Meaning", _top, meaning, 16, Pal.Bone, TextAnchor.MiddleCenter, 20, 132, 600, 16);
            _translation.color = Pal.Bone.A(0);
        }

        int SeedOf(string alienWord)
        {
            if (_puzzle != null)
            {
                foreach (var t in _puzzle.Tiles) if (t.Alien == alienWord) return t.GlyphSeed;
                foreach (var e in _puzzle.Examples) foreach (var t in e.Words) if (t.Alien == alienWord) return t.GlyphSeed;
            }
            return GlyphGen.SeedFor(alienWord);
        }

        string[] LanguageNames()
        {
            var world = (Ui.State ?? Services.State)?.World;
            return new[] { world?.Real?.Name, Lore.Optional(_reveal, "Language", "LanguageName") };
        }

        void BuildPattern()
        {
            Kit.Clear(_lower); Hits.Clear();
            string text = _reveal.Explanation ?? "";
            if (RedactUntilQuiz) text = Lore.Redact(text, LanguageNames());
            int lines = Mathf.Clamp(Kit.Lines(text, 16, 480), 1, 5);
            float h = 26 + lines * 18;
            float y = 172;
            Kit.Box("Fill", _lower, Pal.Hex(0x2A1A0C).A(0.6f), 61, y + 1, 518, h - 2);
            Kit.Frame("Frame", _lower, Pal.Rust, 60, y, 520, h);
            float tw = FontBook.Width("THE PATTERN", 16);
            Kit.Box("TabBack", _lower, Pal.WarmDark, 76, y - 8, tw + 12, 16);
            Kit.Label("Tab", _lower, "THE PATTERN", 16, Pal.Amber, TextAnchor.MiddleLeft, 82, y - 8, tw + 4, 16);
            // the blocked-out name is drawn in rust, so it reads as a gap to fill rather than a glare
            string marked = text.Replace("<", "\u2039").Replace(">", "\u203A");
            marked = System.Text.RegularExpressions.Regex.Replace(marked, "\u2588+", m => "<color=#8F3F1F>" + m.Value + "</color>");
            var body = Kit.Label("Body", _lower, marked, 16, Pal.Ivory, TextAnchor.UpperLeft, 80, y + 12, 480, lines * 18, true);
            body.supportRichText = true;
            body.lineSpacing = 0.8f;
            _cue = Widgets.Cue(_lower, 326, "NAME THE ORIGIN", Pal.Amber);
        }

        // ---- quiz -----------------------------------------------------------------------

        public void ShowQuiz(QuizQuestion quiz, LangPuzzle puzzle, ScriptFamily fam, Action<int> onChoose)
        {
            if (!Active || _reveal == null)
            {
                // the quiz arrived on its own (no reveal before it): open with an empty top
                Open();
                _reveal = null; _puzzle = puzzle; _fam = fam; _cells.Clear(); _pool.Clear();
                _top = Kit.Node("Top", Root, 0, 0, UiRig.StageW, 160);
                _lower = Kit.Node("Lower", Root, 0, 0, UiRig.StageW, UiRig.StageH);
                Kit.Label("Decoded", _top, "TRANSMISSION DECODED", 32, Pal.Ivory, TextAnchor.MiddleCenter, 20, 70, 600, 32);
            }
            else FinishMorph();
            _puzzle = puzzle ?? _puzzle;
            _quiz = quiz ?? new QuizQuestion();
            _onChoose = onChoose; _onContinue = null;
            _chosen = -1; _correct = -1; _cursor = 0;
            BuildQuiz();
            SetStep(Step.Quiz);
        }

        void BuildQuiz()
        {
            Kit.Clear(_lower); Hits.Clear(); _options.Clear();
            Kit.Label("Prompt", _lower, _quiz.Prompt ?? "", 16, Pal.Amber, TextAnchor.MiddleCenter, 20, 166, 600, 16);
            int n = Mathf.Min(_quiz.Options.Length, 8);
            int rows = (n + 1) / 2;
            int rowH = rows <= 3 ? 40 : 30;
            for (int i = 0; i < n; i++)
            {
                var o = new Option();
                float x = n == 1 ? 195 : (i % 2 == 0 ? 64 : 326), y = 194 + (i / 2) * rowH;
                o.Rt = Kit.Node("Option" + i, _lower, x, y, 250, rowH - 6);
                o.Fill = Kit.Box("Fill", o.Rt, Pal.Clear, 1, 1, 248, rowH - 8);
                o.Frame = Kit.Frame("Frame", o.Rt, Pal.Rust, 0, 0, 250, rowH - 6);
                o.Key = Kit.Label("Key", o.Rt, (i + 1).ToString(), 16, Pal.Amber, TextAnchor.MiddleCenter, 6, (rowH - 6 - 16) / 2f, 16, 16);
                o.Label = Kit.Label("Label", o.Rt, _quiz.Options[i] ?? "", 16, Pal.Ivory, TextAnchor.MiddleLeft, 32, (rowH - 6 - 16) / 2f, 190, 16);
                o.Mark = Kit.Pic("Mark", o.Rt, Pictograms.Get("check"), Pal.Clear, 228, (rowH - 6 - 13) / 2f, 13, 13);
                int captured = i;
                AddHit(o.Rt, () => Choose(captured), over => { if (over && _step == Step.Quiz) _cursor = captured; });
                _options.Add(o);
            }
        }

        public void Choose(int index)
        {
            if (_step != Step.Quiz || _stepT < 0.2f || index < 0 || index >= _options.Count) return;
            _chosen = index;
            var world = (Ui.State ?? Services.State)?.World;
            int id = _puzzle != null ? _puzzle.Id : 0;
            _wasRight = false; _correct = -1;
            try
            {
                _wasRight = Services.Language.CheckQuiz(world, id, index);
                if (_wasRight) _correct = index;
                else for (int i = 0; i < _options.Count && _correct < 0; i++) if (i != index && Services.Language.CheckQuiz(world, id, i)) _correct = i;
            }
            catch (Exception e) { Debug.LogException(e); }
            if (_wasRight) Ui.Fx.Kick(burst: 0.05f);
            else Ui.Fx.Kick(shake: 0.3f, red: 0.2f, tear: 0.3f);
            SetStep(Step.Verdict);
        }

        string LanguageName()
        {
            if (_correct >= 0 && _correct < _quiz.Options.Length) return _quiz.Options[_correct];
            var names = LanguageNames();
            return names[1] ?? names[0] ?? "";
        }

        void BuildPayoff()
        {
            Kit.Clear(_lower); Hits.Clear(); _options.Clear();
            var world = (Ui.State ?? Services.State)?.World;
            string lang = LanguageName();
            string fact = Lore.FactFor(_reveal, lang, world);
            Ui.Journal.Record(lang, _reveal, fact, _wasRight);

            string big = lang.ToUpperInvariant();
            float bw = FontBook.Width(big, 32);
            float bx = Mathf.Round((UiRig.StageW - bw) / 2f);
            Kit.Pic("Mark", _lower, Pictograms.Get(_wasRight ? "check" : "cross"), _wasRight ? Pal.Amber : Pal.Blood, bx - 34, 166, 26, 26);
            _stamp = Kit.Label("Language", _lower, big, 32, Pal.Amber, TextAnchor.MiddleCenter, bx - 4, 162, bw + 8, 32);
            Kit.Center(_stamp.rectTransform);

            float y = 202;
            string why = _reveal?.Explanation ?? "";
            if (why.Length > 0)
            {
                int lines = Mathf.Clamp(Kit.Lines(why, 16, 500), 1, 4);
                var t = Kit.Label("Why", _lower, why, 16, Pal.Ivory, TextAnchor.UpperCenter, 70, y, 500, lines * 18, true);
                t.lineSpacing = 0.8f;
                y += lines * 18 + 8;
            }
            if (fact.Length > 0)
            {
                float tw = FontBook.Width("NOW YOU KNOW", 16);
                Kit.Box("Rule", _lower, Pal.Rust, 120, y + 8, 400, 1);
                Kit.Box("TagBack", _lower, Pal.WarmDark, (UiRig.StageW - tw) / 2f - 8, y, tw + 16, 16);
                Kit.Label("Tag", _lower, "NOW YOU KNOW", 16, Pal.Amber, TextAnchor.MiddleCenter, 20, y, 600, 16);
                y += 20;
                int lines = Mathf.Clamp(Kit.Lines(fact, 16, 500), 1, 4);
                var t = Kit.Label("Fact", _lower, fact, 16, Pal.Bone, TextAnchor.UpperCenter, 70, y, 500, lines * 18, true);
                t.lineSpacing = 0.8f;
            }
            _cue = Widgets.Cue(_lower, 330, "BACK INTO THE DARK", Pal.Amber);
            Ui.Fx.Kick(shake: 0.18f);
        }

        // ---- flow -----------------------------------------------------------------------

        void SetStep(Step s)
        {
            _step = s; _stepT = 0;
            if (s == Step.Read) BuildPattern();
            if (s == Step.Payoff) BuildPayoff();
        }

        void FinishMorph()
        {
            if (_reveal == null) return;
            foreach (var c in _cells) { c.Locked = true; if (c.Img) c.Img.enabled = false; }
            Kit.SetText(_realText, _phrase);
            Kit.Group(_alienRow).alpha = 0;
            Kit.SetText(_readingText, _reading);
            _readingText.color = Pal.Amber;
            Kit.Group(_source).alpha = 1;
            _translation.color = Pal.Bone;
        }

        protected override void OnTick(float dt, UiInput input)
        {
            _stepT += dt;
            switch (_step)
            {
                case Step.Morph: TickMorph(dt, input); break;
                case Step.Read:
                    Kit.Group(_lower).alpha = Kit.Ramp(_stepT, 0f, 0.3f);
                    if (_cue) _cue.gameObject.SetActive(_stepT > 0.5f && Kit.Blink(_stepT, 1.4f));
                    if (_stepT > 0.4f && (input.Confirm || input.Click)) { var cb = _onContinue; _onContinue = null; cb?.Invoke(); }
                    break;
                case Step.Quiz: TickQuiz(input); break;
                case Step.Verdict: TickVerdict(); break;
                case Step.Payoff:
                    Kit.Group(_lower).alpha = 1;
                    if (_stamp) { float s = 1f + 0.5f * (1f - Kit.EaseOut(Kit.Ramp(_stepT, 0f, 0.16f))); _stamp.rectTransform.localScale = new Vector3(s, s, 1); }
                    if (_cue) _cue.gameObject.SetActive(_stepT > 0.9f && Kit.Blink(_stepT, 1.4f));
                    if (_stepT > 0.7f && (input.Confirm || input.Click))
                    {
                        var cb = _onChoose; _onChoose = null;
                        int chosen = _chosen;
                        cb?.Invoke(chosen);
                    }
                    break;
            }
        }

        void TickMorph(float dt, UiInput input)
        {
            float t = _stepT;
            // the alien phrase arrives, holds, then breaks up into the characters underneath it
            Kit.Group(_alienRow).alpha = Mathf.Min(Kit.Ramp(t, 0f, 0.25f), 1f - Kit.Ramp(t, 0.75f, 0.35f));
            _shuffleT += dt;
            bool reshuffle = _shuffleT > 0.055f;
            if (reshuffle) _shuffleT = 0;
            int shown = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (c.Locked) { shown = c.CharIndex + 1; continue; }
                if (t >= c.LockAt)
                {
                    c.Locked = true; c.Img.enabled = false; shown = c.CharIndex + 1;
                    continue;
                }
                c.Img.enabled = t > 0.7f;
                if (reshuffle) c.Img.texture = _pool[UnityEngine.Random.Range(0, _pool.Count)];
                c.Img.color = Color.Lerp(Pal.Rust, Pal.Amber, Kit.Ramp(t, c.LockAt - 0.35f, 0.35f));
            }
            if (shown != _shown)
            {
                _shown = shown;
                Kit.SetText(_realText, _phrase.Substring(0, Mathf.Min(shown, _phrase.Length)));
            }
            // the sound line re-types itself from the alien sounds into the real ones
            float rp = Kit.Ramp(t, 1.0f, Mathf.Max(0.2f, _morphEnd - 1.25f));
            if (_reading.Length > 0)
            {
                int n = Mathf.RoundToInt(rp * _reading.Length);
                string tail = n < _alienText.Length ? _alienText.Substring(n) : "";
                Kit.SetText(_readingText, _reading.Substring(0, n) + tail);
                _readingText.color = Color.Lerp(Pal.RustHot, Pal.Amber, rp);
            }
            else _readingText.color = Pal.RustHot.A(1f - rp);

            Kit.Group(_source).alpha = Kit.Ramp(t, _morphEnd, 0.3f);
            _translation.color = Pal.Bone.A(Kit.Ramp(t, _morphEnd + 0.1f, 0.3f));

            bool skip = t > 0.3f && (input.Confirm || input.Click);
            if (t > _morphEnd + 0.5f || skip) { FinishMorph(); SetStep(Step.Read); }
        }

        void TickQuiz(UiInput input)
        {
            Kit.Group(_lower).alpha = Kit.Ramp(_stepT, 0f, 0.2f);
            int n = _options.Count;
            if (n == 0) return;
            if (input.Left || input.Right) _cursor = (_cursor ^ 1) < n ? _cursor ^ 1 : _cursor;
            if (input.Down) _cursor = _cursor + 2 < n ? _cursor + 2 : _cursor;
            if (input.Up) _cursor = _cursor - 2 >= 0 ? _cursor - 2 : _cursor;
            for (int i = 0; i < n; i++)
            {
                bool on = i == _cursor;
                _options[i].Frame.color = on ? Pal.Amber : Pal.Rust;
                _options[i].Fill.color = on ? Pal.Hex(0x3A2410) : Pal.Clear;
            }
            if (input.Digit >= 0 && input.Digit < n) Choose(input.Digit);
            else if (input.Confirm) Choose(_cursor);
        }

        void TickVerdict()
        {
            float t = _stepT;
            for (int i = 0; i < _options.Count; i++)
            {
                var o = _options[i];
                bool chosen = i == _chosen, right = i == _correct;
                if (chosen && _wasRight)
                {
                    o.Fill.color = Color.Lerp(Pal.Ivory, Pal.Amber, Kit.Ramp(t, 0f, 0.25f));
                    o.Frame.color = Pal.Amber; o.Label.color = Pal.WarmDark; o.Key.color = Pal.WarmDark;
                    o.Mark.color = Pal.WarmDark;
                }
                else if (chosen)
                {
                    o.Frame.color = Pal.Blood; o.Label.color = Pal.Blood; o.Key.color = Pal.Blood;
                    o.Fill.color = Pal.BloodDeep.A(0.5f);
                    o.Mark.texture = Pictograms.Get("cross"); o.Mark.color = Pal.Blood;
                    o.Rt.localPosition += new Vector3(Mathf.Sin(t * 60f) * (1f - Kit.Ramp(t, 0f, 0.3f)) * 2f, 0, 0);
                }
                else if (right && t > 0.35f)
                {
                    o.Frame.color = Pal.Amber; o.Fill.color = Pal.Hex(0x3A2410); o.Mark.color = Pal.Amber;
                }
                else
                {
                    float fade = 1f - 0.75f * Kit.Ramp(t, 0f, 0.3f);
                    o.Frame.color = Pal.Rust.A(fade); o.Label.color = Pal.Ivory.A(fade); o.Key.color = Pal.Amber.A(fade);
                    o.Fill.color = Pal.Clear;
                }
            }
            if (t > (_wasRight ? 0.8f : 1.3f)) SetStep(Step.Payoff);
        }
    }
}

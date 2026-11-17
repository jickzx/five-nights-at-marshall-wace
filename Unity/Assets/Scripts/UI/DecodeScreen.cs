using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The warm half of the game. The alien phrase decodes, glyph by glyph, into the real phrase it
    /// mirrors; the pattern is explained with the language's name blocked out; the quiz asks for the
    /// name; and the answer pays off with the name, a one-line explanation and a "now you know" fact.
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
        List<Tile> _answer;
        ScriptFamily _fam;
        Action _onContinue;
        Action<int> _onChoose;
        QuizQuestion _quiz;

        // top: the phrase
        RectTransform _top, _alienRow, _lower;
        Text _realText, _readingText, _translation;
        RectTransform _source;
        string _display = "", _reading = "", _alienText = "";
        bool _rtl;
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<Texture2D> _pool = new List<Texture2D>();
        float _morphEnd, _shuffleT;
        int _locked;

        // bottom: pattern / quiz / payoff
        RectTransform _cue;
        readonly List<Option> _options = new List<Option>();
        int _cursor, _chosen = -1, _correct = -1;
        bool _wasRight;
        Text _stamp;
        Text _sayCue;                // the pronunciation cue on the payoff
        bool _askedToSpeak;          // the payoff asks for the sentence once; P asks again

        /// <summary>What the pronunciation cue shows ("" when hidden).</summary>
        public string PronunciationCue => _sayCue && _sayCue.gameObject.activeInHierarchy ? _sayCue.text : "";

        /// <summary>The payoff is up and the sentence can be played again (P).</summary>
        public bool ReplayOffered => _step == Step.Payoff && _reveal != null && _puzzle != null
                                     && Ui.Pronunciation.PuzzleId == _puzzle.Id && Ui.Pronunciation.State == PronunciationState.Ready;

        GameState State => Ui.State ?? Services.State;

        // ---- reveal ---------------------------------------------------------------------

        /// <param name="answer">The tiles the player sent, when known: their glyphs are what decodes.</param>
        public void ShowReveal(RevealInfo reveal, LangPuzzle puzzle, ScriptFamily fam, List<Tile> answer, Action onContinue)
        {
            Open();
            _reveal = reveal ?? new RevealInfo();
            _puzzle = puzzle; _fam = fam; _answer = answer; _onContinue = onContinue; _onChoose = null; _quiz = null;
            _askedToSpeak = false; _sayCue = null;
            _chosen = -1; _correct = -1; _cells.Clear(); _options.Clear(); _pool.Clear(); _locked = -1; _shuffleT = 0;
            _top = Kit.Node("Top", Root, 0, 0, UiRig.StageW, 160);
            _lower = Kit.Node("Lower", Root, 0, 0, UiRig.StageW, UiRig.StageH);
            BuildPhrase();
            SetStep(Step.Morph);
        }

        void BuildPhrase()
        {
            Lore.SplitReal(_reveal.RealText, out var phrase, out _reading);
            _rtl = Rtl.HasRtl(phrase);
            _display = Rtl.Visual(phrase);             // shaped and in drawing order; set on the label as is
            _alienText = (_reveal.AlienText ?? "").Trim();

            // the alien phrase, large: the glyphs the player just sent, or one per word
            var seeds = new List<int>();
            if (_answer != null && _answer.Count > 0) foreach (var t in _answer) seeds.Add(t.GlyphSeed);
            else foreach (var word in _alienText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) seeds.Add(SeedOf(word));
            if (seeds.Count > 10) seeds.RemoveRange(10, seeds.Count - 10);   // a sentence is up to 8 words; 10 still fits at 48px

            _alienRow = Kit.Node("Alien", _top, 0, 0, UiRig.StageW, 160);
            Kit.Group(_alienRow);
            const int g = 48, gap = 12;
            float total = seeds.Count * g + Mathf.Max(0, seeds.Count - 1) * gap;
            float x = (UiRig.StageW - total) / 2f;
            foreach (int seed in seeds)
            {
                Kit.Pic("Glyph", _alienRow, GlyphGen.Texture(seed, _fam, 24), Pal.Bone, x, 54, g, g);
                x += g + gap;
            }

            // the real phrase it becomes: as large as fits
            int size = 48;
            var font = FontBook.For(_display);
            while (size > 16 && FontBook.Width(_display, size, font) > 560) size -= 16;
            float pw = FontBook.Width(_display, size, font);
            float px = Mathf.Round((UiRig.StageW - pw) / 2f);
            int py = 54 + (48 - size) / 2;
            _realText = _rtl
                ? Kit.Label("Real", _top, "", size, Pal.Ivory, TextAnchor.MiddleRight, px - 8, py, pw + 8, size)
                : Kit.Label("Real", _top, "", size, Pal.Ivory, TextAnchor.MiddleLeft, px, py, pw + 8, size);
            _realText.font = font;

            // scramble cells sit where each real character will land
            for (int i = 0; i < 12; i++) _pool.Add(GlyphGen.Texture(GlyphGen.SeedFor("pool" + i), _fam, size >= 32 ? size / 2 : 16));
            float cx = px;
            for (int i = 0; i < _display.Length; i++)
            {
                float adv = FontBook.Width(_display[i].ToString(), size, font);
                if (!char.IsWhiteSpace(_display[i]) && _cells.Count < 48)
                {
                    float cell = Mathf.Min(adv, size);
                    var img = Kit.Pic("Cell", _top, _pool[i % _pool.Count], Pal.Amber, cx + (adv - cell) / 2f, py + (size - cell) / 2f, cell, cell);
                    img.enabled = false;
                    _cells.Add(new Cell { Img = img, CharIndex = i });
                }
                cx += adv;
            }
            int count = _cells.Count;
            float per = Mathf.Min(0.11f, 1.5f / Mathf.Max(1, count));
            for (int i = 0; i < count; i++) _cells[i].LockAt = 1.0f + (_rtl ? count - 1 - i : i) * per;   // in reading order
            _morphEnd = 1.0f + count * per + 0.25f;

            _readingText = Kit.Label("Reading", _top, _alienText, 16, Pal.RustHot, TextAnchor.MiddleCenter, 20, 108, 600, 16);

            // afterwards: where it came from, and what it means
            _source = Kit.Node("Source", _top, 0, 20, UiRig.StageW, 18);
            string name = (_reveal.AlienName ?? _puzzle?.AlienName ?? State?.World?.AlienName ?? "ALIEN").ToUpperInvariant();
            int minis = Mathf.Min(seeds.Count, 7);
            float sw = FontBook.Width(name, 16) + 10 + minis * 18 + 6 + FontBook.Width(_alienText, 16);
            bool sound = sw <= 600;     // a whole sentence is already spelled out under the glyphs; drop it here if it would run off
            if (!sound) sw -= 6 + FontBook.Width(_alienText, 16);
            float sx = Mathf.Round((UiRig.StageW - sw) / 2f);
            Kit.Label("Name", _source, name, 16, Pal.Rust, TextAnchor.MiddleLeft, sx, 0, 200, 16);
            sx += FontBook.Width(name, 16) + 10;
            for (int i = 0; i < minis; i++) { Kit.Pic("Mini", _source, GlyphGen.Texture(seeds[i], _fam, 16), Pal.BoneDim, sx, 0, 16, 16); sx += 18; }
            if (sound) Kit.Label("Sound", _source, _alienText, 16, Pal.BoneDim, TextAnchor.MiddleLeft, sx + 6, 0, Mathf.Max(300, FontBook.Width(_alienText, 16) + 4), 16);
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

        string[] LanguageNames() => new[] { _reveal?.LanguageName, State?.World?.Real?.Name };

        void BuildPattern()
        {
            Kit.Clear(_lower); Hits.Clear();
            string text = Lore.PatternFor(_reveal);
            if (RedactUntilQuiz) text = Lore.Redact(text, LanguageNames());
            if (text.Length > 0)
            {
                int lines = Mathf.Clamp(Kit.Lines(text, 16, 480), 1, 6);
                float h = 26 + lines * 18;
                float y = 172;
                Kit.Box("Fill", _lower, Pal.Hex(0x2A1A0C).A(0.6f), 61, y + 1, 518, h - 2);
                Kit.Frame("Frame", _lower, Pal.Rust, 60, y, 520, h);
                float tw = FontBook.Width("THE PATTERN", 16);
                Kit.Box("TabBack", _lower, Pal.WarmDark, 76, y - 8, tw + 12, 16);
                Kit.Label("Tab", _lower, "THE PATTERN", 16, Pal.Amber, TextAnchor.MiddleLeft, 82, y - 8, tw + 4, 16);
                // the blocked-out name is drawn in rust, so it reads as a gap to fill rather than a glare
                string marked = text.Replace("<", "‹").Replace(">", "›");
                marked = Regex.Replace(marked, "█+", m => "<color=#8F3F1F>" + m.Value + "</color>");
                var body = Kit.Label("Body", _lower, marked, 16, Pal.Ivory, TextAnchor.UpperLeft, 80, y + 12, 480, lines * 18, true);
                body.supportRichText = true;
                Kit.Pitch(body);
            }
            _cue = Widgets.Cue(_lower, 328, "NAME THE ORIGIN", Pal.Amber);
        }

        // ---- quiz -----------------------------------------------------------------------

        public void ShowQuiz(QuizQuestion quiz, LangPuzzle puzzle, ScriptFamily fam, Action<int> onChoose)
        {
            bool alone = !Active || _reveal == null;
            if (alone)
            {
                // the quiz arrived on its own (no reveal before it): open with an empty top
                Open();
                _reveal = null; _puzzle = puzzle; _fam = fam; _cells.Clear(); _pool.Clear();
                _askedToSpeak = false; _sayCue = null;
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
            // the phrase is already on screen above, so only the question itself is needed
            Kit.Label("Prompt", _lower, Lore.FirstLine(_quiz.Prompt), 16, Pal.Amber, TextAnchor.MiddleCenter, 20, 166, 600, 16);
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
            var world = State?.World;
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
            Services.Voice.NotifyQuizAnswerLocked(id);
            SetStep(Step.Verdict);
        }

        string LanguageName()
        {
            if (_correct >= 0 && _correct < _quiz.Options.Length) return _quiz.Options[_correct];
            var names = LanguageNames();
            return names[0] ?? names[1] ?? "";
        }

        void BuildPayoff()
        {
            Kit.Clear(_lower); Hits.Clear(); _options.Clear();
            string lang = LanguageName();
            string fact = Lore.FactFor(_reveal, lang, State?.World);
            Ui.Journal.Record(lang, _reveal, fact, _wasRight);

            string big = lang.ToUpperInvariant();
            float bw = FontBook.Width(big, 32);
            float bx = Mathf.Round((UiRig.StageW - bw) / 2f) + 16;
            Kit.Pic("Mark", _lower, Pictograms.Get(_wasRight ? "check" : "cross"), _wasRight ? Pal.Amber : Pal.Blood, bx - 34, 163, 26, 26);
            _stamp = Kit.Label("Language", _lower, big, 32, Pal.Amber, TextAnchor.MiddleCenter, bx - 4, 160, bw + 8, 32);
            Kit.Center(_stamp.rectTransform);

            // the one-liner: the quiz's own explanation when it has one, otherwise the pattern in full
            float y = 198;
            string why = !string.IsNullOrWhiteSpace(_quiz?.Explanation) ? _quiz.Explanation.Trim() : Lore.PatternFor(_reveal);
            int factLines = fact.Length > 0 ? Mathf.Clamp(Kit.Lines(fact, 16, 560), 1, 4) : 0;
            if (why.Length > 0)
            {
                int lines = Mathf.Clamp(Kit.Lines(why, 16, 560), 1, factLines >= 4 ? 2 : 3);
                Kit.Pitch(Kit.Label("Why", _lower, why, 16, Pal.Ivory, TextAnchor.UpperCenter, 40, y, 560, lines * 17, true), 17);
                y += lines * 17 + 6;
            }
            if (factLines > 0)
            {
                float tw = FontBook.Width("NOW YOU KNOW", 16);
                Kit.Box("Rule", _lower, Pal.Rust, 120, y + 8, 400, 1);
                Kit.Box("TagBack", _lower, Pal.WarmDark, (UiRig.StageW - tw) / 2f - 8, y, tw + 16, 16);
                Kit.Label("Tag", _lower, "NOW YOU KNOW", 16, Pal.Amber, TextAnchor.MiddleCenter, 20, y, 600, 16);
                y += 19;
                Kit.Pitch(Kit.Label("Fact", _lower, fact, 16, Pal.Bone, TextAnchor.UpperCenter, 40, y, 560, factLines * 17, true), 17);
                y += factLines * 17;
            }
            _cue = Widgets.Cue(_lower, Mathf.Clamp(y + 8, 318, 340), "BACK INTO THE DARK", Pal.Amber);
            // top left, clear of the run timer: what the native voice is doing
            _sayCue = Kit.Label("Say", _lower, "", 16, Pal.Amber, TextAnchor.MiddleLeft, 20, 2, 440, 16);
            Ui.Fx.Kick(shake: 0.18f);
        }

        /// <summary>The cue text for a pronunciation state. Mock audio says plainly that it is a tone, not speech.</summary>
        public static string CueFor(PronunciationView v)
        {
            bool mock = v.Source == VoiceSource.Mock;
            switch (v.State)
            {
                case PronunciationState.Loading: return "VOICE: LOADING";
                case PronunciationState.Preparing: return "VOICE: STARTING";
                case PronunciationState.Playing: return mock ? "MOCK TONE PLAYING (NOT SPEECH)" : "VOICE: PLAYING";
                case PronunciationState.Ready: return "P" + (mock ? "  REPLAY MOCK TONE (NOT SPEECH)" : "  HEAR IT AGAIN");
                case PronunciationState.Unavailable: return "VOICE UNAVAILABLE";
                default: return "";
            }
        }

        void AskToSpeak(bool replay)
        {
            if (_reveal == null || _puzzle == null) return;   // no reveal, no sentence to speak
            try { Services.Voice.RequestPronunciation(_puzzle.Id, replay); } catch (Exception e) { Debug.LogException(e); }
        }

        // ---- flow -----------------------------------------------------------------------

        void SetStep(Step s)
        {
            _step = s; _stepT = 0;
            if (s == Step.Read) BuildPattern();
            if (s == Step.Payoff)
            {
                BuildPayoff();
                // the quiz is answered: only now may the sentence be heard in its own language
                if (!_askedToSpeak && _reveal != null) { _askedToSpeak = true; AskToSpeak(false); }
            }
        }

        void ShowLocked(int cellIndex)
        {
            // everything up to (or, right to left, from) the last character that has locked in
            if (cellIndex < 0) { _realText.text = ""; return; }
            int ch = _cells[cellIndex].CharIndex;
            _realText.text = _rtl ? _display.Substring(ch) : _display.Substring(0, Mathf.Min(ch + 1, _display.Length));
        }

        void FinishMorph()
        {
            if (_reveal == null) return;
            foreach (var c in _cells) { c.Locked = true; if (c.Img) c.Img.enabled = false; }
            _realText.text = _display;
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
                    if (_sayCue)
                    {
                        var pv = Ui.Pronunciation;
                        string say = _puzzle != null && pv.PuzzleId == _puzzle.Id ? CueFor(pv) : "";
                        if (_sayCue.text != say) Kit.SetText(_sayCue, say);
                    }
                    if (input.Speak) AskToSpeak(true);
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
            int newest = -1; float newestAt = -1;
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (!c.Locked && t >= c.LockAt) { c.Locked = true; c.Img.enabled = false; }
                if (c.Locked) { if (c.LockAt > newestAt) { newestAt = c.LockAt; newest = i; } continue; }
                c.Img.enabled = t > 0.7f;
                if (reshuffle) c.Img.texture = _pool[UnityEngine.Random.Range(0, _pool.Count)];
                c.Img.color = Color.Lerp(Pal.Rust, Pal.Amber, Kit.Ramp(t, c.LockAt - 0.35f, 0.35f));
            }
            if (newest != _locked) { _locked = newest; ShowLocked(newest); }

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

using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The first console's worked example: a solved puzzle in a language this run never uses, walked
    /// through in seven short steps before the real puzzle takes the same console. It is laid out exactly
    /// like PuzzleScreen (rows, answer row, tiles, SEND) so the real thing feels familiar; each step dims
    /// everything but the part it talks about, brackets it, and says one or two lines in the band across
    /// the top (where the tutorial's console cues live). In the build step the player presses the real
    /// keys: the tile's number, Backspace, Enter.
    ///
    /// Enter, Space, Right or a click: next. Backspace or Left: back. Delete or the SKIP button: straight
    /// to the real puzzle. Esc/Tab is the game's: it walks away, and the example plays again next time.
    /// onDone fires once, when the last step is passed or the example is skipped.
    /// </summary>
    internal sealed class WorkedExampleScreen : UiScreen
    {
        const int L = 30, R = 610, BandTextX = 150, BandTextW = R - BandTextX;
        const float TypeRate = 80f;      // characters per second: two lines a step, a touch faster than the cues

        public enum Step { Barrier, Rows, Compare, Rule, Target, Build, Decoded }
        public const int StepCount = 7;

        // ---- copy -------------------------------------------------------------------------
        // [X] draws X as a keycap. Lines are fitted to the band at run time (see Fit).

        public const string TagBarrier = "THE BARRIER", TagRows = "THE ROWS", TagCompare = "COMPARE", TagRule = "THE RULE",
            TagTarget = "THE TARGET", TagBuild = "BUILD IT", TagDecoded = "DECODED";

        public const string Barrier1 = "THE MACHINE SPEAKS A LANGUAGE YOU DON'T KNOW.";
        public const string Barrier2 = "ITS WORDS ARE REAL. ONLY THE GLYPHS ARE ALIEN.";
        public const string Rows1 = "EACH ROW IS SOMETHING THE MACHINE HAS ALREADY SAID:";
        public const string Rows2 = "THE MEANING ON THE LEFT, ITS REAL WORDS ON THE RIGHT.";
        public const string Compare1 = "COMPARE THE ROWS: WHICH WORD CARRIES WHICH MEANING?";
        public const string Compare2Fallback = "THE MEANING OF EACH WORD NOW SHOWS BENEATH IT.";
        public const string Target1 = "THE BOTTOM ROW IS YOURS: A MEANING WITH NO WORDS YET.";
        public const string Target2Decoys = "THE TILES ARE THE WORDS. NOT EVERY ONE IS NEEDED.";
        public const string Target2Exact = "THE TILES ARE THE WORDS. PUT THEM IN THE RIGHT ORDER.";
        public const string Build1 = "PRESS [{KEY}] TO PLACE {WORD} ({GLOSS}).";
        public const string Build1Short = "PRESS [{KEY}] TO PLACE {WORD}.";
        public const string Build2 = "[BKSP] TAKES THE LAST TILE BACK.";
        public const string BuildDone1 = "THAT IS THE PHRASE. [ENTER] SENDS IT.";
        public const string BuildDone2 = "WRONG ORDER? [BKSP] TAKES THE LAST TILE BACK.";
        public const string Decoded1 = "ACCEPTED: {REAL} · {TRANSLATION}";
        public const string Decoded1Short = "ACCEPTED: {REAL}";
        public const string Decoded2 = "THIS WAS {LANG}. NOW YOURS: SAME METHOD, NEW TONGUE.";
        public const string Decoded2Short = "THIS WAS {LANG}. NOW YOUR TURN.";
        public const string Keys = "[ENTER] NEXT   ·   [BKSP] BACK   ·   [DEL] SKIP EXAMPLE";
        public const string KeysBuild = "[DEL] SKIP EXAMPLE";
        public const string KeysDone = "NOW YOUR TURN";
        public const string SkipButton = "SKIP";

        /// <summary>Every fixed string the screen can emit, for the font-coverage test.</summary>
        public static IEnumerable<string> AllCopy()
        {
            return new[]
            {
                TagBarrier, TagRows, TagCompare, TagRule, TagTarget, TagBuild, TagDecoded,
                Barrier1, Barrier2, Rows1, Rows2, Compare1, Compare2Fallback, Target1, Target2Decoys, Target2Exact,
                Build1, Build1Short, Build2, BuildDone1, BuildDone2, Decoded1, Decoded1Short, Decoded2, Decoded2Short,
                Keys, KeysBuild, KeysDone, SkipButton,
            };
        }

        // ---- state ------------------------------------------------------------------------

        sealed class WordView { public RawImage Glyph; public Text Cap, Gloss; public string Word, Meaning; }
        sealed class RowView { public RectTransform Node; public CanvasGroup Group; public readonly List<WordView> Words = new List<WordView>(); public float Y, X0, X1; }
        sealed class TileView { public RectTransform Rt; public Image Frame, Fill; public RawImage Glyph; public Text Cap, Key; public float Flash; }
        sealed class Seg { public Text Text; public RectTransform Cap; public int From, Len; public string Full; }

        WorkedExample _ex;
        LangPuzzle _p;
        ScriptFamily _fam;
        Action _onDone;
        bool _done;
        Step _step;
        float _stepT;

        readonly List<RowView> _rows = new List<RowView>();
        readonly List<TileView> _tiles = new List<TileView>();
        readonly List<RawImage> _slotGlyphs = new List<RawImage>();
        RectTransform _ansNode, _tilesNode, _slotRow, _marks, _band, _foot, _decode;
        CanvasGroup _ansGroup, _tilesGroup, _decodeGroup;
        Image _slotCursor, _sendFrame, _sendFill, _skipFrame;
        Text _sendText, _skipText;
        RawImage _sendIcon;
        readonly List<Image> _pips = new List<Image>();
        readonly List<Image> _pulse = new List<Image>();
        readonly List<Seg> _segs = new List<Seg>(), _footSegs = new List<Seg>();
        float _bandT;
        CanvasGroup _bandGroup;
        bool _glossFits;
        float _yAnswer, _wordX, _yEx, _rowH, _yTiles, _tileH;
        int _exRows;

        // build step
        int _placed;
        float _writeT = 1f;
        Texture2D _writeTex;
        GlyphShape _writeShape;

        public WorkedExampleScreen(GameUI ui) : base(ui, "Example") { }
        public override GlassMode Glass => _step == Step.Decoded ? GlassMode.Decode : GlassMode.Console;
        public Step Current => _step;
        public int Placed => _placed;

        public void Show(WorkedExample example, ScriptFamily fam, Action onDone)
        {
            Open();
            _ex = example; _p = example.Puzzle; _fam = fam; _onDone = onDone; _done = false;
            _rows.Clear(); _tiles.Clear(); _slotGlyphs.Clear(); _pips.Clear(); _pulse.Clear(); _segs.Clear();
            _placed = 0; _writeT = 1;
            Build();
            SetStep(Step.Barrier);
        }

        public override void Close()
        {
            base.Close();
            Kit.Kill(_writeTex); _writeTex = null;
        }

        // ---- layout: the console, as PuzzleScreen lays it out --------------------------------

        void Build()
        {
            int n = _p.Tiles.Count;
            var widths = new float[n];
            float sum = 0;
            for (int i = 0; i < n; i++) { widths[i] = Mathf.Ceil(Mathf.Max(48, FontBook.Width(_p.Tiles[i].Alien, 16) + 14) / 2f) * 2f; sum += widths[i] + 6; }
            int tileRows = sum - 6 > R - L ? 2 : 1;
            _tileH = tileRows == 1 ? 58 : 52;
            _yTiles = 322 - tileRows * (_tileH + 2);
            _yAnswer = _yTiles - 58;

            int ex = Mathf.Min(_p.Examples.Count, 6);
            bool twoCol = ex > 3 || (tileRows == 2 && ex > 2);
            _exRows = twoCol ? (ex + 1) / 2 : ex;
            float room = _yAnswer - 4 - 44;
            // the rows get a little more pitch than the console gives them when it fits: the gloss goes under each word
            _rowH = _exRows > 0 ? Mathf.Clamp(Mathf.Floor(room / _exRows), 46, 70) : 52;
            _glossFits = _rowH >= 66;
            _yEx = 44 + Mathf.Max(0, room - _exRows * _rowH);     // bottom-aligned: leaves the band room to grow

            float plateMax = twoCol ? 120 : 216;
            float plateW = Widgets.PlateWidth(_p.TargetMeaning, plateMax);
            for (int i = 0; i < ex; i++) plateW = Mathf.Max(plateW, Widgets.PlateWidth(_p.Examples[i].Picture, plateMax));
            plateW = Mathf.Clamp(plateW, 84, plateMax);
            _wordX = L + plateW + 22;

            for (int i = 0; i < ex; i++)
            {
                var e = _p.Examples[i];
                int col = twoCol ? i % 2 : 0, row = twoCol ? i / 2 : i;
                var rv = new RowView();
                rv.Node = Section("Example" + i, out rv.Group);
                float x = L + col * 292, y = _yEx + row * _rowH;
                rv.Y = y; rv.X0 = x;
                Widgets.Plate(rv.Node, x, y, plateW, plateW, 44, e.Picture, false);
                float wx = x + plateW + 22;
                Colon(rv.Node, wx - 13, y + 14, Pal.Rust);
                foreach (var w in e.Words)
                {
                    string meaning = (w.Gloss ?? "").Trim().ToUpperInvariant();
                    float ww = Widgets.WordWidth(w);
                    float gw = _glossFits && meaning.Length > 0 ? FontBook.Width(meaning, 16) + 4 : 0;
                    float cell = Mathf.Max(ww, gw);
                    var wv = new WordView { Word = w.Alien ?? "", Meaning = meaning };
                    Widgets.Word(rv.Node, wx + (cell - ww) / 2f, y - 2, w, _fam, Pal.Bone, Pal.BoneDim, out wv.Glyph, out wv.Cap);
                    if (_glossFits)
                    {
                        wv.Gloss = Kit.Label("Gloss", rv.Node, meaning, 16, Pal.RustHot.A(0f), TextAnchor.MiddleCenter, wx, y + 48, cell, 16);
                        wv.Gloss.gameObject.SetActive(false);
                    }
                    rv.Words.Add(wv);
                    wx += cell + 6;
                }
                rv.X1 = wx - 6;
                _rows.Add(rv);
            }

            // the answer row: hot plate, empty slots, SEND
            _ansNode = Section("Answer", out _ansGroup);
            Kit.Box("AnswerRule", _ansNode, Pal.RustDeep, L, _yAnswer - 4, R - L, 1);
            Widgets.Plate(_ansNode, L, _yAnswer, plateW, plateW, 44, _p.TargetMeaning, true);
            Colon(_ansNode, _wordX - 13, _yAnswer + 14, Pal.Bone);
            _slotRow = Kit.Node("Slots", _ansNode, _wordX, _yAnswer, R - _wordX - 70, 52);
            _slotCursor = Kit.Frame("Cursor", _ansNode, Pal.RustHot, 0, 0, 32, 32);
            var send = Kit.Node("Send", _ansNode, R - 62, _yAnswer + 8, 62, 32);
            _sendFill = Kit.Box("Fill", send, Pal.Clear, 1, 1, 60, 30);
            _sendFrame = Kit.Frame("Frame", send, Pal.Rust, 0, 0, 62, 32);
            _sendIcon = Kit.Pic("Key", send, Pictograms.Get("enter"), Pal.Rust, 6, 10, 13, 13);
            _sendText = Kit.Label("Text", send, "SEND", 16, Pal.Rust, TextAnchor.MiddleLeft, 24, 8, 36, 16);
            _decode = Kit.Node("Decode", _ansNode, _wordX - 4, _yAnswer - 2, R - _wordX - 66, 56);
            _decodeGroup = Kit.Group(_decode);
            _decode.gameObject.SetActive(false);

            // tiles
            _tilesNode = Section("Tiles", out _tilesGroup);
            int perRow = tileRows == 1 ? n : (n + 1) / 2;
            for (int r = 0; r < tileRows; r++)
            {
                int from = r * perRow, to = Mathf.Min(n, from + perRow);
                float rowW = -6;
                for (int i = from; i < to; i++) rowW += widths[i] + 6;
                float x = Mathf.Round((UiRig.StageW - rowW) / 2f);
                for (int i = from; i < to; i++)
                {
                    _tiles.Add(BuildTile(_tilesNode, i, x, _yTiles + r * (_tileH + 2), widths[i], _tileH));
                    x += widths[i] + 6;
                }
            }

            // the pointer marks, the band and the footer sit over everything
            _marks = Kit.Node("Marks", Root, 0, 0, UiRig.StageW, UiRig.StageH);
            _band = Kit.Node("Band", Root, 0, 0, UiRig.StageW, 44);
            _bandGroup = Kit.Group(_band);
            _foot = Kit.Node("Foot", Root, 0, 322, UiRig.StageW, 38);

            // the SKIP button is a hit before the stage-wide one, so a click on it is not also "next"
            var skip = Kit.Node("Skip", _foot, R - 52, 4, 52, 20);
            _skipFrame = Kit.Frame("Frame", skip, Pal.Rust, 0, 0, 52, 20);
            _skipText = Kit.Label("Text", skip, SkipButton, 16, Pal.Rust, TextAnchor.MiddleCenter, 0, 1, 52, 16);
            AddHit(skip, Skip, over => { var c = over ? Pal.Bone : Pal.Rust; _skipFrame.color = c; _skipText.color = c; });
            AddHit(Root, Next);

            RefreshSlots();
        }

        RectTransform Section(string name, out CanvasGroup group)
        {
            var rt = Kit.Node(name, Root, 0, 0, UiRig.StageW, UiRig.StageH);
            group = Kit.Group(rt);
            group.alpha = 0;
            return rt;
        }

        static void Colon(Transform parent, float x, float y, Color c)
        {
            Kit.Box("Dot", parent, c, x, y, 3, 3);
            Kit.Box("Dot", parent, c, x, y + 10, 3, 3);
        }

        TileView BuildTile(Transform parent, int index, float x, float y, float w, float h)
        {
            var tile = _p.Tiles[index];
            var v = new TileView();
            v.Rt = Kit.Node("Tile" + index, parent, x, y, w, h);
            v.Fill = Kit.Box("Fill", v.Rt, Pal.Soot, 1, 1, w - 2, h - 2);
            v.Frame = Kit.Frame("Frame", v.Rt, Pal.Rust, 0, 0, w, h);
            v.Glyph = Kit.Pic("Glyph", v.Rt, GlyphGen.Texture(tile.GlyphSeed, _fam, Widgets.GlyphPx), Pal.Bone, (w - 32) / 2f, h >= 58 ? 6 : 2, 32, 32);
            v.Cap = Kit.Label("Sound", v.Rt, tile.Alien ?? "", 16, Pal.BoneDim, TextAnchor.MiddleCenter, 0, h - 19, w, 16);
            v.Key = Kit.Label("Key", v.Rt, KeyFor(index), 16, Pal.RustHot, TextAnchor.MiddleLeft, 5, 2, 10, 16);
            return v;
        }

        static string KeyFor(int index) => index < 9 ? (index + 1).ToString() : index == 9 ? "0" : "";

        void RefreshSlots()
        {
            Kit.Clear(_slotRow);
            _slotGlyphs.Clear();
            float x = 0;
            for (int i = 0; i < _placed; i++)
            {
                var tile = _p.Tiles[_ex.Answer[i]];
                float w = Widgets.Word(_slotRow, x, -2, tile, _fam, Pal.Bone, Pal.Bone, out var g, out _);
                _slotGlyphs.Add(g);
                x += w + 6;
            }
            var rowPos = _slotRow.anchoredPosition;
            Kit.Place(_slotCursor.rectTransform, rowPos.x + x + 2, -rowPos.y - 2, 32, 32);
            bool ready = _placed > 0;
            var c0 = ready ? Pal.Bone : Pal.Rust;
            _sendFrame.color = c0; _sendText.color = c0; _sendIcon.color = c0;
        }

        // ---- steps ----------------------------------------------------------------------

        bool Settled => T > 0.3f;
        bool AllPlaced => _placed >= _ex.Answer.Length;
        int NextTile => AllPlaced ? -1 : _ex.Answer[_placed];

        void SetStep(Step step)
        {
            _step = step; _stepT = 0;
            Kit.Clear(_marks); _pulse.Clear();
            if (step != Step.Build && step != Step.Decoded && _placed > 0) { _placed = 0; _writeT = 1; RefreshSlots(); }
            if (step == Step.Decoded && !AllPlaced) { _placed = _ex.Answer.Length; _writeT = 1; RefreshSlots(); }
            bool glossOn = step >= Step.Compare;
            foreach (var rv in _rows)
                foreach (var wv in rv.Words)
                {
                    if (wv.Gloss) wv.Gloss.gameObject.SetActive(glossOn);
                    else if (!_glossFits && wv.Meaning.Length > 0)
                    {
                        // no room for a line of its own: the meaning stands in for the word while we compare
                        bool swap = step == Step.Compare || step == Step.Rule;
                        Kit.SetText(wv.Cap, swap ? wv.Meaning : wv.Word);
                        wv.Cap.color = swap ? Pal.RustHot : Pal.BoneDim;
                    }
                }
            // the decode plate takes the slots' place: the glyphs have given up the real word
            _decode.gameObject.SetActive(step == Step.Decoded);
            _slotRow.gameObject.SetActive(step != Step.Decoded);
            if (step == Step.Decoded) BuildDecode();
            BuildBand();
            BuildFoot();
            BuildMarks();
        }

        void Next()
        {
            if (!Settled || _done) return;
            switch (_step)
            {
                case Step.Build:
                    if (AllPlaced) { Accept(); return; }
                    Place(NextTile);
                    return;
                case Step.Decoded:
                    Finish();
                    return;
                default:
                    SetStep((Step)((int)_step + 1));
                    Ui.Fx.Kick(burst: 0.12f, tear: 0.15f);
                    return;
            }
        }

        void Back()
        {
            if (!Settled || _done) return;
            if (_step == Step.Build && _placed > 0) { Undo(); return; }
            if (_step == Step.Barrier) return;
            SetStep((Step)((int)_step - 1));
            Ui.Fx.Kick(burst: 0.1f, tear: 0.1f);
        }

        void Skip()
        {
            if (!Settled || _done) return;
            Finish();
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            Ui.Fx.Kick(burst: 0.3f, tear: 0.4f);
            _onDone?.Invoke();
        }

        // ---- build step: the real keys -----------------------------------------------------

        void Place(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= _p.Tiles.Count || AllPlaced) return;
            if (tileIndex != NextTile) { Ui.Fx.Kick(tear: 0.3f, burst: 0.1f); return; }   // the wrong key: a flinch, not a misfire
            _placed++;
            _tiles[tileIndex].Flash = 1f;
            RefreshSlots();
            _writeShape = GlyphGen.Build(_p.Tiles[tileIndex].GlyphSeed, _fam);
            if (!_writeTex) _writeTex = GlyphGen.NewTexture(Widgets.GlyphPx);
            _writeT = 0;
            GlyphGen.Paint(_writeTex, _writeShape, 0f);
            _slotGlyphs[_slotGlyphs.Count - 1].texture = _writeTex;
            Ui.Fx.Kick(burst: 0.08f);
            BuildBand(); BuildFoot(); BuildMarks();
        }

        void Undo()
        {
            if (_placed == 0) return;
            _placed--; _writeT = 1;
            RefreshSlots();
            BuildBand(); BuildFoot(); BuildMarks();
        }

        void Accept()
        {
            Ui.Fx.Kick(burst: 0.45f, tear: 0.3f);
            SetStep(Step.Decoded);
        }

        // ---- the band across the top ------------------------------------------------------

        void BuildBand()
        {
            Kit.Clear(_band); _segs.Clear(); _bandT = 0;
            string tag; var lines = new List<string>();
            switch (_step)
            {
                case Step.Barrier: tag = TagBarrier; lines.Add(Barrier1); lines.Add(Barrier2); break;
                case Step.Rows: tag = TagRows; lines.Add(Rows1); lines.Add(Rows2); break;
                case Step.Compare: tag = TagCompare; lines.Add(Compare1); lines.Add(Fit(Glossary(), Compare2Fallback)); break;
                case Step.Rule: tag = TagRule; lines.AddRange(RuleLines(_glossFits ? 3 : 2)); break;
                case Step.Target: tag = TagTarget; lines.Add(Target1); lines.Add(_p.Tiles.Count > _ex.Answer.Length ? Target2Decoys : Target2Exact); break;
                case Step.Build:
                    tag = TagBuild;
                    if (AllPlaced) { lines.Add(BuildDone1); lines.Add(BuildDone2); }
                    else
                    {
                        var t = _p.Tiles[NextTile];
                        string key = KeyFor(NextTile), word = t.Alien ?? "", gloss = (t.Gloss ?? "").Trim().ToUpperInvariant();
                        string full = Build1.Replace("{KEY}", key).Replace("{WORD}", word).Replace("{GLOSS}", gloss);
                        string shortLine = Build1Short.Replace("{KEY}", key).Replace("{WORD}", word);
                        lines.Add(gloss.Length > 0 ? Fit(full, shortLine) : shortLine);
                        lines.Add(Build2);
                    }
                    break;
                default:
                    tag = TagDecoded;
                    string real = (_ex.RealText ?? "").Trim(), tr = (_ex.Translation ?? "").Trim().ToUpperInvariant(), lang = (_ex.LanguageName ?? "").ToUpperInvariant();
                    lines.Add(Fit(Decoded1.Replace("{REAL}", real).Replace("{TRANSLATION}", tr), Decoded1Short.Replace("{REAL}", real)));
                    lines.Add(Fit(Decoded2.Replace("{LANG}", lang), Decoded2Short.Replace("{LANG}", lang)));
                    break;
            }
            float h = lines.Count > 2 ? 62 : 44;
            Kit.Place(_band, 0, 0, UiRig.StageW, h);
            Kit.Box("Fill", _band, Pal.Void.A(0.96f), 0, 0, UiRig.StageW, h);
            Kit.Box("Rule", _band, Pal.Rust, 30, h - 1, UiRig.StageW - 60, 1);
            Kit.Label("Tag", _band, tag, 16, Pal.RustHot, TextAnchor.MiddleLeft, 30, 4, BandTextX - 34, 16);
            // (the tutorial layer draws HOLD Q · SKIP under the tag, at y 22, as it does on the console)
            int cursor = 0;
            for (int i = 0; i < lines.Count; i++) Line(_band, lines[i], BandTextX, 4 + i * 18, BandTextW, Pal.Bone, ref cursor, _segs);
        }

        /// <summary>The first candidate that fits the band's text width.</summary>
        static string Fit(params string[] candidates)
        {
            foreach (var c in candidates) if (c != null && Measure(c) <= BandTextW) return c;
            return candidates[candidates.Length - 1] ?? "";
        }

        /// <summary>"Feuer = FIRE  ·  Werk = WORK": each distinct example word and its meaning, or null.</summary>
        string Glossary()
        {
            var seen = new HashSet<string>();
            var parts = new List<string>();
            foreach (var rv in _rows)
                foreach (var wv in rv.Words)
                    if (wv.Meaning.Length > 0 && seen.Add(wv.Word)) parts.Add(wv.Word + " = " + wv.Meaning);
            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }

        /// <summary>The rule, in capitals, cut to its first sentence or two and wrapped to at most <paramref name="maxLines"/>.</summary>
        List<string> RuleLines(int maxLines)
        {
            string rule = (_ex.Rule ?? "").Trim();
            var sentences = new List<string>();
            int start = 0;
            for (int i = 0; i < rule.Length; i++)
            {
                bool end = (rule[i] == '.' || rule[i] == ':' || rule[i] == ';') && (i + 1 == rule.Length || rule[i + 1] == ' ');
                if (end) { sentences.Add(rule.Substring(start, i + 1 - start).TrimEnd(':', ';').Trim()); start = i + 1; }
            }
            if (start < rule.Length) sentences.Add(rule.Substring(start).Trim());
            var lines = new List<string>();
            string acc = "";
            foreach (var s in sentences)
            {
                string next = (acc.Length == 0 ? s : acc + " " + s).ToUpperInvariant();
                var wrapped = Wrap(next, BandTextW);
                if (wrapped.Count > maxLines) break;
                acc = next; lines = wrapped;
            }
            if (lines.Count == 0)
            {
                lines = Wrap(rule.ToUpperInvariant(), BandTextW);
                if (lines.Count > maxLines) { lines.RemoveRange(maxLines, lines.Count - maxLines); lines[maxLines - 1] = Trunc(lines[maxLines - 1], BandTextW); }
            }
            if (lines.Count == 1 && !lines[0].EndsWith(".")) lines[0] += ".";
            return lines;
        }

        static List<string> Wrap(string s, float width)
        {
            var lines = new List<string>();
            string cur = "";
            foreach (var word in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string probe = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && Measure(probe) > width) { lines.Add(cur); cur = word; }
                else cur = probe;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        static string Trunc(string s, float width)
        {
            while (s.Length > 1 && Measure(s + "…") > width) s = s.Substring(0, s.Length - 1).TrimEnd();
            return s.TrimEnd(',', '.', ';', ':') + "…";
        }

        /// <summary>Width of a line with its [KEY] caps, as Line lays it out.</summary>
        static float Measure(string line)
        {
            float w = 0; int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf('[', i), close = open >= 0 ? line.IndexOf(']', open) : -1;
                if (open < 0 || close < 0) { w += FontBook.Width(line.Substring(i), 16); break; }
                if (open > i) w += FontBook.Width(line.Substring(i, open - i), 16);
                w += Widgets.KeycapWidth(line.Substring(open + 1, close - open - 1)) + 4;
                i = close + 1;
            }
            return w;
        }

        /// <summary>Lays a line of text and [KEY] caps out centred in a box, as type-on segments (full text kept, label starts empty).</summary>
        void Line(Transform parent, string line, float x, float y, float w, Color c, ref int cursor, List<Seg> into)
        {
            float lw = Measure(line);
            float lx = x + Mathf.Round((w - lw) / 2f);
            int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf('[', i), close = open >= 0 ? line.IndexOf(']', open) : -1;
                if (open < 0 || close < 0) { lx += Run(parent, line.Substring(i), lx, y, c, ref cursor, into); break; }
                if (open > i) lx += Run(parent, line.Substring(i, open - i), lx, y, c, ref cursor, into);
                string key = line.Substring(open + 1, close - open - 1);
                var cap = Widgets.Keycap(parent, key, lx, y);
                cap.gameObject.SetActive(false);
                into.Add(new Seg { Cap = cap, From = cursor, Len = 1 });
                cursor += 2;
                lx += cap.sizeDelta.x + 4;
                i = close + 1;
            }
        }

        float Run(Transform parent, string s, float x, float y, Color c, ref int cursor, List<Seg> into)
        {
            if (s.Length == 0) return 0;
            float w = FontBook.Width(s, 16);
            var t = Kit.Label("Run", parent, "", 16, c, TextAnchor.MiddleLeft, x, y + 1, w + 4, 16);
            into.Add(new Seg { Text = t, Full = s, From = cursor, Len = s.Length });
            cursor += s.Length;
            return w;
        }

        // ---- the footer: keys and progress -------------------------------------------------

        void BuildFoot()
        {
            // keep the SKIP button (first child) and rebuild the rest
            for (int i = _foot.childCount - 1; i >= 1; i--) { var ch = _foot.GetChild(i); ch.SetParent(null, false); Kit.Kill(ch.gameObject); }
            _pips.Clear(); _footSegs.Clear();
            int cursor = -1000;    // the footer is not typed on: it is complete from the first frame
            if (_step == Step.Decoded)
                Widgets.Cue(_foot, 6, KeysDone, Pal.Amber);
            else
                Line(_foot, _step == Step.Build ? KeysBuild : Keys, L, 4, R - L - 60, Pal.BoneDim, ref cursor, _footSegs);
            float px = Mathf.Round((UiRig.StageW - (StepCount * 10 - 4)) / 2f);
            for (int i = 0; i < StepCount; i++)
            {
                var c = i < (int)_step ? Pal.Bone : i == (int)_step ? Pal.RustHot : Pal.BoneDim.A(0.5f);
                _pips.Add(Kit.Box("Pip", _foot, c, px + i * 10, 23, 6, 4));
            }
        }

        // ---- pointers -------------------------------------------------------------------

        void BuildMarks()
        {
            Kit.Clear(_marks); _pulse.Clear();
            if (_rows.Count == 0) return;
            float exTop = _rows[0].Y - 2, exBottom = _rows[_rows.Count - 1].Y + (_glossFits ? 64 : 48);
            switch (_step)
            {
                case Step.Rows:
                case Step.Compare:
                case Step.Rule:
                    Bracket(16, exTop, exBottom);
                    break;
                case Step.Target:
                    Bracket(16, _yAnswer - 2, _yAnswer + 46);
                    Chevron(_tiles[0].Rt.anchoredPosition.x + 12, _yTiles - 3);
                    Chevron(_tiles[_tiles.Count - 1].Rt.anchoredPosition.x + _tiles[_tiles.Count - 1].Rt.sizeDelta.x - 16, _yTiles - 3);
                    break;
                case Step.Build:
                    if (!AllPlaced)
                    {
                        var rt = _tiles[NextTile].Rt;
                        Chevron(rt.anchoredPosition.x + rt.sizeDelta.x / 2f - 2, -rt.anchoredPosition.y - 3);
                    }
                    else
                    {
                        // SEND: a bracket on its right, like the tutorial's PuzzleSend cue
                        Bracket(618, _yAnswer + 4, _yAnswer + 48, false);
                    }
                    break;
            }
        }

        void Bracket(float x, float y0, float y1, bool left = true)
        {
            float h = Mathf.Max(8, y1 - y0);
            _pulse.Add(Kit.Box("Spine", _marks, Pal.RustHot, x + (left ? 4 : 0), y0, 2, h));
            _pulse.Add(Kit.Box("Tick", _marks, Pal.RustHot, x, y0, 6, 2));
            _pulse.Add(Kit.Box("Tick", _marks, Pal.RustHot, x, y1 - 2, 6, 2));
        }

        void Chevron(float x, float yTip)
        {
            _pulse.Add(Kit.Box("Arrow", _marks, Pal.RustHot, x, yTip - 6, 5, 2));
            _pulse.Add(Kit.Box("Arrow", _marks, Pal.RustHot, x + 1, yTip - 4, 3, 2));
            _pulse.Add(Kit.Box("Arrow", _marks, Pal.RustHot, x + 2, yTip - 2, 1, 2));
        }

        // ---- the decode: the slots give up the real text ---------------------------------------

        void BuildDecode()
        {
            Kit.Clear(_decode);
            float w = _decode.sizeDelta.x, h = _decode.sizeDelta.y;
            Lore.SplitReal(_ex.RealText, out var phrase, out var reading);
            string tr = (_ex.Translation ?? "").Trim().ToUpperInvariant();
            Kit.Box("Fill", _decode, Pal.WarmDark.A(0.92f), 1, 1, w - 2, h - 2);
            Kit.Frame("Frame", _decode, Pal.Amber, 0, 0, w, h);
            string second = reading.Length > 0 ? reading + (tr.Length > 0 ? "  ·  " + tr : "") : tr;
            if (second.Length > 0)
            {
                Kit.Label("Real", _decode, phrase, 16, Pal.Ivory, TextAnchor.MiddleCenter, 0, 9, w, 16);
                Kit.Label("Reading", _decode, second, 16, Pal.Amber, TextAnchor.MiddleCenter, 0, 30, w, 16);
            }
            else Kit.Label("Real", _decode, phrase, 16, Pal.Ivory, TextAnchor.MiddleCenter, 0, 19, w, 16);
        }

        // ---- tick -----------------------------------------------------------------------------

        protected override void OnTick(float dt, UiInput input)
        {
            _stepT += dt; _bandT += dt;

            // boot: the console comes up with a stutter, then each step relights its part
            float boot = Kit.Ramp(T, 0.05f, 0.25f);
            float flicker = boot >= 1 ? 1 : (boot > 0 && Mathf.Repeat(T * 31f, 1f) > 0.4f ? boot : 0);
            for (int i = 0; i < _rows.Count; i++)
            {
                float target = RowAlpha(i);
                _rows[i].Group.alpha = Mathf.MoveTowards(_rows[i].Group.alpha, target, dt * 4f) * flicker;
                // the glosses fade in one word after another
                foreach (var wv in _rows[i].Words)
                    if (wv.Gloss && wv.Gloss.gameObject.activeSelf)
                    {
                        int k = _rows[i].Words.IndexOf(wv);
                        float a = _step == Step.Compare ? Kit.Ramp(_stepT, 0.5f + (i * _rows[i].Words.Count + k) * 0.25f, 0.2f) : 1f;
                        wv.Gloss.color = Pal.RustHot.A(a);
                    }
            }
            _ansGroup.alpha = Mathf.MoveTowards(_ansGroup.alpha, _step >= Step.Target ? 1f : _step == Step.Barrier ? 0.45f : 0.3f, dt * 4f) * flicker;
            _tilesGroup.alpha = Mathf.MoveTowards(_tilesGroup.alpha, _step == Step.Target || _step == Step.Build ? 1f : _step == Step.Barrier ? 0.45f : _step == Step.Decoded ? 0.4f : 0.3f, dt * 4f) * flicker;
            if (_decode.gameObject.activeSelf) _decodeGroup.alpha = Kit.Ramp(_stepT, 0.25f, 0.4f);

            // the band types itself on
            float a0 = Kit.Ramp(_bandT, 0f, 0.14f);
            int chars = (int)((_bandT - 0.14f) * TypeRate);
            TypeOn(_segs, chars); TypeOn(_footSegs, chars);
            _bandGroup.alpha = a0 >= 1 ? 1 : (Mathf.Repeat(_bandT * 31f, 1f) > 0.4f ? a0 : 0);

            // pointers breathe
            float pa = _stepT < 0.14f ? 0f : 0.55f + 0.45f * Mathf.PingPong(_stepT * 1.4f, 1f);
            for (int i = 0; i < _pulse.Count; i++) if (_pulse[i]) _pulse[i].color = Pal.RustHot.A(pa);
            if (_pips.Count > (int)_step) _pips[(int)_step].color = Color.Lerp(Pal.Rust, Pal.RustHot, Kit.Blink(T, 1.6f) ? 1 : 0.4f);

            // tiles: pressed flash; the one to press next lifts its frame to bone
            for (int i = 0; i < _tiles.Count; i++)
            {
                var v = _tiles[i];
                v.Flash = Mathf.MoveTowards(v.Flash, 0, dt * 4f);
                bool next = _step == Step.Build && i == NextTile;
                v.Fill.color = Color.Lerp(next ? Pal.RustDeep : Pal.Soot, Pal.Bone, v.Flash * 0.8f);
                v.Frame.color = next ? Pal.Bone : Pal.Rust;
                v.Cap.color = next ? Pal.Bone : Pal.BoneDim;
                v.Glyph.color = Color.Lerp(Pal.Bone, Pal.Void, v.Flash);
            }

            // the newest glyph writes itself on, stroke by stroke
            if (_writeT < 1f && _slotGlyphs.Count > 0)
            {
                _writeT = Mathf.Min(1f, _writeT + dt / 0.22f);
                if (_writeT >= 1f) _slotGlyphs[_slotGlyphs.Count - 1].texture = GlyphGen.Texture(_p.Tiles[_ex.Answer[_placed - 1]].GlyphSeed, _fam, Widgets.GlyphPx);
                else GlyphGen.Paint(_writeTex, _writeShape, _writeT);
            }
            _slotCursor.enabled = _step == Step.Build && !AllPlaced && Kit.Blink(T, 1.6f);
            if (_step == Step.Decoded) _slotCursor.enabled = false;
            if (_step == Step.Build && AllPlaced)
            {
                var c = Kit.Blink(_stepT, 2f) ? Pal.Bone : Pal.RustHot;
                _sendFrame.color = c; _sendText.color = c; _sendIcon.color = c;
                _sendFill.color = Pal.RustDeep.A(Kit.Blink(_stepT, 2f) ? 0.6f : 0.2f);
            }
            else _sendFill.color = Pal.Clear;
            if (_step == Step.Decoded) { _sendFrame.color = Pal.Amber; _sendText.color = Pal.Amber; _sendIcon.color = Pal.Amber; }

            if (_done || !Settled) return;
            if (input.Digit >= 0) { if (_step == Step.Build) Place(input.Digit); }
            else if (input.Enter || input.Space || input.Right) Next();
            else if (input.Back || input.Left) Back();
            else if (input.Clear) Skip();
        }

        static void TypeOn(List<Seg> segs, int chars)
        {
            foreach (var s in segs)
            {
                int n = Mathf.Clamp(chars - s.From, 0, s.Len);
                if (s.Text) Kit.SetText(s.Text, n >= s.Len ? s.Full : s.Full.Substring(0, n));
                if (s.Cap && s.Cap.gameObject.activeSelf != (n > 0)) s.Cap.gameObject.SetActive(n > 0);
            }
        }

        float RowAlpha(int i)
        {
            switch (_step)
            {
                case Step.Barrier: return 0.45f;
                case Step.Rows: return _stepT > 0.3f + i * 0.45f ? 1f : 0.3f;     // rows light one after another
                case Step.Compare:
                case Step.Rule: return 1f;
                case Step.Target: return 0.4f;
                case Step.Build: return 0.6f;
                default: return 0.5f;
            }
        }
    }
}

using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The first-run tutorial, drawn over whatever screen is open. The Game thread decides which cue is
    /// live (TutorialView, every frame); this layer only says it and points at it. One card per cue, two
    /// short lines at most, keys drawn as keycaps, and a hold-to-skip line in a corner.
    ///
    /// Where the card sits depends on the screen under it:
    ///   shore (visor)      centred, just under the E prompt box (y 268)
    ///   console            a band across the top (y 0-44, over the console's header), with a leader
    ///                      down the stage margin to a bracket beside the rows, the tiles or SEND
    ///   reveal / quiz      centred at the top, above TRANSMISSION DECODED (y 10-56)
    /// </summary>
    internal sealed class TutorialLayer : UiScreen
    {
        // ---- copy ---------------------------------------------------------------------------
        // [X] draws X as a keycap. {SOLVED} and {TOTAL} are filled from the view.
        // The console cues follow the worked example (WorkedExampleScreen), so they refer back to it and stay short.
        // Nothing here may call the language invented: the words are real, only the glyphs are alien.

        sealed class Copy
        {
            public string Tag;
            public string[] Lines;
            public Copy(string tag, params string[] lines) { Tag = tag; Lines = lines; }
        }

        static readonly Dictionary<TutorialCue, Copy> Script = new Dictionary<TutorialCue, Copy>
        {
            { TutorialCue.Move, new Copy("VISOR",
                "[W][A][S][D] WALK   ·   MOUSE LOOK",
                "[SHIFT] RUN   ·   [F] LAMP") },
            { TutorialCue.Objective, new Copy("OBJECTIVE",
                "WAKE THE SHIP: DECODE {TOTAL} CONSOLES.",
                "FOLLOW THE PALE BEACON. THE TOP LINE POINTS THE WAY.") },
            { TutorialCue.Console, new Copy("CONSOLE",
                "READ IT RIGHT AND ONE CELL OF SHIP POWER WAKES.",
                "READ IT WRONG AND THE GLASS CRACKS. THE BEAST STIRS.") },
            { TutorialCue.PuzzleRead, new Copy("THE ROWS",
                "SAME AS THE EXAMPLE: A MEANING, THEN ITS REAL WORDS.",
                "COMPARE THE ROWS. FIND THE RULE.") },
            { TutorialCue.PuzzleTiles, new Copy("THE TILES",
                "THE TILES ARE THE WORDS. PRESS [1] [2] [3] TO PLACE ONE.",
                "BUILD THE BOTTOM ROW'S MEANING, IN ORDER.") },
            { TutorialCue.PuzzleSend, new Copy("SEND",
                "[ENTER] SEND   ·   [BKSP] TAKE ONE BACK",
                "A MISFIRE CRACKS THE GLASS AND NAMES THE RULE YOU BROKE.") },
            { TutorialCue.Misfire, new Copy("MISFIRE",
                "WRONG. THE HINT BELOW NAMES THE RULE YOU BROKE.",
                "READ THE ROWS AGAIN: WHICH WORDS, IN WHICH ORDER?") },
            { TutorialCue.Reveal, new Copy("DECODED",
                "THE WORDS WERE REAL ALL ALONG. ONLY THE GLYPHS WERE ALIEN.",
                "READ THE RULE, THEN NAME THE LANGUAGE.") },
            { TutorialCue.Quiz, new Copy("QUIZ",
                "WHICH LANGUAGE WAS IT? PRESS ITS NUMBER [1] [2] [3] [4]",
                "NO PENALTY HERE. GUESS IF YOU MUST.") },
            { TutorialCue.Beast, new Copy("THE BEAST",
                "IT GROWS WITH TIME, AND FASTER WITH EVERY MISFIRE.",
                "LOOK AT IT AND IT FREEZES. FOR NOW.") },
            { TutorialCue.NextConsole, new Copy("OBJECTIVE",
                "{SOLVED} OF {TOTAL} CONSOLES WOKEN.",
                "THE REST, THEN THE SHIP. FOLLOW THE TOP LINE.") },
        };

        // In a headset (XrBridge.Active) the cues that name keys say which Touch button does it instead.
        // Everything else is the same copy. The layout is Fnmw.XR's TouchMapper.
        static readonly Dictionary<TutorialCue, Copy> XrScript = new Dictionary<TutorialCue, Copy>
        {
            { TutorialCue.Move, new Copy("VISOR",
                "[L-STICK] WALK   ·   [R-STICK] TURN",
                "TRY BOTH NOW   ·   [L-GRIP] RUN   ·   [X] LAMP") },
            { TutorialCue.Objective, new Copy("OBJECTIVE",
                "WAKE THE SHIP: DECODE {TOTAL} CONSOLES.",
                "FOLLOW THE CHEVRON ON THE GROUND. CONTROLS: LEFT WRIST.") },
            { TutorialCue.PuzzleTiles, new Copy("THE TILES",
                "THE TILES ARE THE WORDS. AIM AT ONE, PULL [TRIGGER].",
                "BUILD THE BOTTOM ROW'S MEANING, IN ORDER.") },
            { TutorialCue.PuzzleSend, new Copy("SEND",
                "[A] SEND   ·   [B] TAKE ONE BACK",
                "A MISFIRE CRACKS THE GLASS AND NAMES THE RULE YOU BROKE.") },
            { TutorialCue.Quiz, new Copy("QUIZ",
                "WHICH LANGUAGE WAS IT? AIM AT IT, PULL [TRIGGER].",
                "NO PENALTY HERE. GUESS IF YOU MUST.") },
        };

        public const string SkipText = "HOLD Q  ·  SKIP TUTORIAL";
        public const string SkipShort = "HOLD Q · SKIP";            // on the console, in the band's left column
        public const string SkipTextXr = "HOLD Y  ·  SKIP TUTORIAL";
        public const string SkipShortXr = "HOLD Y · SKIP";

        static Copy CopyFor(TutorialCue cue)
        {
            if (XrBridge.Active && XrScript.TryGetValue(cue, out var xr)) return xr;
            return Script.TryGetValue(cue, out var c) ? c : null;
        }

        /// <summary>Every string the layer can emit, for the font-coverage test.</summary>
        public static IEnumerable<string> AllCopy()
        {
            yield return SkipText; yield return SkipShort; yield return SkipTextXr; yield return SkipShortXr;
            foreach (var c in Script.Values) { yield return c.Tag; foreach (var l in c.Lines) yield return l; }
            foreach (var c in XrScript.Values) { yield return c.Tag; foreach (var l in c.Lines) yield return l; }
        }

        /// <summary>The card's words as they will read once typed on (tests).</summary>
        public string TextShown
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var s in _segs) sb.Append(s.Text ? s.Text.name : s.Cap ? "[" + s.Cap.name + "]" : "");
                return sb.ToString();
            }
        }

        // ---- layout constants -----------------------------------------------------------------

        const int Line = 18, Pad = 12, TagH = 16;
        const int ShoreY = 268, TopY = 10, BandH = 44, BandTextX = 150;
        const float TypeRate = 60f;      // characters per second
        const float InDur = 0.14f, OutDur = 0.10f;

        enum Spot { Shore, Band, Top }

        sealed class Seg { public Text Text; public RectTransform Cap; public int From, Len; }

        TutorialView _view;
        float _age = 99f;            // seconds since the Game thread last sent a view
        TutorialCue _shown = TutorialCue.None;
        Spot _spot;
        float _cueT, _outT = -1f;
        int _solvedShown = -1, _totalShown = -1;
        bool _bracketsBuilt;

        RectTransform _card, _skip, _pointers;
        CanvasGroup _cardGroup, _skipGroup;
        Text _skipText;
        Image _skipBar;
        readonly List<Seg> _segs = new List<Seg>();
        readonly List<Image> _pulse = new List<Image>();

        public TutorialLayer(GameUI ui) : base(ui, "Tutorial") { }
        public override GlassMode Glass => GlassMode.Visor;      // unused: the layer never owns the glass

        public void Build()
        {
            Open();
            _card = Kit.Node("Card", Root, 0, 0, 10, 10);
            _cardGroup = Kit.Group(_card);
            _pointers = Kit.Node("Pointers", Root, 0, 0, UiRig.StageW, UiRig.StageH);
            _skip = Kit.Node("Skip", Root, 0, 0, 200, 20);
            _skipGroup = Kit.Group(_skip);
            float sw = FontBook.Width(SkipText, 16);
            _skipText = Kit.Label("Text", _skip, SkipText, 16, Pal.BoneDim, TextAnchor.MiddleRight, 0, 0, 200, 16);
            _skipBar = Kit.Box("Bar", _skip, Pal.RustHot, 200 - sw, 17, 0, 1);
            _skip.gameObject.SetActive(false);
            _card.gameObject.SetActive(false);
            Root.gameObject.SetActive(false);
        }

        /// <summary>The Game thread's view for this frame.</summary>
        public void Show(TutorialView view) { _view = view; _age = 0; }

        public string CueShown => _shown.ToString();

        /// <summary>Drops whatever is on screen at once (the layer is being switched off: title, death, escape).</summary>
        public void Hide()
        {
            _shown = TutorialCue.None; _outT = -1; _view = default;
            Kit.Clear(_card); _segs.Clear(); Kit.Clear(_pointers); _pulse.Clear();
            _card.gameObject.SetActive(false); _skip.gameObject.SetActive(false);
        }

        // ---- tick -----------------------------------------------------------------------------

        protected override void OnTick(float dt, UiInput input)
        {
            _age += dt;
            bool live = _view.Active && _age < 0.5f && Ui.TutorialAllowed;
            var cue = live ? _view.Cue : TutorialCue.None;
            if (cue != TutorialCue.None && CopyFor(cue) == null) cue = TutorialCue.None;
            var spot = SpotFor(Ui.Front);

            // a cue that changed (or moved screens) flickers out before the next one boots in
            bool changed = cue != _shown || (cue != TutorialCue.None && spot != _spot)
                || (cue == TutorialCue.NextConsole && (_view.Solved != _solvedShown || _view.Total != _totalShown));
            if (changed && _shown != TutorialCue.None && _outT < 0) _outT = 0;
            if (_outT >= 0)
            {
                _outT += dt;
                _cardGroup.alpha = Mathf.Repeat(_outT * 37f, 1f) > 0.5f ? 1f : 0f;
                Pointers(0f);
                if (_outT < OutDur) { Skip(dt, live); return; }
                _outT = -1;
                _shown = TutorialCue.None;
                _card.gameObject.SetActive(false);
                Kit.Clear(_pointers); _pulse.Clear();
            }
            if (cue != _shown)
            {
                _shown = cue; _spot = spot; _cueT = 0;
                _solvedShown = _view.Solved; _totalShown = _view.Total;
                if (cue != TutorialCue.None) BuildCard(CopyFor(cue), spot);
            }

            if (_shown != TutorialCue.None)
            {
                _cueT += dt;
                // boot: a stutter, then the lines type themselves on
                float a = Kit.Ramp(_cueT, 0f, InDur);
                _cardGroup.alpha = a >= 1 ? 1 : (Mathf.Repeat(_cueT * 31f, 1f) > 0.4f ? a : 0);
                int chars = (int)((_cueT - InDur) * TypeRate);
                foreach (var s in _segs)
                {
                    int n = Mathf.Clamp(chars - s.From, 0, s.Len);
                    if (s.Text) Kit.SetText(s.Text, n >= s.Len ? s.Text.name : s.Text.name.Substring(0, n));
                    if (s.Cap && s.Cap.gameObject.activeSelf != (n > 0)) s.Cap.gameObject.SetActive(n > 0);
                }
                // the bracket breathes, so it reads as a pointer and not a border
                Pointers(_cueT < InDur ? 0f : 0.55f + 0.45f * Mathf.PingPong(_cueT * 1.4f, 1f));
            }
            Skip(dt, live);
        }

        void Pointers(float a)
        {
            for (int i = 0; i < _pulse.Count; i++) if (_pulse[i]) _pulse[i].color = Pal.RustHot.A(a);
        }

        void Skip(float dt, bool live)
        {
            bool show = live;
            if (_skip.gameObject.activeSelf != show) _skip.gameObject.SetActive(show);
            if (!show) return;
            // under the tag in the band's left column while the console is open; bottom right elsewhere
            bool console = Ui.Front is PuzzleScreen || Ui.Front is WorkedExampleScreen;   // both have the band's left column free under the tag
            bool xr = XrBridge.Active;
            string text = console ? (xr ? SkipShortXr : SkipShort) : (xr ? SkipTextXr : SkipText);
            if (_skipText.text != text) Kit.SetText(_skipText, text);
            _skipText.alignment = console ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            Kit.Place(_skip, console ? 30 : 410, console ? 22 : 330, 200, 20);
            float hold = Mathf.Clamp01(_view.SkipHold);
            float sw = FontBook.Width(text, 16);
            _skipText.color = Color.Lerp(Pal.BoneDim.A(0.75f), Pal.Bone, hold);
            Kit.Place(_skipBar.rectTransform, console ? 0 : 200 - sw, 17, Mathf.Round(sw * hold), 1);
            _skipBar.enabled = hold > 0;
            _skipGroup.alpha = hold > 0 ? 1f : 0.85f;
        }

        static Spot SpotFor(UiScreen front)
        {
            if (front is PuzzleScreen || front is WorkedExampleScreen) return Spot.Band;
            if (front is DecodeScreen) return Spot.Top;
            return Spot.Shore;
        }

        // ---- the card -------------------------------------------------------------------------

        void BuildCard(Copy copy, Spot spot)
        {
            Kit.Clear(_card); _segs.Clear();
            Kit.Clear(_pointers); _pulse.Clear();
            _card.gameObject.SetActive(true);

            var lines = new List<List<Seg>>();
            float cardW = 0; int cursor = 0;
            foreach (var raw in copy.Lines)
            {
                string line = raw.Replace("{SOLVED}", _view.Solved.ToString()).Replace("{TOTAL}", _view.Total.ToString());
                var segs = Parse(line, ref cursor, out float w);
                lines.Add(segs);
                cardW = Mathf.Max(cardW, w);
            }
            cardW = Mathf.Ceil((cardW + Pad * 2) / 2f) * 2f;
            int n = lines.Count;
            float cardH = Pad - 2 + n * Line + 6;

            float x, y;
            switch (spot)
            {
                case Spot.Band:
                    // across the top of the console, over its header; the tag takes the left column
                    x = 0; y = 0; cardW = UiRig.StageW; cardH = BandH;
                    break;
                case Spot.Top:
                    x = Mathf.Round((UiRig.StageW - cardW) / 2f); y = TopY;
                    break;
                default:
                    x = Mathf.Round((UiRig.StageW - cardW) / 2f); y = ShoreY;
                    break;
            }
            Kit.Place(_card, x, y, cardW, cardH);

            bool band = spot == Spot.Band;
            Kit.Box("Fill", _card, Pal.Void.A(band ? 0.96f : 0.84f), band ? 0 : 1, band ? 0 : 1, cardW - (band ? 0 : 2), cardH - (band ? 0 : 2));
            if (!band) Kit.Frame("Frame", _card, Pal.Rust, 0, 0, cardW, cardH);
            else Kit.Box("Rule", _card, Pal.Rust, 30, BandH - 1, UiRig.StageW - 60, 1);

            // the tag sits on the card's top edge like a label on a specimen jar; on the band it heads the left column
            float tw = FontBook.Width(copy.Tag, 16);
            if (band) Kit.Label("Tag", _card, copy.Tag, 16, Pal.RustHot, TextAnchor.MiddleLeft, 30, 4, BandTextX - 34, TagH);
            else
            {
                Kit.Box("TagBack", _card, Pal.Void, 8, -TagH / 2f, tw + 12, TagH);
                Kit.Label("Tag", _card, copy.Tag, 16, Pal.RustHot, TextAnchor.MiddleLeft, 14, -TagH / 2f, tw + 4, TagH);
            }

            float ly = band ? 4 : Pad - 2;
            float textL = band ? BandTextX : 0, textW = band ? UiRig.StageW - 30 - BandTextX : cardW;
            for (int i = 0; i < n; i++)
            {
                float lw = 0;
                foreach (var s in lines[i]) lw += s.Cap ? s.Cap.sizeDelta.x + 4 : FontBook.Width(s.Text.name, 16);
                float lx = textL + Mathf.Round((textW - lw) / 2f);
                foreach (var s in lines[i])
                {
                    if (s.Cap)
                    {
                        s.Cap.SetParent(_card, false);
                        Kit.Place(s.Cap, lx, ly, s.Cap.sizeDelta.x, Line);
                        lx += s.Cap.sizeDelta.x + 4;
                        s.Cap.gameObject.SetActive(false);
                    }
                    else
                    {
                        float w = FontBook.Width(s.Text.name, 16);
                        s.Text.rectTransform.SetParent(_card, false);
                        Kit.Place(s.Text.rectTransform, lx, ly + 1, w + 4, 16);
                        Kit.SetText(s.Text, "");
                        lx += w;
                    }
                    _segs.Add(s);
                }
                ly += Line;
            }

            if (band) BuildPointers(_shown);
        }

        /// <summary>Splits a line into text runs and [KEY] caps, built loose (parented to the layer's root) for the caller to place.</summary>
        List<Seg> Parse(string line, ref int cursor, out float width)
        {
            var segs = new List<Seg>();
            width = 0;
            int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf('[', i);
                int close = open >= 0 ? line.IndexOf(']', open) : -1;
                if (open < 0 || close < 0) { AddText(segs, line.Substring(i), ref cursor, ref width); break; }
                if (open > i) AddText(segs, line.Substring(i, open - i), ref cursor, ref width);
                string key = line.Substring(open + 1, close - open - 1);
                float kw = Mathf.Max(18, FontBook.Width(key, 16) + 8);
                var cap = Kit.Node(key, Root, 0, 0, kw, Line);
                Kit.Box("Fill", cap, Pal.Ash, 1, 1, kw - 2, Line - 2);
                Kit.Frame("Frame", cap, Pal.Bone, 0, 0, kw, Line);
                Kit.Label("Key", cap, key, 16, Pal.Bone, TextAnchor.MiddleCenter, 0, 1, kw, 16);
                segs.Add(new Seg { Cap = cap, From = cursor, Len = 1 });
                cursor += 2;
                width += kw + 4;
                i = close + 1;
            }
            return segs;
        }

        void AddText(List<Seg> segs, string s, ref int cursor, ref float width)
        {
            if (s.Length == 0) return;
            // the node's name holds the full string; the label itself is typed on from empty
            var t = Kit.Label(s, Root, "", 16, Pal.Bone, TextAnchor.MiddleLeft, 0, 0, 10, 16);
            segs.Add(new Seg { Text = t, From = cursor, Len = s.Length });
            cursor += s.Length;
            width += FontBook.Width(s, 16);
        }

        // ---- pointers on the console ----------------------------------------------------------

        /// <summary>Reads the open console's geometry back from its nodes, so nothing here depends on its arithmetic.</summary>
        bool ConsoleGeometry(out float yExTop, out float yExBottom, out float yAnswer, out float xTile0, out float xTileEnd, out float yTiles, out float yTilesEnd)
        {
            yExTop = 48; yExBottom = 196; yAnswer = 204; xTile0 = 200; xTileEnd = 440; yTiles = 262; yTilesEnd = 322;
            var puzzle = Ui.Puzzle;
            if (puzzle == null || !puzzle.Active) return false;
            var root = puzzle.Root;
            var slots = root.Find("Answer/Slots") as RectTransform;
            if (slots) yAnswer = -slots.anchoredPosition.y;
            yExBottom = yAnswer - 8;
            var tiles = root.Find("Tiles");
            if (tiles && tiles.childCount > 0)
            {
                xTile0 = float.MaxValue; xTileEnd = 0; yTiles = float.MaxValue; yTilesEnd = 0;
                for (int i = 0; i < tiles.childCount; i++)
                {
                    var rt = tiles.GetChild(i) as RectTransform;
                    if (!rt) continue;
                    xTile0 = Mathf.Min(xTile0, rt.anchoredPosition.x);
                    xTileEnd = Mathf.Max(xTileEnd, rt.anchoredPosition.x + rt.sizeDelta.x);
                    yTiles = Mathf.Min(yTiles, -rt.anchoredPosition.y);
                    yTilesEnd = Mathf.Max(yTilesEnd, -rt.anchoredPosition.y + rt.sizeDelta.y);
                }
            }
            return true;
        }

        void BuildPointers(TutorialCue cue)
        {
            ConsoleGeometry(out float exTop, out float exBottom, out float yAnswer, out float xT0, out float xT1, out float yTiles, out float yTilesEnd);
            // a leader runs from the band down the stage margin to a bracket beside what the words mean
            switch (cue)
            {
                case TutorialCue.PuzzleRead:
                case TutorialCue.Misfire:
                    Leader(18, BandH, exTop);
                    Bracket(16, exTop, exBottom, true);
                    break;
                case TutorialCue.PuzzleTiles:
                    Leader(18, BandH, yTiles);
                    Bracket(16, yTiles, yTilesEnd, true);
                    Chevron(xT0 + 12, yTiles - 3);
                    Chevron(xT1 - 16, yTiles - 3);
                    break;
                case TutorialCue.PuzzleSend:
                    Leader(620, BandH, yAnswer + 4);
                    Bracket(618, yAnswer + 4, yAnswer + 48, false);
                    break;
            }
        }

        void Bracket(float x, float y0, float y1, bool left)
        {
            float h = Mathf.Max(8, y1 - y0);
            _pulse.Add(Kit.Box("Spine", _pointers, Pal.RustHot, x + (left ? 4 : 0), y0, 2, h));
            _pulse.Add(Kit.Box("Tick", _pointers, Pal.RustHot, x, y0, 6, 2));
            _pulse.Add(Kit.Box("Tick", _pointers, Pal.RustHot, x, y1 - 2, 6, 2));
        }

        void Leader(float x, float y0, float y1)
        {
            if (y1 - y0 < 4) return;
            _pulse.Add(Kit.Box("Leader", _pointers, Pal.RustHot, x, y0, 2, y1 - y0));
        }

        void Chevron(float x, float yTip)
        {
            // a small arrow head pointing down at the tiles: 5, 3, 1 pixels wide, tip at yTip
            _pulse.Add(Kit.Box("Arrow", _pointers, Pal.RustHot, x, yTip - 6, 5, 2));
            _pulse.Add(Kit.Box("Arrow", _pointers, Pal.RustHot, x + 1, yTip - 4, 3, 2));
            _pulse.Add(Kit.Box("Arrow", _pointers, Pal.RustHot, x + 2, yTip - 2, 1, 2));
        }
    }
}

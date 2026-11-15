using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The console. Each example sentence is a mural row (meaning plate :: one glyph per word); the target
    /// sentence is one more row the player completes by stamping tiles into it, word by word. A wrong
    /// answer is not labelled: the glyphs melt, the glass cracks and blood runs down it.
    /// Placement is worked out by <see cref="PuzzleLayout"/>.
    /// </summary>
    internal sealed class PuzzleScreen : UiScreen
    {
        const float L = PuzzleLayout.L, R = PuzzleLayout.R;
        const int MaxSlots = PuzzleLayout.MaxSlots;

        sealed class TileView
        {
            public RectTransform Rt; public Image Frame, Fill; public RawImage Glyph; public Text Cap, Key;
            public float Flash; public int Row;
        }

        LangPuzzle _p;
        Action<string[]> _onSubmit;
        ScriptFamily _fam;
        readonly List<int> _picks = new List<int>();
        readonly List<TileView> _tiles = new List<TileView>();
        readonly List<RawImage> _slotGlyphs = new List<RawImage>();
        readonly List<Text> _slotCaps = new List<Text>();
        readonly List<RectTransform> _stagger = new List<RectTransform>();
        RectTransform _slotRow;
        PuzzleLayout _lay;
        Image _slotCursor, _sendFrame, _sendFill;
        Text _sendText, _msg;
        RawImage _sendIcon;
        int _cursor;
        float _wrongT = -1f, _msgT = -1f, _writeT = 1f;
        string _feedback;
        Texture2D _writeTex;
        GlyphShape _writeShape;
        public const string HintKeys = "KEYS PLACE  ·  BKSP UNDO  ·  ENTER SEND";
        public const string HintXr = "TRIGGER PLACES  ·  B UNDO  ·  A SENDS";
        public const string HintKeysWithShip = HintKeys + "  ·  H SHIP";
        public const string HintXrWithShip = HintXr + "  ·  X SHIP";
        /// <summary>The footer's key line: the keyboard's, or the Touch buttons' in a headset.</summary>
        public static string Hint => XrBridge.Active ? HintXr : HintKeys;
        /// <summary>The key line, offering the ship's hint (H on a keyboard, X on Touch) when <paramref name="offerShip"/>.</summary>
        public static string Legend(bool offerShip) => offerShip ? (XrBridge.Active ? HintXrWithShip : HintKeysWithShip) : Hint;
        int _wrong;                       // wrong answers on this console: after the first, the legend offers the ship
        string _footer = "", _hintText;   // what the footer shows; the hint text being typed out
        float _hintT;

        public PuzzleScreen(GameUI ui) : base(ui, "Puzzle") { }
        public override GlassMode Glass => GlassMode.Console;

        public LangPuzzle Puzzle => _p;
        public ScriptFamily Family => _fam;
        public int PickCount => _picks.Count;
        public bool Melting => _wrongT >= 0;
        public string FooterShown => _footer;
        /// <summary>Asking the ship would do something right now (the console takes input and hints exist).</summary>
        public bool ShipOffered => Interactive && _p != null && VoiceHintsAvailable();

        public List<Tile> PickedTiles()
        {
            var list = new List<Tile>();
            if (_p != null) foreach (int i in _picks) list.Add(_p.Tiles[i]);
            return list;
        }

        public void Show(LangPuzzle puzzle, Action<string[]> onSubmit, ScriptFamily fam)
        {
            Open();
            _p = puzzle; _onSubmit = onSubmit; _fam = fam;
            _picks.Clear(); _tiles.Clear(); _stagger.Clear(); _slotGlyphs.Clear(); _slotCaps.Clear();
            _cursor = 0; _wrongT = -1; _msgT = -1; _feedback = null; _writeT = 1;
            _wrong = 0; _footer = ""; _hintText = null; _hintT = 0;
            Build();
        }

        public override void Close()
        {
            base.Close();
            Kit.Kill(_writeTex); _writeTex = null;
        }

        // ---- layout ---------------------------------------------------------------------

        void Build()
        {
            var state = Ui.State ?? Services.State;
            var world = state?.World;

            // header: the tongue's name, and how much of the ship is awake
            var head = Section("Head");
            Kit.Label("Tongue", head, (_p.AlienName ?? world?.AlienName ?? "UNKNOWN TONGUE").ToUpperInvariant(), 16, Pal.RustHot, TextAnchor.MiddleLeft, L, 14, 300, 16);
            int total = state != null ? state.TotalPuzzles : 0, solved = state != null ? state.PuzzlesSolved : 0;
            Widgets.Power(head, R - 140 - Widgets.PowerWidth(total), 14, solved, total, Pal.Bone);
            Kit.Box("Rule", head, Pal.Rust, L, 36, R - L, 1);

            _lay = PuzzleLayout.For(_p);
            float plateW = _lay.PlateW, wordX = _lay.WordX;

            // the mural: one row per example sentence
            for (int i = 0; i < _lay.Rows.Length; i++)
            {
                var e = _p.Examples[i];
                var row = _lay.Rows[i];
                var node = Section("Example" + i);
                Widgets.Plate(node, row.Plate.x, row.Plate.y, plateW, plateW, PuzzleLayout.PlateH, e.Picture, false);
                Colon(node, row.X - 13, row.Plate.y + 14, Pal.Rust);
                var words = Kit.Node("Words", node, row.X, row.Y, row.Room, Widgets.WordH);
                words.localScale = new Vector3(row.Scale, row.Scale, 1f);
                for (int k = 0; k < e.Words.Count; k++)
                    Widgets.Word(words, row.LocalX[k], 0, e.Words[k], _fam, Pal.Bone, Pal.BoneDim, out _, out _);
            }

            // the row the player must complete
            var ans = Section("Answer");
            float yAnswer = _lay.YAnswer;
            Kit.Box("AnswerRule", ans, Pal.RustDeep, L, yAnswer - 4, R - L, 1);
            Widgets.Plate(ans, L, yAnswer, plateW, plateW, PuzzleLayout.PlateH, _p.TargetMeaning, true);
            Colon(ans, wordX - 13, yAnswer + 14, Pal.Bone);
            _slotRow = Kit.Node("Slots", ans, _lay.Slots.x, _lay.Slots.y, _lay.Slots.width, _lay.Slots.height);
            _slotCursor = Kit.Frame("Cursor", ans, Pal.RustHot, 0, 0, 32, 32);

            var sr = _lay.Send;
            var send = Kit.Node("Send", ans, sr.x, sr.y, sr.width, sr.height);
            _sendFill = Kit.Box("Fill", send, Pal.Clear, 1, 1, sr.width - 2, sr.height - 2);
            _sendFrame = Kit.Frame("Frame", send, Pal.Rust, 0, 0, sr.width, sr.height);
            _sendIcon = Kit.Pic("Key", send, Pictograms.Get("enter"), Pal.Rust, 6, 10, 13, 13);
            _sendText = Kit.Label("Text", send, "SEND", 16, Pal.Rust, TextAnchor.MiddleLeft, 24, 8, 36, 16);
            AddHit(send, Submit);

            // tiles
            var tiles = Section("Tiles");
            for (int i = 0; i < _lay.Tiles.Length; i++)
            {
                var r = _lay.Tiles[i];
                _tiles.Add(BuildTile(tiles, i, r.x, r.y, r.width, r.height, _lay.TileRow[i]));
            }

            var foot = Section("Foot");
            _msg = Kit.Label("Message", foot, Hint, 16, Pal.BoneDim, TextAnchor.UpperCenter, L, 324, R - L, 34, true);
            Kit.Pitch(_msg, 17);

            RefreshSlots();
            RefreshTiles();
        }

        RectTransform Section(string name)
        {
            var rt = Kit.Node(name, Root, 0, 0, UiRig.StageW, UiRig.StageH);
            Kit.Group(rt).alpha = 0;
            _stagger.Add(rt);
            return rt;
        }

        static void Colon(Transform parent, float x, float y, Color c)
        {
            Kit.Box("Dot", parent, c, x, y, 3, 3);
            Kit.Box("Dot", parent, c, x, y + 10, 3, 3);
        }

        TileView BuildTile(Transform parent, int index, float x, float y, float w, float h, int row)
        {
            var tile = _p.Tiles[index];
            var v = new TileView { Row = row };
            v.Rt = Kit.Node("Tile" + index, parent, x, y, w, h);
            v.Fill = Kit.Box("Fill", v.Rt, Pal.Soot, 1, 1, w - 2, h - 2);
            v.Frame = Kit.Frame("Frame", v.Rt, Pal.Rust, 0, 0, w, h);
            int g = PuzzleLayout.TileGlyph(h);   // two tile rows are shorter, so their glyphs are smaller
            v.Glyph = Kit.Pic("Glyph", v.Rt, GlyphGen.Texture(tile.GlyphSeed, _fam, g), Pal.Bone, (w - g) / 2f, h >= 58 ? 6 : h >= 50 ? 2 : 1, g, g);
            v.Cap = Kit.Label("Sound", v.Rt, tile.Alien ?? "", 16, Pal.BoneDim, TextAnchor.MiddleCenter, 0, h >= 50 ? h - 19 : h - 16, w, 16);
            string key = index < 9 ? (index + 1).ToString() : index == 9 ? "0" : "";
            v.Key = Kit.Label("Key", v.Rt, key, 16, Pal.RustHot, TextAnchor.MiddleLeft, 5, 2, 10, 16);
            int captured = index;
            AddHit(v.Rt, () => Place(captured), over => { if (over && Interactive) { _cursor = captured; RefreshTiles(); } });
            return v;
        }

        void RefreshSlots()
        {
            Kit.Clear(_slotRow);
            _slotGlyphs.Clear(); _slotCaps.Clear();
            // a long sentence first closes up its gaps, then shrinks, so it never runs into SEND
            var widths = new float[_picks.Count];
            for (int i = 0; i < widths.Length; i++) widths[i] = Widgets.WordWidth(_p.Tiles[_picks[i]]);
            PuzzleLayout.Fit(widths, _lay.SlotRoom(_picks.Count), out float gap, out float scale);
            _slotRow.localScale = new Vector3(scale, scale, 1f);
            float x = 0;
            for (int i = 0; i < _picks.Count; i++)
            {
                var tile = _p.Tiles[_picks[i]];
                float w = Widgets.Word(_slotRow, x, -2, tile, _fam, Pal.Bone, Pal.Bone, out var g, out var c);
                _slotGlyphs.Add(g); _slotCaps.Add(c);
                x += w + gap;
            }
            var rowPos = _slotRow.anchoredPosition;
            Kit.Place(_slotCursor.rectTransform, rowPos.x + x * scale + 2, -rowPos.y - 2, 32, 32);
            bool ready = _picks.Count > 0;
            var c0 = ready ? Pal.Bone : Pal.Rust;
            _sendFrame.color = c0; _sendText.color = c0; _sendIcon.color = c0;
        }

        void RefreshTiles()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                var v = _tiles[i];
                bool on = i == _cursor;
                v.Frame.color = on ? Pal.Bone : Pal.Rust;
                v.Cap.color = on ? Pal.Bone : Pal.BoneDim;
            }
        }

        // ---- behaviour ------------------------------------------------------------------

        bool Interactive => Active && _wrongT < 0 && T > 0.3f;

        public void Place(int tileIndex)
        {
            if (!Interactive || _p == null || tileIndex < 0 || tileIndex >= _p.Tiles.Count) return;
            if (_picks.Count >= MaxSlots) { Ui.Fx.Kick(tear: 0.25f); return; }
            _picks.Add(tileIndex);
            _cursor = tileIndex;
            _tiles[tileIndex].Flash = 1f;
            RefreshSlots(); RefreshTiles();
            GameEvents.RaiseTilePicked(_p.Tiles[tileIndex]);
            // the newest glyph writes itself on, stroke by stroke
            _writeShape = GlyphGen.Build(_p.Tiles[tileIndex].GlyphSeed, _fam);
            if (!_writeTex) _writeTex = GlyphGen.NewTexture(Widgets.GlyphPx);
            _writeT = 0;
            GlyphGen.Paint(_writeTex, _writeShape, 0f);
            _slotGlyphs[_slotGlyphs.Count - 1].texture = _writeTex;
        }

        public void Undo()
        {
            if (!Interactive || _picks.Count == 0) return;
            _picks.RemoveAt(_picks.Count - 1);
            _writeT = 1;
            RefreshSlots();
            GameEvents.RaiseTileRemoved();
        }

        public void ClearPicks()
        {
            if (!Interactive) return;
            _picks.Clear(); _writeT = 1;
            RefreshSlots();
            GameEvents.RaiseTilesCleared();
        }

        public void Submit()
        {
            if (!Interactive) return;
            if (_picks.Count == 0) { Ui.Fx.Kick(tear: 0.3f, burst: 0.1f); return; }
            var ids = new string[_picks.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = _p.Tiles[_picks[i]].Id;
            Ui.RememberAnswer(PickedTiles());
            _onSubmit?.Invoke(ids);   // the director answers with ShowAnswerFeedback or ShowReveal
        }

        /// <summary>H: ask the ship. Costs nothing but time; the voice director decides what (if anything) to show.</summary>
        public void RequestHint()
        {
            if (!Interactive || _p == null) return;
            GameEvents.RaiseHintRequested(_p.Id);
            try { Services.Voice.RequestHint(_p.Id); } catch (Exception e) { Debug.LogException(e); }
        }

        void SetFooter(string text)
        {
            if (text == _footer) return;
            _footer = text;
            Kit.SetText(_msg, text);
        }

        /// <summary>Hint, then subtitle, then the key legend. Long text pages two lines at a time.</summary>
        void FooterIdle(float dt)
        {
            var hint = Ui.Hint;
            if (hint.Visible && _p != null && hint.PuzzleId == _p.Id)
            {
                if (hint.Pending)
                {
                    _hintText = null;
                    SetFooter("SHIP: ANALYSING" + (Kit.Blink(T, 3f) ? "…" : ""));
                    _msg.color = Pal.Amber;
                    return;
                }
                string tag = hint.Source == VoiceSource.Mock ? "SHIP (MOCK): " : "SHIP: ";
                string full = (tag + (hint.Text ?? "")).ToUpperInvariant();
                if (full != _hintText) { _hintText = full; _hintT = 0; }
                _hintT += dt;
                var pages = SubtitleLayer.Pages(full, R - L, 2);
                float typed = _hintT * 60f;
                int pageIndex = 0; float before = 0;
                // type the first page out, then turn pages every five seconds
                if (pages.Count > 1 && _hintT > pages[0].Length / 60f + 5f) pageIndex = (int)((_hintT - pages[0].Length / 60f) / 5f) % pages.Count;
                string page = pages.Count > 0 ? pages[pageIndex] : "";
                if (pageIndex == 0) { int chars = Mathf.Min(page.Length, (int)(typed - before)); page = page.Substring(0, Mathf.Max(0, chars)); }
                SetFooter(page);
                _msg.color = Pal.Amber;
                return;
            }
            _hintText = null;
            string sub = Ui.SubtitleText;
            if (sub != null)
            {
                var pages = SubtitleLayer.Pages(sub, R - L, 2);
                SetFooter(pages.Count == 0 ? "" : pages[Mathf.Min(pages.Count - 1, (int)(Ui.SubtitleAge / 4f))]);
                _msg.color = Pal.RustHot;
                return;
            }
            bool offer = _wrong > 0 && VoiceHintsAvailable();
            SetFooter(Legend(offer));
            _msg.color = Pal.BoneDim;
        }

        static bool VoiceHintsAvailable()
        {
            try { return Services.Voice != null && Services.Voice.HintsAvailable; } catch (Exception) { return false; }
        }

        /// <summary>The machine rejects the phrase.</summary>
        public void Reject(AnswerResult result)
        {
            if (!Active) return;
            _wrongT = 0;
            _wrong++;
            _feedback = result?.Feedback;
            _msgT = 0;
            Ui.Fx.Kick(shake: 1f, burst: 0.75f, tear: 0.9f, red: 0.55f);
            Ui.Fx.Bleed();
            Ui.Fx.Crack(new Vector2(UnityEngine.Random.Range(0.15f, 0.85f), UnityEngine.Random.Range(0.42f, 0.9f)));   // above the tile row
        }

        protected override void OnTick(float dt, UiInput input)
        {
            // boot: sections come up one after another, with a stutter
            for (int i = 0; i < _stagger.Count; i++)
            {
                float a = Kit.Ramp(T, 0.05f + i * 0.07f, 0.12f);
                var g = Kit.Group(_stagger[i]);
                g.alpha = a >= 1 ? 1 : (a > 0 && Mathf.Repeat(T * 31f + i, 1f) > 0.4f ? a : 0);
            }

            // write-on of the newest glyph
            if (_writeT < 1f && _slotGlyphs.Count > 0)
            {
                _writeT = Mathf.Min(1f, _writeT + dt / 0.22f);
                if (_writeT >= 1f) _slotGlyphs[_slotGlyphs.Count - 1].texture = GlyphGen.Texture(_p.Tiles[_picks[_picks.Count - 1]].GlyphSeed, _fam, Widgets.GlyphPx);
                else GlyphGen.Paint(_writeTex, _writeShape, _writeT);
            }

            // tiles: pressed flash, and a tremor in the frame as the beast closes in
            for (int i = 0; i < _tiles.Count; i++)
            {
                var v = _tiles[i];
                v.Flash = Mathf.MoveTowards(v.Flash, 0, dt * 4f);
                var baseFill = i == _cursor ? Pal.RustDeep : Pal.Soot;
                v.Fill.color = Color.Lerp(baseFill, Pal.Bone, v.Flash * 0.8f);
                v.Glyph.color = _wrongT >= 0 ? Color.Lerp(Pal.Bone, Pal.Blood, Mathf.PingPong(_wrongT * 6f, 1f)) : Color.Lerp(Pal.Bone, Pal.Void, v.Flash);
            }

            _slotCursor.enabled = _wrongT < 0 && _picks.Count < MaxSlots && Kit.Blink(T, 1.6f);

            // the wrong answer melts out of its slots
            if (_wrongT >= 0)
            {
                _wrongT += dt;
                for (int i = 0; i < _slotGlyphs.Count; i++)
                {
                    float t = Mathf.Max(0, _wrongT - i * 0.06f);
                    var g = _slotGlyphs[i];
                    if (!g) continue;
                    var rt = g.rectTransform;
                    rt.sizeDelta = new Vector2(32, 32 + t * t * 90f);            // the glyph runs like wet paint
                    g.color = Color.Lerp(Pal.Bone, Pal.Blood, Mathf.Clamp01(t * 5f)).A(1f - Kit.Ramp(t, 0.55f, 0.45f));
                    if (_slotCaps[i]) _slotCaps[i].color = Pal.Blood.A(1f - Kit.Ramp(t, 0.3f, 0.5f));
                }
                if (_wrongT > 1.15f) { _wrongT = -1; _picks.Clear(); RefreshSlots(); }
            }

            // the footer: the machine's own words (a rejection) first, then the ship's hint, then a spoken line, then the keys
            if (_msgT >= 0)
            {
                _msgT += dt;
                string full = string.IsNullOrEmpty(_feedback) ? "" : _feedback.ToUpperInvariant();
                int chars = Mathf.Min(full.Length, (int)(_msgT * 48f));
                SetFooter(full.Substring(0, chars) + (chars < full.Length && Kit.Blink(_msgT, 9f) ? "█" : ""));
                _msg.color = Color.Lerp(Pal.Hex(0xFF5040), Pal.RustHot, Kit.Ramp(_msgT, 2.5f, 3f));
                if (_msgT > 9f) _msgT = -1;
            }
            else FooterIdle(dt);

            if (!Interactive) return;
            if (input.Hint) { RequestHint(); return; }
            if (input.Digit >= 0) Place(input.Digit);
            else if (input.Space) Place(_cursor);
            else if (input.Back) Undo();
            else if (input.Clear) ClearPicks();
            else if (input.Enter) Submit();
            else if (input.Left || input.Right)
            {
                int n = _tiles.Count;
                if (n > 0) { _cursor = (_cursor + (input.Right ? 1 : n - 1)) % n; RefreshTiles(); }
            }
            else if (input.Up || input.Down)
            {
                int row = _tiles.Count > 0 ? _tiles[_cursor].Row : 0;
                int firstOther = _tiles.FindIndex(v => v.Row != row);
                if (firstOther >= 0)
                {
                    int firstSame = _tiles.FindIndex(v => v.Row == row);
                    int count = _tiles.FindAll(v => v.Row != row).Count;
                    _cursor = firstOther + Mathf.Min(_cursor - firstSame, count - 1);
                    RefreshTiles();
                }
            }
        }
    }

    /// <summary>
    /// Where everything on the console goes, worked out from the puzzle alone so tests can check the worst
    /// cases fit. Stage coordinates, origin top-left, y down. Sized for the engine's sentence puzzles
    /// (Fnmw.Lang.PuzzleBuilder): 3 example sentences of up to ~6 words, meanings that wrap to two plate
    /// lines, 4-8 answer words and up to 10 tiles in at most two rows. A row of words that would overflow
    /// first closes its gaps, then shrinks as a whole; the plate column narrows before that happens.
    /// </summary>
    public sealed class PuzzleLayout
    {
        public const float L = 30, R = 610, Top = 40, Bottom = 322, Foot = 324;
        public const float Gap = 6, TightGap = 2, ColW = 292, PlateH = 44, CursorRoom = 36;
        /// <summary>Most tiles a player can place: every tile (PuzzleBuilder.MaxTiles, keys 1-9 then 0).</summary>
        public const int MaxSlots = 10;
        const float AnswerH = 54, RuleH = 4, MinRow = 46, MaxRow = 54, SendW = 62, SendGap = 8;
        const float PlateMin = 84, PlateMaxWide = 216, PlateMaxTwoCol = 120;

        public sealed class Row
        {
            public Rect Plate;
            public float X, Y, Room, Gap, Scale = 1f;   // words node origin, the width it may use, spacing and shrink
            public float[] Widths, LocalX;
            /// <summary>Stage rect of word <paramref name="i"/> after any shrink.</summary>
            public Rect Word(int i) => new Rect(X + LocalX[i] * Scale, Y, Widths[i] * Scale, Widgets.WordH * Scale);
        }

        public bool TwoCol;
        public int TileRows;
        public float PlateW, WordX, RowH, YEx, YAnswer, YTiles, TileH;
        public Row[] Rows;
        public Rect Slots, Send;
        public Rect[] Tiles;
        public int[] TileRow;

        /// <summary>Width the answer words may take with <paramref name="placed"/> tiles down (room for the cursor until full).</summary>
        public float SlotRoom(int placed) => Slots.width - (placed < MaxSlots ? CursorRoom : 0);

        /// <summary>Width of a placed or example word cell (glyph over caption).</summary>
        public static float WordWidth(Tile tile) => Widgets.WordWidth(tile);

        /// <summary>Tile glyph size for a tile height.</summary>
        public static int TileGlyph(float tileH) => tileH >= 50 ? Widgets.GlyphPx : 24;

        /// <summary>Spacing and shrink so a row of cells fits <paramref name="room"/>: normal gaps, then tight gaps, then scaled.</summary>
        public static void Fit(IList<float> widths, float room, out float gap, out float scale)
        {
            float sum = 0;
            int k = widths.Count;
            for (int i = 0; i < k; i++) sum += widths[i];
            gap = Gap; scale = 1f;
            if (k == 0 || sum + Gap * (k - 1) <= room) return;
            gap = TightGap;
            float tight = sum + TightGap * (k - 1);
            if (tight > room) scale = Mathf.Max(0.1f, room / tight);
        }

        static float Natural(IList<float> widths)
        {
            float sum = 0;
            for (int i = 0; i < widths.Count; i++) sum += widths[i] + (i > 0 ? Gap : 0);
            return sum;
        }

        /// <summary>Narrowest plate (from Widgets.PlateWidth's steps) that holds a meaning in two lines.</summary>
        static float TwoLineWidth(string meaning, float maxW)
        {
            string label = (meaning ?? "").Trim().ToUpperInvariant();
            if (label.Length == 0) return PlateMin;
            for (float w = 112; w <= maxW; w += 8) if (Kit.Lines(label, 16, w - 14) <= 2) return w;
            return maxW;
        }

        public static PuzzleLayout For(LangPuzzle p)
        {
            var lay = new PuzzleLayout();
            float span = R - L;

            // ---- vertical: Top..Bottom holds the example rows, the answer rule and row, and the tile rows
            int n = p.Tiles.Count;
            var tileW = new float[n];
            for (int i = 0; i < n; i++) tileW[i] = Mathf.Ceil(Mathf.Max(48, FontBook.Width(p.Tiles[i].Alien, 16) + 14) / 2f) * 2f;
            lay.TileRows = n > 7 || Natural(tileW) > span ? 2 : 1;

            int ex = Mathf.Min(p.Examples.Count, 6);
            lay.TwoCol = ex > 3;
            int exRows = lay.TwoCol ? (ex + 1) / 2 : ex;
            float room = Bottom - Top - RuleH - AnswerH;
            lay.TileH = Mathf.Clamp(Mathf.Floor(((room - exRows * MinRow) / lay.TileRows - 2) / 2f) * 2f, 40, 58);
            lay.RowH = exRows > 0 ? Mathf.Clamp(Mathf.Floor((room - lay.TileRows * (lay.TileH + 2)) / exRows), MinRow, MaxRow) : MaxRow;
            lay.YTiles = Bottom - lay.TileRows * (lay.TileH + 2);
            lay.YAnswer = lay.YTiles - AnswerH;
            lay.YEx = Top + Mathf.Max(0, Mathf.Floor((lay.YAnswer - RuleH - Top - exRows * lay.RowH) / 2f));

            // ---- plates: one width for the whole mural so the glyph columns line up; as wide as the words allow
            var wordW = new float[ex][];
            float widest = 0;
            for (int i = 0; i < ex; i++)
            {
                var words = p.Examples[i].Words;
                wordW[i] = new float[words.Count];
                for (int k = 0; k < words.Count; k++) wordW[i][k] = Widgets.WordWidth(words[k]);
                widest = Mathf.Max(widest, Natural(wordW[i]));
            }
            // the answer: assume the widest tiles, as many as an answer can hold
            var sorted = (float[])tileW.Clone();
            for (int i = 0; i < n; i++) sorted[i] = Widgets.WordWidth(p.Tiles[i]);
            Array.Sort(sorted); Array.Reverse(sorted);
            var answerGuess = new List<float>();
            for (int i = 0; i < Mathf.Min(n, 8); i++) answerGuess.Add(sorted[i]);
            float answerNeed = Natural(answerGuess) + CursorRoom + SendGap + SendW;

            float plateMax;
            if (lay.TwoCol) plateMax = PlateMaxTwoCol;
            else
            {
                float need = PlateMin;
                need = Mathf.Max(need, TwoLineWidth(p.TargetMeaning, PlateMaxWide));
                for (int i = 0; i < ex; i++) need = Mathf.Max(need, TwoLineWidth(p.Examples[i].Picture, PlateMaxWide));
                plateMax = Mathf.Clamp(Mathf.Min(span - 22 - widest, span - 22 - answerNeed), need, PlateMaxWide);
            }
            float plateW = Widgets.PlateWidth(p.TargetMeaning, plateMax);
            for (int i = 0; i < ex; i++) plateW = Mathf.Max(plateW, Widgets.PlateWidth(p.Examples[i].Picture, plateMax));
            lay.PlateW = plateW = Mathf.Clamp(plateW, PlateMin, plateMax);
            lay.WordX = L + plateW + 22;

            lay.Rows = new Row[ex];
            for (int i = 0; i < ex; i++)
            {
                int col = lay.TwoCol ? i % 2 : 0, r = lay.TwoCol ? i / 2 : i;
                float x = L + col * ColW, y = lay.YEx + r * lay.RowH;
                var row = new Row { Plate = new Rect(x, y, plateW, PlateH), X = x + plateW + 22, Y = y - 2, Widths = wordW[i] };
                row.Room = (lay.TwoCol && col == 0 ? L + ColW - Gap : R) - row.X;
                Fit(row.Widths, row.Room, out row.Gap, out row.Scale);
                row.LocalX = new float[row.Widths.Length];
                float wx = 0;
                for (int k = 0; k < row.Widths.Length; k++) { row.LocalX[k] = wx; wx += row.Widths[k] + row.Gap; }
                lay.Rows[i] = row;
            }

            // ---- answer row: slots, then SEND at the right edge
            lay.Slots = new Rect(lay.WordX, lay.YAnswer, R - SendW - SendGap - lay.WordX, 52);
            lay.Send = new Rect(R - SendW, lay.YAnswer + 8, SendW, 32);

            // ---- tiles: one row, or two of up to half each, centred on the stage
            lay.Tiles = new Rect[n];
            lay.TileRow = new int[n];
            int perRow = lay.TileRows == 1 ? n : (n + 1) / 2;
            for (int r = 0; r < lay.TileRows; r++)
            {
                int from = r * perRow, to = Mathf.Min(n, from + perRow);
                var widths = new List<float>();
                for (int i = from; i < to; i++) widths.Add(tileW[i]);
                Fit(widths, span, out float gap, out float scale);
                float rowW = -gap;
                for (int i = from; i < to; i++) rowW += Mathf.Floor(tileW[i] * scale) + gap;
                float x = Mathf.Round((UiRig.StageW - rowW) / 2f);
                for (int i = from; i < to; i++)
                {
                    float w = Mathf.Floor(tileW[i] * scale);
                    lay.Tiles[i] = new Rect(x, lay.YTiles + r * (lay.TileH + 2), w, lay.TileH);
                    lay.TileRow[i] = r;
                    x += w + gap;
                }
            }
            return lay;
        }
    }
}

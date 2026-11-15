using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The console. Example phrases are mural rows (meaning :: glyphs); the answer is one more row the
    /// player completes by stamping tiles into it. A wrong answer is not labelled: the glyphs melt, the
    /// glass cracks and blood runs down it.
    /// </summary>
    internal sealed class PuzzleScreen : UiScreen
    {
        const int L = 30, R = 610, MaxSlots = 8;

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
        Image _slotCursor, _sendFrame, _sendFill;
        Text _sendText, _msg;
        RawImage _sendIcon;
        int _cursor;
        float _wrongT = -1f, _msgT = -1f, _writeT = 1f;
        string _feedback;
        Texture2D _writeTex;
        GlyphShape _writeShape;
        const string Hint = "KEYS PLACE  ·  BKSP UNDO  ·  ENTER SEND";

        public PuzzleScreen(GameUI ui) : base(ui, "Puzzle") { }
        public override GlassMode Glass => GlassMode.Console;

        public LangPuzzle Puzzle => _p;
        public ScriptFamily Family => _fam;
        public int PickCount => _picks.Count;
        public bool Melting => _wrongT >= 0;

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

            // tile rows first: they decide how much room the mural gets
            int n = _p.Tiles.Count;
            var widths = new float[n];
            float sum = 0;
            for (int i = 0; i < n; i++) { widths[i] = Mathf.Ceil(Mathf.Max(48, FontBook.Width(_p.Tiles[i].Alien, 16) + 14) / 2f) * 2f; sum += widths[i] + 6; }
            int tileRows = sum - 6 > R - L ? 2 : 1;
            int tileH = tileRows == 1 ? 58 : 52;
            int yTiles = 322 - tileRows * (tileH + 2);
            int yAnswer = yTiles - 58;

            int ex = Mathf.Min(_p.Examples.Count, 6);
            bool twoCol = ex > 3 || (tileRows == 2 && ex > 2);
            int exRows = twoCol ? (ex + 1) / 2 : ex;
            int rowH = exRows > 0 ? Mathf.Clamp((yAnswer - 4 - 44) / exRows, 46, 54) : 52;
            int yEx = 44 + Mathf.Max(0, (yAnswer - 4 - 44 - exRows * rowH) / 2);

            // one plate width for the whole mural, so the glyph columns line up
            float plateMax = twoCol ? 120 : 216;
            float plateW = Widgets.PlateWidth(_p.TargetMeaning, plateMax);
            for (int i = 0; i < ex; i++) plateW = Mathf.Max(plateW, Widgets.PlateWidth(_p.Examples[i].Picture, plateMax));
            plateW = Mathf.Clamp(plateW, 84, plateMax);
            float wordX = L + plateW + 22;
            for (int i = 0; i < ex; i++)
            {
                var e = _p.Examples[i];
                int col = twoCol ? i % 2 : 0, row = twoCol ? i / 2 : i;
                var node = Section("Example" + i);
                float x = L + col * 292, y = yEx + row * rowH;
                Widgets.Plate(node, x, y, plateW, plateW, 44, e.Picture, false);
                float wx = x + plateW + 22;
                Colon(node, wx - 13, y + 14, Pal.Rust);
                foreach (var w in e.Words)
                    wx += Widgets.Word(node, wx, y - 2, w, _fam, Pal.Bone, Pal.BoneDim, out _, out _) + 6;
            }

            // the row the player must complete
            var ans = Section("Answer");
            Kit.Box("AnswerRule", ans, Pal.RustDeep, L, yAnswer - 4, R - L, 1);
            Widgets.Plate(ans, L, yAnswer, plateW, plateW, 44, _p.TargetMeaning, true);
            Colon(ans, wordX - 13, yAnswer + 14, Pal.Bone);
            _slotRow = Kit.Node("Slots", ans, wordX, yAnswer, R - wordX - 70, 52);
            _slotCursor = Kit.Frame("Cursor", ans, Pal.RustHot, 0, 0, 32, 32);

            var send = Kit.Node("Send", ans, R - 62, yAnswer + 8, 62, 32);
            _sendFill = Kit.Box("Fill", send, Pal.Clear, 1, 1, 60, 30);
            _sendFrame = Kit.Frame("Frame", send, Pal.Rust, 0, 0, 62, 32);
            _sendIcon = Kit.Pic("Key", send, Pictograms.Get("enter"), Pal.Rust, 6, 10, 13, 13);
            _sendText = Kit.Label("Text", send, "SEND", 16, Pal.Rust, TextAnchor.MiddleLeft, 24, 8, 36, 16);
            AddHit(send, Submit);

            // tiles
            var tiles = Section("Tiles");
            int perRow = tileRows == 1 ? n : (n + 1) / 2;
            for (int r = 0; r < tileRows; r++)
            {
                int from = r * perRow, to = Mathf.Min(n, from + perRow);
                float rowW = -6;
                for (int i = from; i < to; i++) rowW += widths[i] + 6;
                float x = Mathf.Round((UiRig.StageW - rowW) / 2f);
                for (int i = from; i < to; i++)
                {
                    _tiles.Add(BuildTile(tiles, i, x, yTiles + r * (tileH + 2), widths[i], tileH, r));
                    x += widths[i] + 6;
                }
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
            v.Glyph = Kit.Pic("Glyph", v.Rt, GlyphGen.Texture(tile.GlyphSeed, _fam, Widgets.GlyphPx), Pal.Bone, (w - 32) / 2f, h >= 58 ? 6 : 2, 32, 32);
            v.Cap = Kit.Label("Sound", v.Rt, tile.Alien ?? "", 16, Pal.BoneDim, TextAnchor.MiddleCenter, 0, h - 19, w, 16);
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
            float x = 0;
            for (int i = 0; i < _picks.Count; i++)
            {
                var tile = _p.Tiles[_picks[i]];
                float w = Widgets.Word(_slotRow, x, -2, tile, _fam, Pal.Bone, Pal.Bone, out var g, out var c);
                _slotGlyphs.Add(g); _slotCaps.Add(c);
                x += w + 6;
            }
            var rowPos = _slotRow.anchoredPosition;
            Kit.Place(_slotCursor.rectTransform, rowPos.x + x + 2, -rowPos.y - 2 + 0, 32, 32);
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
        }

        public void ClearPicks()
        {
            if (!Interactive) return;
            _picks.Clear(); _writeT = 1;
            RefreshSlots();
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

        /// <summary>The machine rejects the phrase.</summary>
        public void Reject(AnswerResult result)
        {
            if (!Active) return;
            _wrongT = 0;
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

            // the machine's own words, typed out
            if (_msgT >= 0)
            {
                _msgT += dt;
                string full = string.IsNullOrEmpty(_feedback) ? "" : _feedback.ToUpperInvariant();
                int chars = Mathf.Min(full.Length, (int)(_msgT * 48f));
                Kit.SetText(_msg, full.Substring(0, chars) + (chars < full.Length && Kit.Blink(_msgT, 9f) ? "█" : ""));
                _msg.color = Color.Lerp(Pal.Hex(0xFF5040), Pal.RustHot, Kit.Ramp(_msgT, 2.5f, 3f));
                if (_msgT > 9f) { _msgT = -1; Kit.SetText(_msg, Hint); _msg.color = Pal.BoneDim; }
            }

            if (!Interactive) return;
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
}

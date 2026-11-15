using System;
using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Core.Telemetry;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The QS consent screen: shown after the title, before the run starts, whenever the QS sender is configured and
    /// waiting for an answer. It prints the statement exactly as the server sent it (its hash is what gets stored), and
    /// nothing is recorded unless the player picks I AGREE.
    ///
    /// Input: the laser and trigger (Touch) or the mouse click either button; the stick or arrow keys move the
    /// highlight and scroll; A or Enter confirms the highlighted button. Nothing is highlighted at first, so a stray
    /// A press carried over from the title cannot agree on the player's behalf.
    /// </summary>
    internal sealed class ConsentScreen : UiScreen
    {
        public const string Title = "BEFORE YOU BEGIN";
        public const string Agree = "I AGREE";
        public const string Decline = "NO THANKS";
        const int TextX = 40, TextY = 50, TextW = 560, Pitch = 18, Visible = 12;

        Action<bool> _onAnswer;
        readonly List<string> _lines = new List<string>();
        readonly List<Text> _rows = new List<Text>();
        int _scroll, _focus = -1;          // -1 nothing highlighted, 0 agree, 1 decline
        RectTransform _agree, _decline, _more, _less;
        Text _wait;
        bool _shownStatement;

        public ConsentScreen(GameUI ui) : base(ui, "Consent") { }
        public override GlassMode Glass => GlassMode.Title;

        /// <summary>Which button is highlighted, for tests: "None", "Agree" or "Decline".</summary>
        public string FocusName => _focus == 0 ? "Agree" : _focus == 1 ? "Decline" : "None";
        public int LineCount => _lines.Count;

        public void Show(Action<bool> onAnswer)
        {
            Open();
            _onAnswer = onAnswer;
            _focus = -1; _scroll = 0; _shownStatement = false;
            _rows.Clear(); _lines.Clear();
            Kit.Box("Back", Root, Pal.Void.A(0.96f), 0, 0, UiRig.StageW, UiRig.StageH);
            Kit.Label("Title", Root, Title, 16, Pal.RustHot, TextAnchor.MiddleLeft, TextX, 18, 400, 16);
            Kit.Box("Rule", Root, Pal.Rust, TextX, 38, TextW, 1);
            for (int i = 0; i < Visible; i++)
                _rows.Add(Kit.Label("Line", Root, "", 16, Pal.Bone, TextAnchor.MiddleLeft, TextX, TextY + i * Pitch, TextW, 16));
            _wait = Kit.Label("Wait", Root, "FETCHING THE STATEMENT…", 16, Pal.BoneDim, TextAnchor.MiddleLeft, TextX, TextY, TextW, 16);
            _less = Arrow("Less", "▲ MORE ABOVE", TextY - 14);
            _more = Arrow("More", "▼ MORE BELOW", TextY + Visible * Pitch);
            _agree = Button("Agree", Agree, 160, () => Answer(true));
            _decline = Button("Decline", Decline, 340, () => Answer(false));
            Paint();
        }

        RectTransform Arrow(string name, string text, float y)
        {
            var rt = Kit.Node(name, Root, TextX, y, TextW, 14);
            Kit.Label("Text", rt, text, 16, Pal.BoneDim, TextAnchor.MiddleRight, 0, -1, TextW, 16);
            AddHit(rt, () => Scroll(name == "More" ? 3 : -3));
            return rt;
        }

        RectTransform Button(string name, string text, float x, Action click)
        {
            var rt = Kit.Node(name, Root, x, 306, 140, 26);
            Kit.Box("Fill", rt, Pal.Ash, 1, 1, 138, 24);
            Kit.Frame("Frame", rt, Pal.Rust, 0, 0, 140, 26);
            Kit.Label("Text", rt, text, 16, Pal.Bone, TextAnchor.MiddleCenter, 0, 5, 140, 16);
            AddHit(rt, click, over => { if (over) { _focus = name == "Agree" ? 0 : 1; Paint(); } });
            return rt;
        }

        void Answer(bool yes)
        {
            if (!Ready) return;
            var cb = _onAnswer; _onAnswer = null;
            cb?.Invoke(yes);
        }

        static QsTelemetry Sender => QsTelemetry.Instance;
        static bool Ready => Sender != null && Sender.WantsConsent;

        void Scroll(int by)
        {
            _scroll = Mathf.Clamp(_scroll + by, 0, Mathf.Max(0, _lines.Count - Visible));
            Paint();
        }

        /// <summary>Word-wrap the statement to the text column, keeping its own line breaks.</summary>
        static List<string> Wrap(string text, float width)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (para.Trim().Length == 0) { lines.Add(""); continue; }
                string indent = para.StartsWith("- ") ? "  " : "";
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string next = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length > 0 && FontBook.Width(next, 16) > width) { lines.Add(cur); cur = indent + word; }
                    else cur = next;
                }
                lines.Add(cur);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        void Paint()
        {
            bool ready = Ready;
            if (ready && !_shownStatement)
            {
                _shownStatement = true;
                _lines.Clear();
                _lines.AddRange(Wrap(Sender.ConsentStatement, TextW));
            }
            _wait.gameObject.SetActive(!ready);
            for (int i = 0; i < _rows.Count; i++)
            {
                int k = _scroll + i;
                Kit.SetText(_rows[i], ready && k < _lines.Count ? _lines[k] : "");
                _rows[i].color = k == 0 ? Pal.Amber : Pal.Bone;   // the statement's own title line
            }
            _less.gameObject.SetActive(ready && _scroll > 0);
            _more.gameObject.SetActive(ready && _scroll + Visible < _lines.Count);
            Style(_agree, _focus == 0, ready);
            Style(_decline, _focus == 1, ready);
        }

        static void Style(RectTransform b, bool focus, bool ready)
        {
            var frame = b.Find("Frame")?.GetComponent<Image>();
            var fill = b.Find("Fill")?.GetComponent<Image>();
            var text = b.Find("Text")?.GetComponent<Text>();
            if (frame) frame.color = !ready ? Pal.Ink : focus ? Pal.Bone : Pal.Rust;
            if (fill) fill.color = focus && ready ? Pal.RustDeep : Pal.Ash;
            if (text) text.color = ready ? Pal.Bone : Pal.BoneDim;
        }

        protected override void OnTick(float dt, UiInput input)
        {
            if (!_shownStatement && Ready) Paint();
            if (Sender == null || Sender.Current == QsTelemetry.State.Stopped)
            {
                // the statement could not be fetched (offline, server down): play on, nothing is recorded
                var cb = _onAnswer; _onAnswer = null; cb?.Invoke(false);
                return;
            }
            if (input.Up) Scroll(-1);
            if (input.Down) Scroll(1);
            if (input.Left) { _focus = 0; Paint(); }
            if (input.Right) { _focus = 1; Paint(); }
            if (T > 0.3f && input.Confirm && _focus >= 0) Answer(_focus == 0);
        }
    }
}

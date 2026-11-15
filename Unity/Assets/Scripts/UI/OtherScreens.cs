using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The visor while roaming. Almost nothing is written on it: a prompt when something can be used,
    /// the ship's power cells, and claw marks that appear as the beast changes. Threat itself is drawn
    /// by the glass (CrtFx), not here.
    /// </summary>
    internal sealed class HudLayer : UiScreen
    {
        RectTransform _prompt, _power, _objective;
        Text _promptText, _objText, _powerLabel, _clawLabel;
        Image _promptFrame, _promptFill, _rail, _railTick, _marker;
        readonly List<Image> _cells = new List<Image>();
        readonly List<Image> _claws = new List<Image>();
        string _promptShown, _objShown;
        int _solved = -1, _total = -1, _stage = 1;
        float _cellFlash, _clawT = 9f;

        public const string PowerLabel = "SHIP POWER";
        public const string ChaseObjective = "RUN TO THE SHIP";
        const float RailHalf = 70f, RailDegrees = 70f;

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
            _power = Kit.Node("Power", Root, 30, 312, 200, 23);
            _powerLabel = Kit.Label("PowerLabel", Root, PowerLabel, 16, Pal.BoneDim.A(0.8f), TextAnchor.MiddleLeft, 30, 316, 120, 16);
            for (int i = 0; i < 3; i++)
            {
                var claw = Kit.Box("Claw", Root, Pal.Blood, 520 + i * 18, 30, 4, 58);
                claw.rectTransform.localRotation = Quaternion.Euler(0, 0, -18);
                claw.enabled = false;
                _claws.Add(claw);
            }
            _clawLabel = Kit.Label("ClawLabel", Root, "", 16, Pal.Blood, TextAnchor.MiddleRight, 380, 96, 220, 16);
            _clawLabel.enabled = false;

            // where to go: a bearing rail at the top of the visor with the target's line and distance under it
            _objective = Kit.Node("Objective", Root, 0, 0, UiRig.StageW, 48);
            _rail = Kit.Box("Rail", _objective, Pal.Rust.A(0.8f), 320 - RailHalf, 14, RailHalf * 2, 1);
            Kit.Box("RailEnd", _objective, Pal.Rust.A(0.8f), 320 - RailHalf, 11, 1, 7);
            Kit.Box("RailEnd", _objective, Pal.Rust.A(0.8f), 320 + RailHalf - 1, 11, 1, 7);
            _railTick = Kit.Box("Centre", _objective, Pal.Rust, 320, 12, 1, 5);
            _marker = Kit.Box("Marker", _objective, Pal.Bone, 318, 10, 3, 9);
            _objText = Kit.Label("Text", _objective, "", 16, Pal.BoneDim, TextAnchor.MiddleCenter, 20, 22, 600, 16);
            _objective.gameObject.SetActive(false);
            _promptShown = null; _objShown = null; _solved = -1; _total = -1;
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

        public string ObjectiveShown => _objShown;

        /// <summary>The thing to walk to: the one console the director has switched on, or the ship in the final chase.</summary>
        static Interactable Target(GameState s) => Interactable.CurrentObjective(s);

        void TickObjective(GameState s)
        {
            var target = Target(s);
            bool show = target != null;
            if (_objective.gameObject.activeSelf != show) _objective.gameObject.SetActive(show);
            if (!show) { _objShown = null; return; }

            bool chase = s != null && s.Phase == GamePhase.Chase;
            int total = s != null ? s.TotalPuzzles : 0, next = s != null ? s.PuzzlesSolved + 1 : 1;
            string what = chase ? ChaseObjective : "CONSOLE " + Mathf.Clamp(next, 1, Mathf.Max(1, total)) + "/" + total;
            var player = Services.Player;
            string text = what;
            bool bearing = false; float angle = 0f;
            if (player)
            {
                Vector3 to = target.transform.position - player.position; to.y = 0;
                int metres = Mathf.RoundToInt(to.magnitude);
                text = what + "  \u00B7  " + metres + " M";
                var cam = Camera.main;
                if (cam && to.sqrMagnitude > 0.01f)
                {
                    Vector3 fwd = cam.transform.forward; fwd.y = 0;
                    if (fwd.sqrMagnitude > 1e-4f) { bearing = true; angle = Vector3.SignedAngle(fwd, to, Vector3.up); }
                }
            }
            if (text != _objShown) { _objShown = text; Kit.SetText(_objText, text); }
            _objText.color = chase ? Pal.Bone : Pal.BoneDim;

            // the marker slides along the rail with the target's bearing; off the ends it pins and blinks
            _rail.enabled = _railTick.enabled = _marker.enabled = bearing;
            if (!bearing) return;
            float t = Mathf.Clamp(angle / RailDegrees, -1f, 1f);
            Kit.Place(_marker.rectTransform, Mathf.Round(320 + t * (RailHalf - 2)) - 1, 10, 3, 9);
            bool behind = Mathf.Abs(angle) > RailDegrees;
            _marker.color = behind ? (Kit.Blink(T, 3f) ? Pal.Bone : Pal.Rust) : Pal.Bone;
        }

        protected override void OnTick(float dt, UiInput input)
        {
            var s = Ui.State ?? Services.State;
            TickObjective(s);
            if (s != null && (s.PuzzlesSolved != _solved || s.TotalPuzzles != _total))
            {
                if (_solved >= 0 && s.PuzzlesSolved > _solved) _cellFlash = 1f;
                _solved = s.PuzzlesSolved; _total = s.TotalPuzzles;
                Kit.Clear(_power); _cells.Clear();
                Kit.Box("Back", _power, Pal.Void.A(0.6f), 0, 0, Widgets.PowerWidth(_total) + 12, 23);
                Widgets.Power(_power, 6, 4, _solved, _total, Pal.Bone, _cells);
                Kit.Move(_powerLabel.rectTransform, 30 + Widgets.PowerWidth(_total) + 20, 316);
            }
            _powerLabel.color = Color.Lerp(Pal.BoneDim.A(0.8f), Pal.Amber, _cellFlash);
            _cellFlash = Mathf.MoveTowards(_cellFlash, 0, dt * 0.6f);
            if (_solved > 0 && _solved <= _cells.Count)
                _cells[_solved - 1].color = Color.Lerp(Pal.Bone, Pal.Amber, _cellFlash * Mathf.PingPong(T * 6f, 1f));

            if (_promptFrame) _promptFrame.color = Pal.Bone.A(0.55f + 0.45f * Mathf.PingPong(T * 1.6f, 1f));

            _clawT += dt;
            // a fresh set of claws says what it means, once, then fades
            bool naming = _stage > 1 && _clawT < 5f;
            if (_clawLabel.enabled != naming) _clawLabel.enabled = naming;
            if (naming)
            {
                Kit.SetText(_clawLabel, "THE BEAST EVOLVES  \u00B7  STAGE " + _stage + " OF 3");
                _clawLabel.color = Pal.Blood.A(Kit.Ramp(_clawT, 0.3f, 0.3f) * (1f - Kit.Ramp(_clawT, 4f, 1f)));
            }
            for (int i = 0; i < _claws.Count; i++)
            {
                bool on = i < _stage - 1;
                _claws[i].enabled = on;
                if (!on) continue;
                bool fresh = i == _stage - 2;
                float a = fresh ? Mathf.Lerp(1f, 0.7f, Kit.Ramp(_clawT, 0.4f, 2f)) : 0.7f;
                float len = fresh ? 58f * Kit.EaseOut(Kit.Ramp(_clawT, i * 0.05f, 0.18f)) : 58f;
                _claws[i].color = Pal.Blood.A(a);
                _claws[i].rectTransform.sizeDelta = new Vector2(4, len);
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
        public const string Line = "Unfortunately, after careful consideration, we decided to move forward with other candidates.";

        public void Show(Action onRestart)
        {
            Open();
            _onRestart = onRestart;
            _line = Kit.Label("Line", Root, Line, 16, Pal.Ivory, TextAnchor.MiddleCenter, 20, 154, 600, 48, wrap: true);
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
            var stops = new[] { (0f, Pal.Hex(0x0A0D1F)), (0.5f, Pal.Hex(0x1B1F45)), (0.78f, Pal.Hex(0x4A3460)), (0.845f, Pal.Hex(0xB86B62)), (0.87f, Pal.Hex(0xF2C79A)), (0.875f, Pal.Hex(0x3A0C16)), (1f, Pal.Hex(0x0E0408)) };
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
                var star = Kit.Box("Star", Root, Pal.Dawn, rng.Next(8, 632), rng.Next(8, 250), 1 + (i % 9 == 0 ? 1 : 0), 1 + (i % 9 == 0 ? 1 : 0));
                _stars.Add(star);
            }
            _trail = Kit.Box("Trail", Root, Pal.Amber.A(0.5f), 541, 200, 2, 60);
            _ship = Kit.Pic("Ship", Root, Pictograms.Get("ship"), Pal.Ivory, 529, 190, 26, 26).rectTransform;

            // a run decodes up to five tongues: with more than two, the list drops to two lines each
            int count = journal != null ? Mathf.Min(journal.Entries.Count, 5) : 0;
            bool roomy = count <= 2;
            float y = roomy ? 34 : 24;
            Fade(Kit.Label("Title", Root, "LIFT-OFF", 32, Pal.Ivory, TextAnchor.MiddleLeft, 44, y, 400, 32), 0.6f);
            y += 38;
            if (roomy)
            {
                Fade(Kit.Label("Sub", Root, TitleCopy.EscapeLine, 16, Pal.Dawn, TextAnchor.MiddleLeft, 44, y, 420, 16), 1.1f);
                y += 22;
            }
            if (state != null)
            {
                string stats = "CONSOLES " + state.PuzzlesSolved + "/" + state.TotalPuzzles + "  \u00B7  MISFIRES " + state.Mistakes + "  \u00B7  " + Lore.Clock(state.Elapsed);
                Fade(Kit.Label("Stats", Root, stats, 16, Pal.BoneDim, TextAnchor.MiddleLeft, 44, y, 420, 16), 1.5f);
            }
            y += roomy ? 30 : 24;
            Fade(Kit.Label("Tag", Root, count > 0 ? "WHAT YOU CARRY OUT" : "NO TONGUE DECODED THIS TIME", 16, Pal.Amber, TextAnchor.MiddleLeft, 44, y, 420, 16), 2.0f);
            Fade(Kit.Box("Rule", Root, Pal.Amber.A(0.6f), 44, y + 19, 250, 1), 2.0f);
            y += roomy ? 28 : 25;

            float at = 2.4f;
            for (int i = 0; i < count; i++)
            {
                var e = journal.Entries[i];
                float nw = FontBook.Width(e.Language.ToUpperInvariant(), 16);
                Fade(Kit.Label("Lang", Root, e.Language.ToUpperInvariant(), 16, Pal.Ivory, TextAnchor.MiddleLeft, 44, y, nw + 4, 16), at);
                if (e.Named) Fade(Kit.Pic("Named", Root, Pictograms.Get("check"), Pal.Amber, 44 + nw + 8, y + 1, 13, 13), at);
                y += roomy ? 20 : 17;
                int phrases = Mathf.Min(e.Phrases.Count, roomy ? 3 : 1);
                for (int p = 0; p < phrases; p++)
                {
                    var ph = e.Phrases[p];
                    float rw = FontBook.Width(Rtl.Visual(ph.Real), 16);
                    Fade(Kit.Label("Real", Root, ph.Real, 16, Pal.Amber, TextAnchor.MiddleLeft, 56, y, rw + 4, 16), at + 0.15f);
                    // a sentence and its meaning may not share a line left of the ship: the meaning drops below, or out when cramped
                    float mw = FontBook.Width(ph.Translation ?? "", 16);
                    if (56 + rw + 14 + mw <= 516)
                        Fade(Kit.Label("Means", Root, ph.Translation, 16, Pal.Dawn, TextAnchor.MiddleLeft, 56 + rw + 14, y, 460 - rw, 16), at + 0.15f);
                    else if (roomy)
                    {
                        y += 19;
                        Fade(Kit.Label("Means", Root, ph.Translation, 16, Pal.Dawn, TextAnchor.MiddleLeft, 68, y, 448, 16), at + 0.15f);
                    }
                    y += roomy ? 19 : 17;
                }
                if (roomy && !string.IsNullOrEmpty(e.Fact))
                {
                    int lines = Mathf.Clamp(Kit.Lines(e.Fact, 16, 430), 1, 3);
                    var t = Kit.Label("Fact", Root, e.Fact, 16, Pal.Dawn, TextAnchor.UpperLeft, 56, y + 2, 430, lines * 18, true);
                    Kit.Pitch(t, 18);
                    Fade(t, at + 0.3f);
                    y += lines * 18 + 4;
                }
                y += roomy ? 8 : 3;
                at += roomy ? 0.5f : 0.3f;
            }
            _cue = Widgets.Cue(Root, 334, "AGAIN", Pal.Ivory);
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

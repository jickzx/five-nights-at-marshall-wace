using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Monster.Beast;
using Fnmw.Monster.Learning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Monster.Overlay
{
    /// <summary>
    /// F3: what the beast has learned about you. Per-concept skill with uncertainty, the next
    /// puzzle's predicted success, aggression and why, the beast's state, and predicted vs actual
    /// for recent answers. F4 (while open) wipes its memory of you.
    /// </summary>
    public class BrainOverlay : MonoBehaviour
    {
        public static bool Visible;
        const float RefHeight = 1080f, PanelW = 660f, Pad = 22f;

        static readonly Color Panel = new Color(0.04f, 0.01f, 0.01f, 0.86f);
        static readonly Color Edge = new Color(0.75f, 0.06f, 0.04f, 1f);
        static readonly Color Track = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color Dim = new Color(0.78f, 0.7f, 0.68f, 1f);
        static readonly Color Good = new Color(0.35f, 0.85f, 0.45f);
        static readonly Color Mid = new Color(0.95f, 0.72f, 0.2f);
        static readonly Color Bad = new Color(0.95f, 0.18f, 0.12f);

        AdaptiveDirector _dir;
        readonly float[] _aggr = new float[160];
        int _aggrHead;
        float _sampleAt, _toastUntil;
        string _toast;
        GUIStyle _title, _sub, _section, _body, _small, _right, _tag, _big;
        float _y, _x, _w;

        public void Bind(AdaptiveDirector director) => _dir = director;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f3Key.wasPressedThisFrame) Visible = !Visible;
                if (Visible && kb.f4Key.wasPressedThisFrame && _dir != null)
                {
                    _dir.ForgetPlayer();
                    _toast = "Memory wiped: the beast has forgotten you";
                    _toastUntil = Time.realtimeSinceStartup + 4f;
                }
            }
            if (Time.unscaledTime >= _sampleAt && _dir != null)
            {
                _sampleAt = Time.unscaledTime + 0.25f;
                _aggr[_aggrHead] = _dir.BeastAggression;
                _aggrHead = (_aggrHead + 1) % _aggr.Length;
            }
        }

        void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, richText = true };
            _title.normal.textColor = new Color(1f, 0.86f, 0.82f);
            _sub = new GUIStyle(GUI.skin.label) { fontSize = 17, richText = true, wordWrap = true };
            _sub.normal.textColor = Dim;
            _section = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _section.normal.textColor = new Color(1f, 0.35f, 0.28f);
            _body = new GUIStyle(GUI.skin.label) { fontSize = 21, richText = true, wordWrap = true };
            _body.normal.textColor = new Color(0.96f, 0.93f, 0.92f);
            _small = new GUIStyle(_body) { fontSize = 17 };
            _small.normal.textColor = Dim;
            _right = new GUIStyle(_body) { alignment = TextAnchor.MiddleRight };
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _tag.normal.textColor = Color.black;
            _big = new GUIStyle(_body) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        }

        void OnGUI()
        {
            if (!Visible || _dir == null) return;
            Styles();
            float scale = Screen.height / RefHeight;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float refW = Screen.width / scale;

            _x = refW - PanelW - 16f; _w = PanelW - 2 * Pad;
            Box(new Rect(_x, 12, PanelW, RefHeight - 24), Panel);
            Box(new Rect(_x, 12, 4, RefHeight - 24), Edge);
            _x += Pad; _y = 26;

            var m = _dir.Model; var s = m.S;
            Label(Row(40), "THE BEAST'S BRAIN", _title);
            Label(Row(24), "online Bayesian logistic regression, learning from your answers", _sub);
            Label(Row(24), $"Remembers you: run {s.Runs}  ·  {s.TotalAnswers} answers learned  ·  <b>F3</b> hide  <b>F4</b> forget", _sub);
            _y += 6;
            Flash();

            Section("WHAT IT HAS LEARNED ABOUT YOU");
            Label(Row(22), "chance you'd solve a medium puzzle on each concept, band = ±1σ", _small);
            if (s.Concepts.Count == 0) Label(Row(28), "Nothing yet. Answer a console puzzle.", _small);
            int shown = 0;
            foreach (var b in s.Concepts)
            {
                if (b.Concept == PlayerModel.General && s.Concepts.Count > 1 && b.Seen == 0) continue;
                if (++shown > 7) break;
                ConceptRow(b, b.Concept == _dir.FocusConcept);
            }

            Section("NEXT PUZZLE");
            var r = Row(30);
            Label(new Rect(r.x, r.y, 230, 30), "Difficulty", _body);
            Bar(new Rect(r.x + 230, r.y + 7, 260, 16), _dir.Difficulty, new Color(0.6f, 0.65f, 0.95f));
            Label(new Rect(r.x + 490, r.y, _w - 490, 30), $"{_dir.Difficulty:0.00}", _right);
            r = Row(30);
            Label(new Rect(r.x, r.y, 230, 30), "Predicted success", _body);
            Bar(new Rect(r.x + 230, r.y + 7, 260, 16), _dir.PredictedSuccess, SkillColor(_dir.PredictedSuccess));
            Box(new Rect(r.x + 230 + 260 * PlayerModel.TargetSuccess - 1, r.y + 3, 2, 24), Color.white);
            Label(new Rect(r.x + 490, r.y, _w - 490, 30), $"{_dir.PredictedSuccess:P0}", _right);
            Label(Row(26), $"Probing <b>{AdaptiveDirector.Pretty(_dir.FocusConcept)}</b>. {_dir.DifficultyReason}", _small);

            Section("AGGRESSION");
            r = Row(48);
            Bar(new Rect(r.x, r.y + 12, _w - 120, 24), _dir.BeastAggression, Color.Lerp(new Color(0.45f, 0.1f, 0.1f), Bad, _dir.BeastAggression));
            Label(new Rect(r.x + _w - 120, r.y, 120, 48), $"{_dir.BeastAggression:0.00}", _big);
            Label(Row(28), $"<b>{_dir.AggressionReason}</b>", _body);
            foreach (var t in _dir.AggressionTerms)
            {
                string col = t.Value > 0.005f ? "#ff6a5a" : t.Value < -0.005f ? "#6fe08a" : "#bbbbbb";
                Label(Row(23), $"<color={col}>{t.Value:+0.00;-0.00; 0.00}</color>  {t.Label}", _small);
            }
            Sparkline(Row(34));

            Section("THE BEAST");
            var brain = Services.BeastBrain as BeastBrain;
            var st = Services.State;
            if (brain != null)
            {
                string dist = float.IsInfinity(st.BeastDistance) ? "-" : $"{st.BeastDistance:0.0} m";
                Label(Row(28), $"Stage <b>{brain.Stage}</b>  ·  {brain.Mode}  ·  {dist}  ·  threat {st.Threat:0.00}", _body);
                r = Row(24);
                Label(new Rect(r.x, r.y, 230, 24), brain.Stage >= 3 ? "Fully evolved" : $"Evolving to stage {brain.Stage + 1}", _small);
                Bar(new Rect(r.x + 230, r.y + 6, _w - 230, 12), brain.EvolutionToNext, new Color(0.8f, 0.15f, 0.1f));
                var anim = BeastController.Instance ? BeastController.Instance.GetComponent<BeastAnimator>() : null;
                Label(Row(26), anim ? $"{brain.Why}   ·   anim: {anim.CurrentClip}" : brain.Why, _small);
            }
            else Label(Row(26), "(another brain is driving the beast)", _small);

            Section("PREDICTED vs WHAT YOU DID");
            History(Row(70), s.History);

            Section("LEARNED WEIGHTS");
            Label(Row(23), $"difficulty {-s.WDifficulty:+0.00;-0.00}   hesitation {s.WHesitation:+0.00;-0.00}   beast near {s.WProximity:+0.00;-0.00}   streak {s.WStreak:+0.00;-0.00}", _small);
            Label(Row(23), $"your pace {Mathf.Exp(s.LogRtBaseline):0}s  ·  hesitation {s.Hesitation:+0.0;-0.0}σ  ·  streak {s.Streak:+0;-0;0}  ·  ability {s.Ability:+0.00;-0.00}", _small);

            GUI.matrix = old;
        }

        void Flash()
        {
            var c = _dir.LastChange;
            float now = Time.realtimeSinceStartup;
            string text = null; Color col = Edge; float age = 99f;
            if (_toast != null && now < _toastUntil) { text = _toast; col = Mid; age = 0; }
            else if (c.Valid && now - c.At < 8f)
            {
                age = now - c.At;
                col = c.Correct ? Good : Bad;
                text = (c.Correct ? "RIGHT" : "WRONG") + $" on {AdaptiveDirector.Pretty(c.Concept)} (it predicted {c.Predicted:P0})\n" +
                       $"skill {c.SkillBefore:P0} → {c.SkillAfter:P0}   aggression {c.AggressionBefore:0.00} → {c.AggressionAfter:0.00}   difficulty {c.DifficultyBefore:0.00} → {c.DifficultyAfter:0.00}";
            }
            var r = Row(58);
            if (text == null) return;
            float a = Mathf.Clamp01(1.2f - age / 8f);
            Box(r, new Color(col.r, col.g, col.b, 0.22f * a + 0.05f));
            Box(new Rect(r.x, r.y, 5, r.height), col);
            var st = new GUIStyle(_small) { fontSize = 17 };
            st.normal.textColor = new Color(1, 1, 1, Mathf.Max(0.5f, a));
            Label(new Rect(r.x + 12, r.y + 4, r.width - 14, r.height - 4), text, st);
        }

        void ConceptRow(ConceptBelief b, bool focus)
        {
            var m = _dir.Model;
            float p = m.SkillAtMedium(b.Concept);
            var (lo, hi) = m.SkillBand(b.Concept);
            var r = Row(34);
            const float nameW = 190, barW = 270;
            Label(new Rect(r.x, r.y, nameW, 34), AdaptiveDirector.Pretty(b.Concept), _body);
            var bar = new Rect(r.x + nameW, r.y + 8, barW, 18);
            Box(bar, Track);
            Box(new Rect(bar.x + barW * lo, bar.y + 6, barW * (hi - lo), 6), new Color(1, 1, 1, 0.22f)); // ±1σ
            Box(new Rect(bar.x, bar.y, barW * p, bar.height), SkillColor(p));
            Label(new Rect(bar.xMax + 8, r.y, 70, 34), $"{p:P0}", _body);
            Label(new Rect(bar.xMax + 74, r.y + 4, 80, 30), $"{b.Correct}/{b.Seen}", _small);
            if (focus)
            {
                var tr = new Rect(r.x + _w - 78, r.y + 7, 78, 20);
                Box(tr, Mid);
                Label(tr, "PROBING", _tag);
            }
        }

        void History(Rect r, List<AnswerRecord> h)
        {
            Box(r, Track);
            if (h.Count == 0) { Label(new Rect(r.x + 10, r.y + 20, r.width, 30), "No answers yet", _small); return; }
            int n = Mathf.Min(h.Count, 16);
            float cw = r.width / 16f;
            float brier = 0;
            for (int i = 0; i < n; i++)
            {
                var a = h[h.Count - n + i];
                float x = r.x + i * cw + 4, bh = (r.height - 18) * a.Predicted;
                Box(new Rect(x, r.yMax - 4 - bh, cw - 8, bh), new Color(1, 1, 1, 0.28f));
                Box(new Rect(x + (cw - 8) / 2 - 6, a.Correct ? r.y + 2 : r.yMax - 16, 12, 12), a.Correct ? Good : Bad);
                brier += (a.Predicted - (a.Correct ? 1 : 0)) * (a.Predicted - (a.Correct ? 1 : 0));
            }
            _y += 2;
            Label(Row(22), $"grey bar = predicted chance, dot = outcome (top right, bottom wrong)   Brier {brier / n:0.00}", _small);
        }

        void Sparkline(Rect r)
        {
            Box(r, Track);
            float cw = r.width / _aggr.Length;
            for (int i = 0; i < _aggr.Length; i++)
            {
                float v = _aggr[(_aggrHead + i) % _aggr.Length];
                float h = r.height * v;
                Box(new Rect(r.x + i * cw, r.yMax - h, Mathf.Max(1f, cw), h), Color.Lerp(new Color(0.4f, 0.1f, 0.1f), Bad, v));
            }
            Label(new Rect(r.x + 6, r.y, 200, r.height), "aggression, last 40 s", _small);
        }

        Rect Row(float h) { var r = new Rect(_x, _y, _w, h); _y += h + 2; return r; }

        void Section(string title)
        {
            _y += 10;
            Box(new Rect(_x, _y, _w, 1), new Color(1, 0.3f, 0.25f, 0.35f));
            _y += 4;
            Label(Row(22), title, _section);
        }

        static Color SkillColor(float p) => p < 0.5f ? Color.Lerp(Bad, Mid, p / 0.5f) : Color.Lerp(Mid, Good, (p - 0.5f) / 0.5f);

        static void Box(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void Bar(Rect r, float v, Color c)
        {
            Box(r, Track);
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), c);
        }

        static void Label(Rect r, string text, GUIStyle st) => GUI.Label(r, text, st);
    }
}

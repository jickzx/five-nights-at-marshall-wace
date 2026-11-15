using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Core.Ai;
using UnityEngine;

namespace Fnmw.Game.Debrief
{
    /// <summary>
    /// Records the run from GameEvents and, when it ends, puts the stock debrief on the end screen at once and asks
    /// Gemini for a better one. A plain class so EditMode tests can drive it without a scene; RunDebrief hosts it.
    /// </summary>
    public sealed class RunDebriefer
    {
        public readonly RunLog Log = new RunLog();
        /// <summary>The debrief last sent to the UI, and whether it came from the model.</summary>
        public string Shown { get; private set; }
        public bool Live { get; private set; }

        AiHandle _pending;
        int _run;          // bumped on every reset: a reply for an older run is dropped even if its handle slipped through
        bool _attached;

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            GameEvents.PhaseChanged += OnPhase;
            GameEvents.PuzzleStarted += OnPuzzle;
            GameEvents.AnswerSubmitted += OnAnswer;
            GameEvents.QuizAnswered += OnQuiz;
            GameEvents.BeastStageChanged += OnStage;
            Reset();
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            GameEvents.PhaseChanged -= OnPhase;
            GameEvents.PuzzleStarted -= OnPuzzle;
            GameEvents.AnswerSubmitted -= OnAnswer;
            GameEvents.QuizAnswered -= OnQuiz;
            GameEvents.BeastStageChanged -= OnStage;
            Cancel();
        }

        /// <summary>Drop any pending reply. Called on restart and destroy.</summary>
        public void Cancel()
        {
            _pending?.Cancel();
            _pending = null;
            _run++;
        }

        /// <summary>Start a fresh log for a new run.</summary>
        public void Reset()
        {
            Cancel();
            Log.Reset();
            Shown = null; Live = false;
        }

        static GameState S => Services.State;

        void OnPhase(GamePhase from, GamePhase to)
        {
            if (from == GamePhase.Title && to == GamePhase.Explore) { Reset(); return; }
            if (to == GamePhase.Escaped || to == GamePhase.Dead)
            {
                bool inPuzzle = from == GamePhase.Puzzle;
                Log.End(to == GamePhase.Escaped, S.Elapsed, S.PuzzlesSolved, S.TotalPuzzles, inPuzzle);
                // demo runs share one seed: never name a console the player has not decoded, or the next demo run is spoiled
                Log.HideUndecoded = DemoMode.On;
                Debrief();
            }
        }

        void OnPuzzle(LangPuzzle p)
        {
            var w = S.World;
            int index = S.CurrentPuzzleIndex;
            if (w?.Puzzles != null)
            {
                int found = w.Puzzles.FindIndex(x => x != null && x.Id == p?.Id);
                if (found >= 0) index = found;
            }
            // the engine sets World.Real for the console it just built; the per-puzzle list is the fallback
            string lang = w?.Real?.Name;
            if (string.IsNullOrWhiteSpace(lang) && w?.Languages != null && index >= 0 && index < w.Languages.Count) lang = w.Languages[index]?.Name;
            Log.PuzzleStarted(p, Mathf.Max(0, index), lang, S.Elapsed);
        }

        void OnAnswer(AnswerEvent e) => Log.Answer(e);
        void OnQuiz(QuizEvent e) => Log.Quiz(e);
        void OnStage(int stage) => Log.BeastStage(stage, S.Elapsed);

        void Debrief()
        {
            Cancel();
            Shown = DebriefWriter.Stock(Log); Live = false;
            ShowOnUi(Shown);
            var ai = Services.Ai;
            if (ai == null || !ai.Available) { Debug.Log("[Debrief] shown: stock (AI unavailable)"); return; }

            int run = _run;
            var worldLangs = new List<string>();
            if (S.World?.Languages != null) foreach (var l in S.World.Languages) if (l != null) worldLangs.Add(l.Name);
            _pending = ai.Request(DebriefWriter.Request(Log), r =>
            {
                if (run != _run) return;   // restarted or reset since: never show it
                _pending = null;
                if (!r.Ok) { Debug.Log("[Debrief] shown: stock (" + r.Status + ")"); return; }
                if (!DebriefWriter.Check(r.Text, Log, worldLangs, out string text, out string why))
                {
                    Debug.Log("[Debrief] shown: stock (reply rejected: " + why + ")");
                    return;
                }
                Shown = text; Live = true;
                ShowOnUi(text);
                Debug.Log("[Debrief] shown: live (" + r.Seconds.ToString("0.0") + " s)");
            });
        }

        static void ShowOnUi(string text)
        {
            try { Services.UI.ShowDebrief(text); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }
    }
}

// CORE CONTRACT. Changes here must be called out in your PR description.
using System;
using UnityEngine;

namespace Fnmw.Core
{
    public struct AnswerEvent
    {
        public int PuzzleId;
        public bool Correct;
        public float ResponseTime;     // seconds from puzzle shown to submit
        public string MissedConcept;   // null when correct, e.g. "plural-suffix"
        public float Difficulty;       // difficulty the puzzle was served at
    }

    public struct QuizEvent
    {
        public int PuzzleId;
        public int ChosenIndex;
        public bool Correct;
        public string RealLanguage;
    }

    /// <summary>Static event bus. Subscribe in OnEnable/Install, unsubscribe on destroy.</summary>
    public static class GameEvents
    {
        public static event Action<GamePhase, GamePhase> PhaseChanged;   // (from, to)
        public static event Action<LangPuzzle> PuzzleStarted;
        public static event Action<AnswerEvent> AnswerSubmitted;
        public static event Action<QuizEvent> QuizAnswered;
        public static event Action<int> BeastStageChanged;
        public static event Action<float> ThreatChanged;
        public static event Action<int> ConsoleActivated;                // console index
        public static event Action PlayerCaught;
        public static event Action Escaped;

        public static void RaisePhaseChanged(GamePhase from, GamePhase to) => Safe(() => PhaseChanged?.Invoke(from, to));
        public static void RaisePuzzleStarted(LangPuzzle p) => Safe(() => PuzzleStarted?.Invoke(p));
        public static void RaiseAnswerSubmitted(AnswerEvent e) => Safe(() => AnswerSubmitted?.Invoke(e));
        public static void RaiseQuizAnswered(QuizEvent e) => Safe(() => QuizAnswered?.Invoke(e));
        public static void RaiseBeastStageChanged(int s) => Safe(() => BeastStageChanged?.Invoke(s));
        public static void RaiseThreatChanged(float t) => Safe(() => ThreatChanged?.Invoke(t));
        public static void RaiseConsoleActivated(int i) => Safe(() => ConsoleActivated?.Invoke(i));
        public static void RaisePlayerCaught() => Safe(() => PlayerCaught?.Invoke());
        public static void RaiseEscaped() => Safe(() => Escaped?.Invoke());

        /// <summary>Drop all subscribers. Called by GameBootstrap on start and by tests.</summary>
        public static void ClearAll()
        {
            PhaseChanged = null; PuzzleStarted = null; AnswerSubmitted = null; QuizAnswered = null;
            BeastStageChanged = null; ThreatChanged = null; ConsoleActivated = null;
            PlayerCaught = null; Escaped = null;
        }

        static void Safe(Action a)
        {
            try { a(); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}

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

    /// <summary>One footfall of the player. Raised by the Game thread's controller; Audio can play a step.</summary>
    public struct FootstepEvent
    {
        public string Surface;         // "shore", "blood" (wading), "metal"
        public float Speed;            // m/s at the time of the step
        public Vector3 Position;
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
        public static event Action<FootstepEvent> Footstep;
        /// <summary>UI raises this when the player places a tile in a puzzle (audio plays the word's motif).</summary>
        public static event Action<Tile> TilePicked;
        /// <summary>UI raises this when the player clears their placed tiles.</summary>
        public static event Action TilesCleared;
        /// <summary>UI raises this when the player takes back the last tile (Backspace / B).</summary>
        public static event Action TileRemoved;
        /// <summary>UI raises this when the player asks the ship for a hint on the open console (puzzle id).</summary>
        public static event Action<int> HintRequested;
        /// <summary>Monster raises this when the beast starts a lunge at the player.</summary>
        public static event Action BeastLunged;
        /// <summary>Game raises this when the death scare starts (the beast fills the view).</summary>
        public static event Action Jumpscare;

        public static void RaisePhaseChanged(GamePhase from, GamePhase to) => Safe(() => PhaseChanged?.Invoke(from, to));
        public static void RaisePuzzleStarted(LangPuzzle p) => Safe(() => PuzzleStarted?.Invoke(p));
        public static void RaiseAnswerSubmitted(AnswerEvent e) => Safe(() => AnswerSubmitted?.Invoke(e));
        public static void RaiseQuizAnswered(QuizEvent e) => Safe(() => QuizAnswered?.Invoke(e));
        public static void RaiseBeastStageChanged(int s) => Safe(() => BeastStageChanged?.Invoke(s));
        public static void RaiseThreatChanged(float t) => Safe(() => ThreatChanged?.Invoke(t));
        public static void RaiseConsoleActivated(int i) => Safe(() => ConsoleActivated?.Invoke(i));
        public static void RaisePlayerCaught() => Safe(() => PlayerCaught?.Invoke());
        public static void RaiseEscaped() => Safe(() => Escaped?.Invoke());
        public static void RaiseFootstep(FootstepEvent e) => Safe(() => Footstep?.Invoke(e));
        public static void RaiseTilePicked(Tile t) => Safe(() => TilePicked?.Invoke(t));
        public static void RaiseTilesCleared() => Safe(() => TilesCleared?.Invoke());
        public static void RaiseTileRemoved() => Safe(() => TileRemoved?.Invoke());
        public static void RaiseHintRequested(int puzzleId) => Safe(() => HintRequested?.Invoke(puzzleId));
        public static void RaiseBeastLunged() => Safe(() => BeastLunged?.Invoke());
        public static void RaiseJumpscare() => Safe(() => Jumpscare?.Invoke());

        /// <summary>Drop all subscribers. Called by GameBootstrap on start and by tests.</summary>
        public static void ClearAll()
        {
            PhaseChanged = null; PuzzleStarted = null; AnswerSubmitted = null; QuizAnswered = null;
            BeastStageChanged = null; ThreatChanged = null; ConsoleActivated = null;
            PlayerCaught = null; Escaped = null; Footstep = null; TilePicked = null; TilesCleared = null;
            TileRemoved = null; HintRequested = null; BeastLunged = null; Jumpscare = null;
        }

        static void Safe(Action a)
        {
            try { a(); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}

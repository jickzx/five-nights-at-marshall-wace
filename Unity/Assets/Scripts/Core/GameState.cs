// CORE CONTRACT. Changes here must be called out in your PR description.
using UnityEngine;

namespace Fnmw.Core
{
    public enum GamePhase { Title, Explore, Puzzle, Reveal, Quiz, Chase, Escaped, Dead }

    /// <summary>
    /// Single shared runtime state. Read it from anywhere via Services.State.
    /// Mutate through the setters so the matching GameEvents fire.
    /// </summary>
    public class GameState
    {
        public GamePhase Phase { get; private set; } = GamePhase.Title;
        public float Elapsed;            // seconds since Explore began
        public int BeastStage { get; private set; } = 1; // 1..3
        public float BeastDistance = float.PositiveInfinity; // metres to player
        public float Threat { get; private set; }        // 0..1, drives music
        public int PuzzlesSolved;
        public int Mistakes;
        public int TotalPuzzles;
        public int Seed;
        public LanguageWorld World;      // generated at game start
        public int CurrentPuzzleIndex = -1;
        /// <summary>0..1, set by the Game thread: 1 holds the beast back (no evolution, no pressing) while the tutorial teaches.</summary>
        public float BeastGrace;
        public float SeaLevel;           // world-space height of the blood surface; World writes it, rises over the run
        public float SeaWaveCrest;       // maximum rendered wave height above SeaLevel, written by World
        public float ShipPower => TotalPuzzles > 0 ? Mathf.Clamp01((float)PuzzlesSolved / TotalPuzzles) : 0f; // 0..1

        public bool IsOver => Phase == GamePhase.Escaped || Phase == GamePhase.Dead;

        public void SetPhase(GamePhase next)
        {
            if (next == Phase) return;
            var prev = Phase;
            Phase = next;
            GameEvents.RaisePhaseChanged(prev, next);
        }

        public void SetBeastStage(int stage)
        {
            stage = Mathf.Clamp(stage, 1, 3);
            if (stage == BeastStage) return;
            BeastStage = stage;
            GameEvents.RaiseBeastStageChanged(stage);
        }

        public void SetThreat(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Approximately(t, Threat)) return;
            Threat = t;
            GameEvents.RaiseThreatChanged(t);
        }
    }
}

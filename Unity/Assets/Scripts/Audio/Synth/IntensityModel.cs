// Maps raw game signals (intensity, threat, beast stage, phase) to a musical mood with hysteresis,
// and quantises mood changes to the beat grid. Pure logic, unit tested.
using System;

namespace Fnmw.Audio.Synth
{
    public enum Mood { Title, Unsettling, Tense, Panic, Death, Escape }

    /// <summary>Mirror of Fnmw.Core.GamePhase so the synth has no Unity dependency.</summary>
    public enum ScorePhase { Title, Explore, Puzzle, Reveal, Quiz, Chase, Escaped, Dead }

    public struct MoodInput
    {
        public float Intensity;      // last value passed to IAudioDirector.SetIntensity
        public float Threat;         // GameState.Threat
        public int Stage;            // 1..3
        public ScorePhase Phase;
        public float BeastDistance;  // metres, +inf if unknown
    }

    public sealed class IntensityModel
    {
        public const float TenseEnter = 0.36f, TenseExit = 0.28f;
        public const float PanicEnter = 0.72f, PanicExit = 0.62f;
        public const float Stage2Floor = 0.40f, Stage3Floor = 0.75f, ChaseFloor = 0.85f;

        public Mood Mood { get; private set; } = Mood.Title;
        /// <summary>Effective 0..1 level after floors.</summary>
        public float Level { get; private set; }

        /// <summary>
        /// Level within the current mood band, 0..1. Drives continuous things like heartbeat rate,
        /// so the score keeps moving between mood switches.
        /// </summary>
        public float Heat
        {
            get
            {
                switch (Mood)
                {
                    case Mood.Unsettling: return Dsp.Clamp01(Level / TenseEnter);
                    case Mood.Tense: return Dsp.Clamp01((Level - TenseExit) / (PanicEnter - TenseExit));
                    case Mood.Panic: return Dsp.Clamp01((Level - PanicExit) / (1f - PanicExit));
                    default: return 0f;
                }
            }
        }

        public void Reset() { Mood = Mood.Title; Level = 0f; }

        /// <summary>Combine the signals into one 0..1 level. Stages and the chase raise the floor.</summary>
        public static float EffectiveLevel(MoodInput i)
        {
            float lvl = Math.Max(Dsp.Clamp01(i.Intensity), Dsp.Clamp01(i.Threat));
            if (!float.IsInfinity(i.BeastDistance) && !float.IsNaN(i.BeastDistance) && i.BeastDistance >= 0f)
                lvl = Math.Max(lvl, Dsp.Clamp01(1f - i.BeastDistance / 30f));
            if (i.Stage >= 3) lvl = Math.Max(lvl, Stage3Floor);
            else if (i.Stage == 2) lvl = Math.Max(lvl, Stage2Floor);
            if (i.Phase == ScorePhase.Chase) lvl = Math.Max(lvl, ChaseFloor);
            return Dsp.Clamp01(lvl);
        }

        /// <summary>Band of a level with no memory (used for the first frame and in tests).</summary>
        public static Mood Band(float level) => level >= PanicEnter ? Mood.Panic : level >= TenseEnter ? Mood.Tense : Mood.Unsettling;

        public Mood Update(MoodInput i)
        {
            // Endings are terminal: once the run is over nothing pulls the score back.
            if (Mood == Mood.Death || Mood == Mood.Escape) return Mood;
            if (i.Phase == ScorePhase.Dead) return Mood = Mood.Death;
            if (i.Phase == ScorePhase.Escaped) return Mood = Mood.Escape;

            Level = EffectiveLevel(i);
            if (i.Phase == ScorePhase.Title) return Mood = Mood.Title;
            if (i.Phase == ScorePhase.Chase) return Mood = Mood.Panic;

            switch (Mood)
            {
                case Mood.Unsettling:
                    if (Level >= PanicEnter) Mood = Mood.Panic;
                    else if (Level >= TenseEnter) Mood = Mood.Tense;
                    break;
                case Mood.Tense:
                    if (Level >= PanicEnter) Mood = Mood.Panic;
                    else if (Level < TenseExit) Mood = Mood.Unsettling;
                    break;
                case Mood.Panic:
                    if (Level < PanicExit) Mood = Level < TenseExit ? Mood.Unsettling : Mood.Tense;
                    break;
                default:
                    Mood = Band(Level);
                    break;
            }
            return Mood;
        }
    }

    /// <summary>
    /// Holds a requested mood until the next musical boundary so changes land on the grid.
    /// Calming down waits for a bar; escalating waits only for a beat; endings are immediate.
    /// </summary>
    public sealed class MoodScheduler
    {
        public const int StepsPerBeat = 4, BeatsPerBar = 4, StepsPerBar = StepsPerBeat * BeatsPerBar;

        public Mood Current { get; private set; } = Mood.Title;
        public Mood Pending { get; private set; } = Mood.Title;
        public long PendingAtStep { get; private set; } = -1;
        public bool HasPending => PendingAtStep >= 0;

        public static int Rank(Mood m)
        {
            switch (m)
            {
                case Mood.Title: return 0;
                case Mood.Unsettling: return 1;
                case Mood.Tense: return 2;
                case Mood.Panic: return 3;
                default: return 4;
            }
        }

        /// <summary>Quantum (in 16th steps) used when moving from one mood to another.</summary>
        public static int QuantumFor(Mood from, Mood to)
        {
            if (to == Mood.Death || to == Mood.Escape) return 0;
            return Rank(to) > Rank(from) ? StepsPerBeat : StepsPerBar;
        }

        /// <summary>First step index strictly after currentStep that is a multiple of quantum.</summary>
        public static long NextBoundary(long currentStep, int quantum)
        {
            if (quantum <= 0) return currentStep;
            return (currentStep / quantum + 1) * quantum;
        }

        /// <summary>Ask for a mood. Returns true if it applied immediately.</summary>
        public bool Request(Mood mood, long currentStep)
        {
            if (Current == Mood.Death || Current == Mood.Escape) return false;
            if (mood == Current) { PendingAtStep = -1; Pending = Current; return false; }
            if (HasPending && mood == Pending) return false;
            int q = QuantumFor(Current, mood);
            if (q == 0) { Current = mood; Pending = mood; PendingAtStep = -1; return true; }
            Pending = mood;
            PendingAtStep = NextBoundary(currentStep, q);
            return false;
        }

        /// <summary>Call on every 16th step. Returns true when the pending mood takes over at this step.</summary>
        public bool OnStep(long step)
        {
            if (!HasPending || step < PendingAtStep) return false;
            Current = Pending;
            PendingAtStep = -1;
            return true;
        }

        public void Reset() { Current = Pending = Mood.Title; PendingAtStep = -1; }
    }
}

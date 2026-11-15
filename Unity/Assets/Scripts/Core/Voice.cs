// CORE CONTRACT. Changes here must be called out in your PR description.
// Optional spoken/hinted features (monster dialogue, ship hints, pronunciation). Every member that other
// interfaces gained for these is a default member, so existing implementations and test doubles compile.
using System;

namespace Fnmw.Core
{
    /// <summary>The three voices. Priority when two want to speak: Native, then Ship, then Monster.</summary>
    public enum VoiceChannel { Monster, Ship, Native }

    /// <summary>Where a line came from, so the game can say truthfully what was heard.
    /// Mock means a synthetic placeholder (never real speech); it wins over Cache when both apply.</summary>
    public enum VoiceSource { Live, Cache, Mock, Offline }

    /// <summary>Mono PCM for the Audio thread to process and play.</summary>
    public sealed class VoiceClip
    {
        public float[] Samples;
        public int SampleRate;
        public VoiceChannel Channel;
        /// <summary>Beast stage 1..3 for Monster processing; ignored for the other channels.</summary>
        public int Stage = 1;
        public float Gain = 1f;
        /// <summary>A synthetic tone pattern from a mock backend, not speech.</summary>
        public bool Mock;
    }

    public enum VoicePlaybackEvent
    {
        /// <summary>Audio is now actually audible (processing finished and the source started). Seconds = length.</summary>
        Started,
        /// <summary>Played to the end.</summary>
        Finished,
        /// <summary>Stopped or replaced before the end (including while still being prepared).</summary>
        Stopped,
        /// <summary>Never played (no beast for a Monster clip, bad data, audio missing).</summary>
        Dropped,
    }

    public struct VoicePlayback
    {
        public int Handle;
        public VoiceChannel Channel;
        public VoicePlaybackEvent Event;
        public float Seconds;
    }

    /// <summary>One subtitle line. Empty Text clears the line.</summary>
    public struct SubtitleLine
    {
        public VoiceChannel Channel;
        public string Text;
        public float Seconds;
        public VoiceSource Source;
    }

    /// <summary>The ship's hint line on a console. Visible false clears it.</summary>
    public struct HintView
    {
        public int PuzzleId;
        public bool Visible;
        public bool Pending;     // "analysing" while a live request is in flight
        public string Text;
        public int Level;        // 1 or 2
        public VoiceSource Source;
    }

    public enum PronunciationState { Hidden, Loading, Preparing, Playing, Ready, Unavailable }

    /// <summary>The pronunciation cue on the quiz payoff.</summary>
    public struct PronunciationView
    {
        public int PuzzleId;
        public PronunciationState State;
        public VoiceSource Source;
    }

    /// <summary>What a hint may be grounded in, from the language engine. Holds nothing answer-specific.</summary>
    public sealed class HintContext
    {
        public string Concept;          // LangConcepts id
        public string Focus;            // what to look at, e.g. "where the action word sits" (approved)
        public string[] Facts;          // approved neutral rule statements for the concept
        public string[] OfflineHints;   // authored, index = level - 1
    }

    /// <summary>Voice thread (Fnmw.Voice). Optional: the fake shows authored hints and no speech.</summary>
    public interface IVoiceDirector
    {
        /// <summary>UI: H at a console.</summary>
        void RequestHint(int puzzleId);
        /// <summary>UI: the language quiz has accepted a choice for this puzzle.</summary>
        void NotifyQuizAnswerLocked(int puzzleId) { }
        /// <summary>UI: the quiz payoff step (replay false), and P on it (replay true). Never earlier.</summary>
        void RequestPronunciation(int puzzleId, bool replay);
        /// <summary>False hides the hint key cue.</summary>
        bool HintsAvailable { get; }
    }
}

// CORE CONTRACT. Changes here must be called out in your PR description.
using System;
using UnityEngine;

namespace Fnmw.Core
{
    /// <summary>Lang thread. Pure logic, no MonoBehaviours required.</summary>
    public interface ILanguageEngine
    {
        LanguageWorld Generate(int seed);
        /// <summary>Regenerate/adjust puzzle i for a difficulty 0..1 (may return the same puzzle).</summary>
        LangPuzzle GetPuzzle(LanguageWorld world, int index, float difficulty);
        AnswerResult Check(LanguageWorld world, int puzzleId, string[] tileIds);
        RevealInfo GetReveal(LanguageWorld world, int puzzleId);
        QuizQuestion GetQuiz(LanguageWorld world, int puzzleId);
        bool CheckQuiz(LanguageWorld world, int puzzleId, int optionIndex);
        /// <summary>Approved, answer-free material a hint for this puzzle may use. Optional: null means no hints.</summary>
        HintContext GetHintContext(LanguageWorld world, int puzzleId, string missedConcept) => null;
        /// <summary>The engine knows the answer, so it has the final say on generated hint text before the quiz.
        /// Optional: the default rejects everything, so an engine that cannot vouch for text shows authored hints only.</summary>
        bool IsHintSafe(LanguageWorld world, int puzzleId, string text) => false;
    }

    /// <summary>Monster thread. Watches the player's answers and sets the pace.</summary>
    public interface IDifficultyDirector
    {
        void Reset(int seed);
        void Observe(AnswerEvent answer);
        float Difficulty { get; }       // 0..1, fed to ILanguageEngine.GetPuzzle
        float BeastAggression { get; }  // 0..1, fed to IBeastBrain
        /// <summary>Grammar concept the director wants probed next (the player's weakest), or null.
        /// Lang may bias GetPuzzle toward it. Optional: defaults to null.</summary>
        string FocusConcept => null;
    }

    public struct BeastPerception
    {
        public Vector3 BeastPosition;
        public Vector3 PlayerPosition;
        public float Distance;
        public GamePhase Phase;
        public int Stage;
        public float Elapsed;
        public float Aggression;
        public int Mistakes;
        public Vector3 PlayerForward; // camera forward, flattened by the brain
        public bool Watched;          // beast is on screen, in fog range and not occluded
        /// <summary>0..1 from GameState.BeastGrace: 1 = the beast neither evolves nor presses (tutorial console).</summary>
        public float Grace;
    }

    public struct BeastIntent
    {
        public Vector3 MoveTarget;   // where to head
        public float Speed;          // m/s
        public int DesiredStage;     // 1..3
        public bool Hunting;         // true once it is actively chasing
    }

    /// <summary>Monster thread. Decides what the beast does each tick.</summary>
    public interface IBeastBrain
    {
        BeastIntent Think(BeastPerception p, float dt);
    }

    /// <summary>Audio thread.</summary>
    public interface IAudioDirector
    {
        /// <summary>0 = unsettling, 0.5 = tense, 1 = panic.</summary>
        void SetIntensity(float intensity01);
        /// <summary>e.g. "console_on", "correct", "wrong", "beast_roar", "stage_up", "death", "liftoff".</summary>
        void PlayStinger(string name);
        /// <summary>Process and play a voice line (3D on the beast for Monster). Events arrive on the main thread:
        /// Started only once it is actually audible. Returns a handle (0 = refused). Optional: the default drops it.</summary>
        int PlayVoice(VoiceClip clip, Action<VoicePlayback> onEvent)
        {
            onEvent?.Invoke(new VoicePlayback { Handle = 0, Channel = clip != null ? clip.Channel : VoiceChannel.Ship, Event = VoicePlaybackEvent.Dropped });
            return 0;
        }
        /// <summary>Stop a channel (also cancels a clip still being prepared). Optional.</summary>
        void StopVoice(VoiceChannel channel, float fade = 0.15f) { }
    }

    /// <summary>UI thread. All screens; the Game thread calls these and waits on the callbacks.</summary>
    public interface IGameUI
    {
        void ShowTitle(Action onStart);
        void ShowHud(GameState state);           // called every frame during play
        void ShowPrompt(string text);            // e.g. "Press E to use console"; null/empty hides
        void ShowPuzzle(LangPuzzle puzzle, Action<string[]> onSubmit);
        void ShowAnswerFeedback(AnswerResult result);
        void ShowReveal(RevealInfo reveal, Action onContinue);
        void ShowQuiz(QuizQuestion quiz, Action<int> onChoose);
        void ShowEnd(bool escaped, GameState state, Action onRestart);
        void HideAll();
        /// <summary>The first-run tutorial, called every frame during play. Optional: defaults to nothing.</summary>
        void ShowTutorial(TutorialView view) { }
        /// <summary>Walk the player through a solved example on the console; call onDone when they finish or skip it.
        /// Optional: the default skips straight to onDone.</summary>
        void ShowWorkedExample(WorkedExample example, Action onDone) { onDone?.Invoke(); }
        /// <summary>A line of speech that is actually playing (empty Text clears). Optional.</summary>
        void ShowSubtitle(SubtitleLine line) { }
        /// <summary>The ship's hint on the console (Visible false clears). Optional.</summary>
        void ShowHint(HintView hint) { }
        /// <summary>The pronunciation cue on the quiz payoff. Optional.</summary>
        void ShowPronunciation(PronunciationView view) { }
    }
}

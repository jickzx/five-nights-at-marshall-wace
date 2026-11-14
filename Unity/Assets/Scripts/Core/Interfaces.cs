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
    }
}

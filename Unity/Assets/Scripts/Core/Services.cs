// CORE CONTRACT. Changes here must be called out in your PR description.
using UnityEngine;

namespace Fnmw.Core
{
    /// <summary>
    /// Service locator. Every slot starts with a trivial fake so the game runs end to end
    /// with any subset of real systems missing. Each area's Install() overwrites its own slots.
    /// </summary>
    public static class Services
    {
        public static GameState State { get; private set; } = new GameState();
        public static ILanguageEngine Language = new Fakes.FakeLanguageEngine();
        public static IDifficultyDirector Difficulty = new Fakes.FakeDifficultyDirector();
        public static IBeastBrain BeastBrain = new Fakes.FakeBeastBrain();
        public static IAudioDirector Audio = new Fakes.FakeAudioDirector();
        /// <summary>Optional voice features (hints, monster dialogue, pronunciation).</summary>
        public static IVoiceDirector Voice = new Fakes.FakeVoiceDirector();
        /// <summary>Text generation (Gemini). The fake is never available, so features fall back to stock lines.</summary>
        public static Ai.IAiText Ai = new Fakes.FakeAiText();
        static IGameUI _ui;
        public static IGameUI UI
        {
            get { return _ui ??= Fakes.FakeGameUI.Create(); }
            set { _ui = value; }
        }

        /// <summary>Set by the Game thread when the player spawns. Null before that.</summary>
        public static Transform Player;

        /// <summary>Restore every slot to its fake and start a fresh GameState.</summary>
        public static void ResetToFakes()
        {
            State = new GameState();
            Language = new Fakes.FakeLanguageEngine();
            Difficulty = new Fakes.FakeDifficultyDirector();
            BeastBrain = new Fakes.FakeBeastBrain();
            Audio = new Fakes.FakeAudioDirector();
            Voice = new Fakes.FakeVoiceDirector();
            Ai = new Fakes.FakeAiText();
            _ui = null;
            Player = null;
        }

        public static void NewState() => State = new GameState();
    }
}

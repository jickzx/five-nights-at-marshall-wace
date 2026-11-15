using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class CoreContractTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); }

        [Test]
        public void FakeLanguageEngine_GeneratesAndChecks()
        {
            var lang = Services.Language;
            var world = lang.Generate(42);
            Assert.That(world.Puzzles.Count, Is.GreaterThan(0));
            Assert.That(world.Real, Is.Not.Null);
            var p = lang.GetPuzzle(world, 0, 0.5f);
            Assert.IsFalse(lang.Check(world, p.Id, new[] { p.Tiles[0].Id }).Correct);
            Assert.IsNotNull(lang.GetReveal(world, p.Id));
            Assert.That(lang.GetQuiz(world, p.Id).Options.Length, Is.GreaterThan(1));
        }

        [Test]
        public void GameState_RaisesPhaseEvent()
        {
            GamePhase seen = GamePhase.Title;
            GameEvents.PhaseChanged += (_, to) => seen = to;
            Services.State.SetPhase(GamePhase.Explore);
            Assert.AreEqual(GamePhase.Explore, seen);
        }

        // ---- optional voice features (Core/Voice.cs and the default members they added)

        sealed class OldAudio : IAudioDirector   // implements only what existed before the voice features
        {
            public void SetIntensity(float i) { }
            public void PlayStinger(string name) { }
        }

        sealed class OldUi : IGameUI
        {
            public void ShowTitle(System.Action onStart) { }
            public void ShowHud(GameState state) { }
            public void ShowPrompt(string text) { }
            public void ShowPuzzle(LangPuzzle puzzle, System.Action<string[]> onSubmit) { }
            public void ShowAnswerFeedback(AnswerResult result) { }
            public void ShowReveal(RevealInfo reveal, System.Action onContinue) { }
            public void ShowQuiz(QuizQuestion quiz, System.Action<int> onChoose) { }
            public void ShowEnd(bool escaped, GameState state, System.Action onRestart) { }
            public void HideAll() { }
        }

        sealed class SpyUi : IGameUI
        {
            public HintView Hint; public PronunciationView Pron;
            public void ShowTitle(System.Action onStart) { }
            public void ShowHud(GameState state) { }
            public void ShowPrompt(string text) { }
            public void ShowPuzzle(LangPuzzle puzzle, System.Action<string[]> onSubmit) { }
            public void ShowAnswerFeedback(AnswerResult result) { }
            public void ShowReveal(RevealInfo reveal, System.Action onContinue) { }
            public void ShowQuiz(QuizQuestion quiz, System.Action<int> onChoose) { }
            public void ShowEnd(bool escaped, GameState state, System.Action onRestart) { }
            public void HideAll() { }
            public void ShowHint(HintView hint) => Hint = hint;
            public void ShowPronunciation(PronunciationView view) => Pron = view;
        }

        [Test]
        public void ResetToFakes_InstallsTheFakeVoiceDirector()   // C1
        {
            Services.Voice = null;
            Services.ResetToFakes();
            Assert.IsInstanceOf<Fnmw.Core.Fakes.FakeVoiceDirector>(Services.Voice);
        }

        [Test]
        public void FakeVoice_ShowsTheEnginesAuthoredHints_AndNoPronunciation()   // C2, C3
        {
            var spy = new SpyUi();
            Services.UI = spy;
            var world = Services.Language.Generate(1);
            Services.State.World = world;
            Services.State.CurrentPuzzleIndex = 0;
            Assert.IsTrue(Services.Voice.HintsAvailable);
            Services.Voice.RequestHint(1);
            Assert.IsTrue(spy.Hint.Visible);
            Assert.AreEqual(VoiceSource.Offline, spy.Hint.Source);
            Assert.AreEqual(1, spy.Hint.Level);
            Services.Voice.RequestHint(1);
            Assert.AreEqual(2, spy.Hint.Level);
            Assert.DoesNotThrow(() => Services.Voice.RequestPronunciation(1, false));
            Assert.AreEqual(PronunciationState.Unavailable, spy.Pron.State);
            var r = Services.Language.GetReveal(world, 1);
            Assert.AreEqual("扉を開けて", r.SpeechText);
            Assert.AreEqual("ja", r.SpeechLang);
            Assert.IsFalse(Services.Language.IsHintSafe(world, 1, "This is Japanese."));
        }

        [Test]
        public void OlderImplementations_StillCompile_AndGetSafeDefaults()   // C4
        {
            IAudioDirector audio = new OldAudio();
            VoicePlaybackEvent seen = VoicePlaybackEvent.Started;
            Assert.AreEqual(0, audio.PlayVoice(new VoiceClip { Samples = new float[10], SampleRate = 24000 }, e => seen = e.Event));
            Assert.AreEqual(VoicePlaybackEvent.Dropped, seen, "the default refuses rather than pretending to play");
            Assert.DoesNotThrow(() => audio.StopVoice(VoiceChannel.Monster));
            IGameUI ui = new OldUi();
            Assert.DoesNotThrow(() => { ui.ShowSubtitle(new SubtitleLine { Text = "x" }); ui.ShowHint(new HintView { Visible = true }); ui.ShowPronunciation(default); });
            ILanguageEngine bare = new BareEngine();
            Assert.IsNull(bare.GetHintContext(null, 1, null));
            Assert.IsFalse(bare.IsHintSafe(null, 1, "anything"), "an engine that cannot vouch for text rejects it");
        }

        sealed class BareEngine : ILanguageEngine
        {
            public LanguageWorld Generate(int seed) => null;
            public LangPuzzle GetPuzzle(LanguageWorld world, int index, float difficulty) => null;
            public AnswerResult Check(LanguageWorld world, int puzzleId, string[] tileIds) => null;
            public RevealInfo GetReveal(LanguageWorld world, int puzzleId) => null;
            public QuizQuestion GetQuiz(LanguageWorld world, int puzzleId) => null;
            public bool CheckQuiz(LanguageWorld world, int puzzleId, int optionIndex) => false;
        }

        [Test]
        public void ModelLoader_FallsBackToLabelledPlaceholder()
        {
            var go = ModelLoader.Load("definitely_missing_model");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponentInChildren<TextMesh>());
            Object.DestroyImmediate(go);
        }
    }
}

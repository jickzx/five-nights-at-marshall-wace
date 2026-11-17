using Fnmw.Core;
using Fnmw.Game;
using Fnmw.UI;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    /// <summary>The tutorial's screens driven through the real UI with scripted input.</summary>
    public class TutorialUiTests
    {
        GameUI _ui;
        WorkedExample _ex;

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            TutorialSession.Reset();
            var engine = new Fnmw.Lang.LanguageEngine();
            Services.Language = engine;
            var state = Services.State;
            state.World = engine.Generate(4242);
            state.TotalPuzzles = 3;
            _ex = WorkedExampleBuilder.Build(engine, state.World, 3);
            _ui = GameUI.Create();
            Services.UI = _ui;
            _ui.ShowHud(state);
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui) { _ui.Dispose(); Object.DestroyImmediate(_ui.gameObject); }
            GameEvents.ClearAll();
            Services.ResetToFakes();
            TutorialSession.Reset();
        }

        void Wait(float seconds) { for (float t = 0; t < seconds; t += 0.05f) _ui.Tick(0.05f, UiInput.None); }
        void Press(UiInput input) { _ui.Tick(0.05f, input); Wait(1.2f); }

        [Test]
        public void WorkedExample_EnterWalksEveryStep_AndHandsOverOnce()
        {
            Assert.IsNotNull(_ex);
            int done = 0;
            _ui.ShowWorkedExample(_ex, () => done++);
            Assert.AreEqual("Example", _ui.ScreenName);
            Wait(1f);
            Assert.AreEqual("Barrier", _ui.ExampleStep);
            string last = null;
            for (int i = 0; i < 40 && done == 0; i++)
            {
                last = _ui.ExampleStep;
                Press(UiInput.PressEnter);
            }
            Assert.AreEqual(1, done, "the real puzzle is handed over exactly once");
            Press(UiInput.PressEnter);
            Assert.AreEqual(1, done);
        }

        [Test]
        public void WorkedExample_TheRealNumberKeysBuildTheAnswer()
        {
            int done = 0;
            _ui.ShowWorkedExample(_ex, () => done++);
            Wait(1f);
            for (int i = 0; i < 10 && _ui.ExampleStep != "Build"; i++) Press(UiInput.PressEnter);
            Assert.AreEqual("Build", _ui.ExampleStep);
            foreach (int tile in _ex.Answer) Press(UiInput.Key(tile));
            Press(UiInput.PressEnter);
            Assert.AreEqual("Decoded", _ui.ExampleStep, "the typed answer is accepted and decoded");
            Press(UiInput.PressEnter);
            Assert.AreEqual(1, done);
        }

        [Test]
        public void WorkedExample_DeleteSkipsStraightToThePuzzle()
        {
            int done = 0;
            _ui.ShowWorkedExample(_ex, () => done++);
            Wait(1f);
            Press(new UiInput { Digit = -1, Clear = true });
            Assert.AreEqual(1, done);
        }

        [Test]
        public void WorkedExample_WalkingAwayNeverHandsOver()
        {
            int done = 0;
            _ui.ShowWorkedExample(_ex, () => done++);
            Wait(1f);
            Press(UiInput.PressEnter);
            _ui.HideAll();
            Wait(1f);
            Press(UiInput.PressEnter);
            Assert.AreEqual(0, done);
            Assert.AreEqual("None", _ui.ScreenName);
        }

        [Test]
        public void TutorialLayer_ShowsTheCueOverTheConsole()
        {
            var p = Services.Language.GetPuzzle(Services.State.World, 0, 0.1f);
            Services.State.SetPhase(GamePhase.Puzzle);   // never drawn over the title
            _ui.ShowPuzzle(p, ids => { });
            for (int i = 0; i < 30; i++)
            {
                _ui.ShowTutorial(new TutorialView { Active = true, Cue = TutorialCue.PuzzleTiles, CueTime = i * 0.05f });
                _ui.Tick(0.05f, UiInput.None);
            }
            Assert.AreEqual("PuzzleTiles", _ui.TutorialCueShown);
            Services.State.SetPhase(GamePhase.Title);
            for (int i = 0; i < 10; i++) { _ui.ShowTutorial(new TutorialView { Active = true, Cue = TutorialCue.PuzzleTiles }); _ui.Tick(0.05f, UiInput.None); }
            Assert.AreNotEqual("PuzzleTiles", _ui.TutorialCueShown, "the title is never written over");
        }

        [Test]
        public void Title_OffersReplayOnlyOnceSeen_AndTAsksForIt()
        {
            bool started = false;
            TutorialSession.Seen = true;
            _ui.ShowTitle(() => started = true);
            Wait(0.5f);
            _ui.Tick(0.05f, UiInput.PressReplay);
            Assert.IsTrue(started);
            Assert.IsTrue(TutorialSession.ReplayRequested);
            Assert.IsTrue(TutorialSession.WantsTutorial);
        }
    }
}

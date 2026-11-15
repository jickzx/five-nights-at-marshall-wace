using System.Collections.Generic;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.UI.Tests
{
    /// <summary>
    /// The voice actions in a headset, end to end through the UI: XrBridge's X press reaches the console as a hint
    /// request and the payoff as a replay, the copy names X instead of H / P, and the UI publishes when X is useful.
    /// </summary>
    public class XrVoiceBindingTests
    {
        sealed class RecordingVoice : IVoiceDirector
        {
            public readonly List<int> Hints = new List<int>();
            public readonly List<(int id, bool replay)> Speaks = new List<(int, bool)>();
            public bool HintsAvailable { get; set; } = true;
            public void RequestHint(int puzzleId) => Hints.Add(puzzleId);
            public void RequestPronunciation(int puzzleId, bool replay) => Speaks.Add((puzzleId, replay));
        }

        GameUI _ui;
        RecordingVoice _voice;

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            XrBridge.Reset();
            _ui = GameUI.Create();
            Services.UI = _ui;
            _voice = new RecordingVoice();
            Services.Voice = _voice;
            Services.State.World = Services.Language.Generate(5);
            Services.State.TotalPuzzles = Services.State.World.Puzzles.Count;
            _ui.ShowHud(Services.State);
            XrBridge.Active = true;
        }

        [TearDown]
        public void TearDown()
        {
            XrBridge.Reset();
            if (_ui) { _ui.Dispose(); Object.DestroyImmediate(_ui.gameObject); }
            GameEvents.ClearAll();
            Services.ResetToFakes();
        }

        void Wait(float seconds) { for (float t = 0; t < seconds; t += 0.05f) _ui.Tick(0.05f, UiInput.None); }

        /// <summary>One frame of Touch input, read the way GameUI.Update reads it.</summary>
        void PressX()
        {
            XrBridge.ClearFrame();
            XrBridge.Hint = XrBridge.Speak = true;
            var input = _ui.Rig.ReadInput(0f);
            XrBridge.ClearFrame();
            _ui.Tick(0.05f, input);
        }

        LangPuzzle OpenPuzzle(System.Action<string[]> onSubmit = null)
        {
            var p = Services.Language.GetPuzzle(Services.State.World, 0, 0.3f);
            Services.State.SetPhase(GamePhase.Puzzle);
            _ui.ShowPuzzle(p, onSubmit ?? (_ => { }));
            Wait(0.6f);
            return p;
        }

        [Test]
        public void ReadInput_CarriesTheBridgesVoicePresses_OnlyInAHeadset()
        {
            XrBridge.Hint = true; XrBridge.Speak = true;
            var vr = _ui.Rig.ReadInput(0f);
            Assert.IsTrue(vr.Hint && vr.Speak);
            XrBridge.Active = false;
            XrBridge.Hint = true; XrBridge.Speak = true;
            var desk = _ui.Rig.ReadInput(0f);
            Assert.IsFalse(desk.Hint || desk.Speak, "the desktop build never reads the bridge");
        }

        [Test]
        public void X_OnTheConsole_AsksTheShipOnce_AndIsOffered()
        {
            var p = OpenPuzzle();
            Assert.IsTrue(XrBridge.HintOffered, "the console takes a hint now");
            Assert.IsFalse(XrBridge.SpeakOffered);
            PressX();
            CollectionAssert.AreEqual(new[] { p.Id }, _voice.Hints);
            Assert.AreEqual(0, _voice.Speaks.Count, "X on the console never plays the sentence");
            Assert.AreEqual(0, _ui.PuzzlePicks, "X places no tile");

            _voice.HintsAvailable = false;
            Wait(0.1f);
            Assert.IsFalse(XrBridge.HintOffered, "no hints: the controller must not offer X");
        }

        [Test]
        public void TheConsoleLegend_NamesTouchButtons_AndOffersXNotH()
        {
            OpenPuzzle(ids => _ui.ShowAnswerFeedback(new AnswerResult { Correct = false, Feedback = "No." }));
            StringAssert.StartsWith("TRIGGER PLACES", _ui.PuzzleFooterShown);
            _ui.Tick(0.05f, UiInput.Key(0)); _ui.Tick(0.05f, UiInput.PressEnter);   // wrong once
            Wait(10f);
            StringAssert.Contains("X SHIP", _ui.PuzzleFooterShown);
            StringAssert.DoesNotContain("H SHIP", _ui.PuzzleFooterShown);
            StringAssert.DoesNotContain("KEYS", _ui.PuzzleFooterShown);
        }

        [Test]
        public void X_OnThePayoff_Replays_AndTheCueSaysX()
        {
            var p = OpenPuzzle();
            var world = Services.State.World;
            _ui.ShowReveal(Services.Language.GetReveal(world, p.Id), () => _ui.ShowQuiz(Services.Language.GetQuiz(world, p.Id), _ => { }));
            PressX();
            Assert.AreEqual(0, _voice.Speaks.Count, "nothing to replay before the quiz is answered");
            Assert.AreEqual(0, _voice.Hints.Count, "X on the decode screen never asks the ship");
            // through the reveal (A to continue from the read step) to the quiz, then answer it
            for (int i = 0; i < 200 && _ui.ScreenName != "Quiz"; i++)
                _ui.Tick(0.05f, _ui.ScreenName == "Read" ? UiInput.PressEnter : UiInput.None);
            Assert.AreEqual("Quiz", _ui.ScreenName);
            Wait(0.5f);
            _ui.Tick(0.05f, UiInput.Key(0));
            Wait(2.5f);
            Assert.AreEqual("Payoff", _ui.ScreenName);
            Assert.AreEqual(1, _voice.Speaks.Count, "the payoff asks once by itself");
            Assert.IsFalse(XrBridge.SpeakOffered, "not offered until the voice is ready");

            _ui.ShowPronunciation(new PronunciationView { PuzzleId = p.Id, State = PronunciationState.Ready, Source = VoiceSource.Live });
            Wait(0.1f);
            Assert.IsTrue(XrBridge.SpeakOffered);
            Assert.IsFalse(XrBridge.HintOffered);
            Assert.AreEqual("X  HEAR IT AGAIN", _ui.PronunciationCueShown);
            PressX();
            Assert.AreEqual(2, _voice.Speaks.Count);
            Assert.IsTrue(_voice.Speaks[1].replay, "X on the payoff is a replay");
            Assert.AreEqual(0, _voice.Hints.Count);
        }

        [Test]
        public void OffersClear_WhenTheScreensGo()
        {
            OpenPuzzle();
            Assert.IsTrue(XrBridge.HintOffered);
            _ui.HideAll();
            Services.State.SetPhase(GamePhase.Explore);
            Wait(0.1f);
            Assert.IsFalse(XrBridge.HintOffered || XrBridge.SpeakOffered, "roaming, X is only the lamp");
        }
    }
}

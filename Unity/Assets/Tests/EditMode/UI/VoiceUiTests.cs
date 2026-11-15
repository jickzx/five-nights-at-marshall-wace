using System.Collections.Generic;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.UI.Tests
{
    /// <summary>The ship's hint line, subtitles, and the pronunciation cue: what the UI asks for, and when.</summary>
    public class VoiceUiTests
    {
        sealed class RecordingVoice : IVoiceDirector
        {
            public readonly List<int> Hints = new List<int>();
            public readonly List<int> Locked = new List<int>();
            public readonly List<(int id, bool replay, string step)> Speaks = new List<(int, bool, string)>();
            public GameUI Ui;
            public bool HintsAvailable { get; set; } = true;
            public void RequestHint(int puzzleId) => Hints.Add(puzzleId);
            public void NotifyQuizAnswerLocked(int puzzleId) => Locked.Add(puzzleId);
            public void RequestPronunciation(int puzzleId, bool replay) => Speaks.Add((puzzleId, replay, Ui ? Ui.ScreenName : "?"));
        }

        GameUI _ui;
        RecordingVoice _voice;

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            _ui = GameUI.Create();
            Services.UI = _ui;
            _voice = new RecordingVoice { Ui = _ui };
            Services.Voice = _voice;
            Services.State.World = Services.Language.Generate(5);
            Services.State.TotalPuzzles = Services.State.World.Puzzles.Count;
            _ui.ShowHud(Services.State);
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui) { _ui.Dispose(); Object.DestroyImmediate(_ui.gameObject); }
            GameEvents.ClearAll();
            Services.ResetToFakes();
        }

        void Wait(float seconds) { for (float t = 0; t < seconds; t += 0.05f) _ui.Tick(0.05f, UiInput.None); }
        void Press(UiInput input) => _ui.Tick(0.05f, input);

        LangPuzzle OpenPuzzle(System.Action<string[]> onSubmit = null)
        {
            var p = Services.Language.GetPuzzle(Services.State.World, 0, 0.3f);
            Services.State.SetPhase(GamePhase.Puzzle);
            _ui.ShowPuzzle(p, onSubmit ?? (_ => { }));
            Wait(0.6f);
            return p;
        }

        [Test]
        public void H_OnTheConsole_AsksOnceForThatPuzzle()   // I1
        {
            var p = OpenPuzzle();
            Press(UiInput.PressHint);
            CollectionAssert.AreEqual(new[] { p.Id }, _voice.Hints);
        }

        [Test]
        public void H_DuringTheMelt_TheWorkedExample_OrTheDecodeScreen_DoesNothing()   // I2
        {
            var p = OpenPuzzle(ids => _ui.ShowAnswerFeedback(new AnswerResult { Correct = false, MissedConcept = "verb-final", Feedback = "The glyphs flicker red." }));
            Press(UiInput.Key(0)); Press(UiInput.PressEnter);   // wrong: the glass melts
            Press(UiInput.PressHint);
            Assert.AreEqual(0, _voice.Hints.Count, "no hint while the wrong answer melts");
            Wait(1.5f);
            Press(UiInput.PressHint);
            Assert.AreEqual(1, _voice.Hints.Count, "after the melt H works again");

            _ui.ShowReveal(Services.Language.GetReveal(Services.State.World, p.Id), () => { });
            Wait(0.5f);
            Press(UiInput.PressHint);
            Assert.AreEqual(1, _voice.Hints.Count, "no hint on the decode screen");

            var example = new WorkedExample { Puzzle = p, Answer = new[] { 1, 0 }, LanguageName = "German", Rule = "r", RealText = "x", Translation = "y" };
            _ui.ShowWorkedExample(example, () => { });
            Wait(0.5f);
            Press(UiInput.PressHint);
            Assert.AreEqual(1, _voice.Hints.Count, "no hint on the worked example");
        }

        [Test]
        public void AHint_TakesTheFooter_WithoutRejectionEffects_AndAWrongAnswerTakesItBack()   // I3, I4
        {
            var p = OpenPuzzle(ids => _ui.ShowAnswerFeedback(new AnswerResult { Correct = false, Feedback = "The glyphs flicker red." }));
            Press(UiInput.Key(0));
            int picks = _ui.PuzzlePicks, cracks = _ui.Cracks;
            _ui.ShowHint(new HintView { PuzzleId = p.Id, Visible = true, Pending = true, Level = 1 });
            Wait(0.1f);
            StringAssert.StartsWith("SHIP: ANALYSING", _ui.PuzzleFooterShown);
            _ui.ShowHint(new HintView { PuzzleId = p.Id, Visible = true, Text = "Where does the action word sit in the rows?", Level = 1, Source = VoiceSource.Offline });
            Wait(2f);
            Assert.AreEqual("SHIP: WHERE DOES THE ACTION WORD SIT IN THE ROWS?", _ui.PuzzleFooterShown);
            Assert.AreEqual(picks, _ui.PuzzlePicks, "picks are untouched");
            Assert.AreEqual(cracks, _ui.Cracks, "no crack, no rejection");
            Press(UiInput.PressEnter);   // wrong answer: the rejection takes the footer
            Wait(0.5f);
            StringAssert.StartsWith("THE GLYPHS", _ui.PuzzleFooterShown);
            Wait(10f);
            StringAssert.StartsWith("SHIP:", _ui.PuzzleFooterShown, "the hint returns once the rejection has had its say");
            _ui.ShowHint(new HintView { PuzzleId = p.Id, Visible = true, Text = "Compare rows 1 and 3.", Source = VoiceSource.Mock });
            Wait(1f);
            StringAssert.StartsWith("SHIP (MOCK):", _ui.PuzzleFooterShown, "mock output is labelled");
            _ui.ShowHint(new HintView { PuzzleId = p.Id + 99, Visible = true, Text = "Stale." });
            Wait(0.2f);
            StringAssert.DoesNotContain("STALE", _ui.PuzzleFooterShown, "a hint for another puzzle is not shown");
        }

        [Test]
        public void TheLegend_OffersTheShip_AfterAWrongAnswer_OnlyWhenHintsExist()
        {
            OpenPuzzle(ids => _ui.ShowAnswerFeedback(new AnswerResult { Correct = false, Feedback = "No." }));
            Wait(0.2f);
            Assert.IsFalse(_ui.PuzzleFooterShown.Contains("H SHIP"), "a player who is never wrong sees the old legend");
            Press(UiInput.Key(0)); Press(UiInput.PressEnter);
            Wait(10f);
            StringAssert.Contains("H SHIP", _ui.PuzzleFooterShown);
            _voice.HintsAvailable = false;
            Wait(0.1f);
            Assert.IsFalse(_ui.PuzzleFooterShown.Contains("H SHIP"));
        }

        [Test]
        public void Pronunciation_IsAskedForExactlyOnce_OnEnteringThePayoff_AndPReplays()   // I5, I6
        {
            var p = OpenPuzzle();
            var world = Services.State.World;
            _ui.ShowReveal(Services.Language.GetReveal(world, p.Id), () => _ui.ShowQuiz(Services.Language.GetQuiz(world, p.Id), _ => { }));
            Assert.AreEqual("Morph", _ui.ScreenName);
            Press(UiInput.PressSpeak);
            Wait(4.5f);
            Assert.AreEqual("Read", _ui.ScreenName);
            Press(UiInput.PressSpeak);
            Press(UiInput.PressEnter);
            Assert.AreEqual("Quiz", _ui.ScreenName);
            Wait(0.6f);
            Press(UiInput.PressSpeak);
            Assert.AreEqual(0, _voice.Speaks.Count, "nothing is spoken before the quiz is answered");
            Assert.AreEqual(0, _voice.Locked.Count);
            Press(UiInput.Key(1));
            CollectionAssert.AreEqual(new[] { p.Id }, _voice.Locked);
            Assert.AreEqual("Verdict", _ui.ScreenName);
            Press(UiInput.PressSpeak);
            Assert.AreEqual(0, _voice.Speaks.Count, "nor while the verdict shows");
            Wait(2f);
            Assert.AreEqual("Payoff", _ui.ScreenName);
            Assert.AreEqual(1, _voice.Speaks.Count);
            Assert.AreEqual((p.Id, false, "Payoff"), _voice.Speaks[0]);
            Wait(1f);
            Assert.AreEqual(1, _voice.Speaks.Count, "asked once, not every frame");
            Press(UiInput.PressSpeak);
            Assert.AreEqual(2, _voice.Speaks.Count);
            Assert.AreEqual((p.Id, true, "Payoff"), _voice.Speaks[1]);
            // the cue follows the reported state, and says plainly when the audio is a mock tone
            _ui.ShowPronunciation(new PronunciationView { PuzzleId = p.Id, State = PronunciationState.Playing, Source = VoiceSource.Live });
            Wait(0.1f);
            Assert.AreEqual("VOICE: PLAYING", _ui.PronunciationCueShown);
            _ui.ShowPronunciation(new PronunciationView { PuzzleId = p.Id, State = PronunciationState.Ready, Source = VoiceSource.Mock });
            Wait(0.1f);
            StringAssert.Contains("MOCK TONE (NOT SPEECH)", _ui.PronunciationCueShown);
            _ui.ShowPronunciation(new PronunciationView { PuzzleId = p.Id, State = PronunciationState.Hidden });
            Wait(0.1f);
            Assert.AreEqual("", _ui.PronunciationCueShown);
        }

        [Test]
        public void AQuizWithNoReveal_AsksForNoPronunciation()   // I7
        {
            _ui.ShowQuiz(Services.Language.GetQuiz(Services.State.World, 1), _ => { });
            Wait(0.6f);
            Press(UiInput.Key(0));
            Assert.DoesNotThrow(() => Wait(2.5f));
            Assert.AreEqual("Payoff", _ui.ScreenName);
            Press(UiInput.PressSpeak);
            Assert.AreEqual(0, _voice.Speaks.Count);
        }

        [Test]
        public void TheFooter_NeverOverlapsTilesOrSlots_OnAnyRealLayout()   // I8
        {
            var eng = new Fnmw.Lang.LanguageEngine();
            var footer = new Rect(PuzzleLayout.L, PuzzleLayout.Foot, PuzzleLayout.R - PuzzleLayout.L, 34);
            var roaming = new Rect(SubtitleLayer.X, SubtitleLayer.Y, SubtitleLayer.W, SubtitleLayer.H);
            int n = 0;
            for (int seed = 1; seed <= 24; seed++)
            {
                var w = eng.Generate(seed);
                for (int i = 0; i < w.Puzzles.Count; i++)
                    foreach (float d in new[] { 0.05f, 0.5f, 0.95f })
                    {
                        var lay = PuzzleLayout.For(eng.GetPuzzle(w, i, d));
                        Assert.IsFalse(footer.Overlaps(lay.Slots), "slots");
                        foreach (var t in lay.Tiles) Assert.IsFalse(footer.Overlaps(t), "tile " + t);
                        n++;
                    }
            }
            Assert.That(footer.yMax, Is.LessThanOrEqualTo(UiRig.StageH));
            Assert.That(roaming.yMax, Is.LessThanOrEqualTo(312), "the roaming line sits above SHIP POWER");
            // long hints page two lines at a time instead of spilling out of the footer
            var pages = SubtitleLayer.Pages(("SHIP: " + new string('x', 20) + " " + string.Join(" ", new string[30]).Replace(" ", "word ")).ToUpperInvariant(), footer.width, 2);
            foreach (var page in pages) Assert.That(Kit.Lines(page, 16, footer.width), Is.LessThanOrEqualTo(2));
            Assert.That(n, Is.GreaterThan(100));
        }

        [Test]
        public void HideAll_AndShowEnd_ClearHintAndSubtitle()   // I9
        {
            var p = OpenPuzzle();
            _ui.ShowHint(new HintView { PuzzleId = p.Id, Visible = true, Text = "Look at row 2." });
            _ui.ShowSubtitle(new SubtitleLine { Channel = VoiceChannel.Monster, Text = "You slipped.", Seconds = 3f, Source = VoiceSource.Live });
            _ui.HideAll();
            Services.State.SetPhase(GamePhase.Explore);
            Wait(0.2f);
            Assert.IsNull(_ui.RoamingSubtitleShown);
            _ui.ShowSubtitle(new SubtitleLine { Channel = VoiceChannel.Monster, Text = "You slipped.", Seconds = 3f, Source = VoiceSource.Live });
            Wait(0.2f);
            Assert.AreEqual("IT: YOU SLIPPED.", _ui.RoamingSubtitleShown);
            Wait(3.2f);
            Assert.IsNull(_ui.RoamingSubtitleShown, "the line goes when its audio ends");
            _ui.ShowSubtitle(new SubtitleLine { Channel = VoiceChannel.Monster, Text = "Run.", Seconds = 3f, Source = VoiceSource.Mock });
            Wait(0.1f);
            Assert.AreEqual("IT (MOCK TONE, NOT SPEECH): RUN.", _ui.RoamingSubtitleShown);
            Services.State.SetPhase(GamePhase.Dead);
            _ui.ShowEnd(false, Services.State, () => { });
            Wait(0.1f);
            Assert.IsNull(_ui.RoamingSubtitleShown);
        }

        [Test]
        public void ASubtitle_OnTheConsole_UsesTheFooter()   // I10's counterpart: the UI draws what it is given
        {
            OpenPuzzle();
            _ui.ShowSubtitle(new SubtitleLine { Channel = VoiceChannel.Monster, Text = "I can wait.", Seconds = 2f, Source = VoiceSource.Live });
            Wait(0.2f);
            Assert.AreEqual("IT: I CAN WAIT.", _ui.PuzzleFooterShown);
            Assert.IsNull(_ui.RoamingSubtitleShown, "not drawn twice");
            Wait(2.2f);
            StringAssert.StartsWith("KEYS PLACE", _ui.PuzzleFooterShown);
        }
    }
}

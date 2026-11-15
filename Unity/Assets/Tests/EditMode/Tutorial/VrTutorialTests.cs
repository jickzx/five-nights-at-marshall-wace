using System.Linq;
using System.Text.RegularExpressions;
using Fnmw.Core;
using Fnmw.Game;
using Fnmw.UI;
using NUnit.Framework;

namespace Fnmw.Tests
{
    /// <summary>
    /// The headset tutorial: it starts by showing the player the controllers in their hands, every card names the
    /// Touch buttons (never a keyboard key), and a tutorial run eases the beast in instead of letting it loose at once.
    /// </summary>
    public class VrTutorialTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); TutorialSession.Reset(); }
        [TearDown] public void TearDown() { TutorialSession.Reset(); XrBridge.Reset(); }

        static TutorialFlow.Inputs Vr(bool down = false, bool button = false, bool at = false) => new TutorialFlow.Inputs
        {
            Phase = GamePhase.Explore, ConsoleDistance = 30f, AtConsole = at, Total = 3,
            NeedTurn = true, HandsStep = true, LookingDown = down, AnyButton = button,
        };

        static TutorialFlow Started() { var f = new TutorialFlow(); f.Begin(); return f; }

        // ---- the Hands step ---------------------------------------------------------------------

        [Test]
        public void Hands_ComesFirstInAHeadset_NotOnDesktop()
        {
            var vr = Started();
            vr.Tick(0.1f, Vr());
            Assert.AreEqual(TutorialCue.Hands, vr.Cue);

            var desk = Started();
            desk.Tick(0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Explore, ConsoleDistance = 30f, Total = 3 });
            Assert.AreEqual(TutorialCue.Move, desk.Cue, "desktop starts with walking, as before");
        }

        [Test]
        public void Hands_MovesOnOnceThePlayerHasLookedDownAWhile()
        {
            var f = Started();
            f.Tick(0.1f, Vr());
            for (int i = 0; i < (int)((TutorialFlow.HandsMin - 0.3f) / 0.1f); i++) f.Tick(0.1f, Vr(down: true));
            Assert.AreEqual(TutorialCue.Hands, f.Cue, "gives the card time to be read");
            for (int i = 0; i < 10; i++) f.Tick(0.1f, Vr(down: true));
            Assert.AreEqual(TutorialCue.Move, f.Cue);
            for (int i = 0; i < 50; i++) f.Tick(0.1f, Vr());
            Assert.AreNotEqual(TutorialCue.Hands, f.Cue, "shown once");
        }

        [Test]
        public void Hands_AButtonPressAlsoCounts_AndNobodyIsStuck()
        {
            var press = Started();
            press.Tick(0.1f, Vr());
            press.Tick(TutorialFlow.HandsMin, Vr());
            press.Tick(0.1f, Vr(button: true));
            Assert.AreEqual(TutorialCue.Move, press.Cue, "pressing a button means they found them");

            var stuck = Started();
            for (float t = 0; t < TutorialFlow.HandsTimeout + 1f; t += 0.5f) stuck.Tick(0.5f, Vr());
            Assert.AreEqual(TutorialCue.Move, stuck.Cue, "times out");

            var console = Started();
            console.Tick(0.1f, Vr());
            console.Tick(0.1f, Vr(at: true));
            Assert.AreNotEqual(TutorialCue.Hands, console.Cue, "walked straight to a console: teach the console");
        }

        // ---- the beast eases in -------------------------------------------------------------------

        [Test]
        public void Grace_IsFullOnTheGuidedConsole_ThenStepsDownToNothing()
        {
            var f = Started();
            Assert.AreEqual(1f, f.GraceFor(GamePhase.Puzzle, 0, 4));
            f.Tick(0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Reveal, Solved = 1, Total = 4 });
            float g1 = f.GraceFor(GamePhase.Explore, 1, 4), g2 = f.GraceFor(GamePhase.Explore, 2, 4), g3 = f.GraceFor(GamePhase.Explore, 3, 4);
            Assert.AreEqual(TutorialFlow.RampStart, g1, 1e-4f, "gentle straight after the first console");
            Assert.Greater(g1, g2); Assert.Greater(g2, g3);
            Assert.Greater(g3, 0f, "still a little gentle before the last console");
            Assert.AreEqual(0f, f.GraceFor(GamePhase.Explore, 4, 4), 1e-4f);
            Assert.AreEqual(0f, f.GraceFor(GamePhase.Chase, 2, 4), "the final run is the real thing");
            Assert.Less(g1, 1f, "eased, not frozen: it evolves and presses, just slower");
        }

        [Test]
        public void Grace_KeepsEasingAfterTheTutorialCardsEnd()
        {
            var f = Started();
            f.Tick(0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Reveal, Solved = 1, Total = 3 });
            f.Tick(0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Explore, Solved = 1, Total = 3 });
            f.Tick(TutorialFlow.BeastSeconds + 0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Explore, Solved = 1, Total = 3 });
            f.Tick(TutorialFlow.NextSeconds + 0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Explore, Solved = 1, Total = 3 });
            Assert.IsFalse(f.Active, "the cards are done");
            Assert.Greater(f.GraceFor(GamePhase.Explore, 1, 3), 0f, "the beast is still eased in");
        }

        [Test]
        public void Grace_NoRampWhenSkippedAbortedOrNeverTaught()
        {
            var skipped = Started();
            for (int i = 0; i < 10; i++) skipped.Tick(0.1f, new TutorialFlow.Inputs { Phase = GamePhase.Explore, Total = 3, SkipHeld = true });
            Assert.AreEqual(0f, skipped.GraceFor(GamePhase.Explore, 1, 3));

            var aborted = Started();
            aborted.Abort();
            Assert.AreEqual(0f, aborted.GraceFor(GamePhase.Explore, 1, 3));

            Assert.AreEqual(0f, new TutorialFlow().GraceFor(GamePhase.Explore, 1, 3), "a run without the tutorial");
        }

        // ---- the cards name the Touch buttons ---------------------------------------------------------

        static readonly Regex KeyboardKey = new Regex(@"\[(E|F|Q|W|A?S?D|SHIFT|ENTER|BKSP|SPACE|TAB|ESC|[0-9])\]");

        [Test]
        public void EveryCard_HasAHeadsetVersion_ThatNamesNoKeyboardKeys()
        {
            foreach (var cue in TutorialLayer.DesktopCues.Concat(new[] { TutorialCue.Hands }))
            {
                var lines = TutorialLayer.XrLines(cue);
                Assert.IsNotNull(lines, cue + ": needs headset copy");
                foreach (var l in lines)
                {
                    Assert.IsFalse(KeyboardKey.IsMatch(l), cue + ": keyboard key in \"" + l + "\"");
                    StringAssert.DoesNotContain("MOUSE", l, cue.ToString());
                }
            }
        }

        [Test]
        public void ActionCards_SayWhichButton()
        {
            var actions = new[] { TutorialCue.Move, TutorialCue.Console, TutorialCue.PuzzleTiles, TutorialCue.PuzzleSend,
                TutorialCue.Misfire, TutorialCue.Reveal, TutorialCue.Quiz, TutorialCue.Beast };
            foreach (var cue in actions)
                Assert.IsTrue(TutorialLayer.XrLines(cue).Any(l => Regex.IsMatch(l, @"\[(A|B|X|Y|TRIGGER|L-GRIP|L-STICK|R-STICK)\]")),
                    cue + ": should name the Touch button to press");
        }
    }
}

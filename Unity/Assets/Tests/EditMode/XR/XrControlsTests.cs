using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    /// <summary>The Touch layout (TouchMapper), pressed frame by frame the way the rig feeds it.</summary>
    public class XrControlsTests
    {
        const float Dt = 1f / 72f;   // Quest 2 frame
        TouchMapper _m;

        [SetUp] public void SetUp() => _m = new TouchMapper();

        MappedInput Frame(TouchFrame f) => _m.Tick(f, Dt);

        /// <summary>Hold a frame for <paramref name="seconds"/>; returns what fired on any frame.</summary>
        MappedInput Hold(TouchFrame f, float seconds)
        {
            var any = new MappedInput();
            for (float t = 0; t < seconds; t += Dt)
            {
                var m = Frame(f);
                any.Enter |= m.Enter; any.Back |= m.Back; any.Clear |= m.Clear; any.Leave |= m.Leave; any.Replay |= m.Replay;
                any.Space |= m.Space; any.Click |= m.Click; any.Interact |= m.Interact; any.Flashlight |= m.Flashlight;
                any.SkipHeld |= m.SkipHeld;
            }
            return any;
        }

        [Test]
        public void A_SendsAndConfirms_AndUsesTheConsole()
        {
            var m = Frame(new TouchFrame { A = true });
            Assert.IsTrue(m.Enter, "A is send / confirm");
            Assert.IsTrue(m.Interact, "A uses the console while roaming");
            Assert.IsFalse(m.Space || m.Back || m.Clear || m.Flashlight);
            Assert.IsFalse(Frame(new TouchFrame { A = true }).Enter, "one press, one send");
        }

        [Test]
        public void RightTrigger_ClicksTheGlassUnderTheLaser_OrPlacesAtTheCursor_NeverBoth()
        {
            var onGlass = Frame(new TouchFrame { RightTrigger = true, PointerOnGlass = true });
            Assert.IsTrue(onGlass.Click); Assert.IsFalse(onGlass.Space, "a click already places the tile under the laser");
            Assert.IsTrue(onGlass.Interact);
            Frame(new TouchFrame());
            var off = Frame(new TouchFrame { RightTrigger = true, PointerOnGlass = false });
            Assert.IsTrue(off.Space, "off the glass: place the tile under the stick cursor / continue");
            Assert.IsFalse(off.Click);
            Assert.IsFalse(off.Enter, "the trigger never sends");
        }

        [Test]
        public void LeftTrigger_DoesNothing()
        {
            var m = Hold(new TouchFrame { LeftTrigger = true }, 1f);
            Assert.IsFalse(m.Enter || m.Space || m.Click || m.Back || m.Clear || m.Leave || m.Interact, "no accidental submit from the left trigger");
        }

        [Test]
        public void B_TapUndoes_OnRelease()
        {
            Assert.IsFalse(Hold(new TouchFrame { B = true }, 0.2f).Back, "nothing while still held");
            var up = Frame(new TouchFrame());
            Assert.IsTrue(up.Back, "a tap undoes when let go");
            Assert.IsFalse(up.Clear);
        }

        [Test]
        public void B_HoldClears_Once_AndNeverUndoes()
        {
            var held = Hold(new TouchFrame { B = true }, TouchMapper.HoldSeconds + 0.4f);
            Assert.IsTrue(held.Clear, "holding B clears");
            Assert.IsFalse(held.Back);
            var up = Frame(new TouchFrame());
            Assert.IsFalse(up.Back, "letting go after a hold is not a tap");
            Assert.IsFalse(up.Clear);
        }

        [Test]
        public void Y_TapLeavesTheConsole_AndAsksForTheTutorialOnTheTitle()
        {
            Hold(new TouchFrame { Y = true }, 0.15f);
            var up = Frame(new TouchFrame());
            Assert.IsTrue(up.Leave);
            Assert.IsTrue(up.Replay, "T on the title");
        }

        [Test]
        public void Y_HoldSkipsTheTutorial_WithoutLeavingFirst()
        {
            var held = Hold(new TouchFrame { Y = true }, 1.0f);    // longer than the tutorial's 0.8 s hold
            Assert.IsTrue(held.SkipHeld, "the tutorial's hold timer runs while Y is down");
            Assert.IsFalse(held.Leave, "holding to skip must not walk away from the console first");
            var up = Frame(new TouchFrame());
            Assert.IsFalse(up.Leave || up.Replay, "letting go after a hold is not a tap");
            Assert.IsFalse(up.SkipHeld);
        }

        [Test]
        public void X_IsTheLamp_AndGripRuns_AndLeftStickWalks()
        {
            var m = Frame(new TouchFrame { X = true, LeftGrip = true, LeftStick = new Vector2(0f, 0.9f) });
            Assert.IsTrue(m.Flashlight);
            Assert.IsFalse(m.Enter, "X no longer sends");
            Assert.IsTrue(m.Run);
            Assert.AreEqual(0.9f, m.Move.y, 1e-4f);
            Assert.AreEqual(Vector2.zero, Frame(new TouchFrame { LeftStick = new Vector2(0.05f, 0.05f) }).Move, "dead zone");
        }

        [Test]
        public void EitherStick_StepsTheScreenCursor()
        {
            Assert.IsTrue(Frame(new TouchFrame { RightStick = new Vector2(0.9f, 0) }).Right);
            Frame(new TouchFrame());
            Assert.IsTrue(Frame(new TouchFrame { LeftStick = new Vector2(0, -0.9f) }).Down);
        }

        [Test]
        public void HoldTap_ProgressFillsThenResets()
        {
            var h = new HoldTap(0.5f);
            h.Tick(true, Dt);
            for (int i = 0; i < 18; i++) h.Tick(true, Dt);
            Assert.That(h.Progress, Is.InRange(0.4f, 0.6f));
            for (int i = 0; i < 30; i++) h.Tick(true, Dt);
            Assert.AreEqual(0f, h.Progress, "fired: no more progress to show");
            h.Tick(false, Dt);
            Assert.IsFalse(h.Tap);
            Assert.AreEqual(0f, h.Held);
        }

        [Test]
        public void WorkedExample_HelpNamesSkippingInsteadOfClearing_AndTracksPracticeStep()
        {
            var reading = XrHelp.ContextFor(GamePhase.Puzzle, false, XrLessonStage.Reading);
            Assert.AreEqual(XrContext.LessonReading, reading);
            XrHelp.RightLabels(reading, false, out var a, out var b, out _, out _, out _);
            Assert.AreEqual("NEXT", a);
            StringAssert.Contains("SKIP EXAMPLE", b);
            StringAssert.DoesNotContain("CLEAR", b);
            var building = XrHelp.ContextFor(GamePhase.Puzzle, false, XrLessonStage.Building);
            Assert.AreEqual(XrContext.LessonBuilding, building);
            StringAssert.Contains("place / send", XrHelp.Card(building, false, true));
            StringAssert.Contains("skip example", XrHelp.Card(building, false, true));
            StringAssert.DoesNotContain("clear", XrHelp.Card(building, false, true));
            StringAssert.DoesNotContain("B  back", XrHelp.Card(XrContext.Screen, false, false));
        }

        [Test]
        public void Labels_NameOnlyWhatWorks()
        {
            XrHelp.RightLabels(XrContext.Console, false, out var a, out var b, out _, out var trig, out _);
            Assert.AreEqual("SEND", a); StringAssert.Contains("UNDO", b); StringAssert.Contains("HOLD", b); Assert.AreEqual("PLACE", trig);
            XrHelp.LeftLabels(XrContext.Console, true, out var x, out var y, out _, out var lt, out _);
            Assert.AreEqual("", x, "X does nothing on the console"); Assert.AreEqual("", lt, "the left trigger does nothing");
            StringAssert.Contains("LEAVE", y); StringAssert.Contains("SKIP", y);
            XrHelp.LeftLabels(XrContext.Roam, false, out x, out y, out var ls, out _, out var grip);
            Assert.AreEqual("LAMP", x); Assert.AreEqual("WALK", ls); Assert.AreEqual("RUN", grip); Assert.AreEqual("", y);
            XrHelp.RightLabels(XrContext.Title, false, out a, out _, out _, out _, out _);
            Assert.AreEqual("START", a);
            XrHelp.LeftLabels(XrContext.Title, false, out _, out y, out _, out _, out _);
            Assert.AreEqual("TUTORIAL", y);
            Assert.AreEqual(XrContext.Cutscene, XrHelp.ContextFor(GamePhase.Explore, true));
            Assert.AreEqual(XrContext.Console, XrHelp.ContextFor(GamePhase.Puzzle, false));
        }
    }
}

using Fnmw.Core;
using NUnit.Framework;

namespace Fnmw.XR.Tests
{
    /// <summary>
    /// The voice actions on Touch: X asks the ship on a console (H on a keyboard) and replays the decoded sentence on
    /// the quiz payoff (P). X is also the lamp while roaming, so the mapping, the labels and the wrist card must agree
    /// on which one a press means where.
    /// </summary>
    public class XrVoiceControlsTests
    {
        const float Dt = 1f / 72f;
        TouchMapper _m;

        [SetUp] public void SetUp() => _m = new TouchMapper();

        [Test]
        public void X_FiresHintAndSpeakOnce_AndNothingThatSendsOrLeaves()
        {
            var m = _m.Tick(new TouchFrame { X = true }, Dt);
            Assert.IsTrue(m.Hint, "X asks the ship on a console");
            Assert.IsTrue(m.Speak, "X replays on the payoff");
            Assert.IsTrue(m.Flashlight, "the same press is still the lamp for the roaming player");
            Assert.IsFalse(m.Enter || m.Space || m.Back || m.Clear || m.Click || m.Leave || m.Replay || m.Interact,
                "X must not send, place, undo, leave or replay the tutorial");
            var held = _m.Tick(new TouchFrame { X = true }, Dt);
            Assert.IsFalse(held.Hint || held.Speak, "holding X asks once, not every frame");
            _m.Tick(new TouchFrame(), Dt);
            Assert.IsTrue(_m.Tick(new TouchFrame { X = true }, Dt).Hint, "a second press asks again");
        }

        [Test]
        public void OtherButtons_NeverAskTheShipOrSpeak()
        {
            var frames = new[]
            {
                new TouchFrame { A = true }, new TouchFrame { RightTrigger = true, PointerOnGlass = true },
                new TouchFrame { RightTrigger = true }, new TouchFrame { LeftTrigger = true }, new TouchFrame { Menu = true },
                new TouchFrame { LeftGrip = true },
            };
            foreach (var f in frames)
            {
                _m = new TouchMapper();
                var m = _m.Tick(f, Dt);
                Assert.IsFalse(m.Hint || m.Speak, "only X is the voice button");
            }
            // B and Y act on release (tap) or after a hold: neither is ever the voice button
            _m = new TouchMapper();
            for (float t = 0; t < 1f; t += Dt) { var m = _m.Tick(new TouchFrame { B = true, Y = true }, Dt); Assert.IsFalse(m.Hint || m.Speak); }
            var up = _m.Tick(new TouchFrame(), Dt);
            Assert.IsFalse(up.Hint || up.Speak);
        }

        [Test]
        public void ConsoleLabel_OffersTheShip_OnlyWhenTheUiDoes()
        {
            XrHelp.LeftLabels(XrContext.Console, false, out var x, out var y, out _, out _, out _, hint: false, speak: true);
            Assert.AreEqual("", x, "no hints available: X is unlabelled (it does nothing there)");
            Assert.AreEqual("LEAVE", y);
            XrHelp.LeftLabels(XrContext.Console, false, out x, out y, out _, out _, out _, hint: true, speak: false);
            Assert.AreEqual("SHIP HINT", x);
            Assert.AreEqual("LEAVE", y, "the ship hint does not displace leave");
        }

        [Test]
        public void PayoffLabel_OffersReplay_OnlyWhenReady_AndRoamingKeepsTheLamp()
        {
            XrHelp.LeftLabels(XrContext.Screen, false, out var x, out _, out _, out _, out _, hint: true, speak: false);
            Assert.AreEqual("", x, "a hint offer means nothing on a screen");
            XrHelp.LeftLabels(XrContext.Screen, false, out x, out _, out _, out _, out _, hint: false, speak: true);
            Assert.AreEqual("HEAR AGAIN", x);
            XrHelp.LeftLabels(XrContext.Roam, false, out x, out _, out _, out _, out _, hint: true, speak: true);
            Assert.AreEqual("LAMP", x, "roaming, X is always the lamp");
            XrHelp.LeftLabels(XrContext.Title, false, out x, out _, out _, out _, out _, hint: true, speak: true);
            Assert.AreEqual("", x);
        }

        [Test]
        public void WristCard_NamesXOnlyWhereItWorks_AndStaysShort()
        {
            StringAssert.DoesNotContain("X  ", XrHelp.Card(XrContext.Console, false, false));
            StringAssert.Contains("X  ask the ship", XrHelp.Card(XrContext.Console, false, false, hint: true));
            StringAssert.DoesNotContain("hear", XrHelp.Card(XrContext.Screen, false, false));
            StringAssert.Contains("X  hear it again", XrHelp.Card(XrContext.Screen, false, false, speak: true));
            StringAssert.DoesNotContain("ship", XrHelp.Card(XrContext.Roam, false, false, hint: true, speak: true));
            foreach (XrContext c in System.Enum.GetValues(typeof(XrContext)))
                Assert.LessOrEqual(XrHelp.Card(c, false, true, true, true).Split('\n').Length, 6, "a heading and at most five lines: " + c);
        }

        [Test]
        public void Bridge_ClearsThePressEachFrame_ButResetClearsTheOffers()
        {
            try
            {
                XrBridge.Hint = XrBridge.Speak = true;
                XrBridge.HintOffered = XrBridge.SpeakOffered = true;
                XrBridge.ClearFrame();
                Assert.IsFalse(XrBridge.Hint || XrBridge.Speak, "presses last one frame");
                Assert.IsTrue(XrBridge.HintOffered && XrBridge.SpeakOffered, "offers are state the UI republishes, not presses");
                XrBridge.Reset();
                Assert.IsFalse(XrBridge.HintOffered || XrBridge.SpeakOffered);
            }
            finally { XrBridge.Reset(); }
        }
    }
}

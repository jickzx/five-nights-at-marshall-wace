using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    public class XrTests
    {
        [TearDown] public void TearDown() => XrBridge.Reset();

        [Test]
        public void NoHeadset_DetectLeavesDesktopControls()
        {
            // The Editor (and the desktop build) has no XR loader running, so the VR port must stay off.
            Assert.IsFalse(XrModule.Detect());
            Assert.IsFalse(XrBridge.Active);
            Assert.AreEqual(-1, XrBridge.Digit);
            XrModule.Install();   // a no-op without a headset
            Assert.IsNull(GameObject.Find("XrRig"));
        }

        [Test]
        public void Bridge_ResetClearsInput()
        {
            XrBridge.Active = true; XrBridge.Move = Vector2.one; XrBridge.Interact = true; XrBridge.Digit = 3; XrBridge.HasPointer = true;
            XrBridge.ClearFrame();
            Assert.IsTrue(XrBridge.Active, "ClearFrame keeps the mode");
            Assert.AreEqual(Vector2.zero, XrBridge.Move);
            Assert.IsFalse(XrBridge.Interact);
            Assert.AreEqual(-1, XrBridge.Digit);
            Assert.IsFalse(XrBridge.HasPointer);
            XrBridge.Reset();
            Assert.IsFalse(XrBridge.Active);
        }

        [Test]
        public void Stick_PressesOnceThenRepeats()
        {
            var r = new StickRepeater();
            r.Tick(new Vector2(0.9f, 0.1f), 0.016f);
            Assert.IsTrue(r.Right);
            int presses = 1;
            float held = 0.016f;
            while (held < StickRepeater.RepeatDelay - 0.02f) { r.Tick(new Vector2(0.9f, 0f), 0.016f); held += 0.016f; if (r.Right) presses++; }
            Assert.AreEqual(1, presses, "no repeat before the delay");
            for (int i = 0; i < 60; i++) { r.Tick(new Vector2(0.9f, 0f), 0.016f); if (r.Right) presses++; }
            Assert.Greater(presses, 2, "held stick repeats");
            r.Tick(Vector2.zero, 0.016f);
            Assert.IsFalse(r.Left || r.Right || r.Up || r.Down);
            r.Tick(new Vector2(0f, -0.8f), 0.016f);
            Assert.IsTrue(r.Down);
            r.Tick(new Vector2(0f, 0.8f), 0.016f);
            Assert.IsTrue(r.Up, "flipping direction presses at once");
        }

        [Test]
        public void Stick_HysteresisHoldsDirection()
        {
            Assert.AreEqual(-1, StickRepeater.Classify(new Vector2(0.5f, 0f), -1), "below the press threshold");
            Assert.AreEqual(1, StickRepeater.Classify(new Vector2(0.5f, 0f), 1), "above release keeps the held direction");
            Assert.AreEqual(-1, StickRepeater.Classify(new Vector2(0.2f, 0f), 1));
            Assert.AreEqual(0, StickRepeater.Classify(new Vector2(-0.9f, 0.2f), -1));
        }

        [Test]
        public void Snap_TurnsOncePerFlick()
        {
            var t = new Turner { Smooth = false, SnapDegrees = 45f };
            Assert.AreEqual(45f, t.Tick(0.9f, 0.016f));
            Assert.AreEqual(0f, t.Tick(0.9f, 0.016f), "held: no second turn");
            Assert.AreEqual(0f, t.Tick(0.1f, 0.016f));
            Assert.AreEqual(-45f, t.Tick(-0.95f, 0.016f), "re-armed after the stick returned");
        }

        [Test]
        public void Smooth_TurnsWithStickAndDeadZone()
        {
            var t = new Turner { Smooth = true, SmoothDegreesPerSecond = 100f };
            Assert.AreEqual(0f, t.Tick(0.1f, 1f));
            Assert.AreEqual(100f, t.Tick(1f, 1f), 1e-3f);
            Assert.Less(t.Tick(-0.5f, 1f), 0f);
        }

        [Test]
        public void Laser_HitsGlassInUvSpace()
        {
            var go = new GameObject("Glass", typeof(RectTransform));
            try
            {
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(640, 360);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one * (2.4f / 640f);
                rt.position = new Vector3(0, 1.6f, 2f);
                rt.rotation = Quaternion.identity;     // readable face toward -Z, where the player stands
                Assert.IsTrue(GlassHit.Raycast(rt, new Vector3(0, 1.6f, 0), Vector3.forward, out var uv, out var p));
                Assert.AreEqual(0.5f, uv.x, 1e-3f); Assert.AreEqual(0.5f, uv.y, 1e-3f);
                Assert.AreEqual(2f, p.z, 1e-3f);
                Assert.IsTrue(GlassHit.Raycast(rt, new Vector3(-0.6f, 1.6f + 0.3375f, 0), Vector3.forward, out uv, out _));
                Assert.AreEqual(0.25f, uv.x, 1e-3f); Assert.AreEqual(0.75f, uv.y, 1e-3f);
                Assert.IsFalse(GlassHit.Raycast(rt, new Vector3(0, 1.6f, 0), Vector3.back, out _, out _), "pointing away");
                Assert.IsFalse(GlassHit.Raycast(rt, new Vector3(5f, 1.6f, 0), Vector3.forward, out _, out _), "off the side");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ControlCard_NamesTheButtonsForEachContext()
        {
            string roam = XrHelp.Card(XrContext.Roam, false, false);
            foreach (var word in new[] { "walk", "run", "turn", "use", "lamp", "snap" }) StringAssert.Contains(word, roam);
            StringAssert.Contains("smooth", XrHelp.Card(XrContext.Roam, true, false));
            StringAssert.Contains("skip tutorial", XrHelp.Card(XrContext.Roam, false, true));
            string console = XrHelp.Card(XrContext.Console, false, false);
            foreach (var word in new[] { "place", "send", "undo", "hold B", "clear", "leave" }) StringAssert.Contains(word, console);
            StringAssert.DoesNotContain("X ", console, "X does nothing on the console any more");
            StringAssert.Contains("replay tutorial", XrHelp.Card(XrContext.Title, false, false));
            foreach (XrContext c in System.Enum.GetValues(typeof(XrContext)))
                Assert.LessOrEqual(XrHelp.Card(c, false, true).Split('\n').Length, 6, "a heading and at most five lines: " + c);
        }
    }
}

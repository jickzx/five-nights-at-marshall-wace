using System.Linq;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    /// <summary>
    /// The tutorial lights the buttons it asks for on the controllers in the player's hands. Every lit button must
    /// do something in that context (its label is not empty), and the model must show the button's own name.
    /// </summary>
    public class XrTutorialGuideTests
    {
        GameObject _root;
        [TearDown] public void TearDown() { if (_root) Object.DestroyImmediate(_root); XrBridge.Reset(); }

        static string LabelFor(XrHelp.Key k, XrContext c)
        {
            XrHelp.RightLabels(c, false, out var a, out var b, out var rs, out var rt, out var rg);
            XrHelp.LeftLabels(c, true, out var x, out var y, out var ls, out var lt, out var lg, hint: true, speak: true);
            switch (k.Button)
            {
                case XrControllerModel.Button.Face1: return k.Left ? x : a;
                case XrControllerModel.Button.Face2: return k.Left ? y : b;
                case XrControllerModel.Button.Stick: return k.Left ? ls : rs;
                case XrControllerModel.Button.Trigger: return k.Left ? lt : rt;
                default: return k.Left ? lg : rg;
            }
        }

        // the context each cue is normally shown in
        static readonly (TutorialCue cue, XrContext ctx)[] Shown =
        {
            (TutorialCue.Move, XrContext.Roam), (TutorialCue.Objective, XrContext.Roam), (TutorialCue.Console, XrContext.Roam),
            (TutorialCue.PuzzleTiles, XrContext.Console), (TutorialCue.PuzzleSend, XrContext.Console), (TutorialCue.Misfire, XrContext.Console),
            (TutorialCue.Reveal, XrContext.Screen), (TutorialCue.Quiz, XrContext.Screen), (TutorialCue.Beast, XrContext.Roam),
            (TutorialCue.NextConsole, XrContext.Roam), (TutorialCue.Example, XrContext.LessonBuilding),
        };

        [Test]
        public void EveryLitButton_DoesSomethingThere()
        {
            foreach (var (cue, ctx) in Shown)
            {
                var keys = XrHelp.TutorialHighlights(cue, ctx);
                Assert.IsNotEmpty(keys, cue + ": an action step lights at least one button");
                foreach (var k in keys)
                    Assert.IsNotEmpty(LabelFor(k, ctx), $"{cue} lights {k}, which does nothing in {ctx}");
            }
        }

        [Test]
        public void TheRightButtons_ForTheBigSteps()
        {
            var tiles = XrHelp.TutorialHighlights(TutorialCue.PuzzleTiles, XrContext.Console);
            Assert.AreEqual("R.Trigger", string.Join(",", tiles.Select(k => k.ToString())));
            var send = XrHelp.TutorialHighlights(TutorialCue.PuzzleSend, XrContext.Console).Select(k => k.ToString()).ToArray();
            CollectionAssert.AreEquivalent(new[] { "R.Face1", "R.Face2" }, send, "A sends, B takes back");
            var move = XrHelp.TutorialHighlights(TutorialCue.Move, XrContext.Roam).Select(k => k.ToString()).ToArray();
            CollectionAssert.AreEquivalent(new[] { "L.Stick", "R.Stick" }, move);
            Assert.IsEmpty(XrHelp.TutorialHighlights(TutorialCue.None, XrContext.Roam), "no tutorial: nothing pulses");
            Assert.IsNotEmpty(XrHelp.TutorialHighlights(TutorialCue.Hands, XrContext.Roam));
        }

        [Test]
        public void Model_LabelsLeadWithTheButtonName_AndGuidedButtonsStandOut()
        {
            _root = new GameObject("Grip");
            var right = new XrControllerModel(_root.transform, left: false);
            var left = new XrControllerModel(_root.transform, left: true);
            right.Label(XrControllerModel.Button.Face1, "SEND");
            left.Label(XrControllerModel.Button.Face1, "LAMP");
            Assert.AreEqual("A  SEND", right.DrawnLabelOf(XrControllerModel.Button.Face1));
            Assert.AreEqual("X  LAMP", left.DrawnLabelOf(XrControllerModel.Button.Face1));
            Assert.AreEqual("SEND", right.LabelOf(XrControllerModel.Button.Face1), "the job alone, as before");
            Assert.AreEqual("", right.DrawnLabelOf(XrControllerModel.Button.Face2), "no job, not guided: hidden");

            var keys = XrHelp.TutorialHighlights(TutorialCue.PuzzleSend, XrContext.Console);
            right.Guide(keys); left.Guide(keys);
            Assert.IsTrue(right.IsGuided(XrControllerModel.Button.Face1));
            Assert.IsTrue(right.IsGuided(XrControllerModel.Button.Face2));
            Assert.IsFalse(left.IsGuided(XrControllerModel.Button.Face1), "only the right hand's A and B");
            Assert.AreEqual("> A  SEND", right.DrawnLabelOf(XrControllerModel.Button.Face1));
            Assert.AreEqual("> B", right.DrawnLabelOf(XrControllerModel.Button.Face2), "a guided button with no label yet still shows its name");

            right.Guide(null);
            Assert.IsFalse(right.IsGuided(XrControllerModel.Button.Face1));
            Assert.AreEqual("A  SEND", right.DrawnLabelOf(XrControllerModel.Button.Face1));
        }

        [Test]
        public void Model_UsesTheModelledArt_LaidOutLikeTouch()
        {
            _root = new GameObject("Grip");
            foreach (var left in new[] { false, true })
            {
                var m = new XrControllerModel(_root.transform, left);
                Assert.IsTrue(m.FromArt, (left ? "left" : "right") + ": the Blender controller in Resources/Models/XR loads");
                float X(string part)
                {
                    foreach (var t in m.Root.GetComponentsInChildren<Transform>())
                        if (t.name == part) return m.Root.InverseTransformPoint(t.GetComponent<Renderer>().bounds.center).x;
                    Assert.Fail(part + " missing"); return 0f;
                }
                float outer = left ? -1f : 1f;
                Assert.Greater(outer * X("Face1"), outer * X("Stick"), "face buttons on the outer side, the stick on the thumb side");
                Assert.Greater(outer * X("Face2"), outer * X("Stick"));
                Assert.Less(outer * X("Grip"), 0f, "the grip on the inner side of the handle");
                var face1 = m.Root.Find("Art/Face1"); var face2 = m.Root.Find("Art/Face2");
                Assert.Greater(m.Root.InverseTransformPoint(face2.position).z, m.Root.InverseTransformPoint(face1.position).z, "B/Y sits above A/X, further along the grip");
                Assert.IsNotNull(m.Root.Find("Art/Stick/StickCap"), "the cap rides on the stick so it tilts with it");
            }
        }

        [Test]
        public void Model_IsNotBlackOnBlack()
        {
            _root = new GameObject("Grip");
            var m = new XrControllerModel(_root.transform, left: false);
            foreach (var r in m.Root.GetComponentsInChildren<Renderer>())
            {
                Color.RGBToHSV(r.sharedMaterial.color, out _, out _, out var v);
                Assert.Greater(v, 0.2f, r.name + " is too dark to see on the night shore");
            }
        }
    }
}

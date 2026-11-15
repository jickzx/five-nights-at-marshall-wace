using Fnmw.Core;
using Fnmw.Game;
using Fnmw.Monster.Beast;
using Fnmw.UI;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests.Quest
{
    /// <summary>
    /// The Quest-only changes outside Fnmw.XR: beast scale in a headset, the beast placed by where the head looks,
    /// the in-world death scare in front of the face, turn practice in the tutorial, and the VR copy on screens.
    /// Desktop behaviour is checked alongside, unchanged.
    /// </summary>
    public class QuestUsabilityTests
    {
        [TearDown] public void TearDown() => XrBridge.Reset();

        // ---- beast scale -------------------------------------------------------------------

        [Test]
        public void VrHeights_AreBiggerThanThePlayer_AndSmallerThanDesktop()
        {
            float[] want = { 2.05f, 2.35f, 2.7f };
            for (int stage = 1; stage <= 3; stage++)
            {
                float vr = BeastController.TargetHeight(stage, true);
                Assert.AreEqual(want[stage - 1], vr, 1e-4f);
                Assert.Greater(vr, 1.8f + 0.2f, "towers over the 1.8 m player");
                Assert.Less(vr, BeastController.TargetHeight(stage, false), "desktop heights unchanged and larger");
                if (stage > 1) Assert.Greater(vr, BeastController.TargetHeight(stage - 1, true), "grows with each stage");
            }
        }

        [Test]
        public void FitScale_VrIsExact_DesktopOnlyFixesImportUnits()
        {
            // a model 2.6 m tall: inside desktop's tolerance for 2.35 m, so desktop keeps it, VR shrinks it
            Assert.AreEqual(1f, BeastController.FitScale(2.6f, 2.35f, exact: false));
            Assert.AreEqual(2.05f / 2.6f, BeastController.FitScale(2.6f, 2.05f, exact: true), 1e-4f);
            Assert.AreEqual(2.35f / 0.0235f, BeastController.FitScale(0.0235f, 2.35f, exact: false), 1e-2f, "centimetre import");
            Assert.AreEqual(1f, BeastController.FitScale(0f, 2f, true), "nothing to measure");
        }

        [Test]
        public void FitHeight_ResizesTheModel_EvenUnderAPoppingRoot()
        {
            var root = new GameObject("Beast");
            try
            {
                root.transform.localScale = Vector3.one * 0.6f;      // AnimateEffects' spawn pop, mid-way
                var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = new Vector3(1f, 2.6f, 1f);
                BeastController.FitHeight(model, 2.35f, exact: true);
                root.transform.localScale = Vector3.one;              // the pop finishes
                float h = model.GetComponent<Renderer>().bounds.size.y;
                Assert.AreEqual(2.35f, h, 0.01f, "fitted in the root's own units, so it is right once the pop ends");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---- the beast goes where the player looks ---------------------------------------------

        [Test]
        public void SkipKeyBeast_GoesInFrontOfTheView_NotTheBody()
        {
            // in VR the body faces +Z but the head has turned 90 degrees right in the room
            GameDirector.BeastSpot(new Vector3(5f, 1f, -2f), 90f, behind: false, 2.2f, out var pos, out var rot);
            Assert.AreEqual(5f + 2.2f, pos.x, 1e-3f); Assert.AreEqual(-2f, pos.z, 1e-3f); Assert.AreEqual(1f, pos.y, 1e-4f);
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, Vector3.left), 0.999f, "faces the player");
            GameDirector.BeastSpot(Vector3.zero, 90f, behind: true, 14f, out pos, out _);
            Assert.AreEqual(-14f, pos.x, 1e-3f);
        }

        [Test]
        public void VrScare_StandsInFrontOfTheFace_OnTheGround_FacingTheHead()
        {
            var head = new Vector3(3f, 1.6f, 4f);
            var headFwd = Quaternion.Euler(25f, -120f, 0) * Vector3.forward;   // looking down-left, body still faces +Z
            DeathCutscene.ScarePose(head, headFwd, Vector3.forward, 0.2f, out var pos, out var rot);
            Vector3 flatFwd = headFwd; flatFwd.y = 0; flatFwd.Normalize();
            Vector3 to = pos - head; to.y = 0;
            Assert.Less(Vector3.Angle(flatFwd, to), 0.5f, "straight ahead of the face");
            Assert.AreEqual(DeathCutscene.XrScareDistance, to.magnitude, 1e-3f);
            Assert.AreEqual(0.2f, pos.y, 1e-4f, "stands on the player's ground");
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, -flatFwd), 0.999f, "looks back at the player");
        }

        // ---- tutorial: practise the turn in VR --------------------------------------------------

        static TutorialFlow.Inputs Walked(bool needTurn, float turned) => new TutorialFlow.Inputs
        {
            Phase = GamePhase.Explore, Moved = 5f, Looked = 60f, ConsoleDistance = 30f, Total = 3,
            NeedTurn = needTurn, Turned = turned,
        };

        [Test]
        public void Tutorial_Vr_WaitsForAStickTurn_DesktopDoesNot()
        {
            var desk = new TutorialFlow(); desk.Begin();
            desk.Tick(0.1f, Walked(false, 0f));
            Assert.AreEqual(TutorialCue.Objective, desk.Cue, "desktop: walking and looking is enough");

            var vr = new TutorialFlow(); vr.Begin();
            vr.Tick(0.1f, Walked(true, 0f));
            Assert.AreEqual(TutorialCue.Move, vr.Cue, "VR: head-looking alone does not teach the stick turn");
            vr.Tick(0.1f, Walked(true, 45f));
            Assert.AreEqual(TutorialCue.Objective, vr.Cue, "one snap turn later it moves on");

            var stuck = new TutorialFlow(); stuck.Begin();
            for (float t = 0; t < TutorialFlow.MoveTimeoutXr + 1f; t += 0.5f) stuck.Tick(0.5f, Walked(true, 0f));
            Assert.AreNotEqual(TutorialCue.Move, stuck.Cue, "nobody is stuck: it times out");
        }

        [Test]
        public void Tutorial_SkipNeedsTheFullHold()
        {
            var f = new TutorialFlow(); f.Begin();
            var held = Walked(true, 0f); held.SkipHeld = true;
            for (float t = 0; t < TutorialFlow.SkipHoldSeconds - 0.1f; t += 0.05f) f.Tick(0.05f, held);
            Assert.IsTrue(f.Active, "not skipped before the hold completes");
            f.Tick(0.2f, held);
            Assert.IsFalse(f.Active);
            Assert.IsTrue(f.Skipped);
        }

        // ---- VR copy on the screens --------------------------------------------------------------

        [Test]
        public void Screens_NameTouchButtonsInVr_KeysOnDesktop()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            TutorialSession.Reset();
            var ui = GameUI.Create();
            try
            {
                Services.UI = ui;
                ui.ShowHud(Services.State);
                Services.State.SetPhase(GamePhase.Explore);
                string Card()
                {
                    for (int i = 0; i < 12; i++) { ui.ShowTutorial(new TutorialView { Active = true, Cue = TutorialCue.Move, CueTime = i * 0.05f }); ui.Tick(0.05f, UiInput.None); }
                    return ui.TutorialTextShown;
                }
                string desk = Card();
                StringAssert.Contains("[W]", desk);
                Assert.AreEqual(PuzzleScreen_HintKeys, ui.PuzzleHint);

                ui.HideAll();
                for (int i = 0; i < 6; i++) { ui.ShowTutorial(default); ui.Tick(0.05f, UiInput.None); }
                XrBridge.Active = true;
                string vr = Card();
                StringAssert.Contains("[L-STICK]", vr);
                StringAssert.Contains("[R-STICK] TURN", vr);
                StringAssert.DoesNotContain("[W]", vr);
                StringAssert.DoesNotContain("ENTER", ui.PuzzleHint);
                StringAssert.Contains("A SEND", ui.PuzzleHint);
            }
            finally
            {
                XrBridge.Reset();
                ui.Dispose(); Object.DestroyImmediate(ui.gameObject);
                GameEvents.ClearAll(); Services.ResetToFakes(); TutorialSession.Reset();
            }
        }

        const string PuzzleScreen_HintKeys = "KEYS PLACE  ·  BKSP UNDO  ·  ENTER SEND";
    }
}

using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    /// <summary>The VR ground chevron: same target as the visor, lies on the real ground, hides when it should.</summary>
    public class XrGuideTests
    {
        GameObject _root;

        [SetUp] public void SetUp() { _root = new GameObject("GuideTestRoot"); Interactable.All.Clear(); }

        [TearDown]
        public void TearDown()
        {
            Interactable.All.Clear();
            if (_root) Object.DestroyImmediate(_root);
            Services.ResetToFakes();
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Quaternion rot)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(_root.transform, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale = size;
            return g;
        }

        XrGuide Guide(out Transform player)
        {
            player = new GameObject("Player").transform;
            player.SetParent(_root.transform, false);
            var cap = player.gameObject.AddComponent<CapsuleCollider>();   // the player's own collider is never "ground"
            cap.center = new Vector3(0, 0.9f, 0); cap.height = 1.8f; cap.radius = 0.35f;
            var g = new GameObject("XrGuide").AddComponent<XrGuide>();
            g.transform.SetParent(_root.transform, false);
            g.Build(player);
            return g;
        }

        [Test]
        public void LiesOnFlatGround_AheadAlongTheWay()
        {
            Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), Quaternion.identity);
            var g = Guide(out var player);
            player.position = new Vector3(2f, 0f, 3f);
            Physics.SyncTransforms();
            Assert.IsTrue(g.PlaceToward(Vector3.right));
            var c = g.Chevron;
            Assert.AreEqual(2f + XrGuide.Ahead, c.position.x, 0.01f);
            Assert.AreEqual(3f, c.position.z, 0.01f);
            Assert.That(c.position.y, Is.InRange(0.005f, 0.05f), "just above the ground: not floating, not buried");
            Assert.Greater(Vector3.Dot(c.forward, Vector3.right), 0.99f, "points the way");
        }

        [Test]
        public void FloodedGround_KeepsGuideAboveWaveCrests_ThenReturnsToDryGround()
        {
            Services.ResetToFakes();
            Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), Quaternion.identity);
            var g = Guide(out _);
            Physics.SyncTransforms();
            Services.State.SeaLevel = 0.4f;
            Services.State.SeaWaveCrest = 0.402f;
            Assert.IsTrue(g.PlaceToward(Vector3.forward));
            Assert.That(g.Chevron.position.y, Is.InRange(0.81f, 0.85f));
            Assert.Less(Vector3.Angle(g.Chevron.up, Vector3.up), 0.01f);
            Services.State.SeaLevel = -1f;
            Assert.IsTrue(g.PlaceToward(Vector3.forward));
            Assert.That(g.Chevron.position.y, Is.InRange(0.005f, 0.05f));
            Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.LessEqual,
                g.Chevron.GetComponent<Renderer>().sharedMaterial.GetFloat("_ZTest"));
        }

        [Test]
        public void FollowsASlope_AndARaisedStep()
        {
            // a ramp rising towards +Z at 20 degrees
            Box("Ramp", new Vector3(0, 0, 4f), new Vector3(10, 0.2f, 10), Quaternion.Euler(-20f, 0, 0));
            var g = Guide(out var player);
            player.position = new Vector3(0, 0.1f, 2.1f);
            Physics.SyncTransforms();
            Assert.IsTrue(g.PlaceToward(Vector3.forward));
            var c = g.Chevron;
            Assert.Less(Vector3.Angle(c.up, Quaternion.Euler(-20f, 0, 0) * Vector3.up), 1f, "tilted with the ground");
            Assert.IsTrue(Physics.Raycast(c.position + Vector3.up, Vector3.down, out var hit, 3f));
            Assert.That(c.position.y - hit.point.y, Is.InRange(0.005f, 0.06f), "sits on the slope surface");
        }

        [Test]
        public void IgnoresPillarTopsAndWalls_AndHidesOverAVoid()
        {
            Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), Quaternion.identity);
            Box("Pillar", new Vector3(0, 2f, XrGuide.Ahead), new Vector3(0.6f, 4f, 0.6f), Quaternion.identity);
            var g = Guide(out var player);
            Physics.SyncTransforms();
            Assert.IsTrue(g.PlaceToward(Vector3.forward));
            Assert.Less(g.Chevron.position.y, 0.1f, "on the ground beside the pillar, not on its top");
            player.position = new Vector3(100f, 0, 100f);   // nothing under the spot
            Physics.SyncTransforms();
            Assert.IsFalse(g.PlaceToward(Vector3.forward));
        }

        [Test]
        public void HidesOffTheRoam_AndFadesNearTheDestination()
        {
            Assert.IsTrue(XrGuide.ShouldShow(GamePhase.Explore, false, false, false));
            Assert.IsTrue(XrGuide.ShouldShow(GamePhase.Chase, false, false, false), "ship run: the chevron leads to the ship");
            Assert.IsFalse(XrGuide.ShouldShow(GamePhase.Puzzle, false, false, false), "at the console");
            Assert.IsFalse(XrGuide.ShouldShow(GamePhase.Quiz, false, false, false));
            Assert.IsFalse(XrGuide.ShouldShow(GamePhase.Explore, true, false, false), "theater up");
            Assert.IsFalse(XrGuide.ShouldShow(GamePhase.Explore, false, true, false), "cutscene");
            Assert.IsFalse(XrGuide.ShouldShow(GamePhase.Dead, false, false, true), "death");
            Assert.AreEqual(XrGuide.MaxAlpha, XrGuide.FadeFor(30f), 1e-4f);
            Assert.AreEqual(0f, XrGuide.FadeFor(XrGuide.FadeNear), 1e-4f);
            Assert.Less(XrGuide.FadeFor(4f), XrGuide.MaxAlpha);
            Assert.LessOrEqual(XrGuide.MaxAlpha, 0.4f, "dim");
        }

        [Test]
        public void Target_IsTheVisorsObjective_LitConsoleThenShip()
        {
            Interactable Make(Interactable.Kind kind, int index, bool on)
            {
                var it = new GameObject(kind + "" + index).AddComponent<Interactable>();
                it.transform.SetParent(_root.transform, false);
                it.Type = kind; it.Index = index; it.Enabled = on;
                if (!Interactable.All.Contains(it)) Interactable.All.Add(it);   // OnEnable does not run in edit mode
                return it;
            }
            var c0 = Make(Interactable.Kind.Console, 0, false);
            var c2 = Make(Interactable.Kind.Console, 2, true);
            var c1 = Make(Interactable.Kind.Console, 1, true);
            var ship = Make(Interactable.Kind.Ship, 0, false);
            var s = new GameState();
            s.SetPhase(GamePhase.Explore);
            Assert.AreSame(c1, Interactable.CurrentObjective(s), "the lowest lit console");
            c1.Enabled = false; c2.Enabled = false;
            Assert.IsNull(Interactable.CurrentObjective(s), "nothing lit: nothing to point at");
            s.SetPhase(GamePhase.Chase);
            Assert.IsNull(Interactable.CurrentObjective(s), "ship not powered yet");
            ship.Enabled = true;
            Assert.AreSame(ship, Interactable.CurrentObjective(s));
            Assert.IsNotNull(c0);
        }
    }
}

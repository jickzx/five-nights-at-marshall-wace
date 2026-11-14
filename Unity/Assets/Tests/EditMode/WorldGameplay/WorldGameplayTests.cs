using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Game;
using Fnmw.World;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class WorldGameplayTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); }

        [Test]
        public void PickRoute_SpreadsConsolesAlongThePath()
        {
            var three = new List<int> { 0, 1, 2 };
            CollectionAssert.AreEqual(new[] { 0, 2 }, GameDirector.PickRoute(three, 2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, GameDirector.PickRoute(three, 3));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, GameDirector.PickRoute(three, 7));
            CollectionAssert.AreEqual(new[] { 0 }, GameDirector.PickRoute(three, 1));
            Assert.IsEmpty(GameDirector.PickRoute(three, 0));
        }

        [Test]
        public void Shore_TilesMeetAtSeamsAndRiseInland()
        {
            for (float z = -10f; z <= 10f; z += 0.5f)
                Assert.AreEqual(WorldLayout.Height(-10f, z), WorldLayout.Height(10f, z), 1e-3f, $"seam mismatch at z={z}");
            Assert.AreEqual(0.36f, WorldLayout.Height(3f, -4f), 0.05f, "waterline from the art notes");
            Assert.Greater(WorldLayout.Height(0, 10f), WorldLayout.Height(0, -9f));
            Assert.Greater(WorldLayout.Height(0, 30f), 8f, "cliffs box the shore in");
        }

        [Test]
        public void Sea_RisesAsAClockAndFloodsTheLowConsole()
        {
            Assert.AreEqual(WorldLayout.BaseSeaLevel, WorldLayout.SeaLevelAt(0, 0), 1e-4f);
            float prev = 0;
            for (float t = 0; t < 400; t += 10) { float l = WorldLayout.SeaLevelAt(t, 0); Assert.GreaterOrEqual(l, prev); prev = l; }
            var spawn = WorldLayout.PlayerSpawn;
            Assert.Greater(WorldLayout.Height(spawn.x, spawn.z), WorldLayout.SeaLevelAt(0, 0), "spawn starts dry");
            var low = WorldLayout.Consoles[1];
            Assert.Less(WorldLayout.Height(low.x, low.z), WorldLayout.SeaLevelAt(240, 0), "the shoreline console is under blood by four minutes");
            Assert.Greater(WorldLayout.SeaLevelAt(100, 30), WorldLayout.SeaLevelAt(100, 0), "the final chase surges the tide");
        }

        [Test]
        public void ForgivingBeast_KeepsItsDistanceAndCantOutrunYou()
        {
            var brain = new ForgivingBeastBrain(new Core.Fakes.FakeBeastBrain());
            var player = Vector3.zero;
            var close = brain.Think(new BeastPerception { BeastPosition = new Vector3(0, 0, -3), PlayerPosition = player, Distance = 3, Stage = 3, Elapsed = 100, Aggression = 1 }, 0.02f);
            Assert.Greater(Vector3.Distance(close.MoveTarget, player), 9f, "retreats when close");
            Assert.Less(close.MoveTarget.z, -3f, "retreats away from the player");
            var far = brain.Think(new BeastPerception { BeastPosition = new Vector3(0, 0, -30), PlayerPosition = player, Distance = 30, Stage = 3, Elapsed = 100, Aggression = 1 }, 0.02f);
            Assert.LessOrEqual(far.Speed, brain.MaxSpeed);
            Assert.Less(brain.MaxSpeed, 3.4f, "slower than the player's walk");
        }

        [Test]
        public void GentleDifficulty_CapsDifficultyAndAggression()
        {
            var inner = new Core.Fakes.FakeDifficultyDirector();
            for (int i = 0; i < 10; i++) inner.Observe(new AnswerEvent { Correct = true });
            var gentle = new GentleDifficulty(inner);
            Assert.LessOrEqual(gentle.Difficulty, 0.4f);
            for (int i = 0; i < 10; i++) gentle.Observe(new AnswerEvent { Correct = false });
            Assert.Less(gentle.BeastAggression, inner.BeastAggression);
        }

        [Test]
        public void CoreAdditions_FootstepEventAndShipPower()
        {
            FootstepEvent seen = default; int n = 0;
            GameEvents.Footstep += e => { seen = e; n++; };
            GameEvents.RaiseFootstep(new FootstepEvent { Surface = "blood", Speed = 3f });
            Assert.AreEqual(1, n); Assert.AreEqual("blood", seen.Surface);
            GameEvents.ClearAll();
            GameEvents.RaiseFootstep(new FootstepEvent());
            Assert.AreEqual(1, n, "ClearAll drops footstep listeners");

            var s = new GameState { TotalPuzzles = 3, PuzzlesSolved = 2 };
            Assert.AreEqual(2f / 3f, s.ShipPower, 1e-4f);
            Assert.AreEqual(0f, new GameState().ShipPower);
        }

        [Test]
        public void EndingTakes_AreFoundInTheArt()
        {
            if (!ModelLoader.HasArt("ship") || !ModelLoader.HasArt("player")) Assert.Ignore("art not imported");
            Assert.IsNotNull(ClipPlayer.Find("ship", "hatch_close"));
            Assert.IsNotNull(ClipPlayer.Find("ship", "takeoff"));
            Assert.IsNotNull(ClipPlayer.Find("player", "death_martyr"));
            Assert.IsNull(ClipPlayer.Find("ship", "definitely_not_a_take"));
        }

        [Test]
        public void Ps1Shaders_AreLoadableFromResources()
        {
            foreach (var n in new[] { "Ps1Lit", "BloodSea", "SkyDome", "CloudDeck", "Rain", "RetroScreen" })
            {
                var sh = Ps1Look.Load(n);
                Assert.IsNotNull(sh, n);
                Assert.IsTrue(sh.isSupported, n + " compiles for this platform");
            }
        }
    }
}

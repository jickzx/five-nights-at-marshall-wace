using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class CoreContractTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); }

        [Test]
        public void FakeLanguageEngine_GeneratesAndChecks()
        {
            var lang = Services.Language;
            var world = lang.Generate(42);
            Assert.That(world.Puzzles.Count, Is.GreaterThan(0));
            Assert.That(world.Real, Is.Not.Null);
            var p = lang.GetPuzzle(world, 0, 0.5f);
            Assert.IsFalse(lang.Check(world, p.Id, new[] { p.Tiles[0].Id }).Correct);
            Assert.IsNotNull(lang.GetReveal(world, p.Id));
            Assert.That(lang.GetQuiz(world, p.Id).Options.Length, Is.GreaterThan(1));
        }

        [Test]
        public void GameState_RaisesPhaseEvent()
        {
            GamePhase seen = GamePhase.Title;
            GameEvents.PhaseChanged += (_, to) => seen = to;
            Services.State.SetPhase(GamePhase.Explore);
            Assert.AreEqual(GamePhase.Explore, seen);
        }

        [Test]
        public void ModelLoader_FallsBackToLabelledPlaceholder()
        {
            var go = ModelLoader.Load("definitely_missing_model");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponentInChildren<TextMesh>());
            Object.DestroyImmediate(go);
        }
    }
}

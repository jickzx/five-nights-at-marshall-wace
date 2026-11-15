using Fnmw.Core;
using Fnmw.Game;
using Fnmw.Monster.Beast;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class TutorialFlowTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); TutorialSession.Reset(); }
        [TearDown] public void TearDown() => TutorialSession.Reset();

        static TutorialFlow.Inputs In(GamePhase phase = GamePhase.Explore, float moved = 0, float looked = 0,
            float dist = 30f, bool at = false, int solved = 0, int total = 3, bool skip = false) =>
            new TutorialFlow.Inputs
            {
                Phase = phase, Moved = moved, Looked = looked, ConsoleDistance = dist, AtConsole = at,
                Solved = solved, Total = total, SkipHeld = skip,
            };

        static TutorialFlow Started() { var f = new TutorialFlow(); f.Begin(); return f; }

        [Test]
        public void Session_WantsTutorialUntilSeen_AndAgainWhenReplayIsAsked()
        {
            Assert.IsTrue(TutorialSession.WantsTutorial, "first run of the session");
            TutorialSession.Seen = true;
            Assert.IsFalse(TutorialSession.WantsTutorial, "a restart does not show it again");
            TutorialSession.ReplayRequested = true;
            Assert.IsTrue(TutorialSession.WantsTutorial, "the title screen can ask for it");
        }

        [Test]
        public void Move_WaitsForWalkingAndLooking()
        {
            var f = Started();
            Assert.AreEqual(TutorialCue.Move, f.Cue);
            f.Tick(1f, In(moved: 5f));
            Assert.AreEqual(TutorialCue.Move, f.Cue, "walking alone is not enough");
            f.Tick(1f, In(moved: 5f, looked: 45f));
            Assert.AreEqual(TutorialCue.Objective, f.Cue);
        }

        [Test]
        public void Move_TimesOutSoNobodyIsStuck()
        {
            var f = Started();
            f.Tick(TutorialFlow.MoveTimeout + 0.1f, In());
            Assert.AreEqual(TutorialCue.Objective, f.Cue);
        }

        [Test]
        public void Objective_HidesNearTheConsole_ThenConsoleCueInRange()
        {
            var f = Started();
            f.Tick(0.1f, In(moved: 5, looked: 45));
            Assert.AreEqual(TutorialCue.Objective, f.Cue);
            f.Tick(1f, In(dist: 5f));
            Assert.AreEqual(TutorialCue.Objective, f.Cue, "shown for a few seconds at least");
            f.Tick(TutorialFlow.ObjectiveMin, In(dist: 5f));
            Assert.AreEqual(TutorialCue.None, f.Cue);
            Assert.IsTrue(f.Active, "still running while walking up");
            f.Tick(0.1f, In(dist: 2f, at: true));
            Assert.AreEqual(TutorialCue.Console, f.Cue);
            f.Tick(0.1f, In(dist: 4f, at: false));
            Assert.AreEqual(TutorialCue.None, f.Cue, "walked off");
        }

        [Test]
        public void GuidedConsole_ReadThenTilesThenSend()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleRead, f.Cue);
            f.Tick(TutorialFlow.ReadSeconds + 0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleTiles, f.Cue);
            f.OnTilePicked();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleSend, f.Cue);
        }

        [Test]
        public void GuidedConsole_PickingATileWhileReadingSkipsToSend()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            f.OnTilePicked();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleSend, f.Cue);
        }

        [Test]
        public void Misfire_ExplainsTheCost_ThenReturnsToSend()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            f.OnAnswer(false);
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.Misfire, f.Cue);
            f.Tick(TutorialFlow.MisfireSeconds + 0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleSend, f.Cue);
        }

        [Test]
        public void SteppingAwayFromTheConsole_PicksUpAgainWhenBack()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Puzzle));
            f.Tick(0.1f, In(GamePhase.Explore, at: true));
            Assert.AreEqual(TutorialCue.Console, f.Cue);
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleRead, f.Cue);
        }

        [Test]
        public void FullRun_RevealQuizBeastNext_ThenDone()
        {
            var f = Started();
            Assert.AreEqual(1f, f.Grace, "the beast is held back on the way to the first console");
            f.Tick(0.1f, In(GamePhase.Puzzle));
            f.Tick(0.1f, In(GamePhase.Reveal, solved: 1));
            Assert.AreEqual(TutorialCue.Reveal, f.Cue);
            Assert.IsTrue(f.Taught, "solving the guided console is what counts");
            Assert.AreEqual(TutorialFlow.RampStart, f.Grace, 1e-4f, "after the first console the hunt starts, eased in (VrTutorialTests)");
            f.Tick(0.1f, In(GamePhase.Quiz, solved: 1));
            Assert.AreEqual(TutorialCue.Quiz, f.Cue);
            f.Tick(0.1f, In(GamePhase.Explore, solved: 1));
            Assert.AreEqual(TutorialCue.Beast, f.Cue);
            f.Tick(TutorialFlow.BeastSeconds + 0.1f, In(GamePhase.Explore, solved: 1));
            Assert.AreEqual(TutorialCue.NextConsole, f.Cue);
            Assert.AreEqual(1, f.View(1, 3).Solved);
            f.Tick(TutorialFlow.NextSeconds + 0.1f, In(GamePhase.Explore, solved: 1));
            Assert.IsFalse(f.Active);
            Assert.IsFalse(f.View(1, 3).Active);
        }

        [Test]
        public void SecondConsole_IsNotGuided()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Reveal, solved: 1));
            f.Tick(0.1f, In(GamePhase.Explore, solved: 1));
            f.Tick(0.1f, In(GamePhase.Puzzle, solved: 1));
            Assert.AreNotEqual(TutorialCue.PuzzleRead, f.Cue);
        }

        [Test]
        public void HoldingSkip_EndsAndCountsAsSeen_TappingDoesNot()
        {
            var f = Started();
            f.Tick(0.3f, In(skip: true));
            Assert.IsTrue(f.Active);
            Assert.That(f.SkipHold01, Is.GreaterThan(0.3f).And.LessThan(0.5f));
            f.Tick(0.1f, In(skip: false));
            Assert.AreEqual(0f, f.SkipHold01, "letting go resets the hold");
            for (int i = 0; i < 10; i++) f.Tick(0.1f, In(skip: true));
            Assert.IsFalse(f.Active);
            Assert.IsTrue(f.Skipped);
            Assert.IsTrue(f.Taught);
            Assert.AreEqual(0f, f.Grace);
        }

        [Test]
        public void Abort_StopsWithoutCountingAsSeen()
        {
            var f = Started();
            f.Abort();
            Assert.IsFalse(f.Active);
            Assert.IsFalse(f.Taught);
            Assert.AreEqual(0f, f.Grace);
        }

        [Test]
        public void DeathOrFinalRun_EndsTheTutorial()
        {
            var f = Started();
            f.Tick(0.1f, In(GamePhase.Dead));
            Assert.IsFalse(f.Active);
            Assert.IsFalse(f.Taught, "died before the first console: it can play again");
            f = Started();
            f.Tick(0.1f, In(GamePhase.Chase, solved: 3));
            Assert.IsFalse(f.Active);
            Assert.IsTrue(f.Taught);
        }

        [Test]
        public void WorkedExample_HasItsOwnCue_ThenTheRealPuzzleIsGuided()
        {
            var f = Started();
            var open = In(GamePhase.Puzzle); open.ExampleOpen = true;
            f.Tick(0.1f, open);
            Assert.AreEqual(TutorialCue.Example, f.Cue);
            f.OnTilePicked();   // tiles placed by the walk-through itself do not count
            f.Tick(5f, open);
            Assert.AreEqual(TutorialCue.Example, f.Cue);
            f.Tick(0.1f, In(GamePhase.Puzzle));
            Assert.AreEqual(TutorialCue.PuzzleRead, f.Cue, "the real puzzle starts at the top");
        }

        [Test]
        public void WorkedExample_IsSolvedGlossedAndInALanguageTheRunDoesNotPlay()
        {
            var engine = new Fnmw.Lang.LanguageEngine();
            foreach (int seed in new[] { 4242, 7, 99, 123456 })
            {
                var run = engine.Generate(seed);
                var ex = WorkedExampleBuilder.Build(engine, run, 3);
                Assert.IsNotNull(ex, $"seed {seed}");
                for (int i = 0; i < 3; i++)
                    Assert.AreNotEqual(run.Languages[i].Name, ex.LanguageName, $"seed {seed}: would give console {i + 1} away");
                Assert.That(ex.Answer.Length, Is.InRange(2, 4));
                Assert.That(ex.Puzzle.Examples.Count, Is.GreaterThanOrEqualTo(2));
                foreach (var t in ex.Puzzle.Tiles) Assert.IsNotEmpty(t.Gloss);
                Assert.IsNotEmpty(ex.Rule);
                var ids = System.Array.ConvertAll(ex.Answer, k => ex.Puzzle.Tiles[k].Id);
                Assert.AreEqual(ids.Length, new System.Collections.Generic.HashSet<string>(ids).Count, "no tile used twice");
            }
        }

        [Test]
        public void WorkedExample_WithTheFakeEngine_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => WorkedExampleBuilder.Build(new Core.Fakes.FakeLanguageEngine(), null, 3));
            Assert.IsNull(WorkedExampleBuilder.Build(null, null, 3));
        }

        // ------------------------------------------------------------------ the beast during the tutorial

        static BeastPerception P(float grace, GamePhase phase = GamePhase.Explore, float aggression = 0.5f) => new BeastPerception
        {
            BeastPosition = new Vector3(0, 0, -20), PlayerPosition = Vector3.zero, Distance = 20, Phase = phase,
            Aggression = aggression, PlayerForward = Vector3.forward, Stage = 1, Grace = grace,
        };

        [Test]
        public void Grace_HoldsEvolutionStill()
        {
            var held = new BeastBrain(); var free = new BeastBrain();
            for (int t = 0; t < 120; t++) { held.Think(P(1f), 1f); free.Think(P(0f), 1f); }
            Assert.AreEqual(0f, held.Evolution, 1e-4f);
            Assert.That(free.Evolution, Is.GreaterThan(0.5f));
        }

        [Test]
        public void Grace_AMisfireStillCostsSomethingButNotAStage()
        {
            var b = new BeastBrain();
            b.Think(P(1f, GamePhase.Puzzle), 0.02f);
            b.OnAnswer(false, 1f);
            b.OnAnswer(false, 1f);
            Assert.That(b.Evolution, Is.GreaterThan(0f), "the lesson is real");
            Assert.AreEqual(1, b.Stage, "two tutorial misfires do not evolve it");
        }

        [Test]
        public void Grace_KeepsItBackWhileYouReadTheFirstConsole()
        {
            var held = new BeastBrain(); var free = new BeastBrain();
            var h = held.Think(P(1f, GamePhase.Puzzle), 0.02f);
            var f = free.Think(P(0f, GamePhase.Puzzle), 0.02f);
            Assert.That(h.MoveTarget.magnitude, Is.GreaterThanOrEqualTo(19.5f), "stays at the fog edge");
            Assert.That(f.MoveTarget.magnitude, Is.LessThan(18f), "normally it creeps up behind you");
        }

        [Test]
        public void Pacing_FirstTimePlayerMeetsStage3OnlyAfterAboutFourMinutes()
        {
            var b = new BeastBrain();
            int stage2At = -1, stage3At = -1;
            for (int t = 0; t < 400; t++)
            {
                b.Think(P(0f, aggression: 0.45f), 1f);
                if (b.Stage == 2 && stage2At < 0) stage2At = t;
                if (b.Stage == 3 && stage3At < 0) stage3At = t;
            }
            Assert.That(stage2At, Is.InRange(100, 140));
            Assert.That(stage3At, Is.InRange(200, 280));
        }

        [Test]
        public void FinalRun_AlwaysAgainstTheHunter()
        {
            var b = new BeastBrain();
            for (int i = 0; i < 4 * 50; i++) b.Think(P(0f, GamePhase.Chase), 0.02f);
            Assert.AreEqual(3, b.Stage, "only stage 3 hunts, so the ship run must reach it");
        }
    }
}

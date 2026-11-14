using System.IO;
using Fnmw.Core;
using Fnmw.Monster.Beast;
using Fnmw.Monster.Learning;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class MonsterLearningTests
    {
        static readonly string[] Concepts = { "word-order", "agreement", "plural-suffix" };

        /// <summary>
        /// A simulated player with a hidden true skill per concept. Plays rounds the way the game
        /// does: ask the director for difficulty and focus, serve, answer, observe.
        /// </summary>
        /// <returns>Mean aggression over the last 10 rounds (the steady state, not one lucky answer).</returns>
        static float Play(AdaptiveDirector dir, System.Func<string, float> trueSkill, float responseTime, int rounds, int seed)
        {
            var rng = new System.Random(seed);
            float tail = 0;
            for (int i = 0; i < rounds; i++)
            {
                string concept = dir.FocusConcept ?? Concepts[i % Concepts.Length];
                float d = dir.Difficulty;
                dir.NotePuzzle(new[] { concept }, 0f);
                float pTrue = PlayerModel.Sigmoid(trueSkill(concept) + 2f - 3f * d);
                bool ok = rng.NextDouble() < pTrue;
                dir.Observe(new AnswerEvent
                {
                    PuzzleId = i, Correct = ok, ResponseTime = responseTime, Difficulty = d,
                    MissedConcept = ok ? null : concept,
                });
                if (i >= rounds - 10) tail += dir.BeastAggression / 10f;
            }
            return tail;
        }

        static AdaptiveDirector Fresh()
        {
            var dir = new AdaptiveDirector();
            dir.NotePuzzle(Concepts, 0f);
            dir.Reset(1);
            return dir;
        }

        [Test]
        public void StrongPlayer_DrivesDifficultyUp_AndAggressionDown()
        {
            var dir = Fresh();
            float d0 = dir.Difficulty, a0 = dir.BeastAggression;
            float steady = Play(dir, _ => 3f, 5f, 40, 7);
            TestContext.WriteLine($"strong: difficulty {d0:0.00} -> {dir.Difficulty:0.00}, aggression {a0:0.00} -> {dir.BeastAggression:0.00} (last-10 mean {steady:0.00})");
            Assert.That(dir.Difficulty, Is.GreaterThan(d0 + 0.3f));
            Assert.That(dir.Difficulty, Is.GreaterThan(0.7f));
            Assert.That(dir.BeastAggression, Is.LessThan(a0));
            Assert.That(steady, Is.LessThan(0.2f));
        }

        [Test]
        public void WeakPlayer_DrivesDifficultyDown_AndAggressionUp()
        {
            var dir = Fresh();
            float d0 = dir.Difficulty, a0 = dir.BeastAggression;
            float steady = Play(dir, _ => -3f, 25f, 40, 11);
            TestContext.WriteLine($"weak: difficulty {d0:0.00} -> {dir.Difficulty:0.00}, aggression {a0:0.00} -> {dir.BeastAggression:0.00} (last-10 mean {steady:0.00})");
            Assert.That(dir.Difficulty, Is.LessThan(d0));
            Assert.That(dir.Difficulty, Is.LessThan(0.15f));
            Assert.That(dir.BeastAggression, Is.GreaterThan(a0));
            Assert.That(steady, Is.GreaterThan(0.7f));
        }

        [Test]
        public void WeakConcept_GetsProbed()
        {
            var dir = Fresh();
            for (int i = 0; i < 8; i++)
            {
                foreach (var c in new[] { "agreement", "plural-suffix" })
                {
                    dir.NotePuzzle(new[] { c }, 0f);
                    dir.Observe(new AnswerEvent { Correct = true, ResponseTime = 8f, Difficulty = dir.Difficulty });
                }
                dir.NotePuzzle(new[] { "word-order" }, 0f);
                dir.Observe(new AnswerEvent { Correct = false, MissedConcept = "word-order", ResponseTime = 8f, Difficulty = dir.Difficulty });
            }
            var m = dir.Model;
            TestContext.WriteLine($"word-order {m.SkillAtMedium("word-order"):P0}, agreement {m.SkillAtMedium("agreement"):P0}, plural {m.SkillAtMedium("plural-suffix"):P0}");
            Assert.AreEqual("word-order", dir.FocusConcept);
            Assert.That(m.SkillAtMedium("word-order"), Is.LessThan(m.SkillAtMedium("agreement") - 0.3f));
            Assert.That(m.SkillAtMedium("word-order"), Is.LessThan(m.SkillAtMedium("plural-suffix") - 0.3f));
        }

        [Test]
        public void WrongAnswer_BlamesOnlyTheMissedConcept()
        {
            var dir = Fresh();
            dir.NotePuzzle(new[] { "word-order", "agreement" }, 0f);
            dir.Observe(new AnswerEvent { Correct = false, MissedConcept = "word-order", ResponseTime = 8f, Difficulty = 0.4f });
            Assert.AreEqual(1, dir.Model.Find("word-order").Seen);
            Assert.AreEqual(0, dir.Model.Find("agreement").Seen);
            dir.NotePuzzle(new[] { "word-order", "agreement" }, 0f);
            dir.Observe(new AnswerEvent { Correct = true, ResponseTime = 8f, Difficulty = 0.4f });
            Assert.AreEqual(2, dir.Model.Find("word-order").Seen);
            Assert.AreEqual(1, dir.Model.Find("agreement").Seen);
        }

        [Test]
        public void EachAnswer_MovesPressureTheRightWay()
        {
            var dir = Fresh();
            dir.NotePuzzle(new[] { "agreement" }, 0f);
            float a = dir.BeastAggression, d = dir.Difficulty;
            dir.Observe(new AnswerEvent { Correct = true, ResponseTime = 6f, Difficulty = d });
            Assert.That(dir.BeastAggression, Is.LessThan(a), "a right answer must calm the beast");
            Assert.That(dir.Difficulty, Is.GreaterThan(d), "a right answer must raise difficulty");

            a = dir.BeastAggression; d = dir.Difficulty;
            dir.NotePuzzle(new[] { "agreement" }, 0f);
            dir.Observe(new AnswerEvent { Correct = false, MissedConcept = "agreement", ResponseTime = 6f, Difficulty = d });
            Assert.That(dir.BeastAggression, Is.GreaterThan(a), "a mistake must embolden the beast");
            Assert.That(dir.Difficulty, Is.LessThan(d), "a mistake must ease difficulty");
        }

        [Test]
        public void SlowRightAnswer_IsWeakerEvidenceThanFastOne()
        {
            var fast = Fresh(); var slow = Fresh();
            foreach (var (dir, rt) in new[] { (fast, 4f), (slow, 60f) })
            {
                dir.NotePuzzle(new[] { "agreement" }, 0f);
                dir.Observe(new AnswerEvent { Correct = true, ResponseTime = rt, Difficulty = 0.4f });
            }
            Assert.That(fast.Model.SkillAtMedium("agreement"), Is.GreaterThan(slow.Model.SkillAtMedium("agreement")));
        }

        [Test]
        public void State_RoundTripsThroughJson()
        {
            var dir = Fresh();
            Play(dir, c => c == "agreement" ? -2f : 1.5f, 9f, 25, 3);
            var json = dir.ToJson();
            var back = AdaptiveDirector.FromJson(json);
            Assert.AreEqual(dir.Difficulty, back.Difficulty, 1e-5f);
            Assert.AreEqual(dir.BeastAggression, back.BeastAggression, 1e-5f);
            Assert.AreEqual(dir.FocusConcept, back.FocusConcept);
            Assert.AreEqual(dir.Model.S.TotalAnswers, back.Model.S.TotalAnswers);
            Assert.AreEqual(dir.Model.S.History.Count, back.Model.S.History.Count);
            foreach (var c in Concepts)
            {
                Assert.AreEqual(dir.Model.Find(c).Mean, back.Model.Find(c).Mean, 1e-5f, c);
                Assert.AreEqual(dir.Model.Find(c).Var, back.Model.Find(c).Var, 1e-5f, c);
            }
            Assert.AreEqual(json, back.ToJson());
        }

        [Test]
        public void Memory_PersistsAcrossRuns_AndCanBeForgotten()
        {
            var path = Path.Combine(Path.GetTempPath(), "fnmw-beast-test-" + System.Guid.NewGuid() + ".json");
            try
            {
                var run1 = new AdaptiveDirector(new BrainStore(path));
                run1.Reset(1);
                Play(run1, _ => -2f, 20f, 12, 5);
                Assert.IsTrue(File.Exists(path));

                var run2 = new AdaptiveDirector(new BrainStore(path));
                Assert.AreEqual(run1.Model.S.TotalAnswers, run2.Model.S.TotalAnswers);
                Assert.AreEqual(run1.FocusConcept, run2.FocusConcept);
                run2.Reset(2);   // a new run keeps the skills, drops the streak
                Assert.AreEqual(2, run2.Model.S.Runs);
                Assert.AreEqual(0, run2.Model.S.Streak);
                Assert.AreEqual(run1.Model.S.TotalAnswers, run2.Model.S.TotalAnswers);
                Assert.That(run2.BeastAggression, Is.GreaterThan(0.5f), "the beast remembers a weak player");

                run2.ForgetPlayer();
                Assert.AreEqual(0, run2.Model.S.TotalAnswers);
                var run3 = new AdaptiveDirector(new BrainStore(path));
                Assert.AreEqual(0, run3.Model.S.TotalAnswers);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        // ------------------------------------------------------------------ beast brain

        static BeastPerception P(float aggression = 0.4f, GamePhase phase = GamePhase.Explore, bool watched = false, float dist = 15f) =>
            new BeastPerception
            {
                BeastPosition = new Vector3(0, 0, -dist), PlayerPosition = Vector3.zero, Distance = dist,
                Phase = phase, Aggression = aggression, PlayerForward = Vector3.forward, Watched = watched, Stage = 1,
            };

        [Test]
        public void Beast_LungesOnWrong_RetreatsOnRight()
        {
            var b = new BeastBrain();
            b.OnAnswer(false, 0.5f);
            var i = b.Think(P(phase: GamePhase.Puzzle), 0.02f);
            Assert.AreEqual(BeastMode.Lunge, b.Mode);
            Assert.That(i.Speed, Is.GreaterThan(5f));
            Assert.That(i.MoveTarget.magnitude, Is.LessThan(10f), "lunges toward the player");
            Assert.IsFalse(i.Hunting, "a stage 1 lunge is a scare, not a kill");

            b.OnAnswer(true, 0.3f);
            i = b.Think(P(phase: GamePhase.Reveal, dist: 5f), 0.02f);
            Assert.AreEqual(BeastMode.Retreat, b.Mode);
            Assert.That(i.MoveTarget.magnitude, Is.GreaterThan(BeastBrain.FogEdge), "backs into the fog");
        }

        [Test]
        public void Beast_FreezesWhenWatched()
        {
            var b = new BeastBrain();
            var i = b.Think(P(aggression: 0.8f, watched: true), 0.02f);
            Assert.AreEqual(BeastMode.Frozen, b.Mode);
            Assert.AreEqual(0f, i.Speed);
            i = b.Think(P(aggression: 0.8f, watched: false), 0.02f);
            Assert.AreNotEqual(BeastMode.Frozen, b.Mode);
            Assert.That(i.Speed, Is.GreaterThan(0f));
        }

        [Test]
        public void Beast_EvolvesWithTime_FasterWithMistakes_ThenHunts()
        {
            var calm = new BeastBrain(); var angry = new BeastBrain();
            for (int t = 0; t < 60; t++) { calm.Think(P(0.2f), 1f); angry.Think(P(0.2f), 1f); if (t % 12 == 0) angry.OnAnswer(false, 0.2f); }
            Assert.That(angry.Evolution, Is.GreaterThan(calm.Evolution));
            Assert.AreEqual(3, angry.Stage);
            Assert.AreEqual(1, calm.Stage);
            var i = angry.Think(P(0.5f), 0.02f);
            Assert.AreEqual(BeastMode.Chase, angry.Mode);
            Assert.IsTrue(i.Hunting);
            Assert.AreEqual(3, i.DesiredStage);
        }

        [Test]
        public void Beast_DoesNotEvolveOnTitle()
        {
            var b = new BeastBrain();
            for (int t = 0; t < 300; t++) b.Think(P(phase: GamePhase.Title), 1f);
            Assert.AreEqual(1, b.Stage);
        }
    }
}

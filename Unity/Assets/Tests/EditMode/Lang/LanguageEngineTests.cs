using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fnmw.Core;
using Fnmw.Lang;
using NUnit.Framework;

namespace Fnmw.Tests.Lang
{
    public class LanguageEngineTests
    {
        static readonly float[] Difficulties = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        static readonly HashSet<string> KnownConcepts = new HashSet<string>(LangConcepts.All);

        static IEnumerable<(LanguageWorld w, LangPuzzle p, int index, float diff)> Sweep(LanguageEngine eng, int seeds, float[] diffs)
        {
            for (int s = 1; s <= seeds; s++)
            {
                var w = eng.Generate(s * 7919);
                for (int i = 0; i < w.Puzzles.Count; i++)
                    foreach (var d in diffs)
                        yield return (w, eng.GetPuzzle(w, i, d), i, d);
            }
        }

        static string Fingerprint(ILanguageEngine eng, LanguageWorld w, LangPuzzle p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Id).Append('|').Append(p.TargetMeaning).Append('|').Append(p.AlienName).Append('|').Append(p.Difficulty);
            foreach (var e in p.Examples) sb.Append('|').Append(e.Picture).Append(':').Append(string.Join(" ", e.Words.Select(t => t.Alien)));
            foreach (var t in p.Tiles) sb.Append('|').Append(t.Id).Append('=').Append(t.Alien).Append('#').Append(t.GlyphSeed);
            var r = eng.GetReveal(w, p.Id);
            sb.Append('|').Append(r.AlienText).Append('|').Append(r.RealText).Append('|').Append(r.Feature);
            var q = eng.GetQuiz(w, p.Id);
            sb.Append('|').Append(string.Join(",", q.Options));
            return sb.ToString();
        }

        [Test]
        public void LangModule_RegistersRealEngine()
        {
            Services.ResetToFakes();
            Fnmw.Lang.LangModule.Install();
            Assert.IsInstanceOf<LanguageEngine>(Services.Language);
            Services.ResetToFakes();
        }

        [Test]
        public void SameSeed_GivesSameWorld_AndDifferentSeedsDiffer()
        {
            var fingerprints = new HashSet<string>();
            for (int s = 1; s <= 40; s++)
            {
                var a = new LanguageEngine();
                var b = new LanguageEngine();
                var wa = a.Generate(s);
                var wb = b.Generate(s);
                Assert.AreEqual(wa.Puzzles.Count, wb.Puzzles.Count);
                CollectionAssert.AreEqual(wa.Languages.Select(l => l.Name), wb.Languages.Select(l => l.Name));
                for (int i = 0; i < wa.Puzzles.Count; i++)
                    foreach (var d in Difficulties)
                    {
                        var fa = Fingerprint(a, wa, a.GetPuzzle(wa, i, d));
                        var fb = Fingerprint(b, wb, b.GetPuzzle(wb, i, d));
                        Assert.AreEqual(fa, fb, $"seed {s} puzzle {i} difficulty {d}");
                    }
                fingerprints.Add(Fingerprint(a, wa, wa.Puzzles[0]));
            }
            Assert.Greater(fingerprints.Count, 35, "different seeds should give different first puzzles");
        }

        [Test]
        public void EveryPuzzle_IsWellFormed_AndItsAnswerIsAccepted()
        {
            var eng = new LanguageEngine();
            int n = 0;
            foreach (var (w, p, i, d) in Sweep(eng, 300, Difficulties))
            {
                n++;
                string ctx = $"seed {w.Seed} console {i} difficulty {d}";
                Assert.That(p.Examples.Count, Is.InRange(2, 3), ctx);
                Assert.That(p.Tiles.Count, Is.InRange(2, 6), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(p.TargetMeaning), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(p.AlienName), ctx);
                Assert.That(p.Difficulty, Is.InRange(0f, 1f), ctx);
                Assert.That(KnownConcepts, Does.Contain(p.Concept), ctx);
                foreach (var c in p.Concepts) Assert.That(KnownConcepts, Does.Contain(c), ctx);
                Assert.AreEqual(p.Tiles.Count, p.Tiles.Select(t => t.Id).Distinct().Count(), "tile ids unique " + ctx);
                Assert.AreEqual(p.Tiles.Count, p.Tiles.Select(t => t.Alien).Distinct().Count(), "tile text unique " + ctx);
                foreach (var t in p.Tiles.Concat(p.Examples.SelectMany(e => e.Words)))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(t.Alien), ctx);
                    foreach (var c in t.Concepts) Assert.That(KnownConcepts, Does.Contain(c), ctx);
                }

                var built = LanguageEngine.Inspect(w, p.Id);
                Assert.Less(built.AnswerIds.Length, p.Tiles.Count, "at least one distractor " + ctx);
                Assert.AreEqual(1, built.ValidAnswerCount, ctx);
                var ok = eng.Check(w, p.Id, built.AnswerIds);
                Assert.IsTrue(ok.Correct, ctx);
                Assert.IsNull(ok.MissedConcept, ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(ok.Feedback), ctx);
            }
            Assert.AreEqual(300 * LanguageEngine.DefaultPuzzleCount * Difficulties.Length, n);
        }

        /// <summary>
        /// Independent of the engine's own solver: try every sequence of distinct tiles through the public
        /// Check and count how many are accepted. Exactly one, and every rejected one names a known concept.
        /// </summary>
        [Test]
        public void EveryPuzzle_HasExactlyOneAcceptedTileSequence()
        {
            var eng = new LanguageEngine();
            foreach (var (w, p, i, d) in Sweep(eng, 40, new[] { 0f, 0.5f, 1f }))
            {
                int accepted = 0;
                var used = new bool[p.Tiles.Count];
                var seq = new List<string>();
                void Dfs()
                {
                    if (seq.Count > 0)
                    {
                        var r = eng.Check(w, p.Id, seq.ToArray());
                        if (r.Correct) accepted++;
                        else Assert.That(KnownConcepts, Does.Contain(r.MissedConcept), $"untagged wrong answer {string.Join(" ", seq)}");
                    }
                    for (int k = 0; k < p.Tiles.Count; k++)
                    {
                        if (used[k]) continue;
                        used[k] = true; seq.Add(p.Tiles[k].Id);
                        Dfs();
                        seq.RemoveAt(seq.Count - 1); used[k] = false;
                    }
                }
                Dfs();
                Assert.AreEqual(1, accepted, $"seed {w.Seed} console {i} difficulty {d}: {p.TargetMeaning}");
            }
        }

        [Test]
        public void EveryAnswerMorph_IsVisibleInTheExamples()
        {
            var eng = new LanguageEngine();
            foreach (var (w, p, i, d) in Sweep(eng, 150, Difficulties))
            {
                var b = LanguageEngine.Inspect(w, p.Id);
                var shown = new HashSet<string>(p.Examples.SelectMany(e => e.Words).SelectMany(t => t.Id.Split('+')));
                foreach (var m in b.AnswerKey.Split('+'))
                    Assert.That(shown, Does.Contain(m), $"seed {w.Seed} console {i}: '{m}' never appears in an example");
                foreach (var e in p.Examples)
                    Assert.AreNotEqual(b.AnswerKey, string.Join("+", e.Words.SelectMany(t => t.Id.Split('+'))), "target copies an example");
            }
        }

        [Test]
        public void WrongAnswers_AreTaggedWithAConcept()
        {
            var eng = new LanguageEngine();
            foreach (var (w, p, i, d) in Sweep(eng, 100, new[] { 0.5f }))
            {
                var b = LanguageEngine.Inspect(w, p.Id);
                // swap in each distractor for each answer slot
                foreach (var t in p.Tiles.Where(t => !b.AnswerIds.Contains(t.Id)))
                    for (int slot = 0; slot < b.AnswerIds.Length; slot++)
                    {
                        var guess = (string[])b.AnswerIds.Clone();
                        guess[slot] = t.Id;
                        var r = eng.Check(w, p.Id, guess);
                        Assert.IsFalse(r.Correct);
                        Assert.That(KnownConcepts, Does.Contain(r.MissedConcept));
                        Assert.IsFalse(string.IsNullOrWhiteSpace(r.Feedback));
                    }
                Assert.IsFalse(eng.Check(w, p.Id, new string[0]).Correct);
                Assert.IsFalse(eng.Check(w, p.Id, null).Correct);
                Assert.IsFalse(eng.Check(w, p.Id, new[] { "no-such-tile" }).Correct);
            }
        }

        [Test]
        public void EveryReveal_And_Quiz_IsValid()
        {
            var eng = new LanguageEngine();
            foreach (var (w, p, i, d) in Sweep(eng, 200, new[] { 0f, 1f }))
            {
                string ctx = $"seed {w.Seed} console {i}";
                var r = eng.GetReveal(w, p.Id);
                foreach (var f in new[] { r.AlienText, r.RealText, r.Translation, r.Feature, r.NowYouKnow, r.Explanation, r.LanguageName, r.AlienName })
                    Assert.IsFalse(string.IsNullOrWhiteSpace(f), ctx);
                Assert.AreEqual(w.Languages[i].Name, r.LanguageName, ctx);
                Assert.AreEqual(p.AlienName, r.AlienName, ctx);
                Assert.That(KnownConcepts, Does.Contain(r.Concept), ctx);
                // the alien phrase in the reveal is exactly the answer, written out
                var b = LanguageEngine.Inspect(w, p.Id);
                Assert.AreEqual(b.Lexicon.Render(b.TargetWords), r.AlienText, ctx);

                var q = eng.GetQuiz(w, p.Id);
                Assert.AreEqual(4, q.Options.Length, ctx);
                Assert.AreEqual(4, q.Options.Distinct().Count(), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Prompt), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Explanation), ctx);
                var correct = Enumerable.Range(0, 4).Where(k => eng.CheckQuiz(w, p.Id, k)).ToList();
                Assert.AreEqual(1, correct.Count, ctx);
                Assert.AreEqual(r.LanguageName, q.Options[correct[0]], ctx);
                Assert.IsFalse(eng.CheckQuiz(w, p.Id, -1));
                Assert.IsFalse(eng.CheckQuiz(w, p.Id, 4));
            }
        }

        [Test]
        public void EveryLanguage_AndEveryPuzzleKind_ShowsUp()
        {
            var eng = new LanguageEngine();
            var langs = new HashSet<string>();
            var kinds = new HashSet<string>();
            foreach (var (w, p, i, d) in Sweep(eng, 200, Difficulties))
            {
                var b = LanguageEngine.Inspect(w, p.Id);
                langs.Add(b.Language.Name);
                kinds.Add(b.Language.Name + "/" + b.Draft.Kind);
            }
            var all = LanguageEngine.AllLanguages();
            Assert.GreaterOrEqual(all.Length, 5);
            CollectionAssert.AreEquivalent(all.Select(l => l.Name), langs);
            foreach (var l in all)
                foreach (var k in l.Kinds)
                    Assert.That(kinds, Does.Contain(l.Name + "/" + k.Name));
        }

        [Test]
        public void Consoles_UseDifferentLanguages_AndEscalate()
        {
            var eng = new LanguageEngine();
            var sums = new float[LanguageEngine.DefaultPuzzleCount];
            for (int s = 1; s <= 100; s++)
            {
                var w = eng.Generate(s);
                Assert.AreEqual(LanguageEngine.DefaultPuzzleCount, w.Puzzles.Count);
                Assert.AreEqual(w.Puzzles.Count, w.Languages.Select(l => l.Name).Distinct().Count(), "one language per console");
                Assert.AreEqual(w.Puzzles.Count, w.GrammarSummary.Count);
                for (int i = 0; i < w.Puzzles.Count; i++) sums[i] += w.Puzzles[i].Difficulty;
            }
            for (int i = 1; i < sums.Length; i++) Assert.Greater(sums[i], sums[i - 1], "later consoles are harder");
        }

        [Test]
        public void DirectorDifficulty_ShiftsThePuzzle()
        {
            var eng = new LanguageEngine();
            int harderTiles = 0, comparisons = 0;
            for (int s = 1; s <= 60; s++)
            {
                var w = eng.Generate(s);
                for (int i = 0; i < w.Puzzles.Count; i++)
                {
                    var easy = eng.GetPuzzle(w, i, 0f);
                    float easyDiff = easy.Difficulty;
                    int easyTiles = easy.Tiles.Count - LanguageEngine.Inspect(w, easy.Id).AnswerIds.Length;
                    var hard = eng.GetPuzzle(w, i, 1f);
                    Assert.Greater(hard.Difficulty, easyDiff);
                    int hardTiles = hard.Tiles.Count - LanguageEngine.Inspect(w, hard.Id).AnswerIds.Length;
                    comparisons++;
                    if (hardTiles >= easyTiles) harderTiles++;
                    Assert.AreSame(hard, w.Puzzles[i], "GetPuzzle replaces the world's puzzle");
                    Assert.IsTrue(eng.Check(w, hard.Id, LanguageEngine.Inspect(w, hard.Id).AnswerIds).Correct, "Check follows the latest version");
                }
            }
            Assert.Greater(harderTiles, comparisons * 0.8f, "harder requests rarely give fewer distractors");
        }

        [Test]
        public void WorldReal_FollowsTheConsoleInPlay()
        {
            var eng = new LanguageEngine();
            var w = eng.Generate(12345);
            for (int i = 0; i < w.Puzzles.Count; i++)
            {
                var p = eng.GetPuzzle(w, i, 0.5f);
                Assert.AreEqual(w.Languages[i].Name, w.Real.Name);
                Assert.AreEqual(p.AlienName, w.AlienName);
            }
        }
    }
}

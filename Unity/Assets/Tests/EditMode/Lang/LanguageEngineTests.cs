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
        const int Seeds = 300;
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

        /// <summary>The fast bitmask hint check gives the same verdict as AllDeducible plus the "no hint holds the whole target" rule.</summary>
        [Test]
        public void DeducibleMasks_AgreesWithAllDeducible()
        {
            var rng = new Rng(42);
            int yes = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                ulong t = rng.Next() & 0x3FF, a = rng.Next() & 0x3FF, b = rng.Next() & 0x3FF, c = rng.Next() & 0x3FF;
                if (t == 0) continue;
                List<string> Morphs(ulong m) => Enumerable.Range(0, 10).Where(i => (m >> i & 1) != 0).Select(i => "m" + i).ToList();
                var tm = Morphs(t);
                var ex = new List<List<string>> { Morphs(a), Morphs(b), Morphs(c) };
                bool slow = !ex.Any(e => tm.All(e.Contains)) && PuzzleBuilder.AllDeducible(ex, tm, out _);
                Assert.AreEqual(slow, PuzzleBuilder.DeducibleMasks(t, a, b, c), $"t {t} a {a} b {b} c {c}");
                if (slow) yes++;
            }
            Assert.Greater(yes, 0, "some random triples should qualify");

            // the pruned triple search finds a triple exactly when brute force does (over hints that share a target morph)
            int found = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                ulong t = rng.Next() & 0xFF;
                if (t == 0) continue;
                var masks = Enumerable.Range(0, 12).Select(_ => rng.Next() & 0xFF).ToList();
                bool any = false;
                for (int x = 0; x < masks.Count && !any; x++)
                for (int y = x + 1; y < masks.Count && !any; y++)
                for (int z = y + 1; z < masks.Count && !any; z++)
                    any = (masks[x] & t) != 0 && (masks[y] & t) != 0 && (masks[z] & t) != 0
                          && PuzzleBuilder.DeducibleMasks(t, masks[x], masks[y], masks[z]);
                var pick = PuzzleBuilder.FindHintTriple(t, masks, rng);
                Assert.AreEqual(any, pick != null, $"trial {trial}");
                if (pick != null) { found++; Assert.IsTrue(PuzzleBuilder.DeducibleMasks(t, masks[pick[0]], masks[pick[1]], masks[pick[2]])); }
            }
            Assert.Greater(found, 0);
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
            foreach (var (w, p, i, d) in Sweep(eng, Seeds, Difficulties))
            {
                n++;
                string ctx = $"seed {w.Seed} console {i} difficulty {d}";
                Assert.AreEqual(PuzzleBuilder.ExampleCount, p.Examples.Count, "three hint sentences " + ctx);
                Assert.That(p.Tiles.Count, Is.InRange(PuzzleBuilder.MinAnswerTiles + 1, PuzzleBuilder.MaxTiles), ctx);
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
                Assert.That(built.AnswerIds.Length, Is.InRange(PuzzleBuilder.MinAnswerTiles, PuzzleBuilder.MaxAnswerTiles), "the target is a sentence of several tiles " + ctx);
                // no single hint already holds the whole answer: the player has to combine hints
                var answerMorphs = built.AnswerKey.Split('+').Distinct().ToList();
                foreach (var e in p.Examples)
                {
                    var em = new HashSet<string>(e.Words.SelectMany(t => t.Id.Split('+')));
                    Assert.IsFalse(answerMorphs.All(em.Contains), "one hint contains the whole target " + ctx);
                }
                Assert.AreEqual(built.AcceptedKeys.Count, built.ValidAnswerCount, ctx);
                var ok = eng.Check(w, p.Id, built.AnswerIds);
                Assert.IsTrue(ok.Correct, ctx);
                Assert.IsNull(ok.MissedConcept, ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(ok.Feedback), ctx);
            }
            Assert.AreEqual(Seeds * LanguageEngine.DefaultPuzzleCount * Difficulties.Length, n);
        }

        /// <summary>
        /// Independent of the engine's own solver: put every ordering of the answer tiles, and every answer with one
        /// tile swapped for a distractor or left out, through the public Check. Exactly the real orders are accepted
        /// (one, or two where Japanese allows the object first), and every rejected one names a known concept.
        /// (Trying every sequence of up to ten tiles is too slow now that answers are whole sentences.)
        /// </summary>
        [Test]
        public void EveryPuzzle_AcceptsExactlyTheRealAnswers()
        {
            var eng = new LanguageEngine();
            foreach (var (w, p, i, d) in Sweep(eng, 40, new[] { 0f, 0.5f, 1f }))
            {
                var b = LanguageEngine.Inspect(w, p.Id);
                string ctx = $"seed {w.Seed} console {i} difficulty {d}: {p.TargetMeaning}";
                void Reject(string[] guess)
                {
                    var r = eng.Check(w, p.Id, guess);
                    Assert.IsFalse(r.Correct, $"{ctx}: accepted {string.Join(" ", guess)}");
                    Assert.That(KnownConcepts, Does.Contain(r.MissedConcept), $"untagged wrong answer {string.Join(" ", guess)}");
                }

                int accepted = 0;
                var ans = b.AnswerIds;
                var used = new bool[ans.Length];
                var seq = new List<string>();
                void Perm()
                {
                    if (seq.Count == ans.Length)
                    {
                        var r = eng.Check(w, p.Id, seq.ToArray());
                        if (r.Correct) accepted++;
                        else Assert.That(KnownConcepts, Does.Contain(r.MissedConcept), $"untagged wrong order {string.Join(" ", seq)}");
                        return;
                    }
                    for (int k = 0; k < ans.Length; k++)
                    {
                        if (used[k]) continue;
                        used[k] = true; seq.Add(ans[k]);
                        Perm();
                        seq.RemoveAt(seq.Count - 1); used[k] = false;
                    }
                }
                Perm();
                Assert.AreEqual(b.AcceptedKeys.Count, accepted, ctx);
                Assert.AreEqual(b.Draft.AlsoCorrect.Count + 1, b.AcceptedKeys.Count, "every extra order is distinct");

                var distractors = p.Tiles.Select(t => t.Id).Where(id => !ans.Contains(id)).ToList();
                for (int slot = 0; slot < ans.Length; slot++)
                {
                    Reject(ans.Where((_, k) => k != slot).ToArray());
                    foreach (var x in distractors)
                    {
                        var g = (string[])ans.Clone(); g[slot] = x; Reject(g);
                        Reject(ans.Take(slot).Append(x).Concat(ans.Skip(slot)).ToArray());
                    }
                }
            }
        }

        [Test]
        public void JapaneseParticles_AcceptTheObjectFirst()
        {
            var eng = new LanguageEngine();
            int seen = 0;
            foreach (var (w, p, i, d) in Sweep(eng, 200, new[] { 0.75f, 1f }))
            {
                var b = LanguageEngine.Inspect(w, p.Id);
                if (b.Draft.Kind != "particles") continue;
                seen++;
                // the intended answer is subject-ga object-o verb; object-o subject-ga verb is real Japanese too
                var a = b.AnswerIds;
                Assert.AreEqual(5, a.Length);
                Assert.IsTrue(eng.Check(w, p.Id, new[] { a[2], a[3], a[0], a[1], a[4] }).Correct, p.TargetMeaning);
            }
            Assert.Greater(seen, 0, "no Japanese particle puzzle was generated");
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
                // the phrase in the reveal is exactly the answer, written out, and it is the real phrase
                var b = LanguageEngine.Inspect(w, p.Id);
                Assert.AreEqual(b.Language.Render(b.TargetWords), r.AlienText, ctx);
                StringAssert.Contains(r.AlienText.ToLowerInvariant(), r.RealText.ToLowerInvariant(), ctx);

                var q = eng.GetQuiz(w, p.Id);
                Assert.AreEqual(4, q.Options.Length, ctx);
                Assert.AreEqual(4, q.Options.Distinct().Count(), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Prompt), ctx);
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Explanation), ctx);
                var correct = Enumerable.Range(0, 4).Where(k => eng.CheckQuiz(w, p.Id, k)).ToList();
                Assert.AreEqual(1, correct.Count, ctx);
                Assert.AreEqual(r.LanguageName, q.Options[correct[0]], ctx);
                StringAssert.StartsWith("Which language", q.Prompt, "the UI shows only the prompt's first line " + ctx);
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

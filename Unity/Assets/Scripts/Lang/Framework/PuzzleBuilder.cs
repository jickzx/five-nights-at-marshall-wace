using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang
{
    /// <summary>A generated puzzle plus everything the engine keeps private (solution, rivals, quiz key).</summary>
    public sealed class BuiltPuzzle
    {
        public LangPuzzle Puzzle;
        public Draft Draft;
        public SourceLanguage Language;
        public string[] AnswerIds;             // tile ids of the intended answer, in order
        public string AnswerKey;               // flattened morph sequence of the intended answer
        public readonly HashSet<string> AcceptedKeys = new HashSet<string>(); // the answer plus any other order the real language allows
        public List<Word> TargetWords;
        public readonly Dictionary<string, Tile> TileById = new Dictionary<string, Tile>();
        public readonly Dictionary<string, string[]> MorphsById = new Dictionary<string, string[]>();
        public readonly Dictionary<string, string> RivalKeys = new Dictionary<string, string>();  // key -> concept
        public string[] QuizOptions;
        public int QuizCorrect;
        public int ValidAnswerCount;           // from the brute-force check; always AcceptedKeys.Count for a shipped puzzle
    }

    /// <summary>
    /// Turns a Draft into a LangPuzzle: renders examples and tiles in the real language (romanised), picks
    /// distractors that test the puzzle's concept, then verifies by brute force that the only tile sequences
    /// a player could justify from the examples are the ones the real language accepts.
    ///
    /// Design: the target is always a SENTENCE (at least MinSentenceWords non-punctuation words, 4-8 answer
    /// tiles) and exactly ExampleCount (3) example sentences ("hints") are shown with their English meanings.
    /// Each hint shares pieces with the target, no single hint contains all of it, so the player must combine
    /// at least two hints, and every target morph must be deducible by comparing hints (AllDeducible).
    ///
    /// Solver model: the player is assumed to know what each hint means and to consider the true grammar
    /// plus every rival grammar of the draft (English word order, no agreement, other suffix orders...).
    /// A rival "survives" if it renders every example exactly as shown. A tile sequence is a valid answer if
    /// some surviving grammar renders the target as that sequence. We enumerate sequences of distinct tiles
    /// (at most MaxTiles = 10, pruned to those that are a morph-prefix of some valid key) and require that
    /// the valid ones are exactly the accepted ones (usually one; Japanese also accepts the object moved to
    /// the front), and that every morph of the answer is visible and deducible from the examples.
    /// </summary>
    public static class PuzzleBuilder
    {
        public const int MaxTiles = 10;        // answer + distractors; UI number keys are 1-9,0
        public const int MinAnswerTiles = 4;
        public const int MaxAnswerTiles = 8;
        public const int MinSentenceWords = 3;  // non-punctuation words in the target
        public const int ExampleCount = 3;      // hints shown per puzzle

        public static string Key(IEnumerable<Word> words) => string.Join("+", words.SelectMany(w => w.M));
        static string ShapeKey(IEnumerable<Word> words) => string.Join(" ", words.Select(w => w.Id));

        public static BuiltPuzzle Build(SourceLanguage lang, Draft d, Rng rng, int id,
                                        float difficulty, int distractors, bool dropOptional, out string error)
        {
            error = null;
            var examples = d.Examples.Where(e => !(dropOptional && e.Optional)).ToList();
            if (examples.Count != ExampleCount) { error = $"needs {ExampleCount} examples, has {examples.Count}"; return null; }

            var target = d.Target(0);
            List<Word> Tiles(List<Word> ws) => d.MorphTiles ? Split(ws) : ws;
            var answerTiles = Tiles(target);
            if (answerTiles.Count > MaxAnswerTiles) { error = $"answer too long ({answerTiles.Count} tiles)"; return null; }
            if (answerTiles.Count < MinAnswerTiles) { error = $"answer too short ({answerTiles.Count} tiles)"; return null; }
            int words = target.Count(w => !(w.M.Length == 1 && lang.Spec(w.M[0]).Kind == MorphKind.Punct));
            if (words < MinSentenceWords) { error = $"target is not a sentence ({words} words)"; return null; }

            var b = new BuiltPuzzle { Draft = d, Language = lang, TargetWords = target, AnswerKey = Key(target) };
            b.AcceptedKeys.Add(b.AnswerKey);
            var answerIds = new HashSet<string>(answerTiles.Select(w => w.Id));
            foreach (var alt in d.AlsoCorrect)
            {
                var altTiles = Tiles(alt);
                if (altTiles.Count != answerTiles.Count || !altTiles.All(w => answerIds.Contains(w.Id))) { error = "an accepted order uses other tiles"; return null; }
                b.AcceptedKeys.Add(Key(alt));
            }

            // ---- rivals that survive the examples
            var shown = examples.Select(e => ShapeKey(e.Render(0))).ToList();
            var surviving = new List<int>();
            for (int h = 0; h < d.HypConcepts.Length; h++)
            {
                bool ok = true;
                for (int i = 0; i < examples.Count && ok; i++) ok = ShapeKey(examples[i].Render(h)) == shown[i];
                if (ok) surviving.Add(h);
            }
            var valid = new HashSet<string>(surviving.Select(h => Key(d.Target(h))));
            valid.UnionWith(b.AcceptedKeys);
            for (int h = 1; h < d.HypConcepts.Length; h++)
            {
                var k = Key(d.Target(h));
                if (!b.AcceptedKeys.Contains(k) && !b.RivalKeys.ContainsKey(k)) b.RivalKeys[k] = d.HypConcepts[h];
            }
            foreach (var (phrase, concept) in d.Mistakes)
            {
                var k = Key(phrase);
                if (!b.AcceptedKeys.Contains(k) && !b.RivalKeys.ContainsKey(k)) b.RivalKeys[k] = concept;
            }

            // ---- distractor pool, best first: rival renders of the target, mistakes, extras, example words
            var pool = new List<(Word w, string concept)>();
            void AddGroup(IEnumerable<(Word, string)> group)
            {
                var g = group.Where(x => !answerIds.Contains(x.Item1.Id) && pool.All(p => p.w.Id != x.Item1.Id)).ToList();
                rng.Shuffle(g);
                pool.AddRange(g);
            }
            AddGroup(Enumerable.Range(1, d.HypConcepts.Length - 1).SelectMany(h => Tiles(d.Target(h)).Select(w => (w, d.HypConcepts[h]))));
            AddGroup(d.Mistakes.SelectMany(m => Tiles(m.phrase).Select(w => (w, m.concept))));
            AddGroup(d.Extras.SelectMany(x => Tiles(new List<Word> { x.word }).Select(w => (w, x.concept ?? LangConcepts.Vocabulary))));
            AddGroup(examples.SelectMany(e => Tiles(e.Render(0))).Select(w => (w, ConceptOf(lang, w) ?? LangConcepts.Vocabulary)));

            int want = System.Math.Max(1, System.Math.Min(distractors, MaxTiles - answerTiles.Count));
            var chosen = new List<(Word w, string concept)>();
            var seenText = new HashSet<string>(answerTiles.Select(w => lang.Render(w, true)));
            if (seenText.Count != answerTiles.Select(w => w.Id).Distinct().Count()) { error = "answer tiles share text"; return null; }
            if (answerIds.Count != answerTiles.Count) { error = "answer repeats a tile"; return null; }
            foreach (var p in pool)
            {
                if (chosen.Count >= want) break;
                if (!seenText.Add(lang.Render(p.w, true))) continue;
                chosen.Add(p);
            }
            if (chosen.Count == 0) { error = "no distractor available"; return null; }

            // ---- tiles
            var allTiles = new List<Tile>();
            foreach (var w in answerTiles) allTiles.Add(MakeTile(lang, w, null));
            foreach (var (w, concept) in chosen) allTiles.Add(MakeTile(lang, w, concept));
            foreach (var t in allTiles) b.TileById[t.Id] = t;
            foreach (var w in answerTiles.Concat(chosen.Select(c => c.w))) b.MorphsById[w.Id] = w.M;
            rng.Shuffle(allTiles);
            b.AnswerIds = answerTiles.Select(w => w.Id).ToArray();

            // ---- checks
            var attested = new HashSet<string>(examples.SelectMany(e => e.Render(0)).SelectMany(w => w.M));
            var missing = target.SelectMany(w => w.M).Where(m => !attested.Contains(m)).ToList();
            if (missing.Count > 0) { error = "unattested morphs: " + string.Join(",", missing); return null; }
            if (examples.Any(e => b.AcceptedKeys.Contains(Key(e.Render(0))))) { error = "target equals an example"; return null; }
            var exampleMorphs = examples.Select(e => e.Render(0).SelectMany(w => w.M).ToList()).ToList();
            var targetMorphs = target.SelectMany(w => w.M).Distinct().ToList();
            // the player must combine at least two hints: no single hint may contain the whole target
            if (exampleMorphs.Any(em => targetMorphs.All(em.Contains))) { error = "one example contains the whole target"; return null; }
            if (!AllDeducible(exampleMorphs, targetMorphs, out var unresolved)) { error = "not deducible: " + unresolved; return null; }

            b.ValidAnswerCount = CountValidSequences(allTiles, b.MorphsById, valid, out var found);
            if (b.ValidAnswerCount != b.AcceptedKeys.Count || !found.SetEquals(b.AcceptedKeys))
            {
                error = $"{b.ValidAnswerCount} valid answers, {b.AcceptedKeys.Count} accepted";
                return null;
            }

            var concepts = new List<string> { d.Concept };
            concepts.AddRange(d.Concepts);
            concepts.AddRange(answerTiles.Select(w => ConceptOf(lang, w)).Where(c => c != null));
            b.Puzzle = new LangPuzzle
            {
                Id = id,
                TargetMeaning = d.TargetMeaning,
                Difficulty = difficulty,
                AlienName = lang.Endonym,
                Concept = d.Concept,
                Concepts = concepts.Where(c => c != null).Distinct().ToArray(),
                Tiles = allTiles,
            };
            foreach (var e in examples)
            {
                var ex = new ExamplePhrase { Picture = e.Picture };
                foreach (var w in e.Render(0)) ex.Words.Add(MakeTile(lang, w, null, false));
                if (IsSentence(e.Picture)) CapitaliseFirstLetter(ex.Words);
                b.Puzzle.Examples.Add(ex);
            }
            return b;
        }

        /// <summary>
        /// Can every target morph be worked out from the hints, rather than guessed? Models a player comparing
        /// the three English meanings against the three real sentences (cross-situational learning). Each morph
        /// key stands for both a visible form and its meaning, and meaning m is present in an example exactly
        /// when form m is. For each form f seen in any example:
        ///   candidates(f) = (meanings of every example containing f) minus (meanings of any example without f)
        /// i.e. f must mean something present every time f appears and absent every time it doesn't. Then
        /// eliminate: a form with one candidate is resolved, and that meaning is struck from every other form
        /// (repeat until nothing changes). The true meaning always survives, so a target morph is deducible
        /// iff it ends with exactly itself as candidate. Two morphs that always co-occur stay ambiguous.
        /// </summary>
        public static bool AllDeducible(List<List<string>> exampleMorphs, IEnumerable<string> targetMorphs, out string unresolved)
        {
            var sets = exampleMorphs.Select(e => new HashSet<string>(e)).ToList();
            var cands = new Dictionary<string, HashSet<string>>();
            foreach (var f in sets.SelectMany(x => x).Distinct())
            {
                HashSet<string> c = null;
                foreach (var s in sets.Where(x => x.Contains(f)))
                    if (c == null) c = new HashSet<string>(s); else c.IntersectWith(s);
                foreach (var s in sets.Where(x => !x.Contains(f))) c.ExceptWith(s);
                cands[f] = c;
            }
            var resolved = new HashSet<string>();
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var f in cands.Keys)
                {
                    if (resolved.Contains(f) || cands[f].Count != 1) continue;
                    resolved.Add(f);
                    var m = cands[f].First();
                    foreach (var g in cands.Keys) if (g != f && cands[g].Remove(m)) changed = true;
                    changed = true;
                }
            }
            foreach (var t in targetMorphs)
                if (!cands.TryGetValue(t, out var c) || c.Count != 1 || !c.Contains(t)) { unresolved = t; return false; }
            unresolved = null;
            return true;
        }

        /// <summary>
        /// AllDeducible, plus "no single hint holds the whole target", on morph bitmasks (SourceLanguage.MorphMask):
        /// t = target morphs, a/b/c = the three hints' morphs. Two forms can be told apart exactly when they appear in
        /// different subsets of the hints, so every target morph must be seen, and must be alone in its region of the
        /// hints' Venn diagram. Same verdict as AllDeducible, in a few nanoseconds.
        /// </summary>
        public static bool DeducibleMasks(ulong t, ulong a, ulong b, ulong c)
        {
            if ((t & ~(a | b | c)) != 0 || (t & ~a) == 0 || (t & ~b) == 0 || (t & ~c) == 0) return false;
            return Alone(a & ~b & ~c, t) && Alone(~a & b & ~c, t) && Alone(~a & ~b & c, t) && Alone(a & b & ~c, t)
                && Alone(a & ~b & c, t) && Alone(~a & b & c, t) && Alone(a & b & c, t);
        }

        // a region holding a target morph must hold nothing else
        static bool Alone(ulong region, ulong t) => (region & t) == 0 || !Many(region);
        static bool Many(ulong bits) => (bits & (bits - 1)) != 0;
        // cut in two, can every target morph of this region end up alone? Only if it holds at most two morphs, or one target morph
        static bool Splittable(ulong region, ulong t)
        {
            if (!Many(region & t)) return true;
            return !Many(region & (region - 1));   // two target morphs: the region must be exactly those two
        }

        /// <summary>
        /// Picks three candidate hints (indices into masks) that make every target morph deducible and pass accept,
        /// trying triples in a random order and stopping at the first. Systematic, so it finds a triple whenever one
        /// exists among the candidates (a random search over triples rarely does: almost none of them qualify).
        /// Null when no triple works.
        /// </summary>
        public static int[] FindHintTriple(ulong target, IList<ulong> masks, Rng rng, System.Func<int, int, int, bool> accept = null)
        {
            var order = Enumerable.Range(0, masks.Count).Where(i => (masks[i] & target) != 0 && (target & ~masks[i]) != 0).ToList();
            rng.Shuffle(order);
            int n = order.Count;
            for (int x = 0; x < n; x++)
            for (int y = x + 1; y < n; y++)
            {
                ulong a = masks[order[x]], b = masks[order[y]], need = target & ~(a | b);
                // the third hint only splits each region of the first two in two, and its own new morphs form one
                // region: prune pairs no third hint can rescue (the search stays fast when no triple exists)
                if (Many(need) || !Splittable(a & ~b, target) || !Splittable(~a & b, target) || !Splittable(a & b, target)) continue;
                for (int z = y + 1; z < n; z++)
                {
                    ulong c = masks[order[z]];
                    if ((need & ~c) != 0 || !DeducibleMasks(target, a, b, c)) continue;
                    if (accept != null && !accept(order[x], order[y], order[z])) continue;
                    return new[] { order[x], order[y], order[z] };
                }
            }
            return null;
        }

        /// <summary>Every sequence of distinct tiles (length 1..n) whose morphs form a valid answer.
        /// Only branches whose morph sequence is a prefix of some valid key are explored (same result, far fewer sequences).</summary>
        public static int CountValidSequences(List<Tile> tiles, Dictionary<string, string[]> morphs, HashSet<string> valid, out HashSet<string> found)
        {
            // every morph-boundary prefix of every valid key ("a", "a+b", "a+b+c" for "a+b+c")
            var prefixes = new HashSet<string>();
            foreach (var v in valid)
            {
                var parts = v.Split('+');
                for (int n = 1; n <= parts.Length; n++) prefixes.Add(string.Join("+", parts, 0, n));
            }
            int count = 0;
            var hits = new HashSet<string>();
            var used = new bool[tiles.Count];
            var seq = new List<string>();
            void Dfs()
            {
                if (seq.Count > 0)
                {
                    var k = string.Join("+", seq);
                    if (!prefixes.Contains(k)) return;   // no valid key starts this way: nothing below can match
                    if (valid.Contains(k)) { count++; hits.Add(k); }
                }
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (used[i]) continue;
                    used[i] = true;
                    int before = seq.Count;
                    seq.AddRange(morphs[tiles[i].Id]);
                    Dfs();
                    seq.RemoveRange(before, seq.Count - before);
                    used[i] = false;
                }
            }
            Dfs();
            found = hits;
            return count;
        }

        public static List<Word> Split(List<Word> words) => words.SelectMany(w => w.M.Select(m => new Word(m))).ToList();

        static string ConceptOf(SourceLanguage lang, Word w) => w.M.Select(m => lang.Spec(m).Concept).FirstOrDefault(c => c != null);

        // a full-sentence caption ("The ship is red.", "Do you eat?") starts with a capital, as the real language writes it
        static bool IsSentence(string picture) => picture.EndsWith(".") || picture.EndsWith("!") || picture.EndsWith("?");

        static void CapitaliseFirstLetter(List<Tile> words)
        {
            foreach (var t in words)
            {
                int i = 0;
                while (i < t.Alien.Length && !char.IsLetter(t.Alien[i])) i++;
                if (i == t.Alien.Length) continue;   // a lone mark like ¿: capitalise the next word
                t.Alien = t.Alien.Substring(0, i) + char.ToUpperInvariant(t.Alien[i]) + t.Alien.Substring(i + 1);
                return;
            }
        }

        static Tile MakeTile(SourceLanguage lang, Word w, string distractorConcept, bool asTile = true)
        {
            var concepts = w.M.Select(m => lang.Spec(m).Concept).Where(c => c != null).ToList();
            if (distractorConcept != null) concepts.Insert(0, distractorConcept);
            return new Tile
            {
                Id = w.Id,
                Alien = lang.Render(w, asTile),
                Gloss = string.Join("-", w.M.Select(m => lang.Spec(m).Gloss)),
                GlyphSeed = lang.GlyphSeed(w),
                Concepts = concepts.Distinct().ToArray(),
            };
        }
    }
}

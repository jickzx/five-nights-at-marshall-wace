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
    /// Solver model: the player is assumed to know what each picture means and to consider the true grammar
    /// plus every rival grammar of the draft (English word order, no agreement, other suffix orders...).
    /// A rival "survives" if it renders every example exactly as shown. A tile sequence is a valid answer if
    /// some surviving grammar renders the target as that sequence. We enumerate every sequence of distinct
    /// tiles (at most 6 tiles, so at most 1956 sequences) and require that the valid ones are exactly the
    /// accepted ones (usually one; Japanese also accepts the object moved to the front), and that every morph
    /// of the answer is visible somewhere in the examples.
    /// </summary>
    public static class PuzzleBuilder
    {
        public const int MaxTiles = 6;

        public static string Key(IEnumerable<Word> words) => string.Join("+", words.SelectMany(w => w.M));
        static string ShapeKey(IEnumerable<Word> words) => string.Join(" ", words.Select(w => w.Id));

        public static BuiltPuzzle Build(SourceLanguage lang, Draft d, Rng rng, int id,
                                        float difficulty, int distractors, bool dropOptional, out string error)
        {
            error = null;
            var examples = d.Examples.Where(e => !(dropOptional && e.Optional)).ToList();
            if (examples.Count < 2 || examples.Count > 3) { error = $"needs 2-3 examples, has {examples.Count}"; return null; }

            var target = d.Target(0);
            List<Word> Tiles(List<Word> ws) => d.MorphTiles ? Split(ws) : ws;
            var answerTiles = Tiles(target);
            if (answerTiles.Count >= MaxTiles) { error = "answer too long"; return null; }

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

        /// <summary>Every sequence of distinct tiles (length 1..n) whose morphs form a valid answer.</summary>
        public static int CountValidSequences(List<Tile> tiles, Dictionary<string, string[]> morphs, HashSet<string> valid, out HashSet<string> found)
        {
            int count = 0;
            var hits = new HashSet<string>();
            var used = new bool[tiles.Count];
            var seq = new List<string>();
            void Dfs()
            {
                if (seq.Count > 0)
                {
                    var k = string.Join("+", seq);
                    if (valid.Contains(k)) { count++; hits.Add(k); }
                }
                if (seq.Count == tiles.Count) return;
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

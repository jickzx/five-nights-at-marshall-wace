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
        public AlienLexicon Lexicon;
        public string[] AnswerIds;             // tile ids of the one valid answer, in order
        public string AnswerKey;               // flattened morph sequence of the answer
        public List<Word> TargetWords;
        public readonly Dictionary<string, Tile> TileById = new Dictionary<string, Tile>();
        public readonly Dictionary<string, string[]> MorphsById = new Dictionary<string, string[]>();
        public readonly Dictionary<string, string> RivalKeys = new Dictionary<string, string>();  // key -> concept
        public string[] QuizOptions;
        public int QuizCorrect;
        public int ValidAnswerCount;           // from the brute-force check; always 1 for a shipped puzzle
    }

    /// <summary>
    /// Turns a Draft into a LangPuzzle: renders examples and tiles in alien text, picks distractors that
    /// test the puzzle's concept, then verifies by brute force that exactly one tile sequence is a valid
    /// answer given the examples.
    ///
    /// Solver model: the player is assumed to know what each picture means and to consider the true grammar
    /// plus every rival grammar of the draft (English word order, no agreement, other suffix orders...).
    /// A rival "survives" if it renders every example exactly as shown. A tile sequence is a valid answer if
    /// some surviving grammar renders the target as that sequence. We enumerate every sequence of distinct
    /// tiles (at most 6 tiles, so at most 1956 sequences) and require exactly one valid answer, and that every
    /// morph of the answer is visible somewhere in the examples.
    /// </summary>
    public static class PuzzleBuilder
    {
        public const int MaxTiles = 6;

        public static string Key(IEnumerable<Word> words) => string.Join("+", words.SelectMany(w => w.M));
        static string ShapeKey(IEnumerable<Word> words) => string.Join(" ", words.Select(w => w.Id));

        public static BuiltPuzzle Build(SourceLanguage lang, AlienLexicon lex, Draft d, Rng rng, int id,
                                        float difficulty, int distractors, bool dropOptional, out string error)
        {
            error = null;
            var examples = d.Examples.Where(e => !(dropOptional && e.Optional)).ToList();
            if (examples.Count < 2 || examples.Count > 3) { error = $"needs 2-3 examples, has {examples.Count}"; return null; }

            var target = d.Target(0);
            var answerTiles = d.MorphTiles ? Split(target) : target;
            if (answerTiles.Count >= MaxTiles) { error = "answer too long"; return null; }

            var b = new BuiltPuzzle { Draft = d, Language = lang, Lexicon = lex, TargetWords = target, AnswerKey = Key(target) };

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
            for (int h = 1; h < d.HypConcepts.Length; h++)
            {
                var k = Key(d.Target(h));
                if (k != b.AnswerKey && !b.RivalKeys.ContainsKey(k)) b.RivalKeys[k] = d.HypConcepts[h];
            }
            foreach (var (phrase, concept) in d.Mistakes)
            {
                var k = Key(phrase);
                if (k != b.AnswerKey && !b.RivalKeys.ContainsKey(k)) b.RivalKeys[k] = concept;
            }

            // ---- distractor pool, best first: rival renders of the target, mistakes, extras, example words
            var answerIds = new HashSet<string>(answerTiles.Select(w => w.Id));
            var pool = new List<(Word w, string concept)>();
            void AddGroup(IEnumerable<(Word, string)> group)
            {
                var g = group.Where(x => !answerIds.Contains(x.Item1.Id) && pool.All(p => p.w.Id != x.Item1.Id)).ToList();
                rng.Shuffle(g);
                pool.AddRange(g);
            }
            List<Word> Tiles(List<Word> ws) => d.MorphTiles ? Split(ws) : ws;
            AddGroup(Enumerable.Range(1, d.HypConcepts.Length - 1).SelectMany(h => Tiles(d.Target(h)).Select(w => (w, d.HypConcepts[h]))));
            AddGroup(d.Mistakes.SelectMany(m => Tiles(m.phrase).Select(w => (w, m.concept))));
            AddGroup(d.Extras.SelectMany(x => Tiles(new List<Word> { x.word }).Select(w => (w, x.concept ?? LangConcepts.Vocabulary))));
            AddGroup(examples.SelectMany(e => Tiles(e.Render(0))).Select(w => (w, ConceptOf(lang, w) ?? LangConcepts.Vocabulary)));

            int want = System.Math.Max(1, System.Math.Min(distractors, MaxTiles - answerTiles.Count));
            var chosen = new List<(Word w, string concept)>();
            var seenAlien = new HashSet<string>(answerTiles.Select(w => lex.Render(w, true)));
            if (seenAlien.Count != answerTiles.Select(w => w.Id).Distinct().Count()) { error = "answer tiles share alien text"; return null; }
            if (answerTiles.Select(w => w.Id).Distinct().Count() != answerTiles.Count) { error = "answer repeats a tile"; return null; }
            foreach (var p in pool)
            {
                if (chosen.Count >= want) break;
                if (!seenAlien.Add(lex.Render(p.w, true))) continue;
                chosen.Add(p);
            }
            if (chosen.Count == 0) { error = "no distractor available"; return null; }

            // ---- tiles
            var allTiles = new List<Tile>();
            foreach (var w in answerTiles) allTiles.Add(MakeTile(lang, lex, w, null));
            foreach (var (w, concept) in chosen) allTiles.Add(MakeTile(lang, lex, w, concept));
            foreach (var t in allTiles) b.TileById[t.Id] = t;
            foreach (var w in answerTiles.Concat(chosen.Select(c => c.w))) b.MorphsById[w.Id] = w.M;
            rng.Shuffle(allTiles);
            b.AnswerIds = answerTiles.Select(w => w.Id).ToArray();

            // ---- checks
            var attested = new HashSet<string>(examples.SelectMany(e => e.Render(0)).SelectMany(w => w.M));
            var missing = target.SelectMany(w => w.M).Where(m => !attested.Contains(m)).ToList();
            if (missing.Count > 0) { error = "unattested morphs: " + string.Join(",", missing); return null; }
            if (examples.Any(e => Key(e.Render(0)) == b.AnswerKey)) { error = "target equals an example"; return null; }

            b.ValidAnswerCount = CountValidSequences(allTiles, b.MorphsById, valid, out var found);
            if (b.ValidAnswerCount != 1) { error = $"{b.ValidAnswerCount} valid answers"; return null; }
            if (found != b.AnswerKey) { error = "the only valid answer is not the intended one"; return null; }

            var concepts = new List<string> { d.Concept };
            concepts.AddRange(d.Concepts);
            concepts.AddRange(answerTiles.Select(w => ConceptOf(lang, w)).Where(c => c != null));
            b.Puzzle = new LangPuzzle
            {
                Id = id,
                TargetMeaning = d.TargetMeaning,
                Difficulty = difficulty,
                AlienName = lex.AlienName,
                Concept = d.Concept,
                Concepts = concepts.Where(c => c != null).Distinct().ToArray(),
                Tiles = allTiles,
            };
            foreach (var e in examples)
            {
                var ex = new ExamplePhrase { Picture = e.Picture };
                foreach (var w in e.Render(0)) ex.Words.Add(MakeTile(lang, lex, w, null, false));
                b.Puzzle.Examples.Add(ex);
            }
            return b;
        }

        /// <summary>Every sequence of distinct tiles (length 1..n) whose morphs form a valid answer.</summary>
        public static int CountValidSequences(List<Tile> tiles, Dictionary<string, string[]> morphs, HashSet<string> valid, out string found)
        {
            int count = 0;
            string first = null;
            var used = new bool[tiles.Count];
            var seq = new List<string>();
            void Dfs()
            {
                if (seq.Count > 0)
                {
                    var k = string.Join("+", seq);
                    if (valid.Contains(k)) { count++; first ??= k; }
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
            found = first;
            return count;
        }

        public static List<Word> Split(List<Word> words) => words.SelectMany(w => w.M.Select(m => new Word(m))).ToList();

        static string ConceptOf(SourceLanguage lang, Word w) => w.M.Select(m => lang.Spec(m).Concept).FirstOrDefault(c => c != null);

        static Tile MakeTile(SourceLanguage lang, AlienLexicon lex, Word w, string distractorConcept, bool asTile = true)
        {
            var concepts = w.M.Select(m => lang.Spec(m).Concept).Where(c => c != null).ToList();
            if (distractorConcept != null) concepts.Insert(0, distractorConcept);
            return new Tile
            {
                Id = w.Id,
                Alien = lex.Render(w, asTile),
                Gloss = string.Join("-", w.M.Select(m => lang.Spec(m).Gloss)),
                GlyphSeed = lex.GlyphSeed(w),
                Concepts = concepts.Distinct().ToArray(),
            };
        }
    }
}

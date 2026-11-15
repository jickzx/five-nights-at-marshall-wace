using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using Fnmw.Lang.Languages;

namespace Fnmw.Lang
{
    /// <summary>
    /// The real ILanguageEngine. A world is a run of consoles, each in a different real language (romanised),
    /// with real words and real grammar. Puzzles escalate along the run, and the director's difficulty
    /// shifts each console easier or harder. Pure C#: no MonoBehaviours, fully deterministic per seed.
    /// </summary>
    public sealed class LanguageEngine : ILanguageEngine
    {
        public const int DefaultPuzzleCount = 5;
        public const float NeutralDifficulty = 0.5f;
        const int MaxAttempts = 120;

        readonly int _puzzleCount;

        public LanguageEngine(int puzzleCount = DefaultPuzzleCount) { _puzzleCount = Math.Max(1, puzzleCount); }

        /// <summary>Every source language, in a fixed order. A world uses a seeded subset.</summary>
        public static SourceLanguage[] AllLanguages() => new SourceLanguage[]
        {
            new Japanese(), new French(), new Spanish(), new German(), new Turkish(), new Mandarin(), new Arabic(),
        };

        internal sealed class WorldData
        {
            public int Seed;
            public SourceLanguage[] Langs;
            public readonly Dictionary<int, BuiltPuzzle> ById = new Dictionary<int, BuiltPuzzle>();
            public readonly Dictionary<string, BuiltPuzzle> Cache = new Dictionary<string, BuiltPuzzle>();
        }

        // ------------------------------------------------------------------ ILanguageEngine

        public LanguageWorld Generate(int seed)
        {
            var rng = new Rng(Hash.Mix(seed, 0x1A46));
            var all = AllLanguages().ToList();
            rng.Shuffle(all);
            // more puzzles than languages: cycle through them again
            var langs = Enumerable.Range(0, _puzzleCount).Select(i => all[i % all.Count]).ToArray();
            var data = new WorldData { Seed = seed, Langs = langs };

            var world = new LanguageWorld { Seed = seed, EngineData = data };
            for (int i = 0; i < langs.Length; i++)
            {
                world.Languages.Add(new RealLanguage { Name = langs[i].Name, Code = langs[i].Code });
                world.GrammarSummary.Add($"Console {i + 1}: {langs[i].Name} ({langs[i].Endonym}). {langs[i].Summary}");
                world.Puzzles.Add(BuildAt(data, i, NeutralDifficulty).Puzzle);
            }
            SetCurrent(world, data, 0);
            return world;
        }

        public LangPuzzle GetPuzzle(LanguageWorld world, int index, float difficulty)
        {
            var data = Data(world);
            index = Math.Max(0, Math.Min(index, world.Puzzles.Count - 1));
            var built = BuildAt(data, index, difficulty);
            world.Puzzles[index] = built.Puzzle;
            SetCurrent(world, data, index);
            return built.Puzzle;
        }

        public AnswerResult Check(LanguageWorld world, int puzzleId, string[] tileIds)
        {
            if (!Data(world).ById.TryGetValue(puzzleId, out var b))
                return new AnswerResult { Correct = false, MissedConcept = LangConcepts.Vocabulary, Feedback = "The console does not recognise this panel." };
            tileIds ??= Array.Empty<string>();

            if (tileIds.Any(id => id == null || !b.MorphsById.ContainsKey(id)))
                return Wrong(b, LangConcepts.Vocabulary);
            var morphs = tileIds.SelectMany(id => b.MorphsById[id]).ToList();
            var key = string.Join("+", morphs);
            if (b.AcceptedKeys.Contains(key))
                return new AnswerResult { Correct = true, MissedConcept = null, Feedback = Feedback.Correct(b.Language.Endonym) };
            return Wrong(b, Diagnose(b, tileIds, morphs, key));
        }

        public RevealInfo GetReveal(LanguageWorld world, int puzzleId)
        {
            var b = Built(world, puzzleId);
            var d = b.Draft;
            return new RevealInfo
            {
                AlienText = b.Language.Render(b.TargetWords),
                RealText = d.RealText,
                Translation = d.Translation,
                LanguageName = b.Language.Name,
                AlienName = b.Language.Endonym,
                Feature = d.Feature,
                NowYouKnow = d.NowYouKnow,
                Concept = d.Concept,
                Explanation = $"{d.Feature} Now you know: {d.NowYouKnow}",
                SpeechText = Speech.Clean(d.RealText),
                SpeechLang = b.Language.Code,
            };
        }

        // ------------------------------------------------------------------ hints

        public HintContext GetHintContext(LanguageWorld world, int puzzleId, string missedConcept)
        {
            if (world?.EngineData is not WorldData data || !data.ById.TryGetValue(puzzleId, out var b)) return null;
            string concept = HintFacts.For(missedConcept) != null ? missedConcept : b.Draft.Concept;
            var e = HintFacts.For(concept);
            if (e == null) { concept = LangConcepts.WordOrder; e = HintFacts.For(concept); }
            return new HintContext { Concept = concept, Focus = e.Focus, Facts = (string[])e.Facts.Clone(), OfflineHints = HintFacts.OfflineHints(concept) };
        }

        public bool IsHintSafe(LanguageWorld world, int puzzleId, string text)
        {
            if (world?.EngineData is not WorldData data || !data.ById.TryGetValue(puzzleId, out var b)) return false;
            return HintSafety.Check(b, text, out _);
        }

        public QuizQuestion GetQuiz(LanguageWorld world, int puzzleId)
        {
            var b = Built(world, puzzleId);
            return new QuizQuestion
            {
                // the UI shows only the first line, so the question goes first
                Prompt = $"Which language is this?\n{b.Draft.RealText} = “{b.Draft.Translation.TrimEnd('.', '!', '?')}”",
                Options = (string[])b.QuizOptions.Clone(),
                Explanation = b.Language.QuizExplanation,
            };
        }

        public bool CheckQuiz(LanguageWorld world, int puzzleId, int optionIndex) => Built(world, puzzleId).QuizCorrect == optionIndex;

        // ------------------------------------------------------------------ difficulty

        /// <summary>Difficulty actually used for console i: a ramp along the run, shifted by the director (0.5 = neutral).</summary>
        public float Effective(int index, int count, float requested)
        {
            float ramp = count <= 1 ? 0f : index / (float)(count - 1);
            float r = float.IsNaN(requested) ? NeutralDifficulty : Math.Max(0f, Math.Min(1f, requested));
            return Math.Max(0f, Math.Min(1f, 0.1f + 0.8f * ramp + (r - NeutralDifficulty) * 0.6f));
        }

        public static int TierFor(float eff) => Math.Min(3, (int)(eff * 4f));
        public static int DistractorsFor(float eff) => 1 + (int)Math.Round(eff * 2f);
        // languages now supply exactly 3 non-optional examples, so dropping optional ones is a no-op
        public static bool DropOptionalFor(float eff) => eff >= 0.65f;

        // ------------------------------------------------------------------ internals

        BuiltPuzzle BuildAt(WorldData data, int index, float requested)
        {
            float eff = Effective(index, data.Langs.Length, requested);
            int tier = TierFor(eff), distractors = DistractorsFor(eff);
            bool drop = DropOptionalFor(eff);
            string cacheKey = $"{index}:{tier}:{distractors}:{drop}";
            if (data.Cache.TryGetValue(cacheKey, out var cached))
            {
                data.ById[cached.Puzzle.Id] = cached;
                return cached;
            }

            var lang = data.Langs[index];
            var rng = new Rng(Hash.Mix(data.Seed, index, tier, distractors, drop ? 1 : 0));
            BuiltPuzzle built = null;
            var errors = new List<string>();
            for (int attempt = 0; attempt < MaxAttempts && built == null; attempt++)
            {
                // after a few failures, step down a tier, then relax the extras
                int t = attempt < MaxAttempts / 2 ? tier : Math.Max(0, tier - 1 - attempt % 2);
                bool dropNow = drop && attempt < MaxAttempts / 3;
                var kind = lang.KindFor(t, rng);
                var draft = kind.Build(rng, t);
                draft.Kind = kind.Name;
                built = PuzzleBuilder.Build(lang, draft, rng, index + 1, eff, distractors, dropNow, out var err);
                if (built == null) errors.Add($"{kind.Name}: {err}");
            }
            if (built == null)
                throw new InvalidOperationException($"{lang.Name}: no valid puzzle for console {index + 1}: {string.Join("; ", errors.Distinct().Take(5))}");

            MakeQuiz(built, rng);
            data.Cache[cacheKey] = built;
            data.ById[built.Puzzle.Id] = built;
            return built;
        }

        static void MakeQuiz(BuiltPuzzle b, Rng rng)
        {
            var options = rng.Sample(b.Language.QuizDistractors, 3);
            options.Add(b.Language.Name);
            rng.Shuffle(options);
            b.QuizOptions = options.ToArray();
            b.QuizCorrect = options.IndexOf(b.Language.Name);
        }

        static void SetCurrent(LanguageWorld world, WorldData data, int index)
        {
            // world.Real/AlienName follow the console in play, so code that reads them (e.g. QuizEvent.RealLanguage) stays right
            world.Real = world.Languages[index];
            world.AlienName = data.Langs[index].Endonym;
        }

        static WorldData Data(LanguageWorld world) =>
            world?.EngineData as WorldData ?? throw new ArgumentException("LanguageWorld was not generated by Fnmw.Lang.LanguageEngine");

        static BuiltPuzzle Built(LanguageWorld world, int puzzleId) =>
            Data(world).ById.TryGetValue(puzzleId, out var b) ? b : throw new ArgumentException($"no puzzle with id {puzzleId}");

        /// <summary>Engine-private view of a puzzle, for tests and debug overlays.</summary>
        internal static BuiltPuzzle Inspect(LanguageWorld world, int puzzleId) => Built(world, puzzleId);

        /// <summary>Debug/cheat only (judges' overlay, scripted recordings): the tile ids of the intended answer.</summary>
        public static string[] DebugSolution(LanguageWorld world, int puzzleId) => (string[])Built(world, puzzleId).AnswerIds.Clone();

        /// <summary>Debug/cheat only: index of the correct quiz option.</summary>
        public static int DebugQuizAnswer(LanguageWorld world, int puzzleId) => Built(world, puzzleId).QuizCorrect;

        static AnswerResult Wrong(BuiltPuzzle b, string concept) =>
            new AnswerResult { Correct = false, MissedConcept = concept, Feedback = Feedback.Wrong(concept) };

        /// <summary>Which concept does this wrong answer show the player missing?</summary>
        static string Diagnose(BuiltPuzzle b, string[] ids, List<string> morphs, string key)
        {
            // 1. exactly what a rival grammar, or a known mistake, would produce
            if (b.RivalKeys.TryGetValue(key, out var rival)) return rival;

            var answer = b.AnswerKey.Split('+');
            // 2. right pieces, wrong order
            if (morphs.Count == answer.Length && morphs.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(answer.OrderBy(x => x, StringComparer.Ordinal)))
                return b.Draft.OrderConcept ?? b.Draft.Concept;
            // 3. a wrong tile: the concept that tile was put there to test
            var answerIds = new HashSet<string>(b.AnswerIds);
            var extra = ids.FirstOrDefault(id => !answerIds.Contains(id));
            if (extra != null && b.TileById.TryGetValue(extra, out var tile) && tile.Concepts.Length > 0) return tile.Concepts[0];
            // 4. a missing tile: the concept it carries
            var missing = b.AnswerIds.FirstOrDefault(id => !ids.Contains(id));
            if (missing != null && b.TileById.TryGetValue(missing, out var mt) && mt.Concepts.Length > 0) return mt.Concepts[0];
            return b.Draft.Concept;
        }
    }
}

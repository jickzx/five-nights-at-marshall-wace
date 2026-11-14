using System.Collections.Generic;
using System.Linq;

namespace Fnmw.Core.Fakes
{
    /// <summary>Two hard-coded puzzles in an over-the-top "Japanese". Replaced by Scripts/Lang.</summary>
    public class FakeLanguageEngine : ILanguageEngine
    {
        static Tile T(string id, string alien, string gloss, params string[] concepts) =>
            new Tile { Id = id, Alien = alien, Gloss = gloss, GlyphSeed = id.GetHashCode(), Concepts = concepts };

        static readonly Dictionary<int, string[]> Solutions = new Dictionary<int, string[]>
        {
            { 1, new[] { "door", "open" } },     // SOV: object before verb
            { 2, new[] { "ship", "start" } },
        };

        public LanguageWorld Generate(int seed)
        {
            var w = new LanguageWorld
            {
                Seed = seed,
                AlienName = "Kraa-Nihhon",
                Real = new RealLanguage { Name = "Japanese", Code = "ja" },
                GrammarSummary = { "Verb always comes last (SOV)", "Every word ends in a long vowel" },
            };
            w.Puzzles.Add(new LangPuzzle
            {
                Id = 1, TargetMeaning = "open door",
                Examples =
                {
                    new ExamplePhrase { Picture = "open eye", Words = { T("eye", "mee-ee", "eye"), T("open", "akeee", "open", "verb-final") } },
                    new ExamplePhrase { Picture = "close door", Words = { T("door", "tobiraaa", "door"), T("close", "shimeee", "close", "verb-final") } },
                },
                Tiles = { T("open", "akeee", "open", "verb-final"), T("door", "tobiraaa", "door"), T("eye", "mee-ee", "eye"), T("close", "shimeee", "close", "verb-final") },
            });
            w.Puzzles.Add(new LangPuzzle
            {
                Id = 2, TargetMeaning = "start ship",
                Examples =
                {
                    new ExamplePhrase { Picture = "start fire", Words = { T("fire", "hiii", "fire"), T("start", "hajimeee", "start", "verb-final") } },
                    new ExamplePhrase { Picture = "stop ship", Words = { T("ship", "funeee", "ship"), T("stop", "tomeee", "stop", "verb-final") } },
                },
                Tiles = { T("start", "hajimeee", "start", "verb-final"), T("fire", "hiii", "fire"), T("ship", "funeee", "ship"), T("stop", "tomeee", "stop", "verb-final") },
            });
            return w;
        }

        public LangPuzzle GetPuzzle(LanguageWorld world, int index, float difficulty)
        {
            var p = world.Puzzles[index];
            p.Difficulty = difficulty;
            return p;
        }

        public AnswerResult Check(LanguageWorld world, int puzzleId, string[] tileIds)
        {
            var sol = Solutions[puzzleId];
            bool ok = tileIds != null && tileIds.SequenceEqual(sol);
            return new AnswerResult
            {
                Correct = ok,
                MissedConcept = ok ? null : "verb-final",
                Feedback = ok ? "The console hums." : "The glyphs flicker red. The verb goes last.",
            };
        }

        public RevealInfo GetReveal(LanguageWorld world, int puzzleId) => puzzleId == 1
            ? new RevealInfo { AlienText = "tobiraaa akeee", RealText = "扉を開けて (tobira o akete)", Translation = "open the door", Explanation = "Japanese puts the verb last; Kraa-Nihhon stretches every vowel." }
            : new RevealInfo { AlienText = "funeee hajimeee", RealText = "船を始動 (fune o shidou)", Translation = "start the ship", Explanation = "Object, then verb, exactly like Japanese." };

        public QuizQuestion GetQuiz(LanguageWorld world, int puzzleId) => new QuizQuestion
        {
            Prompt = "Which language was this based on?",
            Options = new[] { "Japanese", "French", "Spanish", "Finnish" },
        };

        public bool CheckQuiz(LanguageWorld world, int puzzleId, int optionIndex) => optionIndex == 0;
    }
}

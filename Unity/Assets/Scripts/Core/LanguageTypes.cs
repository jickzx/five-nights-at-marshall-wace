// CORE CONTRACT. Changes here must be called out in your PR description.
using System;
using System.Collections.Generic;

namespace Fnmw.Core
{
    /// <summary>The real language a console speaks. (Fields named Alien* below predate real languages; they now hold real romanised text.)</summary>
    [Serializable]
    public class RealLanguage
    {
        public string Name;   // "Japanese"
        public string Code;   // "ja"
    }

    /// <summary>One word/morpheme tile the player can place.</summary>
    [Serializable]
    public class Tile
    {
        public string Id;        // stable id, e.g. "door+PL"
        public string Alien;     // the real word, romanised, shown to the player: "kapı", "-lar", "zhī"
        public string Gloss;     // English meaning (debug / reveal only)
        public int GlyphSeed;    // stable per word; for decoration only, the text is the word
        public string[] Concepts = Array.Empty<string>(); // grammar concepts this tile exercises, e.g. "plural-suffix"
    }

    /// <summary>An example phrase shown at the console (mural/screen): picture + caption in the real language.</summary>
    [Serializable]
    public class ExamplePhrase
    {
        public string Picture;   // meaning key for the picture, e.g. "open eye"
        public List<Tile> Words = new List<Tile>();
    }

    [Serializable]
    public class LangPuzzle
    {
        public int Id;
        public string TargetMeaning;               // what the player must say, e.g. "open door"
        public List<ExamplePhrase> Examples = new List<ExamplePhrase>();
        public List<Tile> Tiles = new List<Tile>(); // shuffled, includes distractors
        public float Difficulty;                    // 0..1 it was generated at
        public string AlienName;                    // the language's own name, romanised, e.g. "Nihongo"
        public string Concept;                      // main grammar concept tested (see LangConcepts)
        public string[] Concepts = Array.Empty<string>(); // every concept the puzzle exercises
        // Solution stays private to the engine: use ILanguageEngine.Check.
    }

    [Serializable]
    public class AnswerResult
    {
        public bool Correct;
        public string MissedConcept; // null when correct
        public string Feedback;      // short line for the UI
    }

    /// <summary>Shown after a puzzle: the answer as the player built it, next to the phrase in its own script.</summary>
    [Serializable]
    public class RevealInfo
    {
        public string AlienText;     // the answer, romanised: "tobira o akete"
        public string RealText;      // own script plus romanisation where Unity can show it: "扉を開けて (tobira o akete)"
        public string Translation;   // e.g. "open the door"
        public string Explanation;   // the feature and the fact, in plain English
        public string LanguageName;  // e.g. "Japanese"
        public string AlienName;     // the language's own name, romanised, e.g. "Nihongo"
        public string Feature;       // the real feature in one or two plain sentences
        public string NowYouKnow;    // one true, memorable fact about the real language
        public string Concept;       // main concept the puzzle tested (see LangConcepts)
    }

    /// <summary>"Which rule makes that right?": the rule the puzzle tested against three false rules about the same language.</summary>
    [Serializable]
    public class QuizQuestion
    {
        public string Prompt;
        public string[] Options = Array.Empty<string>();
        public string Explanation;   // one line to show after the player answers
        // Correct index stays private to the engine: use ILanguageEngine.CheckQuiz.
    }

    [Serializable]
    public class LanguageWorld
    {
        public int Seed;
        public string AlienName;            // own name of the language at the console in play, e.g. "Nihongo"
        public RealLanguage Real;
        public List<string> GrammarSummary = new List<string>(); // judges' overlay
        public List<LangPuzzle> Puzzles = new List<LangPuzzle>();
        public List<RealLanguage> Languages = new List<RealLanguage>(); // real language per puzzle, same order (judges/debug)
        [NonSerialized] public object EngineData;           // opaque engine-private payload
    }
}

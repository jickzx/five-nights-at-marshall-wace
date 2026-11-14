// CORE CONTRACT. Changes here must be called out in your PR description.
using System;
using System.Collections.Generic;

namespace Fnmw.Core
{
    /// <summary>The real language an alien language exaggerates.</summary>
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
        public string Alien;     // alien script/romanisation shown to player
        public string Gloss;     // English meaning (debug / reveal only)
        public int GlyphSeed;    // UI draws a glyph from this
        public string[] Concepts = Array.Empty<string>(); // grammar concepts this tile exercises, e.g. "plural-suffix"
    }

    /// <summary>An example phrase shown at the console (mural/screen), e.g. picture + alien caption.</summary>
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
        // Solution stays private to the engine: use ILanguageEngine.Check.
    }

    [Serializable]
    public class AnswerResult
    {
        public bool Correct;
        public string MissedConcept; // null when correct
        public string Feedback;      // short line for the UI
    }

    /// <summary>Shown after a puzzle: alien phrase next to the real-language phrase it exaggerates.</summary>
    [Serializable]
    public class RevealInfo
    {
        public string AlienText;
        public string RealText;      // e.g. "扉を開けて"
        public string Translation;   // e.g. "open the door"
        public string Explanation;   // how the alien form exaggerates the real one
    }

    /// <summary>"Which language was this based on?"</summary>
    [Serializable]
    public class QuizQuestion
    {
        public string Prompt;
        public string[] Options = Array.Empty<string>();
        // Correct index stays private to the engine: use ILanguageEngine.CheckQuiz.
    }

    [Serializable]
    public class LanguageWorld
    {
        public int Seed;
        public string AlienName;            // in-fiction name of the alien tongue
        public RealLanguage Real;
        public List<string> GrammarSummary = new List<string>(); // judges' overlay
        public List<LangPuzzle> Puzzles = new List<LangPuzzle>();
        [NonSerialized] public object EngineData;           // opaque engine-private payload
    }
}

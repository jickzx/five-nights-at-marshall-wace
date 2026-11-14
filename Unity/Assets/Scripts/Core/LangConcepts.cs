// CORE CONTRACT. Changes here must be called out in your PR description.
namespace Fnmw.Core
{
    /// <summary>
    /// Grammar concepts the language engine tags puzzles, tiles and wrong answers with
    /// (LangPuzzle.Concept/Concepts, Tile.Concepts, AnswerResult.MissedConcept, AnswerEvent.MissedConcept).
    /// The difficulty director can count misses per concept.
    /// </summary>
    public static class LangConcepts
    {
        public const string WordOrder = "word-order";             // general order of words
        public const string VerbFinal = "verb-final";             // verb goes at the end (Japanese, German clauses)
        public const string Particles = "particles";              // little role tags after nouns (Japanese が/を)
        public const string CompoundMeaning = "compound-meaning"; // word meaning built from parts (kanji, German compounds)
        public const string AdjectiveOrder = "adjective-order";   // adjective after the noun (French, Spanish)
        public const string GenderAgreement = "gender-agreement"; // articles/adjectives match the noun's gender
        public const string PluralAgreement = "plural-agreement"; // plural marked on every word of the phrase
        public const string VerbEnding = "verb-ending";           // verb ending says who acts (Spanish)
        public const string Punctuation = "punctuation";          // marks at both ends of a sentence (Spanish ¿ ?)
        public const string SuffixOrder = "suffix-order";         // suffix chains in a fixed order (Turkish)
        public const string VowelHarmony = "vowel-harmony";       // suffix vowels match the root (Turkish)
        public const string MeasureWord = "measure-word";         // classifier between number and noun (Mandarin)
        public const string ConsonantRoot = "consonant-root";     // 3-consonant root carries the meaning (Arabic)
        public const string VowelPattern = "vowel-pattern";       // vowel pattern carries the role (Arabic)
        public const string Vocabulary = "vocabulary";            // picked a word with a different meaning

        public static readonly string[] All =
        {
            WordOrder, VerbFinal, Particles, CompoundMeaning, AdjectiveOrder, GenderAgreement, PluralAgreement,
            VerbEnding, Punctuation, SuffixOrder, VowelHarmony, MeasureWord, ConsonantRoot, VowelPattern, Vocabulary,
        };
    }
}

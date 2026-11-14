using System.Collections.Generic;
using Fnmw.Core;

namespace Fnmw.Lang
{
    /// <summary>Short console lines for right and wrong answers. Hints point at the concept, never at the answer.</summary>
    public static class Feedback
    {
        static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            { LangConcepts.WordOrder, "The glyphs flicker red. Same words, but the examples put them in another order." },
            { LangConcepts.VerbFinal, "The glyphs flicker red. Where does the action word sit in the examples?" },
            { LangConcepts.Particles, "Red flicker. Every noun wears a little tag. Which tag marks who does it?" },
            { LangConcepts.CompoundMeaning, "The sigils grind. Build the word from the meanings of its parts, in the examples' order." },
            { LangConcepts.AdjectiveOrder, "Red flicker. In the examples, which comes first: the thing, or what it is like?" },
            { LangConcepts.GenderAgreement, "The console hisses. The words around a noun must match its gender." },
            { LangConcepts.PluralAgreement, "Close. When there are many, every word in the phrase shows it." },
            { LangConcepts.VerbEnding, "Red flicker. Here the end of the verb says who is acting." },
            { LangConcepts.Punctuation, "The phrase is missing its marks. Look at both ends of the examples." },
            { LangConcepts.SuffixOrder, "Right pieces, wrong train. The suffixes always couple in the same order." },
            { LangConcepts.VowelHarmony, "The suffixes clash. Their vowels must match the vowels of the root." },
            { LangConcepts.MeasureWord, "Red flicker. A counted thing needs the shape-word that fits it." },
            { LangConcepts.ConsonantRoot, "The skeleton is wrong. The meaning lives in the root consonants." },
            { LangConcepts.VowelPattern, "Right bones, wrong flesh. The vowel pattern says what role the word plays." },
            { LangConcepts.Vocabulary, "That word means something else here. Check the pictures again." },
        };

        static readonly string[] CorrectLines =
        {
            "The console hums. {0} accepts your words.",
            "The glyphs burn white. You speak {0}.",
            "A lock unclamps somewhere in the dark. {0} understood you.",
        };

        public static string Wrong(string concept) =>
            concept != null && Hints.TryGetValue(concept, out var h) ? h : "The glyphs flicker red. Compare with the examples.";

        public static string Correct(string alienName) =>
            string.Format(CorrectLines[(int)(Hash.Fnv(alienName) % (uint)CorrectLines.Length)], alienName);
    }
}

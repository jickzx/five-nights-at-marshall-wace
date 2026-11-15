using System.Collections.Generic;
using Fnmw.Core;

namespace Fnmw.Lang
{
    /// <summary>
    /// Approved, neutral grounding for ship hints: per concept, what to look at and one or two rule statements.
    /// None of it names a language, a country or a writing system, and none of it uses a puzzle's words, so it
    /// can be shown before the quiz. The backend keeps the same table in backend/data/hint-facts.json
    /// (SpeechAndHintTests fails if they drift). Authored for this game; a teacher should still review it.
    /// </summary>
    public static class HintFacts
    {
        /// <summary>Bump with any change; the backend's hint cache key includes it.</summary>
        public const string Version = "1";

        public sealed class Entry
        {
            public string Focus;      // "where the action word sits"
            public string[] Facts;    // neutral rule statements
            public string Offline1;   // authored level-1 hint: points attention, states no rule
        }

        public static readonly IReadOnlyDictionary<string, Entry> Table = new Dictionary<string, Entry>
        {
            [LangConcepts.WordOrder] = new Entry
            {
                Focus = "the order the words come in",
                Facts = new[] { "Every row keeps the same order of roles. Copy the order the rows use, not the order English uses." },
                Offline1 = "Same words, but the rows put them in another order. Compare the rows.",
            },
            [LangConcepts.VerbFinal] = new Entry
            {
                Focus = "where the action word sits",
                Facts = new[] { "In these rows the action word can wait until the end of its clause." },
                Offline1 = "Where does the action word sit in the rows?",
            },
            [LangConcepts.Particles] = new Entry
            {
                Focus = "the small tag words after each thing",
                Facts = new[] { "Small tag words follow a noun and say what job it does: who acts, and what is acted on." },
                Offline1 = "Every noun wears a little tag. Which tag marks who does it?",
            },
            [LangConcepts.CompoundMeaning] = new Entry
            {
                Focus = "how longer words are built from smaller parts",
                Facts = new[] { "Some words are built from smaller meaningful parts, joined in a fixed order." },
                Offline1 = "Build the word from the meanings of its parts, in the order the rows use.",
            },
            [LangConcepts.AdjectiveOrder] = new Entry
            {
                Focus = "whether a describing word comes before or after its thing",
                Facts = new[] { "Most describing words here come after the thing they describe." },
                Offline1 = "In the rows, which comes first: the thing, or what it is like?",
            },
            [LangConcepts.GenderAgreement] = new Entry
            {
                Focus = "the little words around each thing",
                Facts = new[] { "Every noun has a grammatical gender, and the little words around it change to match." },
                Offline1 = "The words around a noun must match its gender.",
            },
            [LangConcepts.PluralAgreement] = new Entry
            {
                Focus = "how the rows show that there are many",
                Facts = new[] { "When there is more than one, the plural shows on more than one word of the phrase." },
                Offline1 = "When there are many, every word in the phrase shows it.",
            },
            [LangConcepts.VerbEnding] = new Entry
            {
                Focus = "the endings of the action words",
                Facts = new[] { "The ending of the action word shows who is doing it." },
                Offline1 = "Here the end of the action word says who is acting.",
            },
            [LangConcepts.Punctuation] = new Entry
            {
                Focus = "the marks at both ends of the sentences",
                Facts = new[] { "A question or an exclamation is marked at its start as well as its end." },
                Offline1 = "The phrase is missing its marks. Look at both ends of the rows.",
            },
            [LangConcepts.SuffixOrder] = new Entry
            {
                Focus = "the order of the endings stacked on a word",
                Facts = new[] { "Endings stack onto a word in a fixed order, one meaning per ending." },
                Offline1 = "Right pieces, wrong train. The endings always couple in the same order.",
            },
            [LangConcepts.VowelHarmony] = new Entry
            {
                Focus = "the vowels inside the endings",
                Facts = new[] { "The vowel in an ending changes to match the vowels of the word it joins." },
                Offline1 = "The endings clash. Their vowels must match the vowels of the word they join.",
            },
            [LangConcepts.MeasureWord] = new Entry
            {
                Focus = "the word between this or that and the thing",
                Facts = new[] { "A pointed-at or counted thing takes a measure word that fits its kind, just before the thing." },
                Offline1 = "A counted thing needs the shape-word that fits it.",
            },
            [LangConcepts.ConsonantRoot] = new Entry
            {
                Focus = "the consonants that related words share",
                Facts = new[] { "Related words share a skeleton of consonants that carries the core meaning." },
                Offline1 = "The skeleton is wrong. The meaning lives in the shared consonants.",
            },
            [LangConcepts.VowelPattern] = new Entry
            {
                Focus = "the vowels set around the shared consonants",
                Facts = new[] { "The pattern of vowels around a consonant skeleton says what role the word plays." },
                Offline1 = "Right bones, wrong flesh. The vowel pattern says what role the word plays.",
            },
            [LangConcepts.Vocabulary] = new Entry
            {
                Focus = "which word goes with which picture",
                Facts = new[] { "Each word keeps its meaning from row to row. Match words to the meanings they appear with." },
                Offline1 = "That word means something else here. Check the meanings again.",
            },
        };

        /// <summary>Words that would give the language away before the quiz, besides each language's own names
        /// and quiz distractors (which the engine adds). Lower case, no diacritics.</summary>
        public static readonly string[] Giveaways =
        {
            "japan", "japanese", "nihon", "nippon", "tokyo", "kanji", "kana", "hiragana", "katakana", "romaji",
            "france", "french", "paris", "francais", "spain", "spanish", "espana", "espanol", "castilian", "mexico", "mexican",
            "germany", "german", "deutsch", "berlin", "turkey", "turkish", "turkiye", "turkce", "istanbul", "ankara",
            "china", "chinese", "mandarin", "beijing", "pinyin", "hanzi", "putonghua", "zhongwen", "tonal",
            "arab", "arabic", "arabia", "egypt", "cairo", "quran", "fusha", "levant",
            "korea", "korean", "vietnam", "vietnamese", "thai", "thailand", "cantonese", "italy", "italian", "portugal",
            "portuguese", "brazil", "romania", "romanian", "catalan", "dutch", "netherlands", "sweden", "swedish", "denmark",
            "danish", "norway", "norwegian", "hungary", "hungarian", "finland", "finnish", "mongolia", "mongolian",
            "hebrew", "israel", "persian", "iran", "farsi", "amharic", "ethiopia", "urdu", "pakistan", "india", "hindi",
            "europe", "european", "asia", "asian", "africa", "african", "romance", "germanic", "semitic", "turkic", "sinitic", "slavic",
        };

        /// <summary>Phrases that tell the player what to enter rather than how to reason. Lower case.</summary>
        public static readonly string[] AnswerPhrases =
        {
            "answer", "solution", "correct order", "right order", "in this order", "press", "type", "tile", "tiles",
            "key", "keys", "slot", "slots", "spell", "submit",
        };

        /// <summary>Words that place a word in the sentence. Allowed in approved rules, but never together with a
        /// puzzle word or meaning, because "put X first" is an instruction, not a hint.</summary>
        public static readonly string[] PositionWords =
        {
            "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "last", "final", "before", "after",
            "then", "next", "start", "begin", "beginning", "end", "front", "back", "left", "right", "middle", "follow", "follows",
            "precede", "precedes", "earlier", "later",
        };

        /// <summary>Guided hints: the backend asks the model only to choose rows, then fills these in, so a hint can
        /// point at this puzzle's examples without any generated wording. {a},{b}: row numbers 1-3.</summary>
        public const string GuidedLevel1 = "Compare rows {a} and {b}: look at {focus}.";
        public const string GuidedLevel2 = "{fact} Look at row {a}.";

        public static string Guided(int level, string concept, int a, int b)
        {
            var e = For(concept) ?? Table[LangConcepts.WordOrder];
            return (level <= 1 ? GuidedLevel1 : GuidedLevel2)
                .Replace("{a}", a.ToString()).Replace("{b}", b.ToString()).Replace("{focus}", e.Focus).Replace("{fact}", e.Facts[0]);
        }

        public static Entry For(string concept) =>
            concept != null && Table.TryGetValue(concept, out var e) ? e : null;

        /// <summary>Level-1 and level-2 authored hints for a concept.</summary>
        public static string[] OfflineHints(string concept)
        {
            var e = For(concept) ?? Table[LangConcepts.WordOrder];
            return new[] { e.Offline1, e.Facts[0] + " Find it in the three rows." };
        }
    }
}

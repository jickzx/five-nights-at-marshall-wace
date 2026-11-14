using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Fnmw.Core;

namespace Fnmw.UI
{
    /// <summary>
    /// Text handling for the reveal: splitting a real phrase from its romanisation, hiding the
    /// language's name until the quiz is answered, and the "now you know" line. Pure logic.
    /// </summary>
    public static class Lore
    {
        /// <summary>"扉を開けて (tobira o akete)" becomes the phrase and its reading.</summary>
        public static void SplitReal(string real, out string phrase, out string reading)
        {
            real = (real ?? "").Trim();
            var m = Regex.Match(real, @"^(.*?)\s*[\(（]([^\)）]*)[\)）]\s*$");
            if (m.Success && m.Groups[1].Value.Length > 0) { phrase = m.Groups[1].Value.Trim(); reading = m.Groups[2].Value.Trim(); }
            else { phrase = real; reading = ""; }
        }

        /// <summary>Blocks out every mention of the language, so the quiz that follows is a real question.</summary>
        public static string Redact(string text, params string[] names)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) continue;
                text = Regex.Replace(text, Regex.Escape(name.Trim()), m => new string('█', Math.Max(4, m.Value.Length)), RegexOptions.IgnoreCase);
            }
            return text;
        }

        static readonly Dictionary<string, string> Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ja", "Japanese tags each noun with a tiny particle. を (o) marks the thing acted on, so the verb can wait until the very end." },
            { "fr", "French keeps letters it no longer says. Most final consonants are silent: 'petit' sounds like 'puh-tee'." },
            { "es", "Spanish verbs carry their subject inside them. 'Hablo' already means 'I speak', so 'yo' is usually dropped." },
            { "fi", "Finnish stacks endings onto one word. 'Talossanikin' means 'in my house, too'." },
            { "de", "German glues nouns into new ones. A glove is a 'Handschuh': a hand-shoe." },
            { "it", "Italian doubles consonants to change the word. 'Pala' is a shovel, 'palla' is a ball." },
            { "pt", "Portuguese hums some vowels through the nose and marks them with a tilde, as in 'pão', bread." },
            { "tr", "Turkish endings change their vowel to match the word: 'evler' (houses) but 'kitaplar' (books)." },
            { "ko", "Korean puts the verb last, and its letters are stacked into square blocks, one block per syllable." },
            { "zh", "Mandarin verbs never change for tense. Pitch does the work instead: 'mā' is mother, 'mǎ' is horse." },
            { "ar", "Arabic words grow from three-consonant roots. K-T-B gives 'kitāb' (book) and 'kātib' (writer)." },
            { "ru", "Russian has no word for 'the' or 'a', and in the present tense it drops 'is' altogether." },
            { "hi", "Hindi puts the verb last and its 'prepositions' come after the noun: 'mez par' is 'table on'." },
            { "sw", "Swahili sorts nouns into classes by their prefix. 'Kitabu' is one book, 'vitabu' is several." },
            { "cy", "Welsh changes the first letter of a word to fit its neighbours: 'cath' (cat) becomes 'fy nghath' (my cat)." },
            { "hu", "Hungarian says 'in the house' with one word and a suffix: 'házban'." },
            { "la", "In Latin, endings do the work of word order: 'canis hominem mordet' and 'hominem canis mordet' both mean the dog bites the man." },
            { "el", "Greek writes an accent on almost every word to show where the stress falls, as in 'πόρτα', door." },
            { "haw", "Hawaiian is written with just thirteen letters, and every syllable ends in a vowel." },
        };

        static readonly Dictionary<string, string> NameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Japanese", "ja" }, { "French", "fr" }, { "Spanish", "es" }, { "Finnish", "fi" }, { "German", "de" }, { "Italian", "it" },
            { "Portuguese", "pt" }, { "Turkish", "tr" }, { "Korean", "ko" }, { "Chinese", "zh" }, { "Mandarin", "zh" }, { "Arabic", "ar" },
            { "Russian", "ru" }, { "Hindi", "hi" }, { "Swahili", "sw" }, { "Welsh", "cy" }, { "Hungarian", "hu" }, { "Latin", "la" },
            { "Greek", "el" }, { "Hawaiian", "haw" },
        };

        public static string CodeFor(string languageName, string fallbackCode = null)
        {
            if (!string.IsNullOrEmpty(languageName) && NameToCode.TryGetValue(languageName.Trim(), out var code)) return code;
            return fallbackCode ?? "";
        }

        /// <summary>
        /// The "now you know" line. An engine can supply its own by adding a string field called Fact,
        /// NowYouKnow or FunFact to RevealInfo; otherwise a built-in fact for the language is used, then
        /// the world's own grammar summary.
        /// </summary>
        public static string FactFor(RevealInfo reveal, string languageName, LanguageWorld world)
        {
            var own = Optional(reveal, "Fact", "NowYouKnow", "FunFact");
            if (!string.IsNullOrWhiteSpace(own)) return own.Trim();
            var code = CodeFor(languageName, world?.Real != null && string.Equals(world.Real.Name, languageName, StringComparison.OrdinalIgnoreCase) ? world.Real.Code : null);
            if (!string.IsNullOrEmpty(code))
            {
                if (code.Length > 2 && code[2] == '-') code = code.Substring(0, 2);
                if (Facts.TryGetValue(code, out var fact)) return fact;
            }
            if (world?.GrammarSummary != null && world.GrammarSummary.Count > 0) return world.GrammarSummary[0] + ".";
            return "";
        }

        /// <summary>Reads an optional string member by name, so newer engines can carry more than the contract.</summary>
        public static string Optional(object source, params string[] names)
        {
            if (source == null) return null;
            var type = source.GetType();
            foreach (var name in names)
            {
                var f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(string)) return f.GetValue(source) as string;
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(string) && p.CanRead) return p.GetValue(source) as string;
            }
            return null;
        }

        public static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }

    /// <summary>What the player decoded this run, for the escape screen.</summary>
    public sealed class RunJournal
    {
        public sealed class Phrase { public string Real, Reading, Translation; }

        public sealed class Entry
        {
            public string Language;
            public string Fact;
            public bool Named;                    // the player picked it in the quiz at least once
            public readonly List<Phrase> Phrases = new List<Phrase>();
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public Entry Record(string language, RevealInfo reveal, string fact, bool named)
        {
            if (string.IsNullOrWhiteSpace(language)) language = "Unknown tongue";
            var e = Entries.Find(x => string.Equals(x.Language, language, StringComparison.OrdinalIgnoreCase));
            if (e == null) { e = new Entry { Language = language }; Entries.Add(e); }
            if (!string.IsNullOrWhiteSpace(fact)) e.Fact = fact;
            e.Named |= named;
            if (reveal != null)
            {
                Lore.SplitReal(reveal.RealText, out var phrase, out var reading);
                if (phrase.Length > 0 && !e.Phrases.Exists(p => p.Real == phrase))
                    e.Phrases.Add(new Phrase { Real = phrase, Reading = reading, Translation = reveal.Translation ?? "" });
            }
            return e;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fnmw.Lang
{
    /// <summary>
    /// Decides whether a generated hint may be shown before the quiz. Only the engine knows the answer, so this
    /// is the final check (the backend runs a similar one without the answer). It is deliberately conservative:
    /// a rejected hint just falls back to an authored one. It cannot prove that no paraphrase reveals the answer;
    /// it blocks the known ways: naming the language, quoting puzzle words, ordering them, and giving instructions.
    /// </summary>
    internal static class HintSafety
    {
        public const int MaxLength = 160;

        static readonly Regex Allowed = new Regex(@"^[A-Za-zÀ-ɏḀ-ỿ0-9 .,;:'""!?()\-‘’“”¿¡]+$", RegexOptions.Compiled);
        static readonly Regex Token = new Regex(@"[a-z0-9]+(?:'[a-z0-9]+)*", RegexOptions.Compiled);
        static readonly Regex RowRef = new Regex(@"\brows?\s+[1-3](?:\s*(?:,|and)\s*[1-3])*", RegexOptions.Compiled);
        static readonly Regex Url = new Regex(@"\b(?:https?|www)\b|[a-z]\.[a-z]", RegexOptions.Compiled);
        static readonly Regex DoubleQuoted = new Regex(@"[""“”]([^""“”]{1,48})[""“”]", RegexOptions.Compiled);
        static readonly Regex SingleQuoted = new Regex(@"(?<![a-z0-9])['‘’]([^'‘’]{1,48})['‘’](?![a-z0-9])", RegexOptions.Compiled);

        /// <summary>Common English words that also happen to be puzzle words in some language ("die", "kind", "come").
        /// Unquoted, they are read as English; quoted, they still count.</summary>
        internal static readonly HashSet<string> English = new HashSet<string>
        {
            "the", "and", "for", "are", "was", "not", "but", "you", "all", "can", "her", "his", "one", "our", "out", "has",
            "its", "may", "now", "see", "two", "who", "did", "get", "how", "man", "new", "old", "way", "use", "any", "come",
            "kind", "will", "hat", "die", "son", "sin", "con", "les", "des", "est", "son", "par", "ist", "rot", "bin", "gut",
            "art", "also", "here", "this", "that", "with", "from", "into", "word", "words", "row", "rows", "each", "what",
            "where", "which", "when", "thing", "things", "look", "find", "same", "other", "every", "more", "many", "some",
            "does", "sit", "sits", "end", "role", "roles", "order", "tag", "tags", "noun", "part", "parts", "say", "says",
        };

        internal static readonly HashSet<string> GlossStop = new HashSet<string>
        {
            "this", "that", "these", "those", "with", "from", "into", "very", "have", "will", "what", "which", "when", "where",
            "there", "here", "they", "them", "their", "word", "words", "thing", "things", "each", "every", "more", "many",
            "some", "does", "doing", "please", "give", "make", "look", "find", "same", "other",
        };

        static string[] _langWords;

        /// <summary>Every language name, own name and quiz distractor, folded, plus the fixed giveaway list.</summary>
        static string[] LanguageWords()
        {
            if (_langWords != null) return _langWords;
            var set = new HashSet<string>(HintFacts.Giveaways);
            foreach (var l in LanguageEngine.AllLanguages())
            {
                set.Add(Fold(l.Name)); set.Add(Fold(l.Endonym));
                foreach (var d in l.QuizDistractors) set.Add(Fold(d));
            }
            // multi-word names also count word by word ("mandarin chinese" -> "mandarin", "chinese")
            foreach (var w in set.ToList()) foreach (var part in w.Split(' ')) if (part.Length >= 4) set.Add(part);
            return _langWords = set.Where(w => w.Length > 0).ToArray();
        }

        /// <summary>Lower case, diacritics and look-alike quotes folded away: "Français" -> "francais".</summary>
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c0 in d)
            {
                char c = c0;
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.ModifierLetter) continue;
                if (c == 'ı') c = 'i';
                else if (c == 'ß') { sb.Append("ss"); continue; }
                else if (c == '‘' || c == '’') c = '\'';
                else if (c == '“' || c == '”') c = '"';
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        static bool HasPhrase(string folded, string phrase) =>
            Regex.IsMatch(folded, @"(?<![a-z0-9])" + Regex.Escape(phrase) + @"(?![a-z0-9])");

        static List<string> Tokens(string folded)
        {
            var list = new List<string>();
            foreach (Match m in Token.Matches(folded)) list.Add(m.Value);
            return list;
        }

        /// <summary>True when the token sequence of <paramref name="form"/> appears in <paramref name="tokens"/>.</summary>
        static bool Contains(List<string> tokens, List<string> form)
        {
            if (form.Count == 0 || form.Count > tokens.Count) return false;
            for (int i = 0; i + form.Count <= tokens.Count; i++)
            {
                int k = 0;
                while (k < form.Count && tokens[i + k] == form[k]) k++;
                if (k == form.Count) return true;
            }
            return false;
        }

        public static bool Check(BuiltPuzzle b, string text, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(text)) { reason = "empty"; return false; }
            if (text.Length > MaxLength) { reason = "too long"; return false; }
            if (!Allowed.IsMatch(text)) { reason = "characters"; return false; }
            string f = Fold(text);
            if (Url.IsMatch(f)) { reason = "url"; return false; }
            if (RowRef.Replace(f, " ").Any(char.IsDigit)) { reason = "digits"; return false; }
            foreach (var w in LanguageWords()) if (HasPhrase(f, w)) { reason = "language"; return false; }
            foreach (var p in HintFacts.AnswerPhrases) if (HasPhrase(f, p)) { reason = "instruction"; return false; }

            // approved statements are safe by review, so only what the text adds to them is checked further
            foreach (var e in HintFacts.Table.Values)
            {
                foreach (var fact in e.Facts) f = f.Replace(Fold(fact), " ");
                f = f.Replace(Fold(e.Focus), " ").Replace(Fold(e.Offline1), " ");
            }
            var tokens = Tokens(f);
            bool ordering = HintFacts.PositionWords.Any(p => tokens.Contains(p));
            var quoted = new List<List<string>>();
            foreach (Match m in DoubleQuoted.Matches(f)) quoted.Add(Tokens(m.Groups[1].Value));
            foreach (Match m in SingleQuoted.Matches(f)) quoted.Add(Tokens(m.Groups[1].Value));

            // puzzle words: every tile and example word, any form of it
            var forms = new HashSet<string>();
            foreach (var t in b.Puzzle.Tiles) forms.Add(t.Alien);
            foreach (var e in b.Puzzle.Examples) foreach (var w in e.Words) forms.Add(w.Alien);
            var puzzleRefs = new HashSet<string>();
            var quotedRefs = new HashSet<string>();
            foreach (var raw in forms)
            {
                var folded = Fold(raw).TrimStart('-');
                var ft = Tokens(folded);
                if (ft.Count == 0) continue;
                string key = string.Join(" ", ft);
                bool inQuote = quoted.Any(q => Contains(q, ft));
                if (inQuote) { quotedRefs.Add(key); puzzleRefs.Add(key); continue; }
                bool shortOrEnglish = ft.Count == 1 && (ft[0].Length <= 2 || English.Contains(ft[0]));
                if ((ordering || !shortOrEnglish) && Contains(tokens, ft)) puzzleRefs.Add(key);
            }
            if (quotedRefs.Count > 1 || puzzleRefs.Count > 1) { reason = "puzzle words"; return false; }

            // meanings of the answer's words: "door comes before open" orders the answer without a puzzle word
            var glossRefs = new HashSet<string>();
            var answer = new HashSet<string>(b.AnswerIds);
            foreach (var t in b.Puzzle.Tiles)
            {
                if (!answer.Contains(t.Id) || string.IsNullOrEmpty(t.Gloss)) continue;
                foreach (var part in t.Gloss.Split('-', ' ', '.'))
                    if (part.Length >= 4 && part.All(char.IsLower) && !GlossStop.Contains(part) && tokens.Contains(Fold(part))) glossRefs.Add(part);
            }
            int refs = puzzleRefs.Count + glossRefs.Count;
            if (refs >= 3) { reason = "meanings"; return false; }
            if (refs >= 1 && ordering) { reason = "ordering"; return false; }
            return true;
        }
    }
}

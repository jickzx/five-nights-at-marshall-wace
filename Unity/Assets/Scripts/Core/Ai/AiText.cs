using System;
using System.Text;
using UnityEngine;

namespace Fnmw.Core.Ai
{
    /// <summary>Helpers for checking a model reply before the player sees it. All pure.</summary>
    public static class AiText
    {
        /// <summary>Drop a Markdown code fence around a reply, if the model added one.</summary>
        public static string StripFences(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
            int firstLine = s.IndexOf('\n');
            if (firstLine >= 0) s = s.Substring(firstLine + 1);
            else
            {
                // A fence on one line: ```json {...} ```
                s = s.Substring(3);
                if (s.StartsWith("json", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            }
            int close = s.LastIndexOf("```", StringComparison.Ordinal);
            if (close >= 0) s = s.Substring(0, close);
            return s.Trim();
        }

        /// <summary>Parse a JSON object reply into a [Serializable] class with public fields. False when it is not valid
        /// JSON. A field the reply left out keeps its default, so check the fields you need.</summary>
        public static bool TryParseJson<T>(string text, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { value = JsonUtility.FromJson<T>(StripFences(text)); }
            catch (Exception) { return false; }
            return value != null;
        }

        /// <summary>One tidy line: whitespace collapsed, wrapping quotes removed. Null stays null.</summary>
        public static string Clean(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            string t = sb.ToString();
            // Only a single pair around the whole line is wrapping: `"Run," I said, "now."` keeps its quotes.
            if (t.Length >= 2 && IsQuote(t[0]) && IsQuote(t[t.Length - 1]))
            {
                string inner = t.Substring(1, t.Length - 2);
                bool more = false;
                foreach (char c in inner) if (IsQuote(c)) { more = true; break; }
                if (!more) t = inner.Trim();
            }
            return t;
        }

        /// <summary>A letter or digit of a script that separates words with spaces (below the Hebrew block).</summary>
        static bool Spaced(char c) => c < 0x0590 && char.IsLetterOrDigit(c);

        static bool IsQuote(char c) => c == '"' || c == '“' || c == '”';

        public static int WordCount(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>True when a cleaned line is non-empty and within both limits.</summary>
        public static bool Fits(string s, int maxChars, int maxWords) =>
            !string.IsNullOrWhiteSpace(s) && s.Length <= maxChars && WordCount(s) <= maxWords;

        /// <summary>
        /// True when text contains any of the terms, ignoring case. Use it to reject a reply that names the language
        /// before the quiz or quotes an answer word. Empty terms are ignored. In scripts that put spaces between
        /// words (Latin, Greek, Cyrillic) the term must be a whole word, so "man" is not found in "german". In
        /// scripts that do not (Japanese, Chinese) or that attach particles to words (Arabic, Korean) any
        /// occurrence counts.
        /// </summary>
        public static bool MentionsAny(string text, params string[] terms)
        {
            if (string.IsNullOrEmpty(text) || terms == null) return false;
            foreach (var term in terms)
            {
                if (string.IsNullOrWhiteSpace(term)) continue;
                string t = term.Trim();
                int from = 0;
                while (from <= text.Length - t.Length)
                {
                    int i = text.IndexOf(t, from, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) break;
                    int end = i + t.Length;
                    bool startOk = i == 0 || !(Spaced(t[0]) && Spaced(text[i - 1]));
                    bool endOk = end >= text.Length || !(Spaced(t[t.Length - 1]) && Spaced(text[end]));
                    if (startOk && endOk) return true;
                    from = i + 1;
                }
            }
            return false;
        }
    }
}

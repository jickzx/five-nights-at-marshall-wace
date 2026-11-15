using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Fnmw.Core.Ai;

namespace Fnmw.Game.Debrief
{
    /// <summary>
    /// The run debrief's text: the stock account built from the log, the Gemini request, and the reply check. All pure.
    /// </summary>
    public static class DebriefWriter
    {
        public const string Feature = "debrief";
        /// <summary>About three lines of the end screen's 16 px font across 560 px.</summary>
        public const int MaxChars = 210;
        public const int MaxWords = 42;

        [Serializable] public class Reply { public string debrief; }

        public const string SchemaJson =
            "{\"type\":\"OBJECT\",\"properties\":{\"debrief\":{\"type\":\"STRING\"}},\"required\":[\"debrief\"]}";

        public static readonly string SystemPrompt =
            "You write the debrief shown on the end screen of a short horror game. The player decodes consoles written in real " +
            "human languages while a beast hunts them on a blood-red shore; the run ends when they escape on the ship or the beast catches them. " +
            "Write two or three short sentences, at most " + MaxChars + " characters in all, in plain English, second person, past tense: " +
            "which languages they met, where they slipped, how the beast behaved, and how the run ended. " +
            "Use only the facts given. Do not invent events, numbers, languages, places or feelings that are not in the facts. " +
            "Name only the languages listed. If there were no wrong answers, say so rather than inventing a slip. " +
            "The beast did not choose the puzzles, did not target the player's weak points and did not control the game: " +
            "it only grew stronger over time and with mistakes, and hunted. Never claim otherwise. " +
            "No markdown, no quotation marks, no title. Reply as JSON: {\"debrief\": \"...\"}.";

        public static AiRequest Request(RunLog log) => new AiRequest
        {
            Feature = Feature,
            System = SystemPrompt,
            Prompt = "Facts of this run:\n" + log.Facts(),
            SchemaJson = SchemaJson,
            MaxOutputTokens = 256,
            Temperature = 0.7f,
            TimeoutSeconds = 10f,
        };

        // ------------------------------------------------------------------ stock

        /// <summary>The account built from the log alone. Always two or three sentences within the limits.</summary>
        public static string Stock(RunLog log)
        {
            var parts = new List<string>();
            var langs = log.Languages;
            if (log.Consoles.Count == 0) parts.Add("You never reached a console.");
            else if (langs.Count == 0) parts.Add("You reached " + log.Consoles.Count + (log.Consoles.Count == 1 ? " console." : " consoles."));
            else parts.Add("You read " + Join(langs) + ".");

            if (log.Consoles.Count > 0)
            {
                int n = log.Mistakes;
                var worst = log.WorstConsole;
                if (worst == null) parts.Add("Not one wrong answer.");
                else
                {
                    var sb = new StringBuilder("You slipped ").Append(n).Append(n == 1 ? " time" : " times");
                    string concept = Most(worst.Missed);
                    if (!string.IsNullOrEmpty(concept) && concept != "unknown") sb.Append(n == 1 ? ", on " : ", mostly on ").Append(RunLog.Plain(concept));
                    if (!string.IsNullOrEmpty(worst.Language)) sb.Append(" at the ").Append(worst.Language).Append(" console");
                    parts.Add(sb.Append('.').ToString());
                }
            }

            string stage = "The beast reached stage " + log.HighestStage;
            string clock = RunLog.Clock(log.Elapsed);
            if (log.Outcome == RunOutcome.Escaped) parts.Add(stage + "; you lifted off at " + clock + ".");
            else
            {
                var at = log.Consoles.Find(c => c.Index == log.CaughtAtConsole);
                if (at != null && !string.IsNullOrEmpty(at.Language)) parts.Add(stage + " and caught you at the " + at.Language + " console at " + clock + ".");
                else parts.Add(stage + " and caught you at " + clock + ", " + log.Solved + " of " + log.Total + " consoles decoded.");
            }
            string text = string.Join(" ", parts);
            // a run with five long language names can overrun: drop the slip sentence before the ending
            if (!AiText.Fits(text, MaxChars, MaxWords) && parts.Count == 3) text = parts[0] + " " + parts[2];
            return text;
        }

        static string Join(List<string> items)
        {
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
        }

        static string Most(List<string> items)
        {
            string best = null; int bestN = 0;
            var counts = new Dictionary<string, int>();
            foreach (var s in items)
            {
                counts.TryGetValue(s, out int n);
                counts[s] = ++n;
                if (n > bestN) { best = s; bestN = n; }
            }
            return best;
        }

        // ------------------------------------------------------------------ reply check

        /// <summary>Language names a reply might wrongly bring in. Run languages from the world are added on top.</summary>
        static readonly string[] KnownLanguages =
        {
            "Japanese", "German", "French", "Spanish", "Turkish", "Mandarin", "Chinese", "Cantonese", "Arabic", "Korean",
            "Italian", "Portuguese", "Russian", "Hindi", "Finnish", "Hungarian", "Swahili", "Dutch", "Swedish", "Norwegian",
            "Danish", "Polish", "Greek", "Hebrew", "Persian", "Farsi", "Vietnamese", "Thai", "Indonesian", "Malay", "Tagalog",
            "Welsh", "Irish", "Latin", "Esperanto", "Basque", "Icelandic", "Czech", "Ukrainian", "Bengali", "Urdu", "Tamil",
            "Zulu", "Navajo", "Quechua", "Yoruba", "Klingon", "Sanskrit", "Mongolian", "Tibetan",
        };

        /// <summary>Words that claim the beast steered the game, which it does not (see docs/PITCH.md).</summary>
        static readonly string[] AgencyWords =
        {
            "target", "targets", "targeted", "targeting", "exploit", "exploited", "exploiting", "chose", "chosen", "choosing",
            "picked", "selected", "rigged", "engineered", "controlled", "controlling", "steered", "designed", "probed", "probing",
            "fed you", "tailored", "manipulated",
        };

        static readonly string[] DeathWords = { "caught", "killed", "died", "dead", "death", "devoured", "slain" };
        static readonly string[] EscapeWords = { "lifted off", "lift-off", "liftoff", "flew away", "got away", "made it out", "you escaped" };

        static readonly Dictionary<string, int> NumberWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", 0 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 },
            { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 },
        };

        static readonly Regex Digits = new Regex(@"\d+");
        static readonly Regex Words = new Regex(@"[A-Za-z]+");

        /// <summary>
        /// Check a reply against the log. True with the cleaned text when it may be shown; otherwise false and a short
        /// reason for the console. worldLanguages: every real language of the run's world, met or not.
        /// </summary>
        public static bool Check(string replyJson, RunLog log, IEnumerable<string> worldLanguages, out string text, out string reason)
        {
            text = null;
            if (!AiText.TryParseJson(replyJson, out Reply reply)) { reason = "not JSON"; return false; }
            string t = AiText.Clean(reply.debrief);
            if (!AiText.Fits(t, MaxChars, MaxWords)) { reason = "length"; return false; }
            int sentences = CountSentences(t);
            if (sentences < 2 || sentences > 3 || !EndsSentence(t)) { reason = "sentences " + sentences; return false; }

            var forbidden = ForbiddenLanguages(log, worldLanguages);
            foreach (var name in forbidden)
                if (AiText.MentionsAny(t, name)) { reason = "names " + name; return false; }

            var known = new HashSet<int>();
            foreach (Match m in Digits.Matches(log.Facts())) if (int.TryParse(m.Value, out int v)) known.Add(v);
            foreach (Match m in Digits.Matches(t))
                if (!int.TryParse(m.Value, out int v) || !known.Contains(v)) { reason = "number " + m.Value; return false; }
            foreach (Match m in Words.Matches(t))
                if (NumberWords.TryGetValue(m.Value, out int v) && !known.Contains(v) && !(v == 1 && m.Value.Equals("one", StringComparison.OrdinalIgnoreCase) && OneIsPronoun(t, m.Index)))
                { reason = "number " + m.Value; return false; }

            if (log.Outcome == RunOutcome.Escaped && AiText.MentionsAny(t, DeathWords)) { reason = "says caught"; return false; }
            if (log.Outcome == RunOutcome.Caught && AiText.MentionsAny(t, EscapeWords)) { reason = "says escaped"; return false; }
            if (AiText.MentionsAny(t, AgencyWords)) { reason = "beast agency"; return false; }

            text = t;
            reason = null;
            return true;
        }

        /// <summary>"not one wrong answer", "no one": "one" there is not a count.</summary>
        static bool OneIsPronoun(string t, int at)
        {
            string before = t.Substring(0, at).TrimEnd();
            return before.EndsWith("not", StringComparison.OrdinalIgnoreCase) || before.EndsWith("no", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Every language name the reply must not use: the known list and the world's languages, less the ones met.</summary>
        public static List<string> ForbiddenLanguages(RunLog log, IEnumerable<string> worldLanguages)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in log.Languages)
                foreach (Match m in Words.Matches(l)) allowed.Add(m.Value);
            var all = new List<string>(KnownLanguages);
            if (worldLanguages != null)
                foreach (var l in worldLanguages)
                    if (!string.IsNullOrWhiteSpace(l))
                        foreach (Match m in Words.Matches(l)) all.Add(m.Value);
            var forbidden = new List<string>();
            foreach (var w in all)
                if (!allowed.Contains(w) && !forbidden.Exists(f => f.Equals(w, StringComparison.OrdinalIgnoreCase))) forbidden.Add(w);
            return forbidden;
        }

        /// <summary>Sentence ends: a run of . ! ? followed by a space or the end. "6:12." counts once.</summary>
        public static int CountSentences(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return 0;
            int n = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsEnd(t[i])) continue;
                while (i + 1 < t.Length && IsEnd(t[i + 1])) i++;
                if (i + 1 >= t.Length || char.IsWhiteSpace(t[i + 1])) n++;
            }
            return n;
        }

        static bool EndsSentence(string t) => t.Length > 0 && IsEnd(t[t.Length - 1]);
        static bool IsEnd(char c) => c == '.' || c == '!' || c == '?';
    }
}

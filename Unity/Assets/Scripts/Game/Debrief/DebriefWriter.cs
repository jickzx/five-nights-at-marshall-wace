using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Fnmw.Core.Ai;

namespace Fnmw.Game.Debrief
{
    /// <summary>
    /// The run debrief's text: the stock account built from the log, the OpenRouter request, and the reply check. All pure.
    /// </summary>
    public static class DebriefWriter
    {
        public const string Feature = "debrief";
        /// <summary>About three lines of the end screen's 16 px font across 560 px.</summary>
        public const int MaxChars = 210;
        public const int MaxWords = 42;

        [Serializable] public class Reply { public string debrief; }

        public const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{\"debrief\":{\"type\":\"string\"}},\"required\":[\"debrief\"],\"additionalProperties\":false}";

        public static readonly string SystemPrompt =
            "You write the debrief shown on the end screen of a short horror game. The player decodes consoles written in real " +
            "human languages while a beast hunts them on a blood-red shore; the run ends when they escape on the ship or the beast catches them. " +
            "Write two or three short sentences, at most " + MaxChars + " characters in all, in plain English, second person, past tense: " +
            "which languages they met, where they slipped, how the beast behaved, and how the run ended. " +
            "Use only the facts given. Do not invent events, numbers, languages, places or feelings that are not in the facts. " +
            "Name only the languages listed; never guess the name of a language marked undecoded. If there were no wrong answers, say so rather than inventing a slip. " +
            "The beast did not choose the puzzles, did not target the player's weak points and did not control the game: " +
            "it only grew stronger over time and with mistakes, and hunted. Never claim otherwise. " +
            "Do not call the run flawless or easy if there were wrong answers. Give times as they are, or rounded honestly. " +
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
                    string worstName = log.NameOf(worst);
                    if (worstName != null) sb.Append(" at the ").Append(worstName).Append(" console");
                    parts.Add(sb.Append('.').ToString());
                }
            }

            string stage = "The beast reached stage " + log.HighestStage;
            string clock = RunLog.Clock(log.Elapsed);
            if (log.Outcome == RunOutcome.Escaped) parts.Add(stage + "; you lifted off at " + clock + ".");
            else
            {
                var at = log.Consoles.Find(c => c.Index == log.CaughtAtConsole);
                string atName = log.NameOf(at);
                if (atName != null) parts.Add(stage + " and caught you at the " + atName + " console at " + clock + ".");
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

        /// <summary>Words that say the game was fixed, whoever the subject: nothing was (see docs/PITCH.md).</summary>
        static readonly string[] RiggingWords = { "rigged", "engineered", "manipulated", "manipulating", "tailored", "fed you" };

        /// <summary>
        /// Choosing, targeting and controlling. Fair with the player doing it ("you picked the wrong word"); a false claim
        /// when the beast (or the game, or no one in a passive) does it, because the beast does not steer the run.
        /// </summary>
        static readonly HashSet<string> ChoiceVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "target", "targets", "targeted", "targeting", "exploit", "exploits", "exploited", "exploiting",
            "choose", "chooses", "chose", "chosen", "choosing", "pick", "picks", "picked", "picking",
            "select", "selects", "selected", "selecting", "control", "controls", "controlled", "controlling",
            "steer", "steers", "steered", "steering", "design", "designs", "designed", "probe", "probes", "probed", "probing",
        };

        /// <summary>Subjects standing for the beast, or for anything else a reply might say ran the game.</summary>
        static readonly HashSet<string> BeastSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "beast", "monster", "creature", "hunter", "predator", "thing", "it", "game", "shore", "sea" };

        static readonly HashSet<string> PassiveAux = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "is", "are", "was", "were", "be", "been", "being", "got", "get", "gets" };

        /// <summary>Words before "you" that make it the object ("the beast caught you and chose ..."), not the subject.</summary>
        static readonly HashSet<string> ObjectTakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "caught", "let", "made", "kept", "gave", "cost", "left", "beat", "found", "saw", "sent", "took", "got", "had", "for", "at", "on", "to", "with" };

        /// <summary>Claims of a clean run. Fair when the log has no wrong answers, or when the sentence names a console that had none.</summary>
        static readonly string[] FlawlessPhrases =
        {
            "flawless", "flawlessly", "perfect", "perfectly", "breezed", "breeze", "breezing", "effortless", "effortlessly",
            "spotless", "mistake free", "error free", "without a slip", "without a mistake", "without mistakes", "without error",
            "without errors", "no mistakes", "no slips", "no wrong answers", "not one wrong", "not one slip", "not one mistake",
            "never slipped", "never stumbled", "never faltered", "never missed", "clean sweep",
        };

        /// <summary>Words for wrong answers. A claim of a slip is false when the log has none, unless it is negated ("not one slip").</summary>
        static readonly string[] SlipPhrases =
        {
            "slip", "slips", "slipped", "slipping", "stumble", "stumbled", "stumbling", "trip", "tripped", "tripping",
            "fumble", "fumbled", "mistake", "mistakes", "misstep", "missteps", "blunder", "blundered", "error", "errors",
            "misfire", "misfires", "misfired", "wrong answer", "wrong answers", "wrong guess", "wrong word",
        };

        static readonly HashSet<string> Negators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "no", "not", "never", "without", "zero", "nothing", "none", "nor" };

        static readonly string[] DeathWords = { "caught", "killed", "died", "dead", "death", "devoured", "slain" };
        static readonly string[] EscapeWords = { "lifted off", "lift-off", "liftoff", "flew away", "got away", "made it out", "you escaped" };

        static readonly Dictionary<string, int> NumberWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", 0 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 },
            { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 },
        };

        static readonly HashSet<string> MinuteUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minute", "minutes", "min", "mins" };
        static readonly HashSet<string> SecondUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "second", "seconds", "sec", "secs", "s" };

        static readonly Regex Clock = new Regex(@"\b(\d+):(\d\d)\b");
        static readonly Regex AfterSeconds = new Regex(@"after (\d+) s\b");
        static readonly Regex NumberOrWord = new Regex(@"\d+|[A-Za-z]+");
        static readonly Regex Words = new Regex(@"[A-Za-z]+");
        static readonly Regex SentenceBreak = new Regex(@"(?<=[.!?])\s+");

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

            reason = CheckNumbers(t, log.Facts());
            if (reason != null) return false;

            if (log.Outcome == RunOutcome.Escaped && AiText.MentionsAny(t, DeathWords)) { reason = "says caught"; return false; }
            if (log.Outcome == RunOutcome.Caught && AiText.MentionsAny(t, EscapeWords)) { reason = "says escaped"; return false; }
            if (AiText.MentionsAny(t, RiggingWords)) { reason = "beast agency"; return false; }

            foreach (var sentence in SentenceBreak.Split(t))
            {
                var w = Tokens(sentence);
                if (ClaimsBeastAgency(w)) { reason = "beast agency"; return false; }
                if (log.Mistakes > 0 && Find(w, FlawlessPhrases, out _) && !NamesCleanConsole(sentence, log)) { reason = "says flawless"; return false; }
                if (log.Mistakes == 0 && UnnegatedSlip(w)) { reason = "says slipped"; return false; }
            }

            text = t;
            reason = null;
            return true;
        }

        // ---- numbers

        /// <summary>The numbers the facts back up, by kind. A reply may round a time honestly but must state counts exactly.</summary>
        sealed class Backed
        {
            public readonly HashSet<int> Clocks = new HashSet<int>();    // m:ss in the facts, as seconds
            public readonly HashSet<int> Counts = new HashSet<int>();    // every other number in the facts
            public readonly HashSet<int> Minutes = new HashSet<int>();   // a logged time or duration, floored or rounded to minutes
            public readonly HashSet<int> Seconds = new HashSet<int>();   // a logged time or duration in seconds, exact or to the nearest 5 or 10
        }

        static Backed BackedNumbers(string facts)
        {
            var b = new Backed();
            var rest = facts.ToCharArray();
            var clocks = new List<int>();
            foreach (Match m in Clock.Matches(facts))
            {
                int secs = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                b.Clocks.Add(secs);
                if (!clocks.Contains(secs)) clocks.Add(secs);
                Blank(rest, m.Index, m.Length);
            }
            var durations = new List<int>(clocks);
            for (int i = 0; i < clocks.Count; i++)
                for (int j = 0; j < clocks.Count; j++)
                    if (clocks[j] > clocks[i]) durations.Add(clocks[j] - clocks[i]);
            foreach (Match m in AfterSeconds.Matches(facts))
            {
                durations.Add(int.Parse(m.Groups[1].Value));
                Blank(rest, m.Groups[1].Index, m.Groups[1].Length);
            }
            foreach (int d in durations)
            {
                b.Seconds.Add(d);
                b.Seconds.Add(RoundTo(d, 5));
                b.Seconds.Add(RoundTo(d, 10));
                b.Minutes.Add(d / 60);
                b.Minutes.Add((int)Math.Round(d / 60.0, MidpointRounding.AwayFromZero));
            }
            foreach (Match m in NumberOrWord.Matches(new string(rest)))
                if (char.IsDigit(m.Value[0]) && int.TryParse(m.Value, out int v)) b.Counts.Add(v);
            return b;
        }

        static int RoundTo(int v, int step) => (int)Math.Round(v / (double)step, MidpointRounding.AwayFromZero) * step;

        static void Blank(char[] chars, int from, int length)
        {
            for (int i = from; i < from + length && i < chars.Length; i++) chars[i] = ' ';
        }

        /// <summary>Null when every number in the reply is backed by the facts, else the reason.</summary>
        static string CheckNumbers(string t, string facts)
        {
            var b = BackedNumbers(facts);
            var rest = t.ToCharArray();
            foreach (Match m in Clock.Matches(t))
            {
                if (!int.TryParse(m.Groups[1].Value, out int min) || !b.Clocks.Contains(min * 60 + int.Parse(m.Groups[2].Value))) return "time " + m.Value;
                Blank(rest, m.Index, m.Length);
            }
            string s = new string(rest);
            var tokens = NumberOrWord.Matches(s);
            for (int i = 0; i < tokens.Count; i++)
            {
                var m = tokens[i];
                int v;
                if (char.IsDigit(m.Value[0])) { if (!int.TryParse(m.Value, out v)) return "number " + m.Value; }
                else if (!NumberWords.TryGetValue(m.Value, out v)) continue;
                else if (v == 1 && OneIsPronoun(s, m.Index)) continue;

                // the unit is the next word, after spaces or a hyphen ("six minutes", "6-minute", "21 s")
                string unit = null;
                if (i + 1 < tokens.Count)
                {
                    var next = tokens[i + 1];
                    string gap = s.Substring(m.Index + m.Length, next.Index - m.Index - m.Length);
                    if (gap.Trim(' ', '-').Length == 0) unit = next.Value;
                }
                var backed = unit != null && MinuteUnits.Contains(unit) ? b.Minutes
                    : unit != null && SecondUnits.Contains(unit) ? b.Seconds
                    : b.Counts;
                if (!backed.Contains(v)) return "number " + m.Value;
            }
            return null;
        }

        /// <summary>"not one wrong answer", "no one": "one" there is not a count.</summary>
        static bool OneIsPronoun(string t, int at)
        {
            string before = t.Substring(0, at).TrimEnd();
            return before.EndsWith("not", StringComparison.OrdinalIgnoreCase) || before.EndsWith("no", StringComparison.OrdinalIgnoreCase);
        }

        // ---- wording

        static List<string> Tokens(string s)
        {
            var list = new List<string>();
            foreach (Match m in Words.Matches(s ?? "")) list.Add(m.Value.ToLowerInvariant());
            return list;
        }

        /// <summary>True when a phrase (words separated by spaces) appears in the token list; at is where it starts.</summary>
        static bool Find(List<string> w, string[] phrases, out int at)
        {
            foreach (var phrase in phrases)
            {
                var p = phrase.Split(' ');
                for (int i = 0; i + p.Length <= w.Count; i++)
                {
                    int k = 0;
                    while (k < p.Length && w[i + k] == p[k]) k++;
                    if (k == p.Length) { at = i; return true; }
                }
            }
            at = -1;
            return false;
        }

        /// <summary>A choosing verb whose doer is the beast, the game or no one named (a passive), in one sentence's words.</summary>
        static bool ClaimsBeastAgency(List<string> w)
        {
            for (int i = 0; i < w.Count; i++)
            {
                string v = w[i];
                if (!ChoiceVerbs.Contains(v)) continue;
                if (v.StartsWith("pick", StringComparison.Ordinal) && i + 1 < w.Count && w[i + 1] == "up") continue;   // picked up speed
                if (By(w, i, BeastSubjects)) return true;                                                             // chosen by the beast

                bool participle = !v.EndsWith("ing", StringComparison.Ordinal) && (v.EndsWith("ed", StringComparison.Ordinal) || v == "chosen");
                bool passive = participle && ((i >= 1 && PassiveAux.Contains(w[i - 1])) || (i >= 2 && PassiveAux.Contains(w[i - 2])));
                if (passive)
                {
                    if (!By(w, i, new HashSet<string> { "you", "yourself" })) return true;   // "the puzzles were chosen to break you"
                    continue;
                }

                // the nearest subject before the verb decides: "you picked" is fair, "the beast ... picked" is not
                for (int j = i - 1; j >= 0; j--)
                {
                    if (w[j] == "you" && !(j >= 1 && (ObjectTakers.Contains(w[j - 1]) || w[j - 1].EndsWith("ed", StringComparison.Ordinal)))) break;
                    if (BeastSubjects.Contains(w[j])) return true;
                }
            }
            return false;
        }

        /// <summary>"by [the|its|a] X" within three words after position i, with X in the set.</summary>
        static bool By(List<string> w, int i, HashSet<string> doers)
        {
            for (int k = i + 1; k <= i + 2 && k < w.Count; k++)
            {
                if (w[k] != "by") continue;
                int n = k + 1;
                if (n < w.Count && (w[n] == "the" || w[n] == "its" || w[n] == "a")) n++;
                return n < w.Count && doers.Contains(w[n]);
            }
            return false;
        }

        /// <summary>The sentence names a console's language that had no wrong answers ("you breezed through German").</summary>
        static bool NamesCleanConsole(string sentence, RunLog log)
        {
            foreach (var c in log.Consoles)
            {
                string name = log.NameOf(c);
                if (c.Wrong > 0 || name == null) continue;
                foreach (Match m in Words.Matches(name)) if (AiText.MentionsAny(sentence, m.Value)) return true;
            }
            return false;
        }

        /// <summary>A word for a wrong answer that no "no", "not", "never" or "without" in the three words before negates.</summary>
        static bool UnnegatedSlip(List<string> w)
        {
            foreach (var phrase in SlipPhrases)
            {
                var p = phrase.Split(' ');
                for (int i = 0; i + p.Length <= w.Count; i++)
                {
                    int k = 0;
                    while (k < p.Length && w[i + k] == p[k]) k++;
                    if (k < p.Length) continue;
                    bool negated = i + p.Length < w.Count && w[i + p.Length] == "free";   // mistake-free
                    for (int j = Math.Max(0, i - 3); j < i && !negated; j++) negated = Negators.Contains(w[j]);
                    if (!negated) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Every language name the reply must not use: the known list, the world's languages and any language the log
        /// hides (demo mode), less the words of the ones it may name.
        /// </summary>
        public static List<string> ForbiddenLanguages(RunLog log, IEnumerable<string> worldLanguages)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in log.Languages)
                foreach (Match m in Words.Matches(l)) allowed.Add(m.Value);
            var all = new List<string>(KnownLanguages);
            var extra = new List<string>(log.HiddenLanguages);
            if (worldLanguages != null) extra.AddRange(worldLanguages);
            foreach (var l in extra)
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

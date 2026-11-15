using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fnmw.Core;

namespace Fnmw.Game.Debrief
{
    public enum RunOutcome { None, Escaped, Caught }

    /// <summary>
    /// What happened in one run, recorded from GameEvents. Pure: the caller passes in the times and language names,
    /// so tests can script a run without a scene. Once the run has ended it ignores everything until Reset.
    /// </summary>
    public sealed class RunLog
    {
        public sealed class ConsoleRecord
        {
            public int Index;              // position on the route, 0-based
            public int PuzzleId;
            public string Language;        // real language name, null when the engine did not say
            public string Concept;
            public float StartedAt;        // run seconds when the console first opened
            public readonly List<string> Missed = new List<string>();   // one per wrong answer, in order
            public readonly List<float> ResponseTimes = new List<float>(); // every submit, right or wrong
            public bool Solved;
            public bool? QuizCorrect;      // null when the quiz was not answered
            public int Wrong => Missed.Count;
        }

        public struct StageChange { public int Stage; public float At; }

        public readonly List<ConsoleRecord> Consoles = new List<ConsoleRecord>();
        public readonly List<StageChange> StageChanges = new List<StageChange>();
        public int HighestStage { get; private set; } = 1;
        public float HighestStageAt { get; private set; } = -1f;   // -1: it never left stage 1
        public RunOutcome Outcome { get; private set; }
        public float Elapsed { get; private set; }
        public int Solved { get; private set; }
        public int Total { get; private set; }
        /// <summary>Caught while a console was open and unsolved: the index of that console, else -1.</summary>
        public int CaughtAtConsole { get; private set; } = -1;

        /// <summary>
        /// Demo mode: the seed is fixed, so naming the language of a console the player has not decoded spoils it for
        /// the next demo run. When set, such a console's language is left out of Languages and Facts, and listed in
        /// HiddenLanguages instead.
        /// </summary>
        public bool HideUndecoded { get; set; }

        public bool Ended => Outcome != RunOutcome.None;
        public int Mistakes { get { int n = 0; foreach (var c in Consoles) n += c.Wrong; return n; } }

        public void Reset()
        {
            Consoles.Clear();
            StageChanges.Clear();
            HighestStage = 1; HighestStageAt = -1f;
            Outcome = RunOutcome.None;
            Elapsed = 0; Solved = 0; Total = 0;
            CaughtAtConsole = -1;
            HideUndecoded = false;
        }

        public void PuzzleStarted(LangPuzzle puzzle, int index, string language, float at)
        {
            if (Ended || puzzle == null) return;
            // walking away and coming back reopens the same console, possibly rebuilt with a new id: keep its history
            var c = Consoles.Find(x => x.Index == index);
            if (c == null)
            {
                c = new ConsoleRecord { Index = index, StartedAt = at };
                Consoles.Add(c);
            }
            c.PuzzleId = puzzle.Id;
            if (!string.IsNullOrWhiteSpace(language)) c.Language = language.Trim();
            if (!string.IsNullOrWhiteSpace(puzzle.Concept)) c.Concept = puzzle.Concept;
        }

        public void Answer(AnswerEvent e)
        {
            if (Ended) return;
            var c = Find(e.PuzzleId);
            if (c == null || c.Solved) return;
            c.ResponseTimes.Add(Math.Max(0f, e.ResponseTime));
            if (e.Correct) c.Solved = true;
            else c.Missed.Add(string.IsNullOrWhiteSpace(e.MissedConcept) ? "unknown" : e.MissedConcept);
        }

        public void Quiz(QuizEvent e)
        {
            if (Ended) return;
            var c = Find(e.PuzzleId);
            if (c == null || c.QuizCorrect.HasValue) return;
            c.QuizCorrect = e.Correct;
            if (string.IsNullOrWhiteSpace(c.Language) && !string.IsNullOrWhiteSpace(e.RealLanguage)) c.Language = e.RealLanguage.Trim();
        }

        public void BeastStage(int stage, float at)
        {
            if (Ended) return;
            StageChanges.Add(new StageChange { Stage = stage, At = at });
            if (stage > HighestStage) { HighestStage = stage; HighestStageAt = at; }
        }

        /// <summary>Close the run. caughtInPuzzle: the player was at an open console when caught.</summary>
        public void End(bool escaped, float elapsed, int solved, int total, bool caughtInPuzzle)
        {
            if (Ended) return;
            Outcome = escaped ? RunOutcome.Escaped : RunOutcome.Caught;
            Elapsed = Math.Max(0f, elapsed);
            Solved = solved; Total = total;
            if (!escaped && caughtInPuzzle && Consoles.Count > 0 && !Consoles[Consoles.Count - 1].Solved)
                CaughtAtConsole = Consoles[Consoles.Count - 1].Index;
        }

        /// <summary>The console's language when the debrief may name it, else null.</summary>
        public string NameOf(ConsoleRecord c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Language)) return null;
            return HideUndecoded && !c.Solved ? null : c.Language;
        }

        /// <summary>Distinct languages of the consoles reached that the debrief may name, in the order met.</summary>
        public List<string> Languages
        {
            get
            {
                var list = new List<string>();
                foreach (var c in Consoles)
                {
                    string name = NameOf(c);
                    if (name != null && !list.Exists(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase))) list.Add(name);
                }
                return list;
            }
        }

        /// <summary>Languages of reached consoles that HideUndecoded keeps out of the debrief (empty when it is off).</summary>
        public List<string> HiddenLanguages
        {
            get
            {
                var named = Languages;
                var list = new List<string>();
                foreach (var c in Consoles)
                    if (NameOf(c) == null && !string.IsNullOrWhiteSpace(c.Language)
                        && !named.Exists(l => string.Equals(l, c.Language, StringComparison.OrdinalIgnoreCase))
                        && !list.Exists(l => string.Equals(l, c.Language, StringComparison.OrdinalIgnoreCase)))
                        list.Add(c.Language);
                return list;
            }
        }

        /// <summary>The console with the most wrong answers (earliest on a tie), or null when there were none.</summary>
        public ConsoleRecord WorstConsole
        {
            get
            {
                ConsoleRecord worst = null;
                foreach (var c in Consoles) if (c.Wrong > 0 && (worst == null || c.Wrong > worst.Wrong)) worst = c;
                return worst;
            }
        }

        /// <summary>The concept missed most often (earliest on a tie), or null.</summary>
        public string MostMissedConcept
        {
            get
            {
                var counts = new Dictionary<string, int>();
                string best = null; int bestN = 0;
                foreach (var c in Consoles)
                    foreach (var m in c.Missed)
                    {
                        counts.TryGetValue(m, out int n);
                        counts[m] = ++n;
                        if (n > bestN) { best = m; bestN = n; }
                    }
                return best;
            }
        }

        ConsoleRecord Find(int puzzleId)
        {
            for (int i = Consoles.Count - 1; i >= 0; i--) if (Consoles[i].PuzzleId == puzzleId) return Consoles[i];
            return null;
        }

        public static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The run as compact facts, one per line: everything the model is allowed to know.</summary>
        public string Facts()
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.Append("outcome: ").Append(Outcome == RunOutcome.Escaped ? "escaped on the ship" : Outcome == RunOutcome.Caught ? "caught by the beast" : "unfinished")
              .Append(" at ").Append(Clock(Elapsed)).Append('\n');
            sb.Append("consoles decoded: ").Append(Solved).Append(" of ").Append(Total).Append('\n');
            sb.Append("wrong answers: ").Append(Mistakes).Append('\n');
            foreach (var c in Consoles)
            {
                sb.Append("console ").Append(c.Index + 1).Append(": ")
                  .Append(NameOf(c) ?? (string.IsNullOrWhiteSpace(c.Language) ? "unknown language" : "undecoded language (do not guess its name)"));
                if (!string.IsNullOrEmpty(c.Concept)) sb.Append(", rule ").Append(Plain(c.Concept));
                sb.Append(", opened at ").Append(Clock(c.StartedAt));
                if (c.Wrong > 0)
                {
                    sb.Append(", ").Append(c.Wrong).Append(" wrong (missed ");
                    for (int i = 0; i < c.Missed.Count; i++) sb.Append(i > 0 ? ", " : "").Append(Plain(c.Missed[i]));
                    sb.Append(')');
                }
                if (c.Solved)
                {
                    float total = 0; foreach (var t in c.ResponseTimes) total += t;
                    sb.Append(c.Wrong == 0 ? ", solved first try" : ", then solved");
                    if (c.ResponseTimes.Count > 0) sb.Append(" after ").Append(Math.Round(c.ResponseTimes[c.ResponseTimes.Count - 1]).ToString(inv)).Append(" s");
                }
                else sb.Append(c.Index == CaughtAtConsole ? ", not solved: caught at this console" : ", not solved");
                if (c.QuizCorrect.HasValue) sb.Append(c.QuizCorrect.Value ? ", named the language in the quiz" : ", guessed the language wrong in the quiz");
                sb.Append('\n');
            }
            sb.Append("beast: highest stage ").Append(HighestStage).Append(" of 3");
            if (HighestStageAt >= 0) sb.Append(", reached at ").Append(Clock(HighestStageAt));
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>A concept tag in words: "verb-final" -> "verb final".</summary>
        public static string Plain(string concept) => string.IsNullOrEmpty(concept) ? concept : concept.Replace('-', ' ');
    }
}

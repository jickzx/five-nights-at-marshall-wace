using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// Finds a small, clear puzzle to solve in front of the player at the first console. It asks the language
    /// engine (through ILanguageEngine only) for easy puzzles from other seeds and keeps the first that:
    ///   - is in a language none of this run's consoles use, so it gives nothing away;
    ///   - has at least two examples and a 2 to 4 tile answer, from at most 5 tiles, every tile glossed;
    ///   - teaches a rule you can read off the examples (pick-one-of-two puzzles make a poor first lesson).
    /// Concepts are tried in order of how well they read in a few seconds: compounds first.
    /// </summary>
    public static class WorkedExampleBuilder
    {
        public const int FirstSeed = 9001, Seeds = 24;
        public const float Difficulty = 0.05f;

        static readonly string[] Preference =
        {
            LangConcepts.CompoundMeaning, LangConcepts.SuffixOrder, LangConcepts.MeasureWord,
            LangConcepts.AdjectiveOrder, LangConcepts.VerbFinal, LangConcepts.VerbEnding,
        };

        public static WorkedExample Build(ILanguageEngine engine, LanguageWorld run, int played)
        {
            if (engine == null) return null;
            var avoidLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var avoidTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (run != null)
            {
                for (int i = 0; i < Mathf.Min(played, run.Languages.Count); i++) if (run.Languages[i] != null) avoidLangs.Add(run.Languages[i].Name);
                for (int i = 0; i < Mathf.Min(played, run.Puzzles.Count); i++) if (run.Puzzles[i] != null) avoidTargets.Add(run.Puzzles[i].TargetMeaning ?? "");
            }

            var found = new List<(int rank, WorkedExample ex)>();
            for (int s = 0; s < Seeds; s++)
            {
                LanguageWorld w;
                try { w = engine.Generate(FirstSeed + s); } catch (Exception ex) { Debug.LogException(ex); continue; }
                if (w?.Puzzles == null) continue;
                for (int i = 0; i < w.Puzzles.Count; i++)
                {
                    string lang = i < w.Languages.Count ? w.Languages[i]?.Name : null;
                    if (lang != null && avoidLangs.Contains(lang)) continue;
                    LangPuzzle p;
                    try { p = engine.GetPuzzle(w, i, Difficulty) ?? w.Puzzles[i]; } catch (Exception ex) { Debug.LogException(ex); continue; }
                    int rank = Array.IndexOf(Preference, p.Concept);
                    if (rank < 0 || !Usable(p) || avoidTargets.Contains(p.TargetMeaning ?? "")) continue;
                    var answer = Solve(engine, w, p);
                    if (answer == null || answer.Length < 2) continue;
                    RevealInfo r = null;
                    try { r = engine.GetReveal(w, p.Id); } catch (Exception ex) { Debug.LogException(ex); }
                    if (r == null || avoidLangs.Contains(r.LanguageName ?? "")) continue;
                    found.Add((rank, new WorkedExample
                    {
                        Puzzle = p, Answer = answer, LanguageName = r.LanguageName ?? lang,
                        Rule = string.IsNullOrEmpty(r.Feature) ? r.Explanation : r.Feature,
                        RealText = r.RealText, Translation = r.Translation,
                    }));
                    if (rank == 0) return found[found.Count - 1].ex;
                }
            }
            return found.Count == 0 ? null : found.OrderBy(f => f.rank).First().ex;
        }

        static bool Usable(LangPuzzle p) =>
            p != null && p.Examples != null && p.Examples.Count >= 2 && p.Tiles != null && p.Tiles.Count >= 2 && p.Tiles.Count <= 5
            && p.Tiles.All(t => t != null && !string.IsNullOrEmpty(t.Gloss) && !string.IsNullOrEmpty(t.Alien));

        /// <summary>Shortest tile order the engine accepts (at most 4 tiles), or null.</summary>
        public static int[] Solve(ILanguageEngine engine, LanguageWorld w, LangPuzzle p)
        {
            int n = p.Tiles.Count;
            var cur = new List<int>();
            for (int len = 1; len <= Mathf.Min(4, n); len++)
            {
                var hit = Search(engine, w, p, cur, len);
                if (hit != null) return hit;
            }
            return null;
        }

        static int[] Search(ILanguageEngine engine, LanguageWorld w, LangPuzzle p, List<int> cur, int len)
        {
            if (cur.Count == len)
            {
                try { return engine.Check(w, p.Id, cur.Select(i => p.Tiles[i].Id).ToArray())?.Correct == true ? cur.ToArray() : null; }
                catch { return null; }
            }
            for (int i = 0; i < p.Tiles.Count; i++)
            {
                if (cur.Contains(i)) continue;
                cur.Add(i);
                var hit = Search(engine, w, p, cur, len);
                cur.RemoveAt(cur.Count - 1);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}

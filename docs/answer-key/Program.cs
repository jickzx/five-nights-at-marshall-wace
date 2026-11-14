// Answer key printer. Usage: dotnet run -- [seed ...] [--consoles N] [--all-tiers]
// With no seed it prints the demo seed 4242. It compiles the game's own LanguageEngine, so the
// output is exactly what the game generates for that seed.
using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using Fnmw.Lang;

namespace Fnmw.Core
{
    // Copy of the one interface the engine implements (the real file also holds Unity types).
    public interface ILanguageEngine
    {
        LanguageWorld Generate(int seed);
        LangPuzzle GetPuzzle(LanguageWorld world, int index, float difficulty);
        AnswerResult Check(LanguageWorld world, int puzzleId, string[] tileIds);
        RevealInfo GetReveal(LanguageWorld world, int puzzleId);
        QuizQuestion GetQuiz(LanguageWorld world, int puzzleId);
        bool CheckQuiz(LanguageWorld world, int puzzleId, int optionIndex);
    }
}

static class Program
{
    // Difficulty the beast's director asks for on a fresh machine, before any answer
    // (PlayerModel.DifficultyFor with default priors: (2 - sqrt(1 + pi*1.3/8) * logit(0.7)) / 3).
    static readonly float FreshDifficulty = (float)((2.0 - Math.Sqrt(1 + Math.PI * 1.3 / 8) * Math.Log(0.7 / 0.3)) / 3.0);

    static int Main(string[] args)
    {
        var seeds = new List<int>();
        int consoles = 3; bool allTiers = false, catalog = false; string find = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--consoles") consoles = int.Parse(args[++i]);
            else if (args[i] == "--all-tiers") allTiers = true;
            else if (args[i] == "--catalog") catalog = true;   // one example of every puzzle kind of every language
            else if (args[i] == "--find") find = args[++i];   // e.g. --find Arabic,German,Japanese
            else seeds.Add(int.Parse(args[i]));
        }
        if (find != null) return Find(find.Split(','));
        if (catalog) return Catalog();
        if (seeds.Count == 0) seeds.Add(4242);

        var eng = new LanguageEngine();
        foreach (var seed in seeds)
        {
            var w = eng.Generate(seed);
            Console.WriteLine($"# Seed {seed}");
            Console.WriteLine($"Languages in order (the world has {w.Puzzles.Count} puzzles; the shore has 3 consoles, so only the first {Math.Min(consoles, w.Puzzles.Count)} are played): {string.Join(", ", w.Languages.Select(l => l.Name))}");
            Console.WriteLine($"Fresh-machine director difficulty: {FreshDifficulty:0.000} (demo mode caps it at 0.4)\n");
            for (int i = 0; i < Math.Min(consoles, w.Puzzles.Count); i++)
            {
                // Group requested difficulties by the puzzle they produce (the engine caches by tier/distractors/drop).
                var groups = new List<(float lo, float hi, LangPuzzle p)>();
                for (int k = 0; k <= 100; k++)
                {
                    float d = k / 100f;
                    var p = eng.GetPuzzle(w, i, d);
                    if (groups.Count > 0 && ReferenceEquals(groups[^1].p, p)) groups[^1] = (groups[^1].lo, d, p);
                    else groups.Add((d, d, p));
                }
                var fresh = eng.GetPuzzle(w, i, FreshDifficulty);
                foreach (var (lo, hi, p) in groups)
                {
                    bool isFresh = ReferenceEquals(p, fresh);
                    if (!allTiers && !isFresh) continue;
                    eng.GetPuzzle(w, i, lo);   // puzzle ids are per console, so make this version the current one
                    Print(eng, w, i, p, lo, hi, isFresh);
                }
                if (!allTiers && groups.Count > 1)
                    Console.WriteLine($"  ({groups.Count - 1} other version(s) of console {i + 1} exist at other difficulties: run with --all-tiers)\n");
            }
        }
        return 0;
    }

    static void Print(LanguageEngine eng, LanguageWorld w, int i, LangPuzzle p, float lo, float hi, bool fresh)
    {
        var b = LanguageEngine.Inspect(w, p.Id);
        var reveal = eng.GetReveal(w, p.Id);
        var quiz = eng.GetQuiz(w, p.Id);
        float eff = eng.Effective(i, w.Puzzles.Count, lo);
        Console.WriteLine($"## Console {i + 1}: {b.Language.Name} ({p.AlienName}), kind '{b.Draft.Kind}', tier {LanguageEngine.TierFor(eff)}");
        Console.WriteLine($"   served when the director asks for difficulty {lo:0.00}-{hi:0.00}{(fresh ? "  <- what a fresh run gets" : "")}");
        Console.WriteLine($"   Target: \"{p.TargetMeaning}\"");
        foreach (var e in p.Examples)
            Console.WriteLine($"   Example: {e.Picture,-45} = {string.Join(" ", e.Words.Select(t => t.Alien))}");
        // tiles in the order the player sees them; key number = position (1-based)
        var ids = p.Tiles.Select(t => t.Id).ToList();
        Console.WriteLine("   Tiles:   " + string.Join("   ", p.Tiles.Select((t, k) => $"[{k + 1}] {t.Alien} ({t.Gloss})")));
        var keys = b.AnswerIds.Select(id => ids.IndexOf(id) + 1);
        Console.WriteLine($"   ANSWER:  {string.Join(" ", b.AnswerIds.Select(id => b.TileById[id].Alien))}   -> press {string.Join(", ", keys)} then Enter");
        if (b.AcceptedKeys.Count > 1) Console.WriteLine($"   Also accepted: {string.Join(" | ", b.AcceptedKeys.Where(k => k != b.AnswerKey))}");
        Console.WriteLine($"   Real:    {reveal.RealText} = {reveal.Translation}");
        Console.WriteLine($"   Teaches: {reveal.Feature}");
        Console.WriteLine($"   Fact:    {reveal.NowYouKnow}");
        var q = LanguageEngine.DebugQuizAnswer(w, p.Id);
        Console.WriteLine($"   Quiz:    {string.Join(" / ", quiz.Options.Select((o, k) => k == q ? $"[{k + 1}] *{o}*" : $"[{k + 1}] {o}"))}");
        Console.WriteLine($"   Brute-force check: {b.ValidAnswerCount} valid tile sequence(s) out of every ordering of the {p.Tiles.Count} tiles\n");
    }

    static int Catalog()
    {
        var eng = new LanguageEngine();
        var seen = new HashSet<string>();
        int total = LanguageEngine.AllLanguages().Sum(l => l.Kinds.Count);
        for (int s = 1; s < 400 && seen.Count < total; s++)
        {
            var w = eng.Generate(s);
            for (int i = 0; i < w.Puzzles.Count; i++)
                foreach (var d in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    var p = eng.GetPuzzle(w, i, d);
                    var b = LanguageEngine.Inspect(w, p.Id);
                    if (!seen.Add(b.Language.Name + "/" + b.Draft.Kind)) continue;
                    Console.WriteLine($"(seed {s}, console {i + 1}, requested difficulty {d:0.00})");
                    Print(eng, w, i, p, d, d, false);
                }
        }
        Console.WriteLine($"{seen.Count} of {total} puzzle kinds shown");
        return 0;
    }

    static int Find(string[] langs)
    {
        var eng = new LanguageEngine();
        for (int s = 1; s < 200000; s++)
        {
            var w = eng.Generate(s);
            if (Enumerable.Range(0, langs.Length).All(i => w.Languages[i].Name.StartsWith(langs[i]))) { Console.WriteLine(s); return 0; }
        }
        return 1;
    }
}

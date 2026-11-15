// Puzzle exporter for the beast lab. Usage: dotnet run -c Release -- <out.jsonl> [perKind]
// For every language and puzzle kind it builds `perKind` puzzles with the game's own PuzzleBuilder
// (so each one passed the uniqueness check) and writes one JSON object per line:
//   tiles (id, morphs, text), accepted answer keys, and every rival grammar h with the answer key it
//   produces, the concepts a player believing h has wrong, and whether h survives the examples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    static string Key(IEnumerable<Word> ws) => PuzzleBuilder.Key(ws);

    /// <summary>Concepts a player holding rival h has wrong. Hyps.Bits arrays are read bit by bit.</summary>
    static string[] Wrong(string[] hc, int h)
    {
        if (h == 0) return Array.Empty<string>();
        int n = hc.Length;
        bool bits = (n & (n - 1)) == 0;
        if (!bits) return new[] { hc[h] };
        var r = new List<string>();
        for (int b = 0; (1 << b) < n; b++)
            if ((h & (1 << b)) != 0 && hc[1 << b] != null) r.Add(hc[1 << b]);
        return r.Distinct().ToArray();
    }

    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "puzzles.jsonl";
        int perKind = args.Length > 1 ? int.Parse(args[1]) : 200;
        int written = 0, failed = 0;
        using var w = new StreamWriter(outPath);
        foreach (var lang in LanguageEngine.AllLanguages())
        {
            foreach (var kind in lang.Kinds)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < perKind * 4 && seen.Count < perKind; i++)
                {
                    var rng = new Rng(Hash.Mix(Hash.Fnv(lang.Code), Hash.Fnv(kind.Name), i));
                    int distractors = 1 + i % 3;
                    bool drop = i % 5 == 4;
                    var d = kind.Build(rng, kind.Tier);
                    d.Kind = kind.Name;
                    var b = PuzzleBuilder.Build(lang, d, rng, 1, kind.Tier / 3f, distractors, drop, out _);
                    if (b == null) { failed++; continue; }
                    var sig = string.Join("|", b.Puzzle.Tiles.Select(t => t.Id).OrderBy(x => x)) + "#" + b.AnswerKey;
                    if (!seen.Add(sig)) continue;

                    var shown = b.Puzzle.Examples.Count;
                    var hyps = new List<object>();
                    for (int h = 0; h < d.HypConcepts.Length; h++)
                    {
                        hyps.Add(new
                        {
                            h,
                            key = Key(d.Target(h)),
                            wrong = Wrong(d.HypConcepts, h),
                        });
                    }
                    var rec = new
                    {
                        lang = lang.Code,
                        kind = kind.Name,
                        tier = kind.Tier,
                        concept = d.Concept,
                        concepts = b.Puzzle.Concepts,
                        orderConcept = d.OrderConcept ?? d.Concept,
                        examples = shown,
                        target = d.TargetMeaning,
                        tiles = b.Puzzle.Tiles.Select(t => new { id = t.Id, morphs = b.MorphsById[t.Id], text = t.Alien, concepts = t.Concepts }),
                        answer = b.AnswerIds,
                        accepted = b.AcceptedKeys.ToArray(),
                        hyps,
                        mistakes = b.RivalKeys.Select(kv => new { key = kv.Key, concept = kv.Value }),
                    };
                    w.WriteLine(JsonSerializer.Serialize(rec));
                    written++;
                }
            }
        }
        Console.Error.WriteLine($"wrote {written} puzzles to {outPath} ({failed} drafts failed the uniqueness check, as in the game)");
        return 0;
    }
}

using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Arabic, transliterated: many words are a three-consonant root (the meaning) set into a vowel pattern (the role):
    /// "he did it", "one who does it", "place of doing it". Every word shown is a real word from the table below.
    /// </summary>
    public sealed class Arabic : SourceLanguage
    {
        public override string Name => "Arabic";
        public override string Code => "ar";
        public override string Endonym => "al-ʿArabiyya";
        public override string Summary => "Meaning lives in 3-consonant roots; vowel patterns around them make 'he did', 'one who does', 'place of'.";
        public override string QuizExplanation => "Arabic: words are built from three-consonant roots set into vowel patterns, like k-t-b in kataba 'he wrote', kātib 'writer' and maktab 'office'.";
        public override string[] QuizDistractors => new[] { "Hebrew", "Persian", "Amharic", "Urdu" };

        // the real patterns, C = a root consonant
        static readonly (string key, string shape, string gloss)[] Patterns =
        {
            ("PERF", "CaCaCa", "he did"),        // kataba
            ("AGENT", "CāCiC", "one who does"),  // kātib
            ("PLACE", "maCCaC", "place of"),     // maktab
        };

        sealed class Entry { public string Root, Pattern, Ar, Tr, En; }
        // only real words, and only where the word really has this pattern
        static readonly Entry[] Words =
        {
            new Entry { Root = "ktb", Pattern = "PERF", Ar = "كتب", Tr = "kataba", En = "he wrote" },
            new Entry { Root = "ktb", Pattern = "AGENT", Ar = "كاتب", Tr = "kātib", En = "writer (one who writes)" },
            new Entry { Root = "ktb", Pattern = "PLACE", Ar = "مكتب", Tr = "maktab", En = "office (a place for writing)" },
            new Entry { Root = "qtl", Pattern = "PERF", Ar = "قتل", Tr = "qatala", En = "he killed" },
            new Entry { Root = "qtl", Pattern = "AGENT", Ar = "قاتل", Tr = "qātil", En = "killer (one who kills)" },
            new Entry { Root = "qtl", Pattern = "PLACE", Ar = "مقتل", Tr = "maqtal", En = "place of killing" },
            new Entry { Root = "skn", Pattern = "PERF", Ar = "سكن", Tr = "sakana", En = "he dwelt" },
            new Entry { Root = "skn", Pattern = "AGENT", Ar = "ساكن", Tr = "sākin", En = "inhabitant (one who dwells)" },
            new Entry { Root = "skn", Pattern = "PLACE", Ar = "مسكن", Tr = "maskan", En = "dwelling (a place for living)" },
            new Entry { Root = "lb", Pattern = "AGENT", Ar = "لاعب", Tr = "lāʿib", En = "player (one who plays)" },
            new Entry { Root = "lb", Pattern = "PLACE", Ar = "ملعب", Tr = "malʿab", En = "stadium (a place for playing)" },
            new Entry { Root = "rkb", Pattern = "AGENT", Ar = "راكب", Tr = "rākib", En = "rider (one who rides)" },
            new Entry { Root = "rkb", Pattern = "PLACE", Ar = "مركب", Tr = "markab", En = "boat (a thing to ride in)" },
            new Entry { Root = "drs", Pattern = "PERF", Ar = "درس", Tr = "darasa", En = "he studied" },
            new Entry { Root = "drs", Pattern = "AGENT", Ar = "دارس", Tr = "dāris", En = "learner (one who studies)" },
            new Entry { Root = "jls", Pattern = "PERF", Ar = "جلس", Tr = "jalasa", En = "he sat" },
        };
        static readonly Dictionary<string, string> RootGloss = new Dictionary<string, string>
        {
            { "ktb", "k-t-b (write)" }, { "qtl", "q-t-l (kill)" }, { "skn", "s-k-n (dwell)" }, { "lb", "l-ʿ-b (play)" },
            { "rkb", "r-k-b (ride)" }, { "drs", "d-r-s (study)" }, { "jls", "j-l-s (sit)" },
        };

        static readonly string[] Roots = { "ktb", "qtl", "skn", "lb", "rkb", "drs", "jls" };

        public Arabic()
        {
            foreach (var r in Roots) Add("r." + r, MorphKind.Root3, RootGloss[r].Split(' ')[0], RootGloss[r], LangConcepts.ConsonantRoot);
            foreach (var (key, shape, gloss) in Patterns) Add("p." + key, MorphKind.Pattern, shape, gloss, LangConcepts.VowelPattern);


            Kind("root-pattern", 0, (r, t) => RootPattern(r, false));
            Kind("root-pattern-lean", 2, (r, t) => RootPattern(r, true));
        }

        /// <summary>A root in a pattern is looked up, never generated, so only real words appear.</summary>
        public override string Surface(Word w) =>
            w.M.Length == 2 ? (Find(w.M[0].Substring(2), w.M[1].Substring(2)) ?? throw new KeyNotFoundException($"Arabic: no real word for {w.Id}")).Tr
                            : base.Surface(w);

        static Entry Find(string root, string pattern) => Words.FirstOrDefault(e => e.Root == root && e.Pattern == pattern);
        static Word Wd(string root, string pattern) => W("r." + root, "p." + pattern);
        // reading first, script in brackets: Lore.SplitReal swaps them, and UI/Rtl.cs joins the letters
        static string Real(Entry e) => $"{e.Tr} ({e.Ar})";

        /// <summary>
        /// Full: examples R1P1, R1P2, R2P1, target R2P2. Lean (harder): examples R1P2, R2P3, target R1P3.
        /// </summary>
        Draft RootPattern(Rng rng, bool lean)
        {
            var roots = Roots.ToList();
            var pats = Patterns.Select(p => p.key).ToList();
            while (true)
            {
                var rs = rng.Sample(roots, 2);
                var ps = rng.Sample(pats, 3);
                string r1 = rs[0], r2 = rs[1], p1 = ps[0], p2 = ps[1], p3 = ps[2];
                (string root, string pat)[] ex;
                (string root, string pat) tgt;
                if (lean) { ex = new[] { (r1, p2), (r2, p3) }; tgt = (r1, p3); }
                else { ex = new[] { (r1, p1), (r1, p2), (r2, p1) }; tgt = (r2, p2); }
                var target = Find(tgt.root, tgt.pat);
                var examples = ex.Select(x => Find(x.root, x.pat)).ToList();
                if (target == null || examples.Any(e => e == null)) continue;

                var place = Words.Where(w => w.Pattern == "PLACE").Take(3).ToList();
                var ktb = Words.Where(w => w.Root == "ktb").ToList();
                var d = new Draft
                {
                    Concept = LangConcepts.ConsonantRoot,
                    Concepts = new[] { LangConcepts.VowelPattern },
                    HypConcepts = new string[] { null },
                    TargetMeaning = Cap(target.En),
                    Target = h => P(Wd(target.Root, target.Pattern)),
                    RealText = Real(target),
                    Translation = target.En,
                    Feature = $"Arabic builds words from a root of three consonants, the meaning, set into a vowel pattern, the role: k-t-b 'writing' gives {string.Join(", ", ktb.Select(e => $"{e.Tr} '{Short(e.En)}'"))}.",
                    NowYouKnow = $"The pattern ma-CC-a-C means 'place of': {string.Join(", ", place.Select(e => $"{e.Tr} '{Short(e.En)}' ({RootGloss[e.Root].Split(' ')[0]})"))}.",
                };
                foreach (var e in examples) d.Example(Cap(e.En), h => P(Wd(e.Root, e.Pattern)));
                // wrong root with the right pattern, right root with the wrong pattern: only words that really exist
                foreach (var r in roots.Where(x => x != tgt.root && Find(x, tgt.pat) != null)) d.Mistake(P(Wd(r, tgt.pat)), LangConcepts.ConsonantRoot);
                foreach (var p in pats.Where(x => x != tgt.pat && Find(tgt.root, x) != null)) d.Mistake(P(Wd(tgt.root, p)), LangConcepts.VowelPattern);
                return d;
            }
        }

        static string Short(string en) { int i = en.IndexOf(" ("); return i < 0 ? en : en.Substring(0, i); }
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

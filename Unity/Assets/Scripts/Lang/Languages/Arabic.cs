using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Arabic (Modern Standard), transliterated: many words are a three-consonant root (the meaning) set into a
    /// vowel pattern (the role): "he did it", "one who does it", "place of doing it". Puzzles are short sentences
    /// ("jalasa al-kātib fī al-maktab", the writer sat in the office) built only from real words in the table below.
    /// Case endings are left off, as in unvowelled writing and pause; nouns carry al- (with sun-letter assimilation).
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

        // En: verb = English past; agent/place = English noun. Prep: how English says "fī" with this place.
        sealed class Entry { public string Root, Pattern, Ar, Tr, En, Prep; }
        // only real words, and only where the word really has this pattern
        static readonly Entry[] Words =
        {
            new Entry { Root = "ktb", Pattern = "PERF", Ar = "كتب", Tr = "kataba", En = "wrote" },
            new Entry { Root = "ktb", Pattern = "AGENT", Ar = "كاتب", Tr = "kātib", En = "writer" },
            new Entry { Root = "ktb", Pattern = "PLACE", Ar = "مكتب", Tr = "maktab", En = "office", Prep = "in" },
            new Entry { Root = "qtl", Pattern = "PERF", Ar = "قتل", Tr = "qatala", En = "killed" },
            new Entry { Root = "qtl", Pattern = "AGENT", Ar = "قاتل", Tr = "qātil", En = "killer" },
            new Entry { Root = "skn", Pattern = "PERF", Ar = "سكن", Tr = "sakana", En = "lived" },
            new Entry { Root = "skn", Pattern = "AGENT", Ar = "ساكن", Tr = "sākin", En = "tenant" },
            new Entry { Root = "skn", Pattern = "PLACE", Ar = "مسكن", Tr = "maskan", En = "home", Prep = "in" },
            new Entry { Root = "lb", Pattern = "AGENT", Ar = "لاعب", Tr = "lāʿib", En = "player" },
            new Entry { Root = "lb", Pattern = "PLACE", Ar = "ملعب", Tr = "malʿab", En = "stadium", Prep = "in" },
            new Entry { Root = "rkb", Pattern = "AGENT", Ar = "راكب", Tr = "rākib", En = "rider" },
            new Entry { Root = "rkb", Pattern = "PLACE", Ar = "مركب", Tr = "markab", En = "boat", Prep = "on" },
            new Entry { Root = "drs", Pattern = "PERF", Ar = "درس", Tr = "darasa", En = "studied" },
            new Entry { Root = "drs", Pattern = "AGENT", Ar = "دارس", Tr = "dāris", En = "student" },
            new Entry { Root = "jls", Pattern = "PERF", Ar = "جلس", Tr = "jalasa", En = "sat" },
            new Entry { Root = "tbkh", Pattern = "PERF", Ar = "طبخ", Tr = "ṭabakha", En = "cooked" },
            new Entry { Root = "tbkh", Pattern = "PLACE", Ar = "مطبخ", Tr = "maṭbakh", En = "kitchen", Prep = "in" },
            new Entry { Root = "dkhl", Pattern = "PERF", Ar = "دخل", Tr = "dakhala", En = "entered" },
            new Entry { Root = "dkhl", Pattern = "PLACE", Ar = "مدخل", Tr = "madkhal", En = "entrance", Prep = "at" },
            new Entry { Root = "khrj", Pattern = "PERF", Ar = "خرج", Tr = "kharaja", En = "left" },
            new Entry { Root = "khrj", Pattern = "PLACE", Ar = "مخرج", Tr = "makhraj", En = "exit", Prep = "at" },
        };
        static readonly Dictionary<string, string> RootGloss = new Dictionary<string, string>
        {
            { "ktb", "k-t-b (write)" }, { "qtl", "q-t-l (kill)" }, { "skn", "s-k-n (dwell)" }, { "lb", "l-ʿ-b (play)" },
            { "rkb", "r-k-b (ride)" }, { "drs", "d-r-s (study)" }, { "jls", "j-l-s (sit)" }, { "tbkh", "ṭ-b-kh (cook)" },
            { "dkhl", "d-kh-l (enter)" }, { "khrj", "kh-r-j (go out)" },
        };

        static readonly string[] Roots = { "ktb", "qtl", "skn", "lb", "rkb", "drs", "jls", "tbkh", "dkhl", "khrj" };

        // which places each intransitive verb sensibly takes with fī
        static readonly Dictionary<string, string[]> VerbPlaces = new Dictionary<string, string[]>
        {
            { "jls", new[] { "ktb", "lb", "rkb", "tbkh", "dkhl", "khrj", "skn" } },
            { "ktb", new[] { "ktb", "rkb", "skn" } },
            { "drs", new[] { "ktb", "skn", "rkb" } },
            { "skn", new[] { "skn", "rkb" } },
            { "tbkh", new[] { "tbkh", "rkb", "skn" } },
        };
        static readonly string[] Enterable = { "ktb", "lb", "rkb", "tbkh", "skn" };   // dakhala X, kharaja min X

        public Arabic()
        {
            foreach (var r in Roots) Add("r." + r, MorphKind.Root3, RootGloss[r].Split(' ')[0], RootGloss[r], LangConcepts.ConsonantRoot);
            foreach (var (key, shape, gloss) in Patterns) Add("p." + key, MorphKind.Pattern, shape, gloss, LangConcepts.VowelPattern);
            Add("fi", MorphKind.Particle, "fī", "in");
            Add("min", MorphKind.Particle, "min", "from");

            BuildPool();
            Kind("root-pattern", 0, (r, t) => Make(r, Sentence.Where, false, false));
            Kind("root-pattern-from", 1, (r, t) => Make(r, Sentence.From, false, false));
            Kind("root-pattern-lean", 2, (r, t) => Make(r, Sentence.Where, true, false));
            Kind("root-pattern-from-lean", 3, (r, t) => Make(r, Sentence.From, true, false));
        }

        /// <summary>
        /// A root in a pattern is looked up, never generated, so only real words appear. Nouns (every non-verb
        /// pattern) are definite: al-, assimilated before a sun letter (as-sākin, ar-rākib, ad-dāris).
        /// </summary>
        public override string Surface(Word w) =>
            w.M.Length == 2 ? Tr(Find(w.M[0].Substring(2), w.M[1].Substring(2)) ?? throw new KeyNotFoundException($"Arabic: no real word for {w.Id}"))
                            : base.Surface(w);

        static Entry Find(string root, string pattern) => Words.FirstOrDefault(e => e.Root == root && e.Pattern == pattern);
        static Entry FindW(Word w) => w.M.Length == 2 ? Find(w.M[0].Substring(2), w.M[1].Substring(2)) : null;
        static Word Wd(string root, string pattern) => W("r." + root, "p." + pattern);

        const string SunLetters = "tdrzsṣḍṭẓln";
        static bool Noun(Entry e) => e.Pattern != "PERF";
        static string Tr(Entry e) => !Noun(e) ? e.Tr : SunLetters.IndexOf(e.Tr[0]) >= 0 ? "a" + e.Tr[0] + "-" + e.Tr : "al-" + e.Tr;
        static string Ar(Entry e) => Noun(e) ? "ال" + e.Ar : e.Ar;

        // ---- the sentence pool: every sentence a puzzle may show
        enum Sentence { Where, From, Is, Enter, Bare, Kill, Via }
        sealed class Sent { public Sentence Type; public List<Word> Words; public string En; public HashSet<string> Morphs; public ulong Mask; }
        readonly List<Sent> _pool = new List<Sent>();

        static Entry Agent(string r) => Find(r, "AGENT");
        static Entry Place(string r) => Find(r, "PLACE");
        static Entry Verb(string r) => Find(r, "PERF");

        void BuildPool()
        {
            var agents = Words.Where(e => e.Pattern == "AGENT").ToList();
            var places = Words.Where(e => e.Pattern == "PLACE").ToList();
            var bit = Inventory.Select((m, i) => (m.Key, i)).ToDictionary(x => x.Key, x => 1UL << x.i);
            void Put(Sentence t, string en, params Word[] ws) =>
                _pool.Add(new Sent { Type = t, Words = ws.ToList(), En = en, Morphs = new HashSet<string>(ws.SelectMany(w => w.M)),
                                     Mask = ws.SelectMany(w => w.M).Aggregate(0UL, (m, k) => m | bit[k]) });
            Word Wn(Entry e) => Wd(e.Root, e.Pattern);
            foreach (var s in agents)
            {
                foreach (var kv in VerbPlaces)
                {
                    var v = Verb(kv.Key);
                    foreach (var pr in kv.Value)
                    {
                        var p = Place(pr);
                        Put(Sentence.Where, $"The {s.En} {v.En} {p.Prep} the {p.En}.", Wn(v), Wn(s), W("fi"), Wn(p));
                    }
                    Put(Sentence.Bare, $"The {s.En} {v.En}.", Wn(v), Wn(s));
                }
                Put(Sentence.Bare, $"The {s.En} came in.", Wn(Verb("dkhl")), Wn(s));
                Put(Sentence.Bare, $"The {s.En} went out.", Wn(Verb("khrj")), Wn(s));
                Put(Sentence.Via, $"The {s.En} came in the entrance.", Wn(Verb("dkhl")), Wn(s), W("min"), Wn(Place("dkhl")));
                Put(Sentence.Via, $"The {s.En} left by the exit.", Wn(Verb("khrj")), Wn(s), W("min"), Wn(Place("khrj")));
                foreach (var p in places) Put(Sentence.Is, $"The {s.En} is {p.Prep} the {p.En}.", Wn(s), W("fi"), Wn(p));
                foreach (var pr in Enterable)
                {
                    var p = Place(pr);
                    Put(Sentence.Enter, $"The {s.En} entered the {p.En}.", Wn(Verb("dkhl")), Wn(s), Wn(p));
                    Put(Sentence.From, $"The {s.En} left the {p.En}.", Wn(Verb("khrj")), Wn(s), W("min"), Wn(p));
                }
                foreach (var o in agents.Where(o => o != s))
                    Put(Sentence.Kill, $"The {s.En} killed the {o.En}.", Wn(Verb("qtl")), Wn(s), Wn(o));
            }
            _pool.RemoveAll(s => s.Type == Sentence.Bare && s.Words[0].M[0] == "r.skn");   // bare "sakana" means "calmed down"
        }

        static string Real(Sent s) =>
            // reading first, script in brackets: Lore.SplitReal swaps them, and UI/Rtl.cs joins the letters
            $"{string.Join(" ", s.Words.Select(w => FindW(w) is Entry e ? Tr(e) : w.M[0] == "fi" ? "fī" : "min"))} " +
            $"({string.Join(" ", s.Words.Select(w => FindW(w) is Entry e ? Ar(e) : w.M[0] == "fi" ? "في" : "من"))})";

        /// <summary>
        /// Target: a verb-first sentence (verb, subject, fī/min, place). Hints: three other real sentences that,
        /// compared with each other, pin down every root and every pattern of the target, so the target's nouns
        /// (often never seen as whole words) are derived as root + pattern. Lean: no word of the target is shown
        /// whole in a hint (lean-all: not even the verb); every word must be assembled from a root seen elsewhere.
        /// </summary>
        Draft Make(Rng rng, Sentence type, bool lean, bool allLean = false)
        {
            var targets = _pool.Where(s => s.Type == type).ToList();
            rng.Shuffle(targets);
            foreach (var tgt in targets)
            {
                var tWords = new HashSet<string>(tgt.Words.Where(w => w.M.Length == 2 && (allLean || Noun(FindW(w)))).Select(w => w.Id));
                var sols = Solutions(tgt).Where(x => !lean || x.All(i => !_pool[i].Words.Any(w => tWords.Contains(w.Id)))).ToList();
                if (sols.Count == 0) continue;
                return Draft(tgt, rng.Pick(sols).Select(i => _pool[i]).ToList());
            }
            return Draft(targets[0], _pool.Take(3).ToList());   // PuzzleBuilder rejects it and the engine retries
        }

        readonly Dictionary<Sent, List<int[]>> _solutions = new Dictionary<Sent, List<int[]>>();

        /// <summary>
        /// Every hint triple that passes PuzzleBuilder.AllDeducible for this target, by bitmask: a target morph is
        /// deducible exactly when no other morph appears in the same subset of the three hints.
        /// </summary>
        List<int[]> Solutions(Sent tgt)
        {
            if (_solutions.TryGetValue(tgt, out var have)) return have;
            ulong t = tgt.Mask;
            var c = Enumerable.Range(0, _pool.Count).Where(i => _pool[i] != tgt && (_pool[i].Mask & t) != 0 && (t & ~_pool[i].Mask) != 0).ToArray();
            var sols = new List<int[]>();
            for (int x = 0; x < c.Length; x++)
            for (int y = x + 1; y < c.Length; y++)
            for (int z = y + 1; z < c.Length; z++)
            {
                ulong a = _pool[c[x]].Mask, b = _pool[c[y]].Mask, d = _pool[c[z]].Mask;
                if ((t & ~(a | b | d)) != 0) continue;
                if (Bad(a & ~b & ~d, t) || Bad(~a & b & ~d, t) || Bad(~a & ~b & d, t) || Bad(a & b & ~d, t) ||
                    Bad(a & ~b & d, t) || Bad(~a & b & d, t) || Bad(a & b & d, t)) continue;
                sols.Add(new[] { c[x], c[y], c[z] });
            }
            _solutions[tgt] = sols;
            return sols;
        }

        // a region of the hints' Venn diagram holding a target morph must hold nothing else
        static bool Bad(ulong region, ulong t) => (region & t) != 0 && (region & (region - 1)) != 0;

        Draft Draft(Sent tgt, List<Sent> ex)
        {
            var words = tgt.Words;
            var nouns = words.Where(w => w.M.Length == 2 && Noun(FindW(w))).Select(FindW).ToList();
            var verb = FindW(words[0]);
            var subj = FindW(words[1]);
            var place = nouns.Last();
            // verb-subject order is the normal one in writing; subject-first (SVO) is also correct Arabic
            var svo = new List<Word> { words[1], words[0] }.Concat(words.Skip(2)).ToList();
            var d = new Draft
            {
                Concept = LangConcepts.ConsonantRoot,
                Concepts = new[] { LangConcepts.VowelPattern },
                OrderConcept = LangConcepts.WordOrder,
                HypConcepts = new string[] { null },
                TargetMeaning = tgt.En,
                Target = h => words.ToList(),
                RealText = Real(tgt),
                Translation = tgt.En,
                Feature = $"Words are a three-consonant root set into a vowel pattern: {Tr(subj)} '{subj.En}' is {Rg(subj)} in CāCiC 'one who does', " +
                          $"{Tr(place)} '{place.En}' is {Rg(place)} in maCCaC 'place of'.",
            };
            d.Also(svo);
            var facts = new List<string>
            {
                $"Arabic usually puts the verb first: {Tr(verb)} {Tr(subj)} is word for word '{verb.En} the {subj.En}'.",
                "The pattern ma-CC-a-C means 'place of': maktab 'office' (k-t-b, write), malʿab 'stadium' (l-ʿ-b, play), maṭbakh 'kitchen' (ṭ-b-kh, cook).",
            };
            if (nouns.Any(n => SunLetters.IndexOf(n.Tr[0]) >= 0))
                facts.Add($"'The' is al-, but it takes on a following 'sun letter' in speech: {string.Join(", ", nouns.Where(n => SunLetters.IndexOf(n.Tr[0]) >= 0).Select(Tr))}, still written ال.");
            d.NowYouKnow = facts[(int)(Hash.Fnv(tgt.En) % (uint)facts.Count)];

            foreach (var e in ex) d.Example(e.En, h => e.Words.ToList());

            // wrong root in the right pattern, right root in the wrong pattern: only words that really exist
            for (int i = 0; i < words.Count; i++)
            {
                var e = FindW(words[i]);
                if (e == null)
                {
                    d.Mistake(Swap(words, i, W(words[i].M[0] == "fi" ? "min" : "fi")), LangConcepts.Vocabulary);
                    continue;
                }
                foreach (var r in Roots.Where(x => x != e.Root && Find(x, e.Pattern) != null)) d.Mistake(Swap(words, i, Wd(r, e.Pattern)), LangConcepts.ConsonantRoot);
                foreach (var p in Patterns.Select(x => x.key).Where(x => x != e.Pattern && Find(e.Root, x) != null)) d.Mistake(Swap(words, i, Wd(e.Root, p)), LangConcepts.VowelPattern);
            }
            return d;
        }

        static List<Word> Swap(List<Word> ws, int i, Word w) { var c = ws.ToList(); c[i] = w; return c; }
        static string Rg(Entry e) => RootGloss[e.Root].Split(' ')[0];
    }
}

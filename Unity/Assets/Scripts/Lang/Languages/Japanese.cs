using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Japanese, in Hepburn romaji: the verb comes last, a particle after each noun marks its role (を o, が ga),
    /// and words are built from kanji that each carry a meaning. Every puzzle is a sentence put together from
    /// three hint sentences that each share some of its pieces.
    /// </summary>
    public sealed class Japanese : SourceLanguage
    {
        public override string Name => "Japanese";
        public override string Code => "ja";
        public override string Endonym => "Nihongo";
        public override string Summary => "Verb last; particles after nouns mark their roles (ga, o); words built from meaningful kanji.";
        public override string QuizExplanation => "Japanese: the verb comes last, particles like を (o) and が (ga) mark each noun's role, and kanji build words from meanings.";
        public override string[] QuizDistractors => new[] { "Korean", "Mandarin Chinese", "Vietnamese", "Thai" };

        sealed class Noun { public string Key, Kanji, Romaji, En; }
        // a verb in one form; En = imperative ("Open"), Please = after "please" ("open"), Objects = nouns it fits
        sealed class Verb { public string Key, Kanji, Romaji, En, Please, En3, En1, Intr; public string[] Objects; }

        // ---- polite requests: "Please open the box."
        static readonly Noun[] Things =
        {
            new Noun { Key = "door", Kanji = "扉", Romaji = "tobira", En = "the door" },
            new Noun { Key = "box", Kanji = "箱", Romaji = "hako", En = "the box" },
            new Noun { Key = "ship", Kanji = "船", Romaji = "fune", En = "the ship" },
            new Noun { Key = "star", Kanji = "星", Romaji = "hoshi", En = "the star" },
            new Noun { Key = "key", Kanji = "鍵", Romaji = "kagi", En = "the key" },
            new Noun { Key = "light", Kanji = "光", Romaji = "hikari", En = "the light" },
            new Noun { Key = "water", Kanji = "水", Romaji = "mizu", En = "the water" },
        };
        static readonly Verb[] Requests =
        {
            new Verb { Key = "open", Kanji = "開けて", Romaji = "akete", En = "Open", Please = "open", Objects = new[] { "door", "box" } },
            new Verb { Key = "close", Kanji = "閉めて", Romaji = "shimete", En = "Close", Please = "close", Objects = new[] { "door", "box" } },
            new Verb { Key = "look", Kanji = "見て", Romaji = "mite", En = "Look at", Please = "look at", Objects = new[] { "star", "ship", "light", "door", "box" } },
            new Verb { Key = "search", Kanji = "探して", Romaji = "sagashite", En = "Look for", Please = "look for", Objects = new[] { "key", "ship", "door", "box", "light" } },
            new Verb { Key = "drink", Kanji = "飲んで", Romaji = "nonde", En = "Drink", Please = "drink", Objects = new[] { "water" } },
        };

        // ---- plain statements: "The cat drinks the water."
        static readonly Noun[] Actors =
        {
            new Noun { Key = "beast", Kanji = "獣", Romaji = "kemono", En = "beast" },
            new Noun { Key = "cat", Kanji = "猫", Romaji = "neko", En = "cat" },
            new Noun { Key = "child", Kanji = "子供", Romaji = "kodomo", En = "child" },
        };
        static readonly Noun[] Objects =
        {
            new Noun { Key = "ship", Kanji = "船", Romaji = "fune", En = "ship" },
            new Noun { Key = "door", Kanji = "扉", Romaji = "tobira", En = "door" },
            new Noun { Key = "key", Kanji = "鍵", Romaji = "kagi", En = "key" },
            new Noun { Key = "star", Kanji = "星", Romaji = "hoshi", En = "star" },
            new Noun { Key = "blood", Kanji = "血", Romaji = "chi", En = "blood" },
            new Noun { Key = "water", Kanji = "水", Romaji = "mizu", En = "water" },
        };
        static readonly Verb[] Statements =
        {
            new Verb { Key = "sees", Kanji = "見る", Romaji = "miru", En3 = "sees", En1 = "see", Intr = "watches", Objects = new[] { "ship", "door", "key", "star" } },
            new Verb { Key = "seeks", Kanji = "探す", Romaji = "sagasu", En3 = "looks for", En1 = "look for", Intr = "searches", Objects = new[] { "ship", "door", "key" } },
            new Verb { Key = "breaks", Kanji = "壊す", Romaji = "kowasu", En3 = "breaks", En1 = "break", Objects = new[] { "door", "key", "ship" } },
            new Verb { Key = "drinks", Kanji = "飲む", Romaji = "nomu", En3 = "drinks", En1 = "drink", Intr = "drinks", Objects = new[] { "blood", "water" } },
        };

        // ---- nouns written as kanji, each kanji a tile with its own meaning (and the reading it has in the word)
        sealed class KNoun { public string Key, Kanji, En, Lit; public string[] Glyphs; public string[] Verbs; public bool Compound => Glyphs.Length == 2; }
        static readonly (string key, string form, string gloss)[] Kanji =
        {
            ("den", "den", "electric (電)"), ("sha", "sha", "vehicle (車)"), ("wa", "wa", "talk (話)"), ("kai", "kai", "meet (会)"),
            ("hana", "hana", "flower (花)"), ("niku", "niku", "meat (肉)"), ("sakana", "sakana", "fish (魚)"), ("ya", "ya", "shop (屋)"),
            ("bi", "bi", "fire (火)"), ("kagi", "kagi", "key (鍵)"),
        };
        static readonly KNoun[] KNouns =
        {
            new KNoun { Key = "densha", Kanji = "電車", Glyphs = new[] { "den", "sha" }, En = "the train", Lit = "electric vehicle", Verbs = new[] { "search", "look" } },
            new KNoun { Key = "denwa", Kanji = "電話", Glyphs = new[] { "den", "wa" }, En = "the phone", Lit = "electric talk", Verbs = new[] { "search", "look" } },
            new KNoun { Key = "kaiwa", Kanji = "会話", Glyphs = new[] { "kai", "wa" }, En = "the conversation", Lit = "meet talk", Verbs = new[] { "listen" } },
            new KNoun { Key = "hanaya", Kanji = "花屋", Glyphs = new[] { "hana", "ya" }, En = "the florist", Lit = "flower shop", Verbs = new[] { "search" } },
            new KNoun { Key = "nikuya", Kanji = "肉屋", Glyphs = new[] { "niku", "ya" }, En = "the butcher's", Lit = "meat shop", Verbs = new[] { "search" } },
            new KNoun { Key = "sakanaya", Kanji = "魚屋", Glyphs = new[] { "sakana", "ya" }, En = "the fish shop", Lit = "fish shop", Verbs = new[] { "search" } },
            new KNoun { Key = "hanabi", Kanji = "花火", Glyphs = new[] { "hana", "bi" }, En = "the fireworks", Lit = "flower fire", Verbs = new[] { "look" } },
            new KNoun { Key = "hana", Kanji = "花", Glyphs = new[] { "hana" }, En = "the flowers", Verbs = new[] { "look", "search" } },
            new KNoun { Key = "niku", Kanji = "肉", Glyphs = new[] { "niku" }, En = "the meat", Verbs = new[] { "eat", "search" } },
            new KNoun { Key = "sakana", Kanji = "魚", Glyphs = new[] { "sakana" }, En = "the fish", Verbs = new[] { "eat", "look" } },
            new KNoun { Key = "kagi", Kanji = "鍵", Glyphs = new[] { "kagi" }, En = "the key", Verbs = new[] { "search", "look" } },
        };
        static readonly Verb[] KVerbs =
        {
            new Verb { Key = "search", Kanji = "探して", Romaji = "sagashite", En = "Look for", Please = "look for" },
            new Verb { Key = "look", Kanji = "見て", Romaji = "mite", En = "Look at", Please = "look at" },
            new Verb { Key = "eat", Kanji = "食べて", Romaji = "tabete", En = "Eat", Please = "eat" },
            new Verb { Key = "listen", Kanji = "聞いて", Romaji = "kiite", En = "Listen to", Please = "listen to" },
        };

        public Japanese()
        {
            Add("o", MorphKind.Particle, "o", "OBJ", LangConcepts.Particles);
            Add("ga", MorphKind.Particle, "ga", "SUBJ", LangConcepts.Particles);
            Add("kudasai", MorphKind.Root, "kudasai", "please");
            foreach (var n in Things.Concat(Objects).Concat(Actors).GroupBy(n => n.Key).Select(g => g.First()))
                Add("n." + n.Key, MorphKind.Root, n.Romaji, n.Key);
            foreach (var v in Requests) Add("v." + v.Key, MorphKind.Root, v.Romaji, v.Please, LangConcepts.VerbFinal);
            foreach (var v in KVerbs.Where(v => !Has("v." + v.Key))) Add("v." + v.Key, MorphKind.Root, v.Romaji, v.Please, LangConcepts.VerbFinal);
            foreach (var v in Statements) Add("v." + v.Key, MorphKind.Root, v.Romaji, v.En3, LangConcepts.VerbFinal);
            foreach (var (k, form, gloss) in Kanji) Add("k." + k, MorphKind.Glyph, form, gloss, LangConcepts.CompoundMeaning);

            Kind("verb-last", 0, VerbLast);
            Kind("kanji-compound", 1, CompoundRequest);
            Kind("particles", 2, ParticlesKind);
            // no tier-3 kind: KindFor gives tier 3 the particles kind. A longer Japanese sentence (subject + compound
            // object + verb, 6+ tiles) cannot be pinned down from 3 hints: every piece would need its own pattern of hints.
        }

        // ---------------------------------------------------------------- tier 0: please + verb last
        // rival bit 0: verb (and kudasai) before the object, as in English; bit 1: particle before its noun
        static List<Word> Request(int h, Word obj, string verb, bool please, int verbBit = 0, int particleBit = 1)
        {
            var np = Hyps.On(h, particleBit) ? P(W("o"), obj) : P(obj, W("o"));
            var vp = please ? P(W("v." + verb), W("kudasai")) : P(W("v." + verb));
            return Hyps.On(h, verbBit) ? Cat(vp, np) : Cat(np, vp);
        }

        Draft VerbLast(Rng rng, int tier)
        {
            Draft d = null;
            for (int tries = 0; tries < 30; tries++)
            {
                var v = rng.Pick(Requests);
                var nk = rng.Pick(v.Objects);
                var n = Things.First(t => t.Key == nk);
                string real = $"{n.Kanji}を{v.Kanji}ください ({n.Romaji} o {v.Romaji} kudasai)";
                d = new Draft
                {
                    Concept = LangConcepts.VerbFinal,
                    Concepts = new[] { LangConcepts.Particles },
                    OrderConcept = LangConcepts.VerbFinal,
                    HypConcepts = Hyps.Bits(LangConcepts.VerbFinal, LangConcepts.Particles),
                    TargetMeaning = $"Please {v.Please} {n.En}.",
                    Target = h => Request(h, W("n." + n.Key), v.Key, true),
                    RealText = real,
                    Translation = $"Please {v.Please} {n.En}.",
                    Feature = $"Japanese puts the verb at the very end, and ください (kudasai), 'please', comes after even the verb: {real} is literally '{n.Key} (o) {v.Please} please'.",
                    NowYouKnow = "'Eat sushi' in Japanese is 寿司を食べる (sushi o taberu), literally 'sushi (o) eat': the verb waits until the very end.",
                };
                var pool = new List<Hint>();
                foreach (var v2 in Requests)
                    foreach (var k in v2.Objects)
                    {
                        var n2 = Things.First(t => t.Key == k);
                        pool.Add(new Hint($"{v2.En} {n2.En}!", h => Request(h, W("n." + n2.Key), v2.Key, false)));
                        pool.Add(new Hint($"Please {v2.Please} {n2.En}.", h => Request(h, W("n." + n2.Key), v2.Key, true)));
                    }
                if (PickHints(this, d, pool, rng)) return d;
            }
            return d;
        }

        // ---------------------------------------------------------------- tier 2: ga / o particles
        static List<Word> Svo(int h, string s, string o, string v, string sp = "ga", string op = "o")
        {
            List<Word> Np(string n, string p) => Hyps.On(h, 1) ? P(W(p), W(n)) : P(W(n), W(p));
            if (s == null) return Hyps.On(h, 0) ? Cat(P(W(v)), Np(o, op)) : Cat(Np(o, op), P(W(v)));
            if (o == null) return Cat(Np(s, sp), P(W(v)));
            return Hyps.On(h, 0) ? Cat(Np(s, sp), P(W(v)), Np(o, op)) : Cat(Np(s, sp), Np(o, op), P(W(v)));
        }

        Draft ParticlesKind(Rng rng, int tier)
        {
            Draft d = null;
            for (int tries = 0; tries < 30; tries++)
            {
                var v = rng.Pick(Statements);
                var ok = rng.Pick(v.Objects);
                var o = Objects.First(x => x.Key == ok);
                var s = rng.Pick(Actors);
                string S = "n." + s.Key, O = "n." + o.Key, V = "v." + v.Key;
                string real = $"{s.Kanji}が{o.Kanji}を{v.Kanji} ({s.Romaji} ga {o.Romaji} o {v.Romaji})";
                d = new Draft
                {
                    Concept = LangConcepts.Particles,
                    Concepts = new[] { LangConcepts.VerbFinal },
                    OrderConcept = LangConcepts.VerbFinal,
                    HypConcepts = Hyps.Bits(LangConcepts.VerbFinal, LangConcepts.Particles),
                    TargetMeaning = $"The {s.En} {v.En3} the {o.En}.",
                    Target = h => Svo(h, S, O, V),
                    RealText = real,
                    Translation = $"The {s.En} {v.En3} the {o.En}.",
                    Feature = $"Japanese marks a noun's role with a particle after it: が (ga) marks who does it, を (o) marks what it is done to, and the verb comes last: {real}. 'I' is usually left out when it is obvious.",
                    NowYouKnow = $"Because the particles carry the roles, Japanese can move the object to the front: {o.Kanji}を{s.Kanji}が{v.Kanji} ({o.Romaji} o {s.Romaji} ga {v.Romaji}) still means 'the {s.En} {v.En3} the {o.En}', and the console accepts it too.",
                }
                .Also(Svo(0, O, S, V, "o", "ga"))                         // object first: real, if less usual
                .Mistake(Svo(0, O, S, V), LangConcepts.Particles)        // roles swapped
                .Mistake(Svo(0, S, O, V, "o", "ga"), LangConcepts.Particles); // particles swapped
                var pool = new List<Hint>();
                foreach (var v2 in Statements)
                {
                    foreach (var k in v2.Objects)
                    {
                        var o2 = Objects.First(x => x.Key == k);
                        foreach (var s2 in Actors)
                            pool.Add(new Hint($"The {s2.En} {v2.En3} the {o2.En}.", h => Svo(h, "n." + s2.Key, "n." + o2.Key, "v." + v2.Key)));
                        pool.Add(new Hint($"I {v2.En1} the {o2.En}.", h => Svo(h, null, "n." + o2.Key, "v." + v2.Key)));
                    }
                    if (v2.Intr != null)
                        foreach (var s2 in Actors)
                            pool.Add(new Hint($"The {s2.En} {v2.Intr}.", h => Svo(h, "n." + s2.Key, null, "v." + v2.Key)));
                }
                if (PickHints(this, d, pool, rng)) return d;
            }
            return d;
        }

        // ---------------------------------------------------------------- tier 1: kanji compounds in a polite request
        // rival bit 0: the two kanji the other way round; bit 1: verb first; bit 2: particle first
        static Word KWord(int h, KNoun n) =>
            n.Compound && Hyps.On(h, 0) ? W("k." + n.Glyphs[1], "k." + n.Glyphs[0]) : new Word(n.Glyphs.Select(g => "k." + g).ToArray());

        Draft CompoundRequest(Rng rng, int tier)
        {
            Draft d = null;
            var verbs = KVerbs.ToDictionary(v => v.Key);
            for (int tries = 0; tries < 30; tries++)
            {
                var n = rng.Pick(KNouns.Where(x => x.Compound).ToList());
                var v = verbs[rng.Pick(n.Verbs)];
                var parts = n.Glyphs.Select(g => Kanji.First(k => k.key == g).gloss).ToArray();
                string real = $"{n.Kanji}を{v.Kanji}ください ({Surface(KWord(0, n))} o {v.Romaji} kudasai)";
                string meaning = $"Please {v.Please} {n.En}.";
                d = new Draft
                {
                    Concept = LangConcepts.CompoundMeaning,
                    Concepts = new[] { LangConcepts.VerbFinal, LangConcepts.Particles },
                    MorphTiles = true,
                    OrderConcept = LangConcepts.CompoundMeaning,
                    HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning, LangConcepts.VerbFinal, LangConcepts.Particles),
                    TargetMeaning = meaning,
                    Target = h => Request(h, KWord(h, n), v.Key, true, 1, 2),
                    RealText = real,
                    Translation = meaning,
                    Feature = $"Each kanji carries a meaning, and Japanese builds words by joining them, the describing part first: {n.Kanji} ({Surface(KWord(0, n))}) is {parts[0]} + {parts[1]}, '{n.Lit}'. The verb still comes last, followed only by ください (kudasai), 'please'.",
                    NowYouKnow = "Japanese station signs say 出口 (deguchi), 'go out' + 'mouth', for the exit and 入口 (iriguchi), 'enter' + 'mouth', for the entrance.",
                };
                var pool = new List<Hint>();
                foreach (var n2 in KNouns)
                    foreach (var vk in n2.Verbs)
                    {
                        var v2 = verbs[vk];
                        pool.Add(new Hint($"{v2.En} {n2.En}!", h => Request(h, KWord(h, n2), v2.Key, false, 1, 2)));
                        pool.Add(new Hint($"Please {v2.Please} {n2.En}.", h => Request(h, KWord(h, n2), v2.Key, true, 1, 2)));
                    }
                if (PickHints(this, d, pool, rng)) return d;
            }
            return d;
        }

        // ---------------------------------------------------------------- hint picker
        sealed class Hint
        {
            public readonly string Pic; public readonly Render R;
            public Hint(string pic, Render r) { Pic = pic; R = r; }
        }

        /// <summary>
        /// Picks 3 hint sentences from the pool so that every target morph can be worked out by comparing them
        /// (each target morph appears in a different set of hints from every other morph shown), no hint holds
        /// the whole target, and every rival grammar renders at least one hint differently. Adds them to d.
        /// </summary>
        static bool PickHints(SourceLanguage lang, Draft d, List<Hint> pool, Rng rng)
        {
            const int MaxCandidates = 90;
            var accepted = new HashSet<string> { PuzzleBuilder.Key(d.Target(0)) };
            foreach (var a in d.AlsoCorrect) accepted.Add(PuzzleBuilder.Key(a));
            var target = d.Target(0).SelectMany(w => w.M).Distinct().ToList();
            var rivals = Enumerable.Range(1, d.HypConcepts.Length - 1).Where(h => !accepted.Contains(PuzzleBuilder.Key(d.Target(h)))).ToList();
            string Shape(List<Word> ws) => string.Join(" ", ws.Select(w => w.Id));

            var cands = new List<(Hint hint, string[] morphs, int kills)>();
            foreach (var hint in pool)
            {
                var r0 = hint.R(0);
                if (accepted.Contains(PuzzleBuilder.Key(r0))) continue;
                var ms = r0.SelectMany(w => w.M).Distinct().ToArray();
                int shared = target.Count(ms.Contains);
                if (shared < 2 || shared == target.Count) continue;
                if (r0.Count > 6 || lang.Render(r0).Length > 40) continue;
                int kills = 0;
                string s0 = Shape(r0);
                for (int i = 0; i < rivals.Count; i++) if (Shape(hint.R(rivals[i])) != s0) kills |= 1 << i;
                cands.Add((hint, ms, kills));
            }
            rng.Shuffle(cands);
            if (cands.Count > MaxCandidates) cands.RemoveRange(MaxCandidates, cands.Count - MaxCandidates);

            var index = new Dictionary<string, int>();
            foreach (var c in cands) foreach (var m in c.morphs) if (!index.ContainsKey(m)) index[m] = index.Count;
            if (index.Count > 128) return false;
            var bits = cands.Select(c => Bits.Of(c.morphs.Select(m => index[m]))).ToList();
            var T = Bits.Of(target.Where(index.ContainsKey).Select(m => index[m]));
            if (target.Any(m => !index.ContainsKey(m))) return false;
            int need = (1 << rivals.Count) - 1;

            for (int i = 0; i < cands.Count; i++)
                for (int j = i + 1; j < cands.Count; j++)
                    for (int k = j + 1; k < cands.Count; k++)
                    {
                        if (((cands[i].kills | cands[j].kills | cands[k].kills) & need) != need) continue;
                        Bits A = bits[i], B = bits[j], C = bits[k];
                        if (!T.SubsetOf(A | B | C)) continue;
                        if (!Unique(A & ~B & ~C, T) || !Unique(~A & B & ~C, T) || !Unique(~A & ~B & C, T)
                            || !Unique(A & B & ~C, T) || !Unique(A & ~B & C, T) || !Unique(~A & B & C, T) || !Unique(A & B & C, T)) continue;
                        var chosen = new List<Hint> { cands[i].hint, cands[j].hint, cands[k].hint };
                        rng.Shuffle(chosen);
                        foreach (var c in chosen) d.Example(c.Pic, c.R);
                        return true;
                    }
            return false;
        }

        // a set of morphs that holds a target morph must hold nothing else
        static bool Unique(Bits cls, Bits target) => (cls & target).IsEmpty || cls.Count == 1;

        struct Bits
        {
            public ulong Lo, Hi;
            public static Bits Of(IEnumerable<int> ix) { var b = new Bits(); foreach (var i in ix) { if (i < 64) b.Lo |= 1UL << i; else b.Hi |= 1UL << (i - 64); } return b; }
            public static Bits operator |(Bits a, Bits b) => new Bits { Lo = a.Lo | b.Lo, Hi = a.Hi | b.Hi };
            public static Bits operator &(Bits a, Bits b) => new Bits { Lo = a.Lo & b.Lo, Hi = a.Hi & b.Hi };
            public static Bits operator ~(Bits a) => new Bits { Lo = ~a.Lo, Hi = ~a.Hi };
            public bool IsEmpty => Lo == 0 && Hi == 0;
            public bool SubsetOf(Bits o) => (Lo & ~o.Lo) == 0 && (Hi & ~o.Hi) == 0;
            public int Count { get { int n = 0; ulong x = Lo; while (x != 0) { x &= x - 1; n++; } x = Hi; while (x != 0) { x &= x - 1; n++; } return n; } }
        }
    }
}

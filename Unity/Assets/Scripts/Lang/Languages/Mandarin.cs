using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Mandarin Chinese, in pinyin with tone marks: subject-verb-object order, a number or 'this'/'that' normally
    /// needs a measure word chosen by the kind of thing (animals, flat things, things held by a handle, vehicles),
    /// and nouns never change for plural. Every puzzle is a sentence put together from three hint sentences.
    /// </summary>
    public sealed class Mandarin : SourceLanguage
    {
        public override string Name => "Mandarin Chinese";
        public override string Code => "zh";
        public override string Endonym => "Zhōngwén";
        public override string Summary => "A counted or pointed-at thing normally needs a measure word between the number and the noun; nouns for things never change for plural.";
        public override string[] QuizDistractors => new[] { "Cantonese", "Japanese", "Vietnamese", "Korean" };
        public override string QuizExplanation => "Mandarin Chinese: numbers and 'this' normally need a measure word that fits the noun's kind, and nouns for animals and things never take a plural ending.";

        sealed class Cl { public string Key, Han, Pinyin, Kind; }
        static readonly Cl[] Classifiers =
        {
            new Cl { Key = "zhi", Han = "只", Pinyin = "zhī", Kind = "animals" },
            new Cl { Key = "zhang", Han = "张", Pinyin = "zhāng", Kind = "flat things" },
            new Cl { Key = "ba", Han = "把", Pinyin = "bǎ", Kind = "things held by a handle" },
            new Cl { Key = "liang", Han = "辆", Pinyin = "liàng", Kind = "vehicles" },
        };
        sealed class Noun { public string Key, Han, Pinyin, En, EnPl, Cl; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "cat", Han = "猫", Pinyin = "māo", En = "cat", EnPl = "cats", Cl = "zhi" },
            new Noun { Key = "bird", Han = "鸟", Pinyin = "niǎo", En = "bird", EnPl = "birds", Cl = "zhi" },
            new Noun { Key = "wolf", Han = "狼", Pinyin = "láng", En = "wolf", EnPl = "wolves", Cl = "zhi" },
            new Noun { Key = "table", Han = "桌子", Pinyin = "zhuōzi", En = "table", EnPl = "tables", Cl = "zhang" },
            new Noun { Key = "bed", Han = "床", Pinyin = "chuáng", En = "bed", EnPl = "beds", Cl = "zhang" },
            new Noun { Key = "map", Han = "地图", Pinyin = "dìtú", En = "map", EnPl = "maps", Cl = "zhang" },
            new Noun { Key = "knife", Han = "刀", Pinyin = "dāo", En = "knife", EnPl = "knives", Cl = "ba" },
            new Noun { Key = "key", Han = "钥匙", Pinyin = "yàoshi", En = "key", EnPl = "keys", Cl = "ba" },
            new Noun { Key = "umbrella", Han = "伞", Pinyin = "sǎn", En = "umbrella", EnPl = "umbrellas", Cl = "ba" },
            new Noun { Key = "car", Han = "车", Pinyin = "chē", En = "car", EnPl = "cars", Cl = "liang" },
            new Noun { Key = "truck", Han = "卡车", Pinyin = "kǎchē", En = "truck", EnPl = "trucks", Cl = "liang" },
            new Noun { Key = "bicycle", Han = "自行车", Pinyin = "zìxíngchē", En = "bicycle", EnPl = "bicycles", Cl = "liang" },
        };
        sealed class Det { public string Key, Han, Pinyin, En; public bool Plural; }
        static readonly Det Two = new Det { Key = "two", Han = "两", Pinyin = "liǎng", En = "two", Plural = true };
        static readonly Det Three = new Det { Key = "three", Han = "三", Pinyin = "sān", En = "three", Plural = true };
        static readonly Det This = new Det { Key = "this", Han = "这", Pinyin = "zhè", En = "this" };
        static readonly Det That = new Det { Key = "that", Han = "那", Pinyin = "nà", En = "that" };
        static readonly Det[] Numbers = { Two, Three };
        static readonly Det[] Pointers = { This, That };

        sealed class Pro { public string Key, Han, Pinyin, En; public bool Third; }
        static readonly Pro[] Pronouns =
        {
            new Pro { Key = "I", Han = "我", Pinyin = "wǒ", En = "I" },
            new Pro { Key = "you", Han = "你", Pinyin = "nǐ", En = "you" },
            new Pro { Key = "he", Han = "他", Pinyin = "tā", En = "he", Third = true },
        };
        sealed class Verb { public string Key, Han, Pinyin, En, En3; }
        static readonly Verb Have = new Verb { Key = "have", Han = "有", Pinyin = "yǒu", En = "have", En3 = "has" };
        static readonly Verb[] Verbs =
        {
            Have,
            new Verb { Key = "buy", Han = "买", Pinyin = "mǎi", En = "buy", En3 = "buys" },
            new Verb { Key = "see", Han = "看", Pinyin = "kàn", En = "look at", En3 = "looks at" },
            new Verb { Key = "want", Han = "要", Pinyin = "yào", En = "want", En3 = "wants" },
        };
        sealed class Adj { public string Key, Han, Pinyin, En; }
        static readonly Adj[] Adjs =
        {
            new Adj { Key = "big", Han = "大", Pinyin = "dà", En = "big" },
            new Adj { Key = "small", Han = "小", Pinyin = "xiǎo", En = "small" },
        };

        public Mandarin()
        {
            foreach (var c in Classifiers) Add("cl." + c.Key, MorphKind.Particle, c.Pinyin, "MW:" + c.Kind, LangConcepts.MeasureWord);
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Pinyin, n.En);
            foreach (var d in Numbers.Concat(Pointers)) Add("d." + d.Key, MorphKind.Root, d.Pinyin, d.En);
            foreach (var p in Pronouns) Add("p." + p.Key, MorphKind.Root, p.Pinyin, p.En);
            foreach (var v in Verbs) Add("v." + v.Key, MorphKind.Root, v.Pinyin, v.En);
            foreach (var a in Adjs) Add("a." + a.Key, MorphKind.Root, a.Pinyin, a.En);
            Add("hen", MorphKind.Root, "hěn", "very/is");

            Kind("measure-word", 0, (r, t) => Build(r, Shape.Have));
            Kind("measure-word-this", 2, (r, t) => Build(r, Shape.ThisIsBig));
            Kind("measure-word-svo", 3, (r, t) => Build(r, Shape.VerbThis));
        }

        enum Shape { Have, ThisIsBig, VerbThis }

        static Cl ClOf(Noun n) => Classifiers.First(c => c.Key == n.Cl);

        // ---- one noun phrase. Rivals: h 1 no measure word, h 2 measure word after the noun, h 3 the puzzle's
        // measure word and a second one swapped between their kinds. noun == null: the measure word stands alone ("this one").
        sealed class Rivals { public string A, B; }
        static List<Word> NP(int h, Rivals rv, Det d, Noun n, string cl = null)
        {
            if (d == null) return P(W("n." + n.Key));
            cl = cl ?? n.Cl;
            if (h == 3) cl = cl == rv.A ? rv.B : cl == rv.B ? rv.A : cl;
            if (n == null) return h == 1 ? P(W("d." + d.Key)) : P(W("d." + d.Key), W("cl." + cl));
            if (h == 1) return P(W("d." + d.Key), W("n." + n.Key));
            if (h == 2) return P(W("d." + d.Key), W("n." + n.Key), W("cl." + cl));
            return P(W("d." + d.Key), W("cl." + cl), W("n." + n.Key));
        }

        // subject (or null: "there is") + verb + noun phrase
        static List<Word> Svo(int h, Rivals rv, Pro s, Verb v, Det d, Noun n, string cl = null)
        {
            var subj = s == null ? new List<Word>() : P(W("p." + s.Key));
            return Cat(subj, P(W("v." + v.Key)), NP(h, rv, d, n, cl));
        }
        static List<Word> IsAdj(int h, Rivals rv, Det d, Noun n, Adj a, string cl = null) => Cat(NP(h, rv, d, n, cl), P(W("hen"), W("a." + a.Key)));

        // ---- English
        static string Thing(Det d, Noun n, string cl)
        {
            if (n == null) return d.En + " one";
            if (d == null) return null;
            return $"{d.En} {(d.Plural ? n.EnPl : n.En)}";
        }
        static string SayVerb(Pro s, Verb v, Det d, Noun n, string cl = null)
        {
            if (s == null) return d != null && d.Plural ? $"There are {Thing(d, n, cl)}." : $"There is {A(n)}.";
            string obj = d == null ? A(n) : Thing(d, n, cl);
            return Cap($"{s.En} {(s.Third ? v.En3 : v.En)} {obj}.");
        }
        static string SayAdj(Det d, Noun n, Adj a, string cl = null)
        {
            if (d == null) return $"{Cap(n.EnPl)} are {a.En}.";
            return Cap($"{Thing(d, n, cl)} {(d.Plural ? "are" : "is")} {a.En}.");
        }
        static string A(Noun n) => ("aeiou".IndexOf(n.En[0]) >= 0 ? "an " : "a ") + n.En;
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ---- Chinese characters
        static string HanNP(Det d, Noun n, string cl)
        {
            var c = Classifiers.First(x => x.Key == (cl ?? n.Cl));
            return $"{d.Han}{c.Han}{(n == null ? "" : n.Han)}";
        }

        Draft Build(Rng rng, Shape shape)
        {
            Draft d = null;
            var mc = LangConcepts.MeasureWord;
            for (int tries = 0; tries < 30; tries++)
            {
                var n = rng.Pick(Nouns);
                var c = ClOf(n);
                var other = rng.Pick(Classifiers.Where(x => x != c).ToList());
                var rv = new Rivals { A = c.Key, B = other.Key };
                var wrong = rng.Pick(Classifiers.Where(x => x != c && x != other).ToList());
                var s = rng.Pick(Pronouns);
                var a = rng.Pick(Adjs);
                d = new Draft
                {
                    Concept = mc,
                    Concepts = new[] { LangConcepts.WordOrder },
                    OrderConcept = mc,
                    HypConcepts = new[] { null, mc, mc, mc },
                };
                d.Extra(W("cl." + wrong.Key), mc);
                string han, pin, en;
                switch (shape)
                {
                    case Shape.Have:
                    {
                        var num = rng.Pick(Numbers);
                        d.Target = h => Svo(h, rv, s, Have, num, n);
                        en = SayVerb(s, Have, num, n);
                        han = $"{s.Han}{Have.Han}{HanNP(num, n, null)}";
                        d.Feature = $"Mandarin almost never puts a number straight onto a noun: a measure word that fits the kind of thing goes in between. {num.Han}{c.Han}{n.Han} ({num.Pinyin} {c.Pinyin} {n.Pinyin}) uses {c.Han} ({c.Pinyin}) for {c.Kind}.";
                        d.NowYouKnow = $"Mandarin nouns for animals and things have no plural form: {n.Han} ({n.Pinyin}) means '{n.En}' or '{n.EnPl}', and the number and measure word do the counting. 有 (yǒu) means both 'have' and 'there is'.";
                        break;
                    }
                    case Shape.ThisIsBig:
                    {
                        var dem = rng.Pick(Pointers);
                        d.Target = h => IsAdj(h, rv, dem, n, a);
                        en = SayAdj(dem, n, a);
                        han = $"{HanNP(dem, n, null)}很{a.Han}";
                        d.Feature = $"In Mandarin even 'this' and 'that' take a measure word before the noun, chosen by its kind: {HanNP(dem, n, null)} ({dem.Pinyin} {c.Pinyin} {n.Pinyin}) uses {c.Han} ({c.Pinyin}) for {c.Kind}. The measure word can even stand alone: {dem.Han}{c.Han} ({dem.Pinyin} {c.Pinyin}) is '{dem.En} one'.";
                        d.NowYouKnow = $"Mandarin adjectives work like verbs, so there is no 'is': 很 (hěn) literally means 'very', but {a.Han} '{a.En}' needs it in a plain statement like this one.";
                        break;
                    }
                    default:
                    {
                        var dem = rng.Pick(Pointers);
                        var v = rng.Pick(Verbs.Where(x => x != Have).ToList());
                        d.Target = h => Svo(h, rv, s, v, dem, n);
                        en = SayVerb(s, v, dem, n);
                        han = $"{s.Han}{v.Han}{HanNP(dem, n, null)}";
                        d.Feature = $"Mandarin puts subject, verb and object in the same order as English, but 'this' or 'that' takes a measure word for the kind of thing: {HanNP(dem, n, null)} ({dem.Pinyin} {c.Pinyin} {n.Pinyin}) uses {c.Han} ({c.Pinyin}) for {c.Kind}.";
                        d.NowYouKnow = "个 (gè) is the all-purpose measure word, and speakers fall back on it when unsure: 一个人 (yí ge rén) means 'one person'.";
                        break;
                    }
                }
                pin = Render(d.Target(0));
                d.TargetMeaning = en;
                d.Translation = en;
                d.RealText = $"{han}。({Cap(pin)}.)";

                if (PickHints(d, Pool, rv, rng)) return d;
            }
            return d;
        }

        List<Hint> HintPool()
        {
            var pool = new List<Hint>();
            foreach (var n in Nouns)
            {
                foreach (var v in Verbs)
                {
                    foreach (var s in Pronouns)
                    {
                        pool.Add(new Hint(SayVerb(s, v, null, n), (rv, h) => Svo(h, rv, s, v, null, n)));
                        foreach (var d in Numbers.Concat(Pointers))
                            pool.Add(new Hint(SayVerb(s, v, d, n), (rv, h) => Svo(h, rv, s, v, d, n)));
                    }
                }
                pool.Add(new Hint(SayVerb(null, Have, null, n), (rv, h) => Svo(h, rv, null, Have, null, n)));
                foreach (var d in Numbers)
                    pool.Add(new Hint(SayVerb(null, Have, d, n), (rv, h) => Svo(h, rv, null, Have, d, n)));
                foreach (var a in Adjs)
                {
                    pool.Add(new Hint(SayAdj(null, n, a), (rv, h) => IsAdj(h, rv, null, n, a)));
                    foreach (var d in Pointers)
                        pool.Add(new Hint(SayAdj(d, n, a), (rv, h) => IsAdj(h, rv, d, n, a)));
                }
            }
            // the measure word on its own, the noun understood: 这辆很大 'this one is big', 我要那张 'I want that one'
            foreach (var c in Classifiers)
                foreach (var d in Pointers)
                {
                    foreach (var a in Adjs)
                        pool.Add(new Hint(Cap($"{d.En} one is {a.En}."), (rv, h) => IsAdj(h, rv, d, null, a, c.Key)));
                    foreach (var v in Verbs)
                        foreach (var s in Pronouns)
                            pool.Add(new Hint(Cap($"{s.En} {(s.Third ? v.En3 : v.En)} {d.En} one."), (rv, h) => Svo(h, rv, s, v, d, null, c.Key)));
                }
            foreach (var hint in pool)
            {
                hint.R0 = hint.F(null, 0);   // the true grammar never looks at the rivals
                hint.Mask = MorphMask(hint.R0);
                hint.Key = PuzzleBuilder.Key(hint.R0);
                hint.Fits = hint.R0.Count <= 6 && Render(hint.R0).Length <= 40;
            }
            return pool;
        }

        // every hint sentence and its true rendering, built once and shared (it is the same for every instance):
        // only the rival renderings depend on the puzzle
        static List<Hint> _hintPool;
        List<Hint> Pool => _hintPool ??= HintPool();

        // ---------------------------------------------------------------- hint picker
        sealed class Hint
        {
            public readonly string Pic; public readonly Func<Rivals, int, List<Word>> F;
            public List<Word> R0; public ulong Mask; public string Key; public bool Fits;
            public Hint(string pic, Func<Rivals, int, List<Word>> f) { Pic = pic; F = f; }
        }

        /// <summary>
        /// Picks 3 hint sentences from the pool so that every target morph can be worked out by comparing them
        /// (each target morph appears in a different set of hints from every other morph shown), no hint holds
        /// the whole target, and every rival grammar renders at least one hint differently. Adds them to d.
        /// </summary>
        bool PickHints(Draft d, List<Hint> pool, Rivals rv, Rng rng)
        {
            const int MaxCandidates = 90;
            var accepted = new HashSet<string> { PuzzleBuilder.Key(d.Target(0)) };
            foreach (var a in d.AlsoCorrect) accepted.Add(PuzzleBuilder.Key(a));
            ulong target = MorphMask(d.Target(0));
            var rivals = Enumerable.Range(1, d.HypConcepts.Length - 1).Where(h => !accepted.Contains(PuzzleBuilder.Key(d.Target(h)))).ToList();
            string Shape(List<Word> ws) => string.Join(" ", ws.Select(w => w.Id));

            var cands = new List<(Hint hint, int kills)>();
            foreach (var hint in pool)
            {
                ulong shared = hint.Mask & target;
                if ((shared & (shared - 1)) == 0 || shared == target) continue;   // shares at least two target morphs, not all
                if (!hint.Fits || accepted.Contains(hint.Key)) continue;
                int kills = 0;
                string s0 = Shape(hint.R0);
                for (int i = 0; i < rivals.Count; i++) if (Shape(hint.F(rv, rivals[i])) != s0) kills |= 1 << i;
                cands.Add((hint, kills));
            }
            rng.Shuffle(cands);
            if (cands.Count > MaxCandidates) cands.RemoveRange(MaxCandidates, cands.Count - MaxCandidates);

            int need = (1 << rivals.Count) - 1;
            var pick = PuzzleBuilder.FindHintTriple(target, cands.Select(c => c.hint.Mask).ToList(), rng,
                (i, j, k) => ((cands[i].kills | cands[j].kills | cands[k].kills) & need) == need);
            if (pick == null) return false;
            var chosen = pick.Select(i => cands[i].hint).ToList();
            rng.Shuffle(chosen);
            foreach (var c in chosen) d.Example(c.Pic, h => c.F(rv, h));
            return true;
        }
    }
}

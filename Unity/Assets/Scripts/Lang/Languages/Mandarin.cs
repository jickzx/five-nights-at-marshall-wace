using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Mandarin Chinese, in pinyin with tone marks: a number or 'this' normally needs a measure word chosen by the kind
    /// of thing (animals, flat things, things held by a handle, vehicles), and nouns for things never change for plural.
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
            new Noun { Key = "mouse", Han = "老鼠", Pinyin = "lǎoshǔ", En = "mouse", EnPl = "mice", Cl = "zhi" },
            new Noun { Key = "table", Han = "桌子", Pinyin = "zhuōzi", En = "table", EnPl = "tables", Cl = "zhang" },
            new Noun { Key = "bed", Han = "床", Pinyin = "chuáng", En = "bed", EnPl = "beds", Cl = "zhang" },
            new Noun { Key = "map", Han = "地图", Pinyin = "dìtú", En = "map", EnPl = "maps", Cl = "zhang" },
            new Noun { Key = "photo", Han = "照片", Pinyin = "zhàopiàn", En = "photo", EnPl = "photos", Cl = "zhang" },
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

        public Mandarin()
        {
            foreach (var c in Classifiers) Add("cl." + c.Key, MorphKind.Particle, c.Pinyin, "MW:" + c.Kind, LangConcepts.MeasureWord);
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Pinyin, n.En);
            foreach (var d in new[] { Two, Three, This }) Add("d." + d.Key, MorphKind.Root, d.Pinyin, d.En);


            Kind("measure-word", 0, (r, t) => Counting(r, false));
            Kind("measure-word-this", 2, (r, t) => Counting(r, true));
        }

        static Cl ClOf(Noun n) => Classifiers.First(c => c.Key == n.Cl);
        static string En(Det d, Noun n) => $"{d.En} {(d.Plural ? n.EnPl : n.En)}";
        static string Real(Det d, Noun n) { var c = ClOf(n); return $"{d.Han}{c.Han}{n.Han} ({d.Pinyin} {c.Pinyin} {n.Pinyin})"; }

        Draft Counting(Rng rng, bool demonstrative)
        {
            var cats = rng.Sample(Classifiers, 3);
            Cl c = cats[0], c2 = cats[1], c3 = cats[2];
            var inC = rng.Sample(Nouns.Where(x => x.Cl == c.Key).ToList(), 2);
            Noun n = inC[0], nSame = inC[1];
            Noun nOther = rng.Pick(Nouns.Where(x => x.Cl == c2.Key).ToList());
            Det num = rng.Chance(0.5f) ? Two : Three;
            Det num2 = num == Two ? Three : Two;
            Det target = demonstrative ? This : num;

            // h 1: no measure word (with 'this' only: measure words only after numbers); h 2: measure word after the noun;
            // h 3: the two measure words of the puzzle swapped between their kinds
            List<Word> NP(int h, Det d, Noun x)
            {
                string cl = x.Cl;
                if (h == 3) cl = cl == c.Key ? c2.Key : cl == c2.Key ? c.Key : cl;
                if (h == 1 && (!demonstrative || d == This)) return P(W("d." + d.Key), W("n." + x.Key));
                if (h == 2) return P(W("d." + d.Key), W("n." + x.Key), W("cl." + cl));
                return P(W("d." + d.Key), W("cl." + cl), W("n." + x.Key));
            }
            var mc = LangConcepts.MeasureWord;
            var d = new Draft
            {
                Concept = mc,
                OrderConcept = mc,
                HypConcepts = new[] { null, mc, mc, mc },
                TargetMeaning = Cap(En(target, n)),
                Target = h => NP(h, target, n),
                RealText = Real(target, n),
                Translation = En(target, n),
                Feature = demonstrative
                    ? $"In Mandarin even 'this' normally takes a measure word before the noun, chosen by the kind of thing: {Real(This, n)} '{En(This, n)}' uses {c.Han} ({c.Pinyin}) for {c.Kind}."
                    : $"Mandarin almost never puts a number straight onto a noun: a measure word that fits the kind of thing goes in between. {Real(num, n)} '{En(num, n)}' uses {c.Han} ({c.Pinyin}) for {c.Kind}; {Real(num2, nOther)} '{En(num2, nOther)}' uses {c2.Han} ({c2.Pinyin}) for {c2.Kind}.",
                NowYouKnow = demonstrative
                    ? "个 (gè) is the all-purpose measure word, and speakers fall back on it when unsure: 一个人 (yí ge rén) means 'one person'."
                    : $"Mandarin nouns for animals and things have no plural form: {n.Han} ({n.Pinyin}) means '{n.En}' or '{n.EnPl}', and the number and measure word do the counting.",
            };
            if (demonstrative)
            {
                d.Example(Cap(En(This, nOther)), h => NP(h, This, nOther))
                 .Example(Cap(En(num, nSame)), h => NP(h, num, nSame));
            }
            else
            {
                d.Example(Cap(En(num2, nSame)), h => NP(h, num2, nSame))
                 .Example(Cap(En(num, nOther)), h => NP(h, num, nOther));
            }
            d.Example($"{n.En} / {n.EnPl}", h => P(W("n." + n.Key)));
            d.Extra(W("cl." + c3.Key), mc);
            return d;
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

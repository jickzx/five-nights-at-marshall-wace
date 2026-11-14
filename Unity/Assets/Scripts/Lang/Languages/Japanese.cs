using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Japanese, exaggerated: the verb always comes last, every noun wears a role particle (を o, が ga),
    /// and words are stacked from glyphs that each carry a meaning (kanji compounds).
    /// </summary>
    public sealed class Japanese : SourceLanguage
    {
        public override string Name => "Japanese";
        public override string Code => "ja";
        public override string Summary => "Verb always last; particles after nouns mark their roles; words stacked from meaningful glyphs (kanji).";
        public override string[] QuizDistractors => new[] { "Korean", "Mandarin Chinese", "Vietnamese", "Thai" };
        public override string QuizExplanation => "Japanese: the verb comes last, particles like を (o) and が (ga) mark each noun's role, and kanji build words from meanings.";

        sealed class Noun { public string Key, Kanji, Romaji, En; }
        sealed class Verb { public string Key, Kanji, Romaji, En; public string[] Objects; }

        // te-form requests, for "Open the door!"
        static readonly Noun[] Things =
        {
            new Noun { Key = "door", Kanji = "扉", Romaji = "tobira", En = "the door" },
            new Noun { Key = "eye", Kanji = "目", Romaji = "me", En = "your eyes" },
            new Noun { Key = "box", Kanji = "箱", Romaji = "hako", En = "the box" },
            new Noun { Key = "ship", Kanji = "船", Romaji = "fune", En = "the ship" },
            new Noun { Key = "star", Kanji = "星", Romaji = "hoshi", En = "the star" },
            new Noun { Key = "key", Kanji = "鍵", Romaji = "kagi", En = "the key" },
            new Noun { Key = "light", Kanji = "光", Romaji = "hikari", En = "the light" },
        };
        static readonly Verb[] Requests =
        {
            new Verb { Key = "open", Kanji = "開けて", Romaji = "akete", En = "Open", Objects = new[] { "door", "eye", "box" } },
            new Verb { Key = "close", Kanji = "閉めて", Romaji = "shimete", En = "Close", Objects = new[] { "door", "box" } },
            new Verb { Key = "look", Kanji = "見て", Romaji = "mite", En = "Look at", Objects = new[] { "star", "ship", "light", "door" } },
            new Verb { Key = "break", Kanji = "壊して", Romaji = "kowashite", En = "Break", Objects = new[] { "door", "key", "box" } },
            new Verb { Key = "search", Kanji = "探して", Romaji = "sagashite", En = "Search for", Objects = new[] { "key", "ship", "door", "light" } },
        };

        // plain present statements, for "The beast drinks the blood."
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
            new Verb { Key = "sees", Kanji = "見る", Romaji = "miru", En = "sees", Objects = new[] { "ship", "door", "key", "star" } },
            new Verb { Key = "seeks", Kanji = "探す", Romaji = "sagasu", En = "looks for", Objects = new[] { "ship", "door", "key" } },
            new Verb { Key = "breaks", Kanji = "壊す", Romaji = "kowasu", En = "breaks", Objects = new[] { "door", "key", "ship" } },
            new Verb { Key = "drinks", Kanji = "飲む", Romaji = "nomu", En = "drinks", Objects = new[] { "blood", "water" } },
        };

        public Japanese()
        {
            Add("o", MorphKind.Particle, "OBJ", LangConcepts.Particles);
            Add("ga", MorphKind.Particle, "SUBJ", LangConcepts.Particles);
            foreach (var n in Things.Concat(Objects).Concat(Actors).GroupBy(n => n.Key).Select(g => g.First()))
                Add("n." + n.Key, MorphKind.Root, n.Key);
            foreach (var v in Requests) Add("v." + v.Key, MorphKind.Root, v.Key.ToLowerInvariant(), LangConcepts.VerbFinal);
            foreach (var v in Statements) Add("v." + v.Key, MorphKind.Root, v.En, LangConcepts.VerbFinal);
            // kanji (one glyph each) and the kana endings that turn them into verbs
            foreach (var (k, g) in new[] { ("enter", "enter"), ("out", "go out"), ("mouth", "mouth/opening"), ("fire", "fire"), ("mountain", "mountain"), ("flower", "flower"), ("electric", "electric"), ("talk", "talk"), ("vehicle", "vehicle") })
                Add("k." + k, MorphKind.Glyph, g, LangConcepts.CompoundMeaning);
            foreach (var k in new[] { "ru", "su", "te", "tte" }) Add("k." + k, MorphKind.Glyph, "(kana ending)");

            Kind("verb-last", 0, VerbLast);
            Kind("kanji-compound", 1, Compound);
            Kind("particles", 2, ParticlesKind);
            Kind("compound-sentence", 3, CompoundSentence);
        }

        // ---------------------------------------------------------------- tier 0: verb last
        static List<Word> Sov(int h, string obj, string verb)
        {
            var np = Hyps.On(h, 1) ? P(W("o"), W(obj)) : P(W(obj), W("o"));
            return Hyps.On(h, 0) ? Cat(P(W(verb)), np) : Cat(np, P(W(verb)));
        }

        Draft VerbLast(Rng rng, int tier)
        {
            while (true)
            {
                var v = rng.Pick(Requests);
                var nk = rng.Pick(v.Objects);
                var n = Things.First(t => t.Key == nk);
                var v2s = Requests.Where(x => x != v && x.Objects.Contains(n.Key)).ToList();
                var n2s = v.Objects.Where(k => k != n.Key).ToList();
                if (v2s.Count == 0 || n2s.Count == 0) continue;
                var v2 = rng.Pick(v2s);
                var n2k = rng.Pick(n2s);
                var n2 = Things.First(t => t.Key == n2k);
                string real = $"{n.Kanji}を{v.Kanji} ({n.Romaji} o {v.Romaji})";
                return new Draft
                {
                    Concept = LangConcepts.VerbFinal,
                    Concepts = new[] { LangConcepts.Particles },
                    OrderConcept = LangConcepts.VerbFinal,
                    HypConcepts = Hyps.Bits(LangConcepts.VerbFinal, LangConcepts.Particles),
                    TargetMeaning = $"{v.En} {n.En}!",
                    Target = h => Sov(h, "n." + n.Key, "v." + v.Key),
                    RealText = real,
                    Translation = $"{v.En} {n.En}!",
                    Feature = $"Japanese puts the verb at the end, and the particle を (o) follows the thing being acted on: {real} is literally '{n.Key} (o) {v.Key.ToLowerInvariant()}'.",
                    NowYouKnow = "'Eat sushi' in Japanese is 寿司を食べる (sushi o taberu), literally 'sushi (o) eat': the verb waits until the very end.",
                }
                .Example($"{v2.En} {n.En}!", h => Sov(h, "n." + n.Key, "v." + v2.Key))
                .Example($"{v.En} {n2.En}!", h => Sov(h, "n." + n2.Key, "v." + v.Key));
            }
        }

        // ---------------------------------------------------------------- tier 2: ga / o particles
        static List<Word> Svo(int h, string s, string o, string v, string sp = "ga", string op = "o")
        {
            List<Word> Np(string n, string p) => Hyps.On(h, 1) ? P(W(p), W(n)) : P(W(n), W(p));
            return Hyps.On(h, 0) ? Cat(Np(s, sp), P(W(v)), Np(o, op)) : Cat(Np(s, sp), Np(o, op), P(W(v)));
        }

        Draft ParticlesKind(Rng rng, int tier)
        {
            while (true)
            {
                var v = rng.Pick(Statements);
                var ok = rng.Pick(v.Objects);
                var o = Objects.First(x => x.Key == ok);
                var s = rng.Pick(Actors);
                var s2 = rng.Pick(Actors.Where(x => x != s).ToList());
                var v2s = Statements.Where(x => x != v && x.Objects.Any(k => k != o.Key)).ToList();
                if (v2s.Count == 0) continue;
                var v2 = rng.Pick(v2s);
                var o2k = rng.Pick(v2.Objects.Where(k => k != o.Key).ToList());
                var o2 = Objects.First(x => x.Key == o2k);
                string S = "n." + s.Key, O = "n." + o.Key, V = "v." + v.Key;
                string real = $"{s.Kanji}が{o.Kanji}を{v.Kanji} ({s.Romaji} ga {o.Romaji} o {v.Romaji})";
                return new Draft
                {
                    Concept = LangConcepts.Particles,
                    Concepts = new[] { LangConcepts.VerbFinal },
                    OrderConcept = LangConcepts.VerbFinal,
                    HypConcepts = Hyps.Bits(LangConcepts.VerbFinal, LangConcepts.Particles),
                    TargetMeaning = $"The {s.En} {v.En} the {o.En}.",
                    Target = h => Svo(h, S, O, V),
                    RealText = real,
                    Translation = $"The {s.En} {v.En} the {o.En}.",
                    Feature = $"Japanese marks a noun's role with a particle after it: が (ga) marks who does it, を (o) marks what it is done to, and the verb comes last: {real}.",
                    NowYouKnow = $"Real Japanese is freer than the aliens: because the particles carry the roles, {o.Kanji}を{s.Kanji}が{v.Kanji} ({o.Romaji} o {s.Romaji} ga {v.Romaji}) still means 'the {s.En} {v.En} the {o.En}'.",
                }
                .Example($"The {s.En} {v2.En} the {o2.En}.", h => Svo(h, S, "n." + o2.Key, "v." + v2.Key))
                .Example($"The {s2.En} {v.En} the {o.En}.", h => Svo(h, "n." + s2.Key, O, V))
                .Mistake(Svo(0, O, S, V), LangConcepts.Particles)        // roles swapped
                .Mistake(Svo(0, S, O, V, "o", "ga"), LangConcepts.Particles); // particles swapped
            }
        }

        // ---------------------------------------------------------------- tier 1: kanji compounds
        static Word Cmp(int h, string a, string b) => Hyps.On(h, 0) ? W("k." + b, "k." + a) : W("k." + a, "k." + b);
        static Word Kana(string glyph, string ending) => W("k." + glyph, "k." + ending);

        Draft Compound(Rng rng, int tier)
        {
            var d = new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                OrderConcept = LangConcepts.CompoundMeaning,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning),
            };
            switch (rng.Range(0, 3))
            {
                case 0:
                    d.TargetMeaning = "exit";
                    d.Target = h => P(Cmp(h, "out", "mouth"));
                    d.Example("entrance", h => P(Cmp(h, "enter", "mouth")))
                     .Example("to enter", h => P(Kana("enter", "ru")))
                     .Example("to go out", h => P(Kana("out", "ru")));
                    d.RealText = "出口 (deguchi)";
                    d.Translation = "exit (literally 'go out' + 'mouth')";
                    d.Feature = "Each kanji carries a meaning, and Japanese builds words by stacking them: 入口 (iriguchi), 'entrance', is 入 'enter' + 口 'mouth, opening'.";
                    d.NowYouKnow = "Japanese station signs say 出口 (deguchi) for the exit and 入口 (iriguchi) for the entrance. You can read them now.";
                    break;
                case 1:
                    d.TargetMeaning = "sparks (fire-flowers)";
                    d.Target = h => P(Cmp(h, "fire", "flower"));
                    d.Example("volcano (a fire-mountain)", h => P(Cmp(h, "fire", "mountain")))
                     .Example("fireworks (flower-fire)", h => P(Cmp(h, "flower", "fire")));
                    d.RealText = "火花 (hibana)";
                    d.Translation = "sparks (literally 'fire flowers')";
                    d.Feature = "Each kanji carries a meaning, and Japanese builds words from them, the describing part first: 火山 (kazan), 'fire mountain', is a volcano.";
                    d.NowYouKnow = "花火 (hanabi), 'flower fire', means fireworks. Flip the kanji to 火花 (hibana) and it means sparks.";
                    break;
                default:
                    d.TargetMeaning = "telephone (electric talk)";
                    d.Target = h => P(Cmp(h, "electric", "talk"));
                    d.Example("train (an electric vehicle)", h => P(Cmp(h, "electric", "vehicle")))
                     .Example("to talk", h => P(Kana("talk", "su")));
                    d.RealText = "電話 (denwa)";
                    d.Translation = "telephone (literally 'electric talk')";
                    d.Feature = "Each kanji carries a meaning, and Japanese builds words by stacking them: 電車 (densha), 'electric vehicle', is a train.";
                    d.NowYouKnow = "電車 (densha), 'electric vehicle', is a train and 電話 (denwa), 'electric talk', is a telephone: one kanji, two everyday words.";
                    break;
            }
            return d;
        }

        // ---------------------------------------------------------------- tier 3: compound inside a sentence
        Draft CompoundSentence(Rng rng, int tier)
        {
            List<Word> Open(int h, Word thing)
            {
                var np = P(thing, W("o"));
                return Hyps.On(h, 1) ? Cat(P(W("v.open")), np) : Cat(np, P(W("v.open")));
            }
            return new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                Concepts = new[] { LangConcepts.VerbFinal, LangConcepts.Particles },
                MorphTiles = true,
                OrderConcept = LangConcepts.VerbFinal,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning, LangConcepts.VerbFinal),
                TargetMeaning = "Open the exit!",
                Target = h => Open(h, Cmp(h, "out", "mouth")),
                RealText = "出口を開けて (deguchi o akete)",
                Translation = "Open the exit!",
                Feature = "Japanese builds words from meaningful kanji (出 'go out' + 口 'mouth' = 出口 'exit') and puts the verb last, after the particle を (o).",
                NowYouKnow = "Japanese station signs say 出口 (deguchi) for the exit and 入口 (iriguchi) for the entrance. You can read them now.",
            }
            .Example("Open the entrance!", h => Open(h, Cmp(h, "enter", "mouth")))
            .Example("Go in!", h => P(Kana("enter", "tte")))
            .Example("Go out!", h => P(Kana("out", "te")));
        }
    }
}

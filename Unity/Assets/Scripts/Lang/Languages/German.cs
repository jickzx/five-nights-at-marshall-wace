using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// German, as short sentences: the verb is the second element of a main clause (Heute kauft Anna Brot), it goes
    /// to the very end after weil 'because' and dass 'that' (…, dass Anna Brot kauft), and nouns join into one-word
    /// compounds whose last part names the thing (Hand + Schuhe = Handschuhe 'gloves').
    ///
    /// Every puzzle is built on a fixed plan of three hints so that each piece of the target varies on its own
    /// across them (see PuzzleBuilder.AllDeducible). No sentence-final full stop is used as a tile.
    /// </summary>
    public sealed class German : SourceLanguage
    {
        public override string Name => "German";
        public override string Code => "de";
        public override string Endonym => "Deutsch";
        public override string Summary => "Verb second in a main clause, verb last after weil/dass; nouns join into one-word compounds, last part names the thing.";
        public override string QuizExplanation => "German: the verb comes second in a plain sentence but last after weil or dass, and nouns join into compounds whose last part names the thing.";
        public override string[] QuizDistractors => new[] { "Dutch", "Swedish", "Danish", "Norwegian" };

        // ---- vocabulary
        static readonly (string de, string en)[] Names = { ("Anna", "Anna"), ("Tom", "Tom"), ("Lena", "Lena"), ("Max", "Max"), ("Paul", "Paul") };

        sealed class Obj { public string De, En; }
        static readonly Obj[] Objs =
        {
            new Obj { De = "Brot", En = "bread" }, new Obj { De = "Suppe", En = "soup" }, new Obj { De = "Reis", En = "rice" },
            new Obj { De = "Käse", En = "cheese" }, new Obj { De = "Tee", En = "tea" }, new Obj { De = "Kaffee", En = "coffee" },
            new Obj { De = "Milch", En = "milk" }, new Obj { De = "Wasser", En = "water" },
        };

        sealed class Verb { public string De, En; public string[] Takes; }
        static readonly Verb[] Verbs =
        {
            new Verb { De = "kauft", En = "buys", Takes = new[] { "Brot", "Suppe", "Reis", "Käse", "Tee", "Kaffee", "Milch", "Wasser" } },
            new Verb { De = "isst", En = "eats", Takes = new[] { "Brot", "Suppe", "Reis", "Käse" } },
            new Verb { De = "trinkt", En = "drinks", Takes = new[] { "Tee", "Kaffee", "Milch", "Wasser", "Suppe" } },
            new Verb { De = "kocht", En = "cooks", Takes = new[] { "Suppe", "Reis" } },
            new Verb { De = "holt", En = "gets", Takes = new[] { "Brot", "Käse", "Milch", "Wasser" } },
        };
        // verbs that sensibly take any of the compound objects below
        static readonly Verb[] ThingVerbs =
        {
            new Verb { De = "kauft", En = "buys" }, new Verb { De = "sucht", En = "looks for" },
            new Verb { De = "braucht", En = "needs" }, new Verb { De = "findet", En = "finds" },
        };

        static readonly (string de, string en)[] Times = { ("heute", "Today"), ("morgen", "Tomorrow"), ("jetzt", "Now"), ("oft", "Often") };

        // a main clause, its comma and the conjunction, as one tile: what follows is a verb-final clause
        static readonly (string key, string de, string en)[] Leads =
        {
            ("weil", "ich bleibe, weil", "I stay because"),
            ("dass.weiss", "ich weiß, dass", "I know"),
            ("dass.glaube", "ich glaube, dass", "I think"),
            ("dass.hoffe", "ich hoffe, dass", "I hope"),
        };

        // ---- compounds. Every compound here is a real German word written as its parts joined, no linking letters.
        // Head: the parts share the first (modifier) part, and the last part of the target is a plain word on its own.
        // Tail: the parts share the last (head) part, and the first part of the target is a plain word on its own.
        sealed class CSet
        {
            public bool SharedFirst;
            public string A, B, En, Solo;            // target = A+B; Solo = English of the part that appears alone
            public (string part, string en)[] Others; // A+part (SharedFirst) or part+B, two of them
        }
        static readonly CSet[] Compounds =
        {
            new CSet { SharedFirst = true, A = "Hand", B = "Schuhe", En = "gloves", Solo = "shoes", Others = new[] { ("Taschen", "handbags"), ("Tücher", "towels") } },
            new CSet { SharedFirst = true, A = "Haus", B = "Schuhe", En = "slippers", Solo = "shoes", Others = new[] { ("Schlüssel", "house keys"), ("Tiere", "pets") } },
            new CSet { SharedFirst = true, A = "Brot", B = "Messer", En = "bread knives", Solo = "knives", Others = new[] { ("Körbe", "bread baskets"), ("Dosen", "lunch boxes") } },
            new CSet { SharedFirst = true, A = "Auto", B = "Schlüssel", En = "car keys", Solo = "keys", Others = new[] { ("Reifen", "tyres"), ("Sitze", "car seats") } },
            new CSet { SharedFirst = true, A = "Taschen", B = "Lampen", En = "torches", Solo = "lamps", Others = new[] { ("Messer", "pocketknives"), ("Tücher", "tissues") } },
            new CSet { SharedFirst = false, A = "Taschen", B = "Lampen", En = "torches", Solo = "bags", Others = new[] { ("Tisch", "table lamps"), ("Steh", "floor lamps") } },
            new CSet { SharedFirst = false, A = "Taschen", B = "Messer", En = "pocketknives", Solo = "bags", Others = new[] { ("Brot", "bread knives"), ("Küchen", "kitchen knives") } },
            new CSet { SharedFirst = false, A = "Taschen", B = "Tücher", En = "tissues", Solo = "bags", Others = new[] { ("Hand", "towels"), ("Tisch", "tablecloths") } },
        };
        static readonly Dictionary<string, string> PartGloss = new Dictionary<string, string>
        {
            { "Hand", "hand" }, { "Haus", "house" }, { "Brot", "bread" }, { "Auto", "car" }, { "Taschen", "bags/pockets" },
            { "Schuhe", "shoes" }, { "Messer", "knives" }, { "Schlüssel", "keys" }, { "Lampen", "lamps" }, { "Tücher", "cloths" },
            { "Körbe", "baskets" }, { "Dosen", "tins" }, { "Reifen", "tyres" }, { "Sitze", "seats" }, { "Tiere", "animals" },
            { "Tisch", "table" }, { "Steh", "stand" }, { "Küchen", "kitchen" },
        };

        const string V2Feature = "In a German main clause the verb is always the second element: if a word like heute 'today' comes first, the subject moves after the verb";
        const string VfFeature = "After weil 'because' or dass 'that' (always with a comma before it) German sends the verb to the very end of the clause";
        const string CompoundFeature = "German writes a compound as one word, and its last part names what the thing is";

        public German()
        {
            foreach (var n in Names) Add("s." + n.de, MorphKind.Root, n.de, n.en);
            foreach (var o in Objs) Add("o." + o.De, MorphKind.Root, o.De, o.En);
            foreach (var v in Verbs.Concat(ThingVerbs).Select(v => v.De).Distinct())
                Add("v." + v, MorphKind.Root, v, Verbs.Concat(ThingVerbs).First(x => x.De == v).En);
            foreach (var t in Times) Add("t." + t.de, MorphKind.Root, t.de, t.en.ToLowerInvariant(), LangConcepts.WordOrder);
            foreach (var l in Leads) Add("l." + l.key, MorphKind.Particle, l.de, l.en, LangConcepts.VerbFinal);
            foreach (var p in PartGloss) Add("c." + p.Key, MorphKind.Root, p.Key, p.Value, LangConcepts.CompoundMeaning);

            Kind("compound", 0, (r, t) => Retry(r, CompoundKind));
            Kind("verb-second", 1, (r, t) => Retry(r, V2Kind));
            Kind("verb-final", 2, (r, t) => Retry(r, VfKind));
            Kind("compound-clause", 3, (r, t) => Retry(r, r.Chance(0.5f) ? (System.Func<Rng, Draft>)ComboV2 : ComboVf));
        }

        /// <summary>A compound is one word: capital first letter, the rest lower case (Hand + Schuhe = Handschuhe).</summary>
        public override string Surface(Word w)
        {
            var s = base.Surface(w);
            return w.M.Length > 1 ? char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant() : s;
        }

        // keep every shown sentence within the UI line (40 characters) and every English caption within two short lines
        Draft Retry(Rng rng, System.Func<Rng, Draft> make)
        {
            Draft d = null;
            for (int i = 0; i < 40; i++)
            {
                d = make(rng);
                if (d.Examples.All(e => Render(e.Render(0)).Length <= 38 && e.Picture.Length <= 33) && Render(d.Target(0)).Length <= 38 && d.TargetMeaning.Length <= 33) return d;
            }
            return d;
        }

        static Word S(string name) => W("s." + name);
        static Word V(string verb) => W("v." + verb);
        static Word O(string obj) => W("o." + obj);
        // rival: head first, i.e. the compound reversed
        static Word Cmp(int h, int bit, string first, string last) => Hyps.On(h, bit) ? W("c." + last, "c." + first) : W("c." + first, "c." + last);

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
        string Text(List<Word> ws) => Cap(Render(ws)) + ".";

        // ---------------------------------------------------------------- compound (tier 0)
        // target  S V A+B           Anna kauft Handschuhe.
        // hint 1  S1 V A+Q          Tom kauft Handtaschen.
        // hint 2  S V2 A+R          Anna braucht Handtücher.
        // hint 3  S V B             Anna kauft Schuhe.
        // (mirrored when the parts share their last part: Q+B, R+B, and A alone)
        Draft CompoundKind(Rng rng)
        {
            var c = rng.Pick(Compounds);
            var names = rng.Sample(Names, 2);
            var verbs = rng.Sample(ThingVerbs, 2);
            string s = names[0].de, s1 = names[1].de, v = verbs[0].De, v2 = verbs[1].De;
            var (q, qEn) = c.Others[0];
            var (rr, rEn) = c.Others[1];
            Word Part(int h, string other) => c.SharedFirst ? Cmp(h, 0, c.A, other) : Cmp(h, 0, other, c.B);
            Word Solo() => W("c." + (c.SharedFirst ? c.B : c.A));

            var d = new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                OrderConcept = LangConcepts.CompoundMeaning,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning),
                TargetMeaning = $"{s} {verbs[0].En} {c.En}.",
                Target = h => P(S(s), V(v), Cmp(h, 0, c.A, c.B)),
                Translation = $"{s} {verbs[0].En} {c.En}.",
                Feature = $"{CompoundFeature}: {c.A} + {c.B.ToLowerInvariant()} = {c.A}{c.B.ToLowerInvariant()}, '{c.En}'.",
                NowYouKnow = CompoundFact(c),
            };
            d.RealText = Text(d.Target(0));
            d.Example($"{s1} {verbs[0].En} {qEn}.", h => P(S(s1), V(v), Part(h, q)));
            d.Example($"{s} {verbs[1].En} {rEn}.", h => P(S(s), V(v2), Part(h, rr)));
            d.Example($"{s} {verbs[0].En} {c.Solo}.", h => P(S(s), V(v), Solo()));
            return d;
        }

        static string CompoundFact(CSet c)
        {
            switch (c.A + c.B)
            {
                case "HandSchuhe": return "A German glove is a Handschuh, a 'hand shoe', and a slipper is a Hausschuh, a 'house shoe'.";
                case "HausSchuhe": return "A slipper is a Hausschuh, a 'house shoe', and a glove is a Handschuh, a 'hand shoe'.";
                case "TaschenLampen": return "A torch is a Taschenlampe, a 'pocket lamp': Tasche means both bag and pocket.";
                case "TaschenMesser": return "A pocketknife is a Taschenmesser, a 'pocket knife': Tasche means both bag and pocket.";
                case "TaschenTücher": return "A paper tissue is a Taschentuch, a 'pocket cloth', from the days of cloth handkerchiefs.";
                default: return "German compounds can get very long: Donaudampfschifffahrtsgesellschaft, 'Danube steamship company', is one word.";
            }
        }

        // ---------------------------------------------------------------- verb second (tier 1)
        // target  T V S O       Heute kauft Anna Brot.
        // hint 1  S V O         Anna kauft Brot.
        // hint 2  T V S2 O2     Heute kauft Tom Milch.
        // hint 3  T V3 S O3     Heute isst Anna Käse.
        // rival (bit 0): English order, time S V O
        static List<Word> V2(int h, Word t, Word v, Word s, Word o) => Hyps.On(h, 0) ? P(t, s, v, o) : P(t, v, s, o);

        Draft V2Kind(Rng rng)
        {
            var (v, o, o2, v3, o3) = PickVerbs(rng);
            var names = rng.Sample(Names, 2);
            var time = rng.Pick(Times);
            string s = names[0].de, s2 = names[1].de;
            Word T = W("t." + time.de);
            var d = new Draft
            {
                Concept = LangConcepts.WordOrder,
                OrderConcept = LangConcepts.WordOrder,
                HypConcepts = Hyps.Bits(LangConcepts.WordOrder),
                TargetMeaning = $"{time.en} {s} {v.En} {o.En}.",
                Target = h => V2(h, T, V(v.De), S(s), O(o.De)),
                Translation = $"{time.en} {s} {v.En} {o.En}.",
                NowYouKnow = "German keeps the verb in second place even after a long opening: Am Montag um acht Uhr trinkt Anna Tee.",
            };
            d.RealText = Text(d.Target(0));
            d.Feature = $"{V2Feature}: {s} {v.De} {o.De}, but {d.RealText}";
            d.Example($"{s} {v.En} {o.En}.", h => P(S(s), V(v.De), O(o.De)));
            d.Example($"{time.en} {s2} {v.En} {o2.En}.", h => V2(h, T, V(v.De), S(s2), O(o2.De)));
            d.Example($"{time.en} {s} {v3.En} {o3.En}.", h => V2(h, T, V(v3.De), S(s), O(o3.De)));
            return d;
        }

        // v takes o and o2; v3 takes o and o3; all different
        static (Verb v, Obj o, Obj o2, Verb v3, Obj o3) PickVerbs(Rng rng)
        {
            while (true)
            {
                var o = rng.Pick(Objs);
                var vs = Verbs.Where(x => x.Takes.Contains(o.De)).ToList();
                if (vs.Count < 2) continue;
                var two = rng.Sample(vs, 2);
                Verb v = two[0], v3 = two[1];
                var o2s = Objs.Where(x => x != o && v.Takes.Contains(x.De)).ToList();
                var o3s = Objs.Where(x => x != o && v3.Takes.Contains(x.De)).ToList();
                if (o2s.Count == 0 || o3s.Count == 0) continue;
                var o2 = rng.Pick(o2s);
                var o3 = rng.Pick(o3s.Where(x => x != o2).DefaultIfEmpty(null).ToList());
                if (o3 == null) continue;
                return (v, o, o2, v3, o3);
            }
        }

        // ---------------------------------------------------------------- verb final (tier 2)
        // target  L S O V        Ich weiß, dass Anna Brot kauft.
        // hint 1  S V O          Anna kauft Brot.
        // hint 2  L S2 O2 V      Ich weiß, dass Tom Milch kauft.
        // hint 3  L S O3 V3      Ich weiß, dass Anna Käse isst.
        // rivals: bit 0 = the verb stays second after weil/dass; bit 1 = the verb is last in a main clause too
        static List<Word> Clause(int h, Word l, Word s, Word o, Word v) => Hyps.On(h, 0) ? P(l, s, v, o) : P(l, s, o, v);
        static List<Word> Main(int h, Word s, Word v, Word o) => Hyps.On(h, 1) ? P(s, o, v) : P(s, v, o);

        Draft VfKind(Rng rng)
        {
            var (v, o, o2, v3, o3) = PickVerbs(rng);
            var names = rng.Sample(Names, 2);
            var lead = rng.Pick(Leads);
            string s = names[0].de, s2 = names[1].de;
            Word L = W("l." + lead.key);
            string En(string who, Verb vb, Obj ob) => $"{lead.en} {who} {vb.En} {ob.En}.";
            var d = new Draft
            {
                Concept = LangConcepts.VerbFinal,
                OrderConcept = LangConcepts.VerbFinal,
                HypConcepts = Hyps.Bits(LangConcepts.VerbFinal, LangConcepts.VerbFinal),
                TargetMeaning = En(s, v, o),
                Target = h => Clause(h, L, S(s), O(o.De), V(v.De)),
                Translation = En(s, v, o),
                NowYouKnow = "In a German clause with weil or dass, listeners wait until the very last word to hear the verb.",
            };
            d.RealText = Text(d.Target(0));
            d.Feature = $"{VfFeature}: {s} {v.De} {o.De}, but {d.RealText}";
            d.Example($"{s} {v.En} {o.En}.", h => Main(h, S(s), V(v.De), O(o.De)));
            d.Example(En(s2, v, o2), h => Clause(h, L, S(s2), O(o2.De), V(v.De)));
            d.Example(En(s, v3, o3), h => Clause(h, L, S(s), O(o3.De), V(v3.De)));
            return d;
        }

        // ---------------------------------------------------------------- compound inside a clause (tier 3)
        // target  T V S A+B          Heute kauft Anna Handschuhe.      (or  L S A+B V: Ich weiß, dass Anna Handschuhe kauft.)
        // hint 1  S V A+Q            Anna kauft Handtaschen.
        // hint 2  T V S2 A+R         Heute kauft Tom Handtücher.
        // hint 3  T V S B            Heute kauft Anna Schuhe.
        // rivals: bit 0 = compound reversed; bit 1 = the order rival of the clause type
        (CSet c, string s, string s2, Verb v) ComboPick(Rng rng)
        {
            var names = rng.Sample(Names, 2);
            return (rng.Pick(Compounds), names[0].de, names[1].de, rng.Pick(ThingVerbs));
        }

        Word ComboObj(CSet c, int h, int which) // 0 target, 1 Q, 2 R, 3 solo
        {
            if (which == 0) return Cmp(h, 0, c.A, c.B);
            if (which == 3) return W("c." + (c.SharedFirst ? c.B : c.A));
            var other = c.Others[which - 1].part;
            return c.SharedFirst ? Cmp(h, 0, c.A, other) : Cmp(h, 0, other, c.B);
        }
        static string ComboEn(CSet c, int which) => which == 0 ? c.En : which == 3 ? c.Solo : c.Others[which - 1].en;

        Draft ComboV2(Rng rng)
        {
            var (c, s, s2, v) = ComboPick(rng);
            var time = rng.Pick(Times);
            Word T = W("t." + time.de);
            List<Word> Sent(int h, string who, int which) => Hyps.On(h, 1) ? P(T, S(who), V(v.De), ComboObj(c, h, which)) : P(T, V(v.De), S(who), ComboObj(c, h, which));
            var d = new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                Concepts = new[] { LangConcepts.WordOrder },
                OrderConcept = LangConcepts.WordOrder,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning, LangConcepts.WordOrder),
                TargetMeaning = $"{time.en} {s} {v.En} {c.En}.",
                Target = h => Sent(h, s, 0),
                Translation = $"{time.en} {s} {v.En} {c.En}.",
                NowYouKnow = CompoundFact(c),
            };
            d.RealText = Text(d.Target(0));
            d.Feature = $"{V2Feature}. {CompoundFeature}: {c.A}{c.B.ToLowerInvariant()} is '{c.En}'.";
            d.Example($"{s} {v.En} {ComboEn(c, 1)}.", h => P(S(s), V(v.De), ComboObj(c, h, 1)));
            d.Example($"{time.en} {s2} {v.En} {ComboEn(c, 2)}.", h => Sent(h, s2, 2));
            d.Example($"{time.en} {s} {v.En} {ComboEn(c, 3)}.", h => Sent(h, s, 3));
            return d;
        }

        Draft ComboVf(Rng rng)
        {
            var (c, s, s2, v) = ComboPick(rng);
            var lead = rng.Pick(Leads.Where(l => l.key != "dass.glaube").ToList());   // keep the line short
            Word L = W("l." + lead.key);
            List<Word> Sent(int h, string who, int which) => Hyps.On(h, 1) ? P(L, S(who), V(v.De), ComboObj(c, h, which)) : P(L, S(who), ComboObj(c, h, which), V(v.De));
            string En(string who, int which) => $"{lead.en} {who} {v.En} {ComboEn(c, which)}.";
            var d = new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                Concepts = new[] { LangConcepts.VerbFinal },
                OrderConcept = LangConcepts.VerbFinal,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning, LangConcepts.VerbFinal),
                TargetMeaning = En(s, 0),
                Target = h => Sent(h, s, 0),
                Translation = En(s, 0),
                NowYouKnow = CompoundFact(c),
            };
            d.RealText = Text(d.Target(0));
            d.Feature = $"{VfFeature}. {CompoundFeature}: {c.A}{c.B.ToLowerInvariant()} is '{c.En}'.";
            d.Example($"{s} {v.En} {ComboEn(c, 1)}.", h => P(S(s), V(v.De), ComboObj(c, h, 1)));
            d.Example(En(s2, 2), h => Sent(h, s2, 2));
            d.Example(En(s, 3), h => Sent(h, s, 3));
            return d;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// German, exaggerated: nouns glue into ever longer compounds whose last part names the thing,
    /// the verb is kicked to the end of "because" clauses, and (like German nouns) everything is capitalised.
    /// </summary>
    public sealed class German : SourceLanguage
    {
        public override string Name => "German";
        public override string Code => "de";
        public override string Summary => "Nouns glued into compounds, last part names the thing; verb kicked to the end of 'because' clauses; written in capitals.";
        public override string[] QuizDistractors => new[] { "Dutch", "Swedish", "Danish", "Norwegian" };
        public override string QuizExplanation => "German: nouns join into compounds whose last part names the thing, and the verb goes to the end of clauses like weil 'because'.";
        public override bool UpperCase => true;

        const string CompoundFeature = "German glues words into one long noun, and the last part names what the thing is: Handtasche, 'hand bag', is a kind of bag, and Hausschuh, 'house shoe', is a kind of shoe (a slipper).";

        sealed class Set
        {
            public string Target, Real, Translation, Now;
            public string[] TargetParts;
            public (string picture, string[] parts)[] Examples;
        }

        static readonly Set[] TwoPart =
        {
            new Set
            {
                Target = "glove (a 'shoe' for the hand)", TargetParts = new[] { "hand", "shoe" },
                Examples = new[] { ("slipper (a shoe for the house)", new[] { "house", "shoe" }), ("handbag (a bag for the hand)", new[] { "hand", "bag" }) },
                Real = "Handschuh", Translation = "glove (literally 'hand shoe')",
                Now = "German for glove is Handschuh, literally 'hand shoe', and a slipper is a Hausschuh, a 'house shoe'.",
            },
            new Set
            {
                Target = "blood moon (a moon of blood)", TargetParts = new[] { "blood", "moon" },
                Examples = new[] { ("full moon (a moon that is full)", new[] { "full", "moon" }), ("bloodbath (a bath of blood)", new[] { "blood", "bath" }) },
                Real = "Blutmond", Translation = "blood moon",
                Now = "German compounds can get absurdly long: Donaudampfschifffahrtsgesellschaft, 'Danube steamship company', is a single word.",
            },
            new Set
            {
                Target = "tool (stuff for work)", TargetParts = new[] { "work", "stuff" },
                Examples = new[] { ("fireworks (work done with fire)", new[] { "fire", "work" }), ("lighter (stuff for fire)", new[] { "fire", "stuff" }) },
                Real = "Werkzeug", Translation = "tool (literally 'work stuff')",
                Now = "Feuerzeug, 'fire stuff', is a lighter, and Werkzeug, 'work stuff', is a tool.",
            },
        };

        static readonly Set[] ThreePart =
        {
            new Set
            {
                Target = "glove compartment (a compartment for gloves)", TargetParts = new[] { "hand", "shoe", "compartment" },
                Examples = new[] { ("glove (a 'shoe' for the hand)", new[] { "hand", "shoe" }), ("cooling compartment (a cool compartment)", new[] { "cool", "compartment" }) },
                Real = "Handschuhfach", Translation = "glove compartment (literally 'hand-shoe compartment')",
                Now = "A German car's glove compartment is the Handschuhfach: the 'hand-shoe compartment'.",
            },
            new Set
            {
                Target = "toolbox (a box for tools)", TargetParts = new[] { "work", "stuff", "box" },
                Examples = new[] { ("tool (stuff for work)", new[] { "work", "stuff" }), ("letterbox (a box for letters)", new[] { "letter", "box" }) },
                Real = "Werkzeugkasten", Translation = "toolbox (literally 'work-stuff box')",
                Now = "German compounds stack without limit: Werkzeugkasten, 'toolbox', is Werk + Zeug + Kasten, 'work stuff box'.",
            },
        };

        sealed class Noun { public string Key, Art, De, En; public string[] Adjs; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "ship", Art = "das", De = "Schiff", En = "ship", Adjs = new[] { "red", "cold", "dark" } },
            new Noun { Key = "door", Art = "die", De = "Tür", En = "door", Adjs = new[] { "open", "red", "dark" } },
            new Noun { Key = "beast", Art = "das", De = "Biest", En = "beast", Adjs = new[] { "dead", "cold" } },
            new Noun { Key = "moon", Art = "der", De = "Mond", En = "moon", Adjs = new[] { "red", "dark", "cold" } },
        };
        static readonly Dictionary<string, string> AdjDe = new Dictionary<string, string>
        {
            { "red", "rot" }, { "cold", "kalt" }, { "dark", "dunkel" }, { "open", "offen" }, { "dead", "tot" },
        };

        public German()
        {
            foreach (var k in new[] { "hand", "shoe", "house", "bag", "full", "moon", "blood", "bath", "fire", "work", "stuff", "cool", "compartment", "letter", "box" })
                Add("c." + k, MorphKind.Root, k, LangConcepts.CompoundMeaning);
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.En);
            foreach (var a in new[] { "red", "cold", "dark", "open", "dead" }) Add("a." + a, MorphKind.Root, a);
            Add("because", MorphKind.Particle, "because", LangConcepts.VerbFinal);
            Add("is", MorphKind.Root, "is", LangConcepts.VerbFinal);

            Kind("compound", 0, (r, t) => CompoundKind(r, TwoPart));
            Kind("verb-final-clause", 2, VerbFinalKind);
            Kind("long-compound", 3, (r, t) => CompoundKind(r, ThreePart));
        }

        // rival (bit 0): head first, i.e. the compound reversed
        static Word Cmp(int h, string[] parts)
        {
            var ks = parts.Select(p => "c." + p).ToArray();
            if (Hyps.On(h, 0)) System.Array.Reverse(ks);
            return new Word(ks);
        }

        Draft CompoundKind(Rng rng, Set[] sets)
        {
            var s = rng.Pick(sets);
            var d = new Draft
            {
                Concept = LangConcepts.CompoundMeaning,
                OrderConcept = LangConcepts.CompoundMeaning,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.CompoundMeaning),
                TargetMeaning = s.Target,
                Target = h => P(Cmp(h, s.TargetParts)),
                RealText = s.Real,
                Translation = s.Translation,
                Feature = CompoundFeature,
                NowYouKnow = s.Now,
            };
            foreach (var (picture, parts) in s.Examples) d.Example(picture, h => P(Cmp(h, parts)));
            return d;
        }

        Draft VerbFinalKind(Rng rng, int tier)
        {
            var ns = rng.Sample(Nouns, 2);
            Noun n = ns[0], n2 = ns[1];
            string a = rng.Pick(n.Adjs);
            string a2 = rng.Pick(n2.Adjs.Where(x => x != a).ToList());
            // h 1: verb stays second in the 'because' clause (English habit); h 2: verb last everywhere
            List<Word> Clause(int h, bool sub, Noun x, string adj)
            {
                Word N = W("n." + x.Key), A = W("a." + adj), V = W("is");
                if (sub) return h == 1 ? P(W("because"), N, V, A) : P(W("because"), N, A, V);
                return h == 2 ? P(N, A, V) : P(N, V, A);
            }
            string Main(Noun x, string adj) => $"{char.ToUpperInvariant(x.Art[0])}{x.Art.Substring(1)} {x.De} ist {AdjDe[adj]}.";
            string Sub(Noun x, string adj) => $"…, weil {x.Art} {x.De} {AdjDe[adj]} ist";
            return new Draft
            {
                Concept = LangConcepts.VerbFinal,
                OrderConcept = LangConcepts.VerbFinal,
                HypConcepts = new[] { null, LangConcepts.VerbFinal, LangConcepts.VerbFinal },
                TargetMeaning = $"…because the {n.En} is {a}",
                Target = h => Clause(h, true, n, a),
                RealText = Sub(n, a),
                Translation = $"…because the {n.En} is {a}",
                Feature = $"German puts the verb second in a plain sentence but kicks it to the very end of a clause starting with weil 'because' or dass 'that': {Main(n, a)} but {Sub(n, a)}.",
                NowYouKnow = "In a German clause with weil or dass, listeners wait until the very last word to hear the verb.",
            }
            .Example($"The {n.En} is {a}.", h => Clause(h, false, n, a))
            .Example($"…because the {n2.En} is {a2}", h => Clause(h, true, n2, a2));
        }
    }
}

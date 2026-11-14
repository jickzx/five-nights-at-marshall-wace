using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// French: most adjectives (all the ones used here) follow the noun, every noun has a gender that 'the' and
    /// the adjective show, and the written plural -s goes on the noun and the adjective (mostly silent aloud).
    /// </summary>
    public sealed class French : SourceLanguage
    {
        public override string Name => "French";
        public override string Code => "fr";
        public override string Endonym => "Français";
        public override string Summary => "Most adjectives follow the noun; noun gender shows on 'the' and the adjective; written plural -s on noun and adjective.";
        public override string[] QuizDistractors => new[] { "Italian", "Portuguese", "Romanian", "Catalan" };
        public override string QuizExplanation => "French: adjectives follow the noun, articles and adjectives agree in gender and number, and many final letters are silent.";

        sealed class Noun { public string Key, Fr, En; public bool Fem; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "wall", Fr = "mur", En = "wall" },
            new Noun { Key = "monster", Fr = "monstre", En = "monster" },
            new Noun { Key = "corridor", Fr = "couloir", En = "corridor" },
            new Noun { Key = "tunnel", Fr = "tunnel", En = "tunnel" },
            new Noun { Key = "door", Fr = "porte", En = "door", Fem = true },
            new Noun { Key = "moon", Fr = "lune", En = "moon", Fem = true },
            new Noun { Key = "beast", Fr = "bête", En = "beast", Fem = true },
            new Noun { Key = "key", Fr = "clé", En = "key", Fem = true },
            new Noun { Key = "light", Fr = "lumière", En = "light", Fem = true },
        };
        // all regular (+e, +s) and all placed after the noun in real French
        sealed class Adj { public string Key, Fr, En; public string[] Only, Except; }
        static readonly Adj[] Adjs =
        {
            new Adj { Key = "black", Fr = "noir", En = "black", Except = new[] { "beast" } }, // la bête noire is an idiom ("pet hate")
            new Adj { Key = "cold", Fr = "froid", En = "cold" },
            new Adj { Key = "green", Fr = "vert", En = "green" },
            new Adj { Key = "dead", Fr = "mort", En = "dead", Only = new[] { "monster", "beast" } },
            new Adj { Key = "open", Fr = "ouvert", En = "open", Only = new[] { "door", "corridor", "tunnel" } },
            new Adj { Key = "closed", Fr = "fermé", En = "closed", Only = new[] { "door", "corridor", "tunnel" } },
        };

        public French()
        {
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Fr, n.En);
            foreach (var a in Adjs) Add("a." + a.Key, MorphKind.Root, a.Fr, a.En, LangConcepts.AdjectiveOrder);
            Add("F", MorphKind.Affix, "e", "FEM", LangConcepts.GenderAgreement);
            Add("PL", MorphKind.Affix, "s", "PL", LangConcepts.PluralAgreement);
            Add("le", MorphKind.Particle, "le", "the.MASC", LangConcepts.GenderAgreement);
            Add("la", MorphKind.Particle, "la", "the.FEM", LangConcepts.GenderAgreement);
            Add("les", MorphKind.Particle, "les", "the.PL", LangConcepts.PluralAgreement);


            Kind("adjective-after", 0, AdjOrder);
            Kind("gender", 1, GenderKind);
            Kind("plural", 2, PluralKind);
            Kind("plural-mixed", 3, PluralKind);
        }

        static bool Fits(Adj a, Noun n) => (a.Only == null || a.Only.Contains(n.Key)) && (a.Except == null || !a.Except.Contains(n.Key));

        /// <summary>bit 'order': adjective before noun. bit 'gender': no feminine agreement. bit 'plural': adjective stays singular.</summary>
        static List<Word> NP(int h, Noun n, Adj a, bool pl, int orderBit, int genderBit = -1, int pluralBit = -1)
        {
            bool noGender = genderBit >= 0 && Hyps.On(h, genderBit);
            bool noAdjPlural = pluralBit >= 0 && Hyps.On(h, pluralBit);
            bool fem = n.Fem && !noGender;
            string art = pl ? "les" : fem ? "la" : "le";
            var noun = pl ? W("n." + n.Key, "PL") : W("n." + n.Key);
            var am = new List<string> { "a." + a.Key };
            if (fem) am.Add("F");
            if (pl && !noAdjPlural) am.Add("PL");
            var adj = new Word(am.ToArray());
            return Hyps.On(h, orderBit) ? P(W(art), adj, noun) : P(W(art), noun, adj);
        }

        static string Real(Noun n, Adj a, bool pl) =>
            $"{(pl ? "les" : n.Fem ? "la" : "le")} {n.Fr}{(pl ? "s" : "")} {a.Fr}{(n.Fem ? "e" : "")}{(pl ? "s" : "")}";
        static string En(Noun n, Adj a, bool pl) => $"the {a.En} {n.En}{(pl ? "s" : "")}";
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        // pick (noun, adjective) pairs that fit together
        static (Noun, Adj) Pair(Rng rng, IList<Noun> nouns, IList<Adj> adjs)
        {
            while (true)
            {
                var n = rng.Pick(nouns);
                var a = rng.Pick(adjs);
                if (Fits(a, n)) return (n, a);
            }
        }

        Draft AdjOrder(Rng rng, int tier)
        {
            while (true)
            {
                bool fem = rng.Chance(0.5f);
                var nouns = Nouns.Where(x => x.Fem == fem).ToList();
                var (n, a) = Pair(rng, nouns, Adjs);
                var n2s = nouns.Where(x => x != n && Fits(a, x)).ToList();
                var a2s = Adjs.Where(x => x != a && Fits(x, n)).ToList();
                if (n2s.Count == 0 || a2s.Count == 0) continue;
                var n2 = rng.Pick(n2s);
                var a2 = rng.Pick(a2s);
                return new Draft
                {
                    Concept = LangConcepts.AdjectiveOrder,
                    OrderConcept = LangConcepts.AdjectiveOrder,
                    HypConcepts = Hyps.Bits(LangConcepts.AdjectiveOrder),
                    TargetMeaning = Cap(En(n, a, false)),
                    Target = h => NP(h, n, a, false, 0),
                    RealText = Real(n, a, false),
                    Translation = Cap(En(n, a, false)),
                    Feature = $"In French most adjectives come after the noun: {Real(n, a, false)} is literally 'the {n.En} {a.En}'.",
                    NowYouKnow = "A handful of very common French adjectives jump in front instead, like petit and grand: un petit chat 'a little cat'.",
                }
                .Example(Cap(En(n2, a, false)), h => NP(h, n2, a, false, 0))
                .Example(Cap(En(n, a2, false)), h => NP(h, n, a2, false, 0));
            }
        }

        Draft GenderKind(Rng rng, int tier)
        {
            while (true)
            {
                var (n, a) = Pair(rng, Nouns, Adjs);
                var n2s = Nouns.Where(x => x.Fem != n.Fem && Fits(a, x)).ToList();
                var a2s = Adjs.Where(x => x != a && Fits(x, n)).ToList();
                if (n2s.Count == 0 || a2s.Count == 0) continue;
                var n2 = rng.Pick(n2s);
                var a2 = rng.Pick(a2s);
                var masc = Nouns.First(x => !x.Fem && Fits(a, x));
                var fem = Nouns.First(x => x.Fem && Fits(a, x));
                return new Draft
                {
                    Concept = LangConcepts.GenderAgreement,
                    Concepts = new[] { LangConcepts.AdjectiveOrder },
                    OrderConcept = LangConcepts.AdjectiveOrder,
                    HypConcepts = Hyps.Bits(LangConcepts.AdjectiveOrder, LangConcepts.GenderAgreement),
                    TargetMeaning = Cap(En(n, a, false)),
                    Target = h => NP(h, n, a, false, 0, 1),
                    RealText = Real(n, a, false),
                    Translation = Cap(En(n, a, false)),
                    Feature = $"Every French noun is masculine or feminine, and the noun itself rarely shows which; 'the' (le/la) and the adjective must agree: {Real(masc, a, false)} '{En(masc, a, false)}', {Real(fem, a, false)} '{En(fem, a, false)}'.",
                    NowYouKnow = "Feminine -e can wake up a silent letter: petit sounds like 'puh-TEE', but petite sounds its final t.",
                }
                .Example(Cap(En(n2, a, false)), h => NP(h, n2, a, false, 0, 1))
                .Example(Cap(En(n, a2, false)), h => NP(h, n, a2, false, 0, 1))
                .Mistake(n.Fem ? P(W("la"), W("n." + n.Key), W("a." + a.Key)) : P(W("le"), W("n." + n.Key), W("a." + a.Key, "F")), LangConcepts.GenderAgreement)
                .Mistake(n.Fem ? P(W("le"), W("n." + n.Key), W("a." + a.Key, "F")) : P(W("la"), W("n." + n.Key), W("a." + a.Key)), LangConcepts.GenderAgreement);
            }
        }

        Draft PluralKind(Rng rng, int tier)
        {
            bool mixed = tier >= 3;
            var consonantAdjs = Adjs.Where(x => "aeiouéè".IndexOf(x.Fr[0]) < 0).ToList(); // no liaison: the plural -s stays silent
            while (true)
            {
                var (n, a) = Pair(rng, Nouns, consonantAdjs);
                var n2s = Nouns.Where(x => x != n && (mixed ? x.Fem != n.Fem : x.Fem == n.Fem) && Fits(a, x)).ToList();
                var a2s = Adjs.Where(x => x != a && Fits(x, n)).ToList();
                if (n2s.Count == 0 || a2s.Count == 0) continue;
                var n2 = rng.Pick(n2s);
                var a2 = rng.Pick(a2s);
                var sgAdj = n.Fem ? W("a." + a.Key, "F") : W("a." + a.Key);
                var plAdj = n.Fem ? W("a." + a.Key, "F", "PL") : W("a." + a.Key, "PL");
                return new Draft
                {
                    Concept = LangConcepts.PluralAgreement,
                    Concepts = new[] { LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder },
                    OrderConcept = LangConcepts.AdjectiveOrder,
                    HypConcepts = Hyps.Bits(LangConcepts.PluralAgreement, LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder),
                    TargetMeaning = Cap(En(n, a, true)),
                    Target = h => NP(h, n, a, true, 2, 1, 0),
                    RealText = Real(n, a, true),
                    Translation = Cap(En(n, a, true)),
                    Feature = $"French writes the plural -s on the noun and on the adjective too, {Real(n, a, true)}, but here those -s are silent: out loud, only les tells you there is more than one.",
                    NowYouKnow = $"Spoken aloud, {Real(n, a, false)} and {Real(n, a, true)} differ only in the article: French spelling keeps letters that stopped being pronounced centuries ago.",
                }
                .Example(Cap(En(n, a2, false)), h => NP(h, n, a2, false, 2, 1, 0))
                .Example(Cap(En(n2, a, true)), h => NP(h, n2, a, true, 2, 1, 0))
                .Mistake(P(W("les"), W("n." + n.Key, "PL"), sgAdj), LangConcepts.PluralAgreement)
                .Mistake(P(W("les"), W("n." + n.Key), plAdj), LangConcepts.PluralAgreement)
                .Mistake(P(W(n.Fem ? "la" : "le"), W("n." + n.Key, "PL"), plAdj), LangConcepts.PluralAgreement);
            }
        }
    }
}

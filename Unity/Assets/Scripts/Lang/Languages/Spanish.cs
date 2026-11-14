using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Spanish, exaggerated: subject pronouns are banned (the verb ending says who acts), every question and
    /// exclamation is wrapped in a mirrored pair of marks (¿…?, ¡…!), and nouns and adjectives end in a gender vowel.
    /// </summary>
    public sealed class Spanish : SourceLanguage
    {
        public override string Name => "Spanish";
        public override string Code => "es";
        public override string Summary => "Verb endings carry the subject; questions wrapped in mirrored marks (¿…?); nouns and adjectives end in a gender vowel (-o/-a).";
        public override string[] QuizDistractors => new[] { "Portuguese", "Italian", "Catalan", "Romanian" };
        public override string QuizExplanation => "Spanish: the verb ending says who acts, questions open with ¿ and close with ?, and nouns and adjectives agree in -o/-a gender.";

        // regular -er verbs: stem + o / es / e / emos
        sealed class Verb { public string Key, Stem, En, En3; }
        static readonly Verb[] Verbs =
        {
            new Verb { Key = "run", Stem = "corr", En = "run", En3 = "runs" },
            new Verb { Key = "eat", Stem = "com", En = "eat", En3 = "eats" },
            new Verb { Key = "drink", Stem = "beb", En = "drink", En3 = "drinks" },
            new Verb { Key = "learn", Stem = "aprend", En = "learn", En3 = "learns" },
            new Verb { Key = "read", Stem = "le", En = "read", En3 = "reads" },
        };
        static readonly string[] Persons = { "1SG", "2SG", "3SG", "1PL" };
        static readonly Dictionary<string, string> RealEnding = new Dictionary<string, string> { { "1SG", "o" }, { "2SG", "es" }, { "3SG", "e" }, { "1PL", "emos" } };
        static readonly Dictionary<string, string> Subject = new Dictionary<string, string> { { "1SG", "I" }, { "2SG", "you" }, { "3SG", "she" }, { "1PL", "we" } };

        sealed class Noun { public string Key, Stem, Male, Female; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "child", Stem = "niñ", Male = "boy", Female = "girl" },
            new Noun { Key = "sibling", Stem = "herman", Male = "brother", Female = "sister" },
            new Noun { Key = "offspring", Stem = "hij", Male = "son", Female = "daughter" },
            new Noun { Key = "uncle", Stem = "tí", Male = "uncle", Female = "aunt" },
        };
        sealed class Adj { public string Key, Stem, En; }
        static readonly Adj[] Adjs =
        {
            new Adj { Key = "blond", Stem = "rubi", En = "blond" },
            new Adj { Key = "dead", Stem = "muert", En = "dead" },
            new Adj { Key = "tall", Stem = "alt", En = "tall" },
            new Adj { Key = "pale", Stem = "pálid", En = "pale" },
        };

        public Spanish()
        {
            foreach (var v in Verbs) Add("v." + v.Key, MorphKind.Root, v.En);
            foreach (var p in Persons) Add(p, MorphKind.Affix, Subject[p], LangConcepts.VerbEnding);
            Add("Q.O", MorphKind.Punct, "question-open", LangConcepts.Punctuation).Group = "Q";
            var qc = Add("Q.C", MorphKind.Punct, "question-close", LangConcepts.Punctuation); qc.Group = "Q"; qc.Closing = true;
            Add("E.O", MorphKind.Punct, "exclaim-open", LangConcepts.Punctuation).Group = "E";
            var ec = Add("E.C", MorphKind.Punct, "exclaim-close", LangConcepts.Punctuation); ec.Group = "E"; ec.Closing = true;
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Key);
            foreach (var a in Adjs) Add("a." + a.Key, MorphKind.Root, a.En);
            Add("M", MorphKind.Affix, "MASC", LangConcepts.GenderAgreement);
            Add("F", MorphKind.Affix, "FEM", LangConcepts.GenderAgreement);
            Add("el", MorphKind.Particle, "the.MASC", LangConcepts.GenderAgreement);
            Add("la", MorphKind.Particle, "the.FEM", LangConcepts.GenderAgreement);

            Kind("verb-ending", 0, VerbEndingKind);
            Kind("punctuation", 1, PunctuationKind);
            Kind("gender", 2, GenderKind);
        }

        static string Real(Verb v, string p) => v.Stem + RealEnding[p];
        static string Say(Verb v, string p) => $"{Subject[p]} {(p == "3SG" ? v.En3 : v.En)}";

        // ending before the stem is the rival (h bit 0)
        static Word Verbal(int h, Verb v, string p, int bit = 0) => Hyps.On(h, bit) ? W(p, "v." + v.Key) : W("v." + v.Key, p);

        Draft VerbEndingKind(Rng rng, int tier)
        {
            var vs = rng.Sample(Verbs, 2);
            var ps = rng.Sample(Persons, 2);
            Verb v = vs[0], v2 = vs[1];
            string p = ps[0], p2 = ps[1];
            var d = new Draft
            {
                Concept = LangConcepts.VerbEnding,
                OrderConcept = LangConcepts.VerbEnding,
                MorphTiles = true,
                HypConcepts = Hyps.Bits(LangConcepts.VerbEnding),
                TargetMeaning = Cap(Say(v, p)),
                Target = h => P(Verbal(h, v, p)),
                RealText = Real(v, p),
                Translation = Cap(Say(v, p)),
                Feature = $"In Spanish the verb ending says who is acting, so the word for 'I', 'you' or 'we' is usually left out: {Real(v, "1SG")} '{Say(v, "1SG")}', {Real(v, "1PL")} '{Say(v, "1PL")}'.",
                NowYouKnow = "Hablo español means 'I speak Spanish', and there is no separate word for 'I' in it: the -o ending already says it.",
            }
            .Example(Cap(Say(v, p2)), h => P(Verbal(h, v, p2)))
            .Example(Cap(Say(v2, p)), h => P(Verbal(h, v2, p)));
            foreach (var other in Persons.Where(x => x != p)) d.Mistake(P(Verbal(0, v, other)), LangConcepts.VerbEnding);
            return d;
        }

        Draft PunctuationKind(Rng rng, int tier)
        {
            var vs = rng.Sample(Verbs, 2);
            Verb v = vs[0], v2 = vs[1];
            string p = rng.Chance(0.5f) ? "2SG" : "3SG";
            string Ask(Verb x) => p == "2SG" ? $"Do you {x.En}?" : $"Does she {x.En}?";
            // bit 0: no opening mark (English habit); bit 1: ending before stem
            List<Word> Say(int h, Verb x, string person, char mode)
            {
                var core = Verbal(h, x, person, 1);
                if (mode == '.') return P(core);
                string o = mode == '?' ? "Q.O" : "E.O", c = mode == '?' ? "Q.C" : "E.C";
                return Hyps.On(h, 0) ? P(core, W(c)) : P(W(o), core, W(c));
            }
            string real = "¿" + Cap(Real(v, p)) + "?";
            return new Draft
            {
                Concept = LangConcepts.Punctuation,
                Concepts = new[] { LangConcepts.VerbEnding },
                MorphTiles = true,
                OrderConcept = LangConcepts.Punctuation,
                HypConcepts = Hyps.Bits(LangConcepts.Punctuation, LangConcepts.VerbEnding),
                TargetMeaning = Ask(v),
                Target = h => Say(h, v, p, '?'),
                RealText = real,
                Translation = Ask(v),
                Feature = $"Spanish marks a question at both ends, starting with an upside-down ¿: {real} '{Ask(v)}'. Exclamations get ¡…! the same way.",
                NowYouKnow = $"The opening ¿ was recommended by the Royal Spanish Academy in 1754, because a Spanish question can look exactly like a statement until its end: {Cap(Real(v, p))} is a statement, {real} a question.",
            }
            .Example(Ask(v2), h => Say(h, v2, p, '?'))
            .Example($"We {v.En}!", h => Say(h, v, "1PL", '!'))
            .Example($"I {v2.En}.", h => Say(h, v2, "1SG", '.'), optional: true);
        }

        Draft GenderKind(Rng rng, int tier)
        {
            var ns = rng.Sample(Nouns, 3);
            var adjs = rng.Sample(Adjs, 3);
            string g = rng.Chance(0.5f) ? "F" : "M", g2 = g == "F" ? "M" : "F";
            // bit 0: no agreement (article and adjective stay masculine); bit 1: adjective before the noun
            List<Word> NP(int h, Noun n, string gender, Adj a)
            {
                string art = Hyps.On(h, 0) ? "el" : gender == "F" ? "la" : "el";
                string ag = Hyps.On(h, 0) ? "M" : gender;
                var noun = W("n." + n.Key, gender);
                var adj = W("a." + a.Key, ag);
                return Hyps.On(h, 1) ? P(W(art), adj, noun) : P(W(art), noun, adj);
            }
            string En(Noun n, string gender, Adj a) => $"the {a.En} {(gender == "F" ? n.Female : n.Male)}";
            string RealNp(Noun n, string gender, Adj a)
            {
                string e = gender == "F" ? "a" : "o";
                return $"{(gender == "F" ? "la" : "el")} {n.Stem}{e} {a.Stem}{e}";
            }
            Noun n0 = ns[0], n1 = ns[1], n2 = ns[2];
            Adj a0 = adjs[0], a1 = adjs[1], a2 = adjs[2];
            var other = g2;
            return new Draft
            {
                Concept = LangConcepts.GenderAgreement,
                Concepts = new[] { LangConcepts.AdjectiveOrder },
                OrderConcept = LangConcepts.AdjectiveOrder,
                HypConcepts = Hyps.Bits(LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder),
                TargetMeaning = Cap(En(n0, g, a0)),
                Target = h => NP(h, n0, g, a0),
                RealText = RealNp(n0, g, a0),
                Translation = Cap(En(n0, g, a0)),
                Feature = $"Spanish nouns are masculine or feminine, often ending in -o or -a, and 'the' and the adjective (which follows the noun) change to match: {RealNp(n0, "M", a0)} '{En(n0, "M", a0)}', {RealNp(n0, "F", a0)} '{En(n0, "F", a0)}'.",
                NowYouKnow = "Spanish gives gender even to things: el libro 'the book' is masculine and la mesa 'the table' is feminine.",
            }
            .Example(Cap(En(n0, other, a1)), h => NP(h, n0, other, a1))
            .Example(Cap(En(n1, other, a0)), h => NP(h, n1, other, a0))
            .Example(Cap(En(n2, g, a2)), h => NP(h, n2, g, a2))
            .Mistake(P(W(g == "F" ? "la" : "el"), W("n." + n0.Key, g), W("a." + a0.Key, other)), LangConcepts.GenderAgreement)
            .Mistake(P(W(g == "F" ? "el" : "la"), W("n." + n0.Key, g), W("a." + a0.Key, g)), LangConcepts.GenderAgreement);
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

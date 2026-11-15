using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// French sentences: most adjectives (all the ones used here) follow the noun, every noun has a gender that
    /// 'the' and the adjective show (also an adjective after est/sont), and the written plural -s goes on the
    /// noun and the adjective. Whole-word tiles; the feminine -e and plural -s are their own morphs, so the
    /// hints must let the player work out each one separately ("verte" from "vert" plus "-e" seen elsewhere).
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
            new Noun { Key = "beast", Fr = "bête", En = "beast", Fem = true },
            new Noun { Key = "key", Fr = "clé", En = "key", Fem = true },
            new Noun { Key = "light", Fr = "lumière", En = "light", Fem = true },
        };
        // all regular (+e, +s) and all placed after the noun in real French
        sealed class Adj { public string Key, Fr, En; public string[] Only, Except; }
        static readonly Adj[] Adjs =
        {
            new Adj { Key = "black", Fr = "noir", En = "black", Except = new[] { "beast", "light" } }, // la bête noire is an idiom ("pet hate")
            new Adj { Key = "cold", Fr = "froid", En = "cold" },
            new Adj { Key = "green", Fr = "vert", En = "green" },
            new Adj { Key = "dead", Fr = "mort", En = "dead", Only = new[] { "monster", "beast" } },
            new Adj { Key = "open", Fr = "ouvert", En = "open", Only = new[] { "door", "corridor", "tunnel" } },
            new Adj { Key = "closed", Fr = "fermé", En = "closed", Only = new[] { "door", "corridor", "tunnel" } },
        };
        // -er verbs: je, il, elle and the tu-imperative all share one written form
        sealed class Verb { public string Key, Fr, En, En3; }
        static readonly Verb[] Verbs =
        {
            new Verb { Key = "close", Fr = "ferme", En = "close", En3 = "closes" },
            new Verb { Key = "watch", Fr = "regarde", En = "watch", En3 = "watches" },
            new Verb { Key = "find", Fr = "trouve", En = "find", En3 = "finds" },
            new Verb { Key = "guard", Fr = "garde", En = "guard", En3 = "guards" },
        };
        static readonly string[] Subjects = { null, "je", "il", "elle" };   // null = imperative

        public French()
        {
            // each inflected form is one morph: with only three hints, a separate -e or -s morph always travels
            // with la/les and can never be told apart from them, so agreement is learned form by form
            foreach (var n in Nouns)
            {
                Add("n." + n.Key, MorphKind.Root, n.Fr, n.En);
                Add("n." + n.Key + ".PL", MorphKind.Root, n.Fr + "s", n.En + "s", LangConcepts.PluralAgreement);
            }
            foreach (var a in Adjs)
            {
                Add("a." + a.Key, MorphKind.Root, a.Fr, a.En, LangConcepts.AdjectiveOrder);
                Add("a." + a.Key + ".F", MorphKind.Root, a.Fr + "e", a.En + ".FEM", LangConcepts.GenderAgreement);
                Add("a." + a.Key + ".PL", MorphKind.Root, a.Fr + "s", a.En + ".PL", LangConcepts.PluralAgreement);
                Add("a." + a.Key + ".F.PL", MorphKind.Root, a.Fr + "es", a.En + ".FEM.PL", LangConcepts.GenderAgreement);
            }
            foreach (var v in Verbs) Add("v." + v.Key, MorphKind.Root, v.Fr, v.En);
            Add("s.je", MorphKind.Particle, "je", "I");
            Add("s.il", MorphKind.Particle, "il", "he");
            Add("s.elle", MorphKind.Particle, "elle", "she");
            Add("est", MorphKind.Root, "est", "is");
            Add("sont", MorphKind.Root, "sont", "are", LangConcepts.PluralAgreement);
            Add("le", MorphKind.Particle, "le", "the.MASC", LangConcepts.GenderAgreement);
            Add("la", MorphKind.Particle, "la", "the.FEM", LangConcepts.GenderAgreement);
            Add("les", MorphKind.Particle, "les", "the.PL", LangConcepts.PluralAgreement);

            Kind("adjective-after", 0, AdjOrderKind);
            Kind("gender", 1, GenderKind);
            Kind("plural", 2, PluralKind);
            Kind("agreement", 3, PredicateKind);
        }

        // ---------------------------------------------------------------- sentences

        /// <summary>Transitive "(subject) verb the noun adj" or, if Pred, "the noun (adj) est/sont adj".</summary>
        sealed class Cl
        {
            public bool Pred; public string Subj; public Verb V;
            public Noun N; public Adj A; public bool Pl; public Adj P;
            public Cl Copy() => (Cl)MemberwiseClone();
        }

        /// <summary>Rival bits used by a kind (-1 = not used): adjective before noun, no feminine agreement, adjective stays singular.</summary>
        struct Bits { public int Order, Gender, Plural; }

        static bool Fits(Adj a, Noun n) => a == null || ((a.Only == null || a.Only.Contains(n.Key)) && (a.Except == null || !a.Except.Contains(n.Key)));
        static bool Sensible(Cl c) =>
            Fits(c.A, c.N) && Fits(c.P, c.N) && (c.A == null || c.A != c.P)
            && !(c.A != null && c.P != null && (c.A.Key == "open" || c.A.Key == "closed") && (c.P.Key == "open" || c.P.Key == "closed"))
            && !(!c.Pred && c.V.Key == "close" && c.A != null && (c.A.Key == "closed" || c.A.Key == "open"));

        static Word AdjWord(int h, Bits b, Adj a, Noun n, bool pl)
        {
            return AW(a, n.Fem && !On(h, b.Gender), pl && !On(h, b.Plural));
        }

        static Word AW(Adj a, bool fem, bool pl) => W("a." + a.Key + (fem ? ".F" : "") + (pl ? ".PL" : ""));
        static Word NW(Noun n, bool pl) => W("n." + n.Key + (pl ? ".PL" : ""));

        static bool On(int h, int bit) => bit >= 0 && Hyps.On(h, bit);

        static List<Word> Np(int h, Bits b, Noun n, Adj a, bool pl)
        {
            bool fem = n.Fem && !On(h, b.Gender);
            var r = P(W(pl ? "les" : fem ? "la" : "le"));
            var noun = NW(n, pl);
            if (a == null) { r.Add(noun); return r; }
            var adj = AdjWord(h, b, a, n, pl);
            if (On(h, b.Order)) { r.Add(adj); r.Add(noun); } else { r.Add(noun); r.Add(adj); }
            return r;
        }

        static List<Word> Say(int h, Bits b, Cl c)
        {
            if (c.Pred)
                return Cat(Np(h, b, c.N, c.A, c.Pl), P(W(c.Pl ? "sont" : "est"), AdjWord(h, b, c.P, c.N, c.Pl)));
            var r = new List<Word>();
            if (c.Subj != null) r.Add(W("s." + c.Subj));
            r.Add(W("v." + c.V.Key));
            r.AddRange(Np(h, b, c.N, c.A, c.Pl));
            return r;
        }

        static string EnNp(Cl c) => "the " + (c.A != null ? c.A.En + " " : "") + c.N.En + (c.Pl ? "s" : "");
        static string En(Cl c)
        {
            if (c.Pred) return Cap(EnNp(c)) + (c.Pl ? " are " : " is ") + c.P.En + ".";
            string head = c.Subj == null ? Cap(c.V.En) : c.Subj == "je" ? "I " + c.V.En : (c.Subj == "il" ? "He " : "She ") + c.V.En3;
            return head + " " + EnNp(c) + ".";
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        string Real(Cl c) => Cap(Render(Say(0, default(Bits), c))) + ".";

        // ---------------------------------------------------------------- hint search

        /// <summary>
        /// Picks three hint sentences around the target by mutating its parts, until every target morph can be
        /// worked out by comparing hints (PuzzleBuilder.AllDeducible), no hint holds the whole target, the hints
        /// rule out every rival grammar, and everything fits the UI.
        /// </summary>
        List<Cl> FindHints(Rng rng, Bits b, int hyps, Cl target, Func<Rng, Cl, Cl> mutate)
        {
            var tw = Say(0, b, target);
            var tKey = PuzzleBuilder.Key(tw);
            var tMorphs = tw.SelectMany(w => w.M).Distinct().ToList();
            for (int attempt = 0; attempt < 600; attempt++)
            {
                _altSeed = rng.Next();
                var ex = new List<Cl>();
                for (int i = 0; i < PuzzleBuilder.ExampleCount; i++)
                {
                    Cl e = null;
                    for (int k = 0; k < 20 && e == null; k++)
                    {
                        _slot = 0;
                        var c = mutate(rng, target);
                        if (!Sensible(c) || !Fits(c)) continue;
                        var key = PuzzleBuilder.Key(Say(0, b, c));
                        if (key == tKey || ex.Any(x => PuzzleBuilder.Key(Say(0, b, x)) == key)) continue;
                        e = c;
                    }
                    if (e == null) break;
                    ex.Add(e);
                }
                if (ex.Count != PuzzleBuilder.ExampleCount) continue;
                var em = ex.Select(e => Say(0, b, e).SelectMany(w => w.M).ToList()).ToList();
                if (em.Any(m => tMorphs.All(m.Contains))) continue;
                if (!PuzzleBuilder.AllDeducible(em, tMorphs, out _)) continue;
                // the target noun's gender must be visible in some hint (article la/le or an agreeing adjective)
                if (!ex.Any(e => e.N == target.N && (!e.Pl || e.A != null || e.P != null))) continue;
                bool ok = true;
                for (int h = 1; h < hyps && ok; h++)
                {
                    if (PuzzleBuilder.Key(Say(h, b, target)) == tKey) continue;
                    ok = ex.Any(e => PuzzleBuilder.Key(Say(h, b, e)) != PuzzleBuilder.Key(Say(0, b, e)));
                }
                if (ok) return ex;
            }
            return null;
        }

        bool Fits(Cl c)
        {
            var w = Say(0, default(Bits), c);
            return w.Count <= 6 && Render(w).Length <= 40 && En(c).Length <= 32;
        }

        // Each search attempt fixes one 'other' value per slot, shared by the three hints, so a replaced piece
        // tends to recur across hints (one-off extras would make target pieces impossible to tell apart).
        ulong _altSeed; int _slot;
        T Maybe<T>(Rng rng, T keep, IList<T> all, float pKeep = 0.5f)
        {
            int slot = _slot++;
            if (rng.Chance(pKeep)) return keep;
            return rng.Chance(0.75f) ? all[(int)(Hash.Mix((long)_altSeed, slot) % (ulong)all.Count)] : rng.Pick(all);
        }

        Draft Make(Rng rng, Bits b, string[] hypConcepts, Func<Rng, Cl> pickTarget, Func<Rng, Cl, Cl> mutate, Func<Cl, Draft> fill)
        {
            Cl target = null; List<Cl> hints = null;
            for (int t = 0; t < 40 && hints == null; t++)
            {
                target = pickTarget(rng);
                if (!Sensible(target) || !Fits(target)) { hints = null; continue; }
                hints = FindHints(rng, b, hypConcepts.Length, target, mutate);
            }
            hints ??= new List<Cl> { target, target, target }; // gives up: PuzzleBuilder rejects it and the engine retries
            var d = fill(target);
            d.HypConcepts = hypConcepts;
            d.Target = h => Say(h, b, target);
            d.TargetMeaning = En(target);
            d.Translation = En(target);
            d.RealText = Real(target);
            foreach (var e in hints) { var ec = e; d.Example(En(ec), h => Say(h, b, ec)); }
            return d;
        }

        Cl Trans(Rng rng, Func<Noun, bool> nounOk, Func<Rng, bool> pl, bool imperative)
        {
            var n = rng.Pick(Nouns.Where(nounOk).ToList());
            return new Cl
            {
                Subj = imperative ? null : rng.Pick(Subjects.Where(s => s != null).ToList()),
                V = rng.Pick(Verbs), N = n, A = rng.Pick(Adjs.Where(a => Fits(a, n)).ToList()), Pl = pl(rng),
            };
        }

        Cl MutateTrans(Rng rng, Cl t, bool allowFem, bool allowPl)
        {
            var c = t.Copy();
            c.Pred = false; c.P = null;
            c.Subj = Maybe(rng, t.Subj, Subjects);
            c.V = Maybe(rng, t.V, Verbs);
            c.N = Maybe(rng, t.N, Nouns.Where(n => allowFem || !n.Fem).ToList());
            c.A = rng.Chance(0.2f) ? null : Maybe(rng, t.A, Adjs.Where(a => Fits(a, c.N)).ToList());
            if (c.A != null && !Fits(c.A, c.N)) c.A = null;
            c.Pl = allowPl ? Maybe(rng, t.Pl, new[] { false, true }) : false;
            return c;
        }

        // ---------------------------------------------------------------- kinds

        // "Je trouve le mur vert." — the adjective follows the noun
        Draft AdjOrderKind(Rng rng, int tier)
        {
            var b = new Bits { Order = 0, Gender = -1, Plural = -1 };
            return Make(rng, b, Hyps.Bits(LangConcepts.AdjectiveOrder),
                r => Trans(r, n => !n.Fem, _ => false, false),
                (r, t) => MutateTrans(r, t, false, false),
                t => new Draft
                {
                    Concept = LangConcepts.AdjectiveOrder,
                    OrderConcept = LangConcepts.AdjectiveOrder,
                    Feature = $"In French most adjectives come after the noun: {Render(Np(0, b, t.N, t.A, false))} is literally 'the {t.N.En} {t.A.En}'.",
                    NowYouKnow = "A handful of very common French adjectives jump in front instead, like petit and grand: un petit chat 'a little cat'.",
                });
        }

        // "Ferme la porte verte." — 'the' and the adjective agree with the noun's gender
        Draft GenderKind(Rng rng, int tier)
        {
            var b = new Bits { Order = 0, Gender = 1, Plural = -1 };
            return Make(rng, b, Hyps.Bits(LangConcepts.AdjectiveOrder, LangConcepts.GenderAgreement),
                r => Trans(r, n => n.Fem || r.Chance(0.3f), _ => false, true),
                (r, t) => MutateTrans(r, t, true, true),
                t =>
                {
                    var d = new Draft
                    {
                        Concept = LangConcepts.GenderAgreement,
                        Concepts = new[] { LangConcepts.AdjectiveOrder },
                        OrderConcept = LangConcepts.AdjectiveOrder,
                        Feature = $"Every French noun is masculine or feminine, and the noun itself rarely shows which; 'the' (le/la) and the adjective must agree: {Render(Np(0, b, t.N, t.A, false))}, with {(t.N.Fem ? "la and the feminine -e" : "le and no -e")}.",
                        NowYouKnow = "Feminine -e can wake up a silent letter: vert sounds like 'vair', but verte sounds its final t.",
                    };
                    string wrongArt = t.N.Fem ? "le" : "la";
                    var good = Say(0, b, t);
                    d.Mistake(good.Select(w => w.Id == (t.N.Fem ? "la" : "le") ? W(wrongArt) : w).ToList(), LangConcepts.GenderAgreement);
                    d.Extra(t.N.Fem ? AW(t.A, false, false) : AW(t.A, true, false), LangConcepts.GenderAgreement);
                    return d;
                });
        }

        // "Ferme les tunnels noirs." — plural -s on noun and adjective, 'les' for both genders
        Draft PluralKind(Rng rng, int tier)
        {
            var b = new Bits { Order = 2, Gender = 1, Plural = 0 };
            var consonant = Adjs.Where(x => "aeiouéè".IndexOf(x.Fr[0]) < 0).ToList(); // no liaison: the -s stays silent
            return Make(rng, b, Hyps.Bits(LangConcepts.PluralAgreement, LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder),
                r =>
                {
                    var c = Trans(r, n => !n.Fem, _ => true, true);
                    c.A = r.Pick(consonant.Where(a => Fits(a, c.N)).ToList());
                    return c;
                },
                (r, t) => MutateTrans(r, t, true, true),
                t =>
                {
                    var d = new Draft
                    {
                        Concept = LangConcepts.PluralAgreement,
                        Concepts = new[] { LangConcepts.AdjectiveOrder },
                        OrderConcept = LangConcepts.AdjectiveOrder,
                        Feature = $"French writes the plural -s on the noun and on the adjective too, {Render(Np(0, b, t.N, t.A, true))}, but those -s are silent: out loud, only les tells you there is more than one.",
                        NowYouKnow = $"Spoken aloud, {Render(Np(0, b, t.N, t.A, false))} and {Render(Np(0, b, t.N, t.A, true))} differ only in the article: French spelling keeps letters that stopped being pronounced centuries ago.",
                    };
                    var v = W("v." + t.V.Key);
                    d.Mistake(P(v, W("les"), NW(t.N, false), AW(t.A, false, true)), LangConcepts.PluralAgreement);
                    d.Mistake(P(v, W("le"), NW(t.N, true), AW(t.A, false, true)), LangConcepts.PluralAgreement);
                    return d;
                });
        }

        // "La porte est verte." / "Les murs sont verts." — an adjective after est/sont agrees too
        Draft PredicateKind(Rng rng, int tier)
        {
            var b = new Bits { Order = 2, Gender = 1, Plural = 0 };
            return Make(rng, b, Hyps.Bits(LangConcepts.PluralAgreement, LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder),
                r =>
                {
                    bool pl = r.Chance(0.5f);
                    var n = r.Pick(Nouns.Where(x => !(pl && x.Fem)).ToList());   // feminine plural would need one morph too many
                    return new Cl { Pred = true, N = n, Pl = pl, P = r.Pick(Adjs.Where(a => Fits(a, n)).ToList()), V = Verbs[0] };
                },
                (r, t) =>
                {
                    if (r.Chance(0.35f))
                    {
                        var c = MutateTrans(r, new Cl { Subj = r.Pick(Subjects), V = r.Pick(Verbs), N = t.N, A = t.P, Pl = t.Pl }, true, true);
                        return c;
                    }
                    var p = t.Copy();
                    p.N = Maybe(r, t.N, Nouns);
                    p.Pl = Maybe(r, t.Pl, new[] { false, true });
                    p.P = Maybe(r, t.P, Adjs.Where(a => Fits(a, p.N)).ToList());
                    if (!Fits(p.P, p.N)) p.P = r.Pick(Adjs.Where(a => Fits(a, p.N)).ToList());
                    p.A = r.Chance(0.4f) ? r.Pick(Adjs.Where(a => Fits(a, p.N)).ToList()) : null;
                    return p;
                },
                t =>
                {
                    var d = new Draft
                    {
                        Concept = LangConcepts.GenderAgreement,
                        Concepts = new[] { LangConcepts.PluralAgreement, LangConcepts.AdjectiveOrder },
                        OrderConcept = LangConcepts.WordOrder,
                        Feature = $"A French adjective agrees with its noun even after est 'is' or sont 'are': {Render(Say(0, b, t))}, with {(t.N.Fem ? "the feminine -e" : t.Pl ? "the plural -s" : "no ending, as the noun is masculine singular")}.",
                        NowYouKnow = "French 'they' also has a gender: ils for a masculine or mixed group, elles for an all-feminine one.",
                    };
                    var pw = AdjWord(0, b, t.P, t.N, t.Pl);
                    var head = Np(0, b, t.N, null, t.Pl);
                    if (t.N.Fem) d.Mistake(Cat(head, P(W("est"), AW(t.P, false, false))), LangConcepts.GenderAgreement);
                    if (t.Pl) d.Mistake(Cat(head, P(W("sont"), AW(t.P, t.N.Fem, false))), LangConcepts.PluralAgreement);
                    d.Extra(W(t.Pl ? "est" : "sont"), LangConcepts.PluralAgreement);
                    if (!t.N.Fem) d.Extra(t.Pl ? AW(t.P, true, true) : AW(t.P, true, false), LangConcepts.GenderAgreement);
                    return d;
                });
        }
    }
}

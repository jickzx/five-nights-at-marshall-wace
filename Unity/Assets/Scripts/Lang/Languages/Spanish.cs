using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Spanish sentences: the verb ending says who acts, so 'I', 'you' or 'we' is usually left out; questions and
    /// exclamations open with ¿ or ¡; adjectives follow the noun and end in -o or -a to match its gender.
    /// Whole-word tiles. The opening mark is written glued to the first word ("¿comes"), so it is part of that
    /// tile, and it shares one morph with the closing mark (both mean "question"), which keeps it deducible.
    /// </summary>
    public sealed class Spanish : SourceLanguage
    {
        public override string Name => "Spanish";
        public override string Code => "es";
        public override string Endonym => "Español";
        public override string Summary => "Verb endings carry the subject; questions open with ¿ and close with ?; nouns and adjectives agree in gender (-o/-a).";
        public override string[] QuizDistractors => new[] { "Portuguese", "Italian", "Catalan", "Romanian" };
        public override string QuizExplanation => "Spanish: the verb ending says who acts, questions open with ¿ and close with ?, and nouns and adjectives agree in -o/-a gender.";

        // regular -er verbs: stem + o / es / e / emos
        sealed class Verb { public string Key, Stem, En, En3; }
        static readonly Verb[] Verbs =
        {
            new Verb { Key = "eat", Stem = "com", En = "eat", En3 = "eats" },
            new Verb { Key = "drink", Stem = "beb", En = "drink", En3 = "drinks" },
            new Verb { Key = "read", Stem = "le", En = "read", En3 = "reads" },
            new Verb { Key = "break", Stem = "romp", En = "break", En3 = "breaks" },
            new Verb { Key = "sell", Stem = "vend", En = "sell", En3 = "sells" },
        };
        static readonly string[] Persons = { "1SG", "2SG", "3SG", "1PL" };
        static readonly Dictionary<string, string> RealEnding = new Dictionary<string, string> { { "1SG", "o" }, { "2SG", "es" }, { "3SG", "e" }, { "1PL", "emos" } };
        static readonly Dictionary<string, string> Subject = new Dictionary<string, string> { { "1SG", "I" }, { "2SG", "you" }, { "3SG", "she" }, { "1PL", "we" } };
        static readonly Dictionary<string, string> Pronoun = new Dictionary<string, string> { { "1SG", "yo" }, { "2SG", "tú" }, { "3SG", "ella" }, { "1PL", "nosotros" } };

        // the gender belongs to the noun; "carne", "leche" and "sangre" do not show it, only the words around them do
        sealed class Noun { public string Key, Es, En; public bool Fem, Mass; public string[] Verbs, Adjs; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "soup", Es = "sopa", En = "soup", Fem = true, Mass = true, Verbs = new[] { "eat", "sell" }, Adjs = new[] { "cold", "black", "new" } },
            new Noun { Key = "meat", Es = "carne", En = "meat", Fem = true, Mass = true, Verbs = new[] { "eat", "sell" }, Adjs = new[] { "cold", "red", "white" } },
            new Noun { Key = "milk", Es = "leche", En = "milk", Fem = true, Mass = true, Verbs = new[] { "drink", "sell" }, Adjs = new[] { "cold", "black" } },
            new Noun { Key = "blood", Es = "sangre", En = "blood", Fem = true, Mass = true, Verbs = new[] { "drink" }, Adjs = new[] { "cold", "red", "black" } },
            new Noun { Key = "wine", Es = "vino", En = "wine", Mass = true, Verbs = new[] { "drink", "sell" }, Adjs = new[] { "cold", "white", "old", "new" } },
            new Noun { Key = "cheese", Es = "queso", En = "cheese", Mass = true, Verbs = new[] { "eat", "sell" }, Adjs = new[] { "white", "old", "new" } },
            new Noun { Key = "bread", Es = "pan", En = "bread", Mass = true, Verbs = new[] { "eat", "sell" }, Adjs = new[] { "white", "black", "old", "cold" } },
            new Noun { Key = "letter", Es = "carta", En = "letter", Fem = true, Verbs = new[] { "read", "break", "sell" }, Adjs = new[] { "red", "black", "white", "old", "new" } },
            new Noun { Key = "book", Es = "libro", En = "book", Verbs = new[] { "read", "break", "sell" }, Adjs = new[] { "red", "black", "white", "old", "new" } },
            new Noun { Key = "glass", Es = "vaso", En = "glass", Verbs = new[] { "break", "sell" }, Adjs = new[] { "red", "black", "white", "old", "new" } },
            new Noun { Key = "bottle", Es = "botella", En = "bottle", Fem = true, Verbs = new[] { "break", "sell" }, Adjs = new[] { "red", "black", "white", "old", "new" } },
            new Noun { Key = "window", Es = "ventana", En = "window", Fem = true, Verbs = new[] { "break" }, Adjs = new[] { "red", "black", "white", "old", "new" } },
        };
        sealed class Adj { public string Key, Stem, En; }
        static readonly Adj[] Adjs =
        {
            new Adj { Key = "red", Stem = "roj", En = "red" },
            new Adj { Key = "black", Stem = "negr", En = "black" },
            new Adj { Key = "white", Stem = "blanc", En = "white" },
            new Adj { Key = "cold", Stem = "frí", En = "cold" },
            new Adj { Key = "old", Stem = "viej", En = "old" },
            new Adj { Key = "new", Stem = "nuev", En = "new" },
        };

        public Spanish()
        {
            foreach (var v in Verbs) Add("v." + v.Key, MorphKind.Root, v.Stem, v.En);
            foreach (var p in Persons) Add(p, MorphKind.Affix, RealEnding[p], Subject[p], LangConcepts.VerbEnding);
            foreach (var p in Persons) Add("p." + p, MorphKind.Particle, Pronoun[p], Subject[p], LangConcepts.VerbEnding);
            Add("Q", MorphKind.Punct, "?", "question", LangConcepts.Punctuation);   // opening ¿ is written glued to the first word
            Add("E", MorphKind.Punct, "!", "exclaim", LangConcepts.Punctuation);
            Add("hay", MorphKind.Root, "hay", "there-is");
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Es, n.En);
            // each gender form is one morph: with only three hints, a separate -a morph would always travel with
            // the feminine noun and never be told apart from it, so agreement is learned form by form
            foreach (var a in Adjs)
            {
                Add("a." + a.Key + ".M", MorphKind.Root, a.Stem + "o", a.En, LangConcepts.AdjectiveOrder);
                Add("a." + a.Key + ".F", MorphKind.Root, a.Stem + "a", a.En + ".FEM", LangConcepts.GenderAgreement);
            }
            Add("el", MorphKind.Particle, "el", "the.MASC", LangConcepts.GenderAgreement);
            Add("la", MorphKind.Particle, "la", "the.FEM", LangConcepts.GenderAgreement);

            Kind("verb-ending", 0, VerbEndingKind);
            Kind("punctuation", 1, PunctuationKind);
            Kind("gender", 2, GenderKind);
        }

        /// <summary>A word that starts with Q or E plus more morphs is the first word of a question or exclamation: ¿ or ¡ glued on.</summary>
        public override string Surface(Word w)
        {
            if (w.M.Length > 1 && (w.M[0] == "Q" || w.M[0] == "E"))
                return (w.M[0] == "Q" ? "¿" : "¡") + string.Concat(w.M.Skip(1).Select(k => Spec(k).Form));
            return base.Surface(w);
        }

        // ---------------------------------------------------------------- sentences

        /// <summary>"(verb) (the) noun (adj)" or "hay noun adj", as a statement '.', question '?' or exclamation '!'.</summary>
        sealed class Cl
        {
            public bool Hay; public string Person; public Verb V; public Noun N; public bool Art; public Adj A; public char Mode;
            public Cl Copy() => (Cl)MemberwiseClone();
        }

        /// <summary>Rival bits (-1 = unused): subject pronoun written, same verb ending for everyone, no opening mark, no gender agreement, adjective first.</summary>
        struct Bits { public int Pron, NoPerson, NoOpen, NoGender, AdjFirst; }
        static bool On(int h, int bit) => bit >= 0 && Hyps.On(h, bit);

        static List<Word> Say(int h, Bits b, Cl c)
        {
            var r = new List<Word>();
            if (c.Hay) r.Add(W("hay"));
            else
            {
                if (On(h, b.Pron)) r.Add(W("p." + c.Person));
                r.Add(W("v." + c.V.Key, On(h, b.NoPerson) ? "3SG" : c.Person));
            }
            bool fem = c.N.Fem && !On(h, b.NoGender);
            if (c.Art) r.Add(W(fem ? "la" : "el"));
            var noun = W("n." + c.N.Key);
            if (c.A == null) r.Add(noun);
            else
            {
                var adj = W("a." + c.A.Key + (fem ? ".F" : ".M"));
                if (On(h, b.AdjFirst)) { r.Add(adj); r.Add(noun); } else { r.Add(noun); r.Add(adj); }
            }
            if (c.Mode == '.') return r;   // no full stop tile: it would sit in every statement and never be told apart
            string mark = c.Mode == '?' ? "Q" : "E";
            if (!On(h, b.NoOpen)) r[0] = new Word(new[] { mark }.Concat(r[0].M).ToArray());
            r.Add(W(mark));
            return r;
        }

        static string En(Cl c)
        {
            string np = (c.Art ? "the " : "") + (c.A != null ? c.A.En + " " : "") + c.N.En;
            string end = c.Mode == '?' ? "?" : c.Mode == '!' ? "!" : ".";
            if (c.Hay) return c.Mode == '?' ? $"Is there {np}?" : $"There is {np}{end}";
            string s = Subject[c.Person];
            if (c.Mode == '?')
                return (c.Person == "3SG" ? "Does she " : c.Person == "1SG" ? "Do I " : $"Do {s} ") + $"{c.V.En} {np}?";
            return Cap(s) + " " + (c.Person == "3SG" ? c.V.En3 : c.V.En) + " " + np + end;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        string Real(Cl c)
        {
            var s = Render(Say(0, default(Bits), c)) + (c.Mode == '.' ? "." : "");
            int i = 0;
            while (i < s.Length && !char.IsLetter(s[i])) i++;
            return i < s.Length ? s.Substring(0, i) + char.ToUpperInvariant(s[i]) + s.Substring(i + 1) : s;
        }

        static bool Sensible(Cl c) =>
            (c.Hay || c.N.Verbs.Contains(c.V.Key))
            && (c.A == null || c.N.Adjs.Contains(c.A.Key))
            && (c.Art || c.N.Mass)                     // countable nouns need 'the' here
            && !(c.Hay && c.Art)                       // 'hay' takes no definite article
            && !(c.Mode == '?' && c.Person == "1SG");

        bool Fits(Cl c)
        {
            var w = Say(0, default(Bits), c);
            return w.Count <= 7 && w.Count(x => !(x.M.Length == 1 && Spec(x.M[0]).Kind == MorphKind.Punct)) <= 6
                && Render(w).Length <= 40 && En(c).Length <= 32;
        }

        // ---------------------------------------------------------------- hint search (same idea as French)

        List<Cl> FindHints(Rng rng, Bits b, int hyps, Cl target, Func<Rng, Cl, Cl> mutate)
        {
            var tw = Say(0, b, target);
            var tKey = PuzzleBuilder.Key(tw);
            ulong tm = MorphMask(tw);
            // rival grammars that would render the target differently: some hint must show each one wrong
            int need = 0;
            for (int h = 1; h < hyps; h++) if (PuzzleBuilder.Key(Say(h, b, target)) != tKey) need |= 1 << h;

            // candidate hints: distinct sensible mutations of the target that share some, but not all, of its morphs
            var cands = new List<Cl>();
            var masks = new List<ulong>();
            var refutes = new List<int>();     // rival hypotheses this hint renders differently
            var gender = new List<bool>();     // shows the target noun's gender
            var seen = new HashSet<string> { tKey };
            for (int i = 0; i < CandidateTries && cands.Count < MaxCandidates; i++)
            {
                _altSeed = rng.Next();
                _slot = 0;
                var c = mutate(rng, target);
                if (!Sensible(c) || !Fits(c)) continue;
                var w = Say(0, b, c);
                var key = PuzzleBuilder.Key(w);
                if (!seen.Add(key)) continue;
                ulong m = MorphMask(w);
                if ((m & tm) == 0 || (tm & ~m) == 0) continue;
                int r = 0;
                for (int h = 1; h < hyps; h++) if ((need & (1 << h)) != 0 && PuzzleBuilder.Key(Say(h, b, c)) != key) r |= 1 << h;
                cands.Add(c); masks.Add(m); refutes.Add(r);
                gender.Add(c.N == target.N && (c.Art || c.A != null));
            }
            // three of them that make every target morph deducible (PuzzleBuilder.AllDeducible, by bitmask), show
            // the target noun's gender somewhere, and rule out every rival grammar
            var pick = PuzzleBuilder.FindHintTriple(tm, masks, rng,
                (x, y, z) => (gender[x] || gender[y] || gender[z]) && ((refutes[x] | refutes[y] | refutes[z]) & need) == need);
            return pick?.Select(i => cands[i]).ToList();
        }

        const int CandidateTries = 400, MaxCandidates = 120;

        // Each candidate hint draws a fresh 'other' value per slot (mostly from _altSeed, sometimes from rng).
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
                if (!Sensible(target) || !Fits(target)) continue;
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

        Cl Pick(Rng rng, bool hay, IList<string> persons, Func<Noun, bool> nounOk, bool adj, char mode)
        {
            var n = rng.Pick(Nouns.Where(nounOk).ToList());
            return new Cl
            {
                Hay = hay, Person = rng.Pick(persons), V = ByKey(Verbs, rng.Pick(n.Verbs)), N = n,
                Art = hay ? false : !n.Mass || rng.Chance(adj ? 0.3f : 0.8f),
                A = adj ? ByKey(Adjs, rng.Pick(n.Adjs)) : null, Mode = mode,
            };
        }

        Cl Mutate(Rng rng, Cl t, bool hayOk, float pAdj, string modes)
        {
            var c = t.Copy();
            c.Hay = hayOk && Maybe(rng, t.Hay, new[] { false, true });
            c.N = Maybe(rng, t.N, Nouns);
            c.Person = Maybe(rng, t.Person, Persons);
            c.V = c.N.Verbs.Contains(t.V.Key) && rng.Chance(0.5f) ? t.V : ByKey(Verbs, rng.Pick(c.N.Verbs));
            c.Art = !c.Hay && (!c.N.Mass || rng.Chance(0.5f));
            if (rng.Chance(pAdj)) c.A = t.A != null && c.N.Adjs.Contains(t.A.Key) && rng.Chance(0.5f) ? t.A : ByKey(Adjs, rng.Pick(c.N.Adjs));
            else c.A = null;
            c.Mode = Maybe(rng, t.Mode, modes.ToCharArray());
            return c;
        }

        static Verb ByKey(Verb[] all, string k) => all.First(x => x.Key == k);
        static Adj ByKey(Adj[] all, string k) => all.First(x => x.Key == k);

        // ---------------------------------------------------------------- kinds

        // "Bebemos el vino." — the ending -emos already says 'we'
        Draft VerbEndingKind(Rng rng, int tier)
        {
            var b = new Bits { Pron = 0, NoPerson = 1, NoOpen = -1, NoGender = -1, AdjFirst = -1 };
            return Make(rng, b, Hyps.Bits(LangConcepts.VerbEnding, LangConcepts.VerbEnding),
                r => { var c = Pick(r, false, new[] { "1SG", "2SG", "1PL" }, _ => true, true, '.'); c.Art = true; return c; },
                (r, t) => { var c = Mutate(r, t, false, 0.6f, "."); c.Art = true; return c; },
                t =>
                {
                    var d = new Draft
                    {
                        Concept = LangConcepts.VerbEnding,
                        OrderConcept = LangConcepts.WordOrder,
                        Feature = $"In Spanish the verb ending says who is acting, so the word for 'I', 'you' or 'we' is usually left out: {t.V.Stem}{RealEnding[t.Person]} already means '{Subject[t.Person]} {t.V.En}'.",
                        NowYouKnow = "Hablo español means 'I speak Spanish', and there is no separate word for 'I' in it: the -o ending already says it.",
                    };
                    foreach (var p in Persons.Where(x => x != t.Person)) d.Extra(W("v." + t.V.Key, p), LangConcepts.VerbEnding);
                    return d;
                });
        }

        // "¿Comes la sopa?" — a question opens with ¿ and closes with ?
        Draft PunctuationKind(Rng rng, int tier)
        {
            var b = new Bits { NoOpen = 0, Pron = 1, NoPerson = -1, NoGender = -1, AdjFirst = -1 };
            return Make(rng, b, Hyps.Bits(LangConcepts.Punctuation, LangConcepts.VerbEnding),
                r => { var c = Pick(r, false, new[] { "2SG", "3SG", "1PL" }, _ => true, false, '?'); c.Art = true; return c; },
                (r, t) => { var c = Mutate(r, t, false, 0f, "?!."); c.Art = true; return c; },
                t =>
                {
                    var real = Real(t);
                    var d = new Draft
                    {
                        Concept = LangConcepts.Punctuation,
                        Concepts = new[] { LangConcepts.VerbEnding },
                        OrderConcept = LangConcepts.Punctuation,
                        Feature = $"Spanish marks a question at both ends, starting with an upside-down ¿: {real}. Exclamations get ¡…! the same way.",
                        NowYouKnow = "The opening ¿ was recommended by the Royal Spanish Academy in 1754, because a Spanish question can look exactly like a statement until its end.",
                    };
                    d.Extra(W("v." + t.V.Key, t.Person), LangConcepts.Punctuation);
                    d.Extra(W("E", "v." + t.V.Key, t.Person), LangConcepts.Punctuation);
                    return d;
                });
        }

        // "¿Hay sopa fría?" / "Bebes leche fría." — the adjective follows the noun and takes its gender
        Draft GenderKind(Rng rng, int tier)
        {
            var b = new Bits { NoGender = 0, AdjFirst = 1, NoOpen = 2, Pron = -1, NoPerson = -1 };
            return Make(rng, b, Hyps.Bits(LangConcepts.GenderAgreement, LangConcepts.AdjectiveOrder, LangConcepts.Punctuation),
                r =>
                {
                    var c = Pick(r, r.Chance(0.6f), new[] { "1SG", "2SG", "3SG", "1PL" }, n => n.Mass && (n.Fem || r.Chance(0.4f)), true, r.Chance(0.5f) ? '?' : '.');
                    c.Art = c.Mode == '.';   // a statement has no ¿ ?, so it takes 'the' to stay four words long
                    if (c.Art) c.Hay = false;
                    return c;
                },
                (r, t) => Mutate(r, t, true, 0.75f, "?!."),
                t =>
                {
                    string e = t.N.Fem ? "a" : "o";
                    var d = new Draft
                    {
                        Concept = LangConcepts.GenderAgreement,
                        Concepts = new[] { LangConcepts.AdjectiveOrder },
                        OrderConcept = LangConcepts.AdjectiveOrder,
                        Feature = $"Spanish adjectives follow the noun and end in -o or -a to match its gender: {t.N.Es} {t.A.Stem}{e}." +
                                  (t.N.Es.EndsWith("e") ? $" {Cap(t.N.Es)} itself does not show its gender; only the words around it do." : ""),
                        NowYouKnow = t.N.Key == "blood" ? "A sangre fría, 'in cold blood', is a Spanish idiom too: done calmly, on purpose." : "Spanish gives gender even to things: el libro 'the book' is masculine and la mesa 'the table' is feminine.",
                    };
                    d.Extra(W("a." + t.A.Key + (t.N.Fem ? ".M" : ".F")), LangConcepts.GenderAgreement);
                    return d;
                });
        }
    }
}

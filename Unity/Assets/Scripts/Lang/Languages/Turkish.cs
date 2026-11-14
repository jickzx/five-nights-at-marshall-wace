using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Turkish: ideas are suffixes added in a fixed order (plural, "my", case), and each suffix takes the form
    /// the real rules give it: vowel harmony (-ler/-lar; -im/-ım/-üm/-um), -m after a vowel, t- after a voiceless consonant.
    /// </summary>
    public sealed class Turkish : SourceLanguage
    {
        public override string Name => "Turkish";
        public override string Code => "tr";
        public override string Endonym => "Türkçe";
        public override string Summary => "Ideas snap on as suffixes in a fixed order (plural, my, in/from); suffix vowels match the word they join (vowel harmony).";
        public override string QuizExplanation => "Turkish: words grow by chains of suffixes in a fixed order, and the suffix vowels harmonise with the word.";
        public override string[] Myths => new[]
        {
            "'In' and 'from' are separate words before the noun, as in English.",
            "Suffix vowels never change.",
            "'My' is a separate word before the noun.",
            "Suffixes can go on in any order.",
        };

        sealed class Noun { public string Key, Tr, En, EnPl, Prep; public bool Back; }
        static readonly Noun[] Nouns =
        {
            new Noun { Key = "house", Tr = "ev", En = "house", EnPl = "houses", Prep = "in" },
            new Noun { Key = "ship", Tr = "gemi", En = "ship", EnPl = "ships", Prep = "on" },
            new Noun { Key = "village", Tr = "köy", En = "village", EnPl = "villages", Prep = "in" },
            new Noun { Key = "eye", Tr = "göz", En = "eye", EnPl = "eyes", Prep = "in" },
            new Noun { Key = "door", Tr = "kapı", En = "door", EnPl = "doors", Prep = "at", Back = true },
            new Noun { Key = "room", Tr = "oda", En = "room", EnPl = "rooms", Prep = "in", Back = true },
            new Noun { Key = "box", Tr = "kutu", En = "box", EnPl = "boxes", Prep = "in", Back = true },
            new Noun { Key = "head", Tr = "kafa", En = "head", EnPl = "heads", Prep = "in", Back = true },
        };

        // every real form of each suffix; the morph key is slot + form, e.g. "P1.ım"
        static readonly (string slot, string gloss, string[] forms)[] Suffixes =
        {
            ("PL", "PL", new[] { "ler", "lar" }),
            ("P1", "my", new[] { "m", "im", "ım", "üm", "um" }),
            ("LOC", "in", new[] { "de", "da", "te", "ta" }),
            ("ABL", "from", new[] { "den", "dan", "ten", "tan" }),
        };

        // every order of the three suffix slots (plural, possessive, case); index 0 is the real order
        static readonly int[][] Perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        static readonly string[] AllHyps = Enumerable.Range(0, 12)
            .Select(h => h == 0 ? null : h == 6 ? LangConcepts.VowelHarmony : LangConcepts.SuffixOrder).ToArray();

        public Turkish()
        {
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.Tr, n.En);
            foreach (var (slot, gloss, forms) in Suffixes)
                foreach (var f in forms) Add(slot + "." + f, MorphKind.Affix, f, gloss, LangConcepts.SuffixOrder);

            Rule(LangConcepts.SuffixOrder, "Suffixes join the word in a fixed order: plural, then 'my', then 'in' or 'from'.");
            Rule(LangConcepts.VowelHarmony, "Each suffix's vowel changes to match the word: -ler after e, i, ö, ü and -lar after a, ı, o, u.");

            Kind("suffix-order", 0, (r, t) => Chain(r, false, false));
            Kind("long-chain", 1, (r, t) => Chain(r, true, false));
            Kind("vowel-harmony", 2, (r, t) => Chain(r, false, true));
            Kind("chain-harmony", 3, (r, t) => Chain(r, true, true));
        }

        // ---- the real Turkish rules
        const string Vowels = "aeıioöuü";
        static bool IsBack(char v) => "aıou".IndexOf(v) >= 0;
        static char LastVowel(string s) { for (int i = s.Length - 1; i >= 0; i--) if (Vowels.IndexOf(s[i]) >= 0) return s[i]; return 'e'; }

        /// <summary>The form a suffix takes after the word so far. Ignoring harmony = a learner who always uses the e/i forms.</summary>
        static string Suffix(string slot, string word, bool ignoreHarmony)
        {
            char v = ignoreHarmony ? 'e' : LastVowel(word);
            bool back = IsBack(v);
            switch (slot)
            {
                case "PL": return back ? "lar" : "ler";
                case "P1":
                    if (Vowels.IndexOf(word[word.Length - 1]) >= 0) return "m";
                    return "ei".IndexOf(v) >= 0 ? "im" : "aı".IndexOf(v) >= 0 ? "ım" : "öü".IndexOf(v) >= 0 ? "üm" : "um";
                default:
                    string d = "fsthşçkp".IndexOf(word[word.Length - 1]) >= 0 ? "t" : "d";
                    return d + (back ? "a" : "e") + (slot == "ABL" ? "n" : "");
            }
        }

        /// <summary>h = suffix-order permutation + 6 * (harmony ignored).</summary>
        static List<Word> Form(int h, Noun n, bool pl, bool poss, string kase)
        {
            var slots = new[] { pl ? "PL" : null, poss ? "P1" : null, kase };
            var m = new List<string> { "n." + n.Key };
            string word = n.Tr;
            foreach (var i in Perms[h % 6])
            {
                if (slots[i] == null) continue;
                string f = Suffix(slots[i], word, h >= 6);
                m.Add(slots[i] + "." + f);
                word += f;
            }
            return P(new Word(m.ToArray()));
        }

        static List<string> RealParts(Noun n, bool pl, bool poss, string kase) =>
            Form(0, n, pl, poss, kase)[0].M.Select(k => k.StartsWith("n.") ? n.Tr : k.Substring(k.IndexOf('.') + 1)).ToList();

        static string Real(Noun n, bool pl, bool poss, string kase) => string.Concat(RealParts(n, pl, poss, kase));
        static string Segmented(Noun n, bool pl, bool poss, string kase) => string.Join("-", RealParts(n, pl, poss, kase));

        static string En(Noun n, bool pl, bool poss, string kase)
        {
            string noun = pl ? n.EnPl : n.En;
            if (poss) noun = "my " + noun;
            else if (kase != null) noun = "the " + noun;
            else if (!pl) noun = "a " + noun;
            return kase == null ? noun : $"{(kase == "LOC" ? n.Prep : "from")} {noun}";
        }

        Draft Chain(Rng rng, bool longChain, bool harmony)
        {
            bool back = rng.Chance(0.5f);
            var same = Nouns.Where(x => x.Back == back).ToList();
            var picks = rng.Sample(same, 2);
            Noun n = picks[0], n3 = picks[1];
            Noun n2 = harmony ? rng.Pick(Nouns.Where(x => x.Back != back).ToList()) : n3;
            string kase = rng.Chance(0.6f) ? "LOC" : "ABL";
            string otherCase = kase == "LOC" ? "ABL" : "LOC";
            bool poss = longChain;

            var d = new Draft
            {
                Concept = harmony ? LangConcepts.VowelHarmony : LangConcepts.SuffixOrder,
                Concepts = harmony ? new[] { LangConcepts.SuffixOrder } : System.Array.Empty<string>(),
                OrderConcept = LangConcepts.SuffixOrder,
                MorphTiles = true,
                HypConcepts = AllHyps,
                TargetMeaning = Cap(En(n, true, poss, kase)),
                Target = h => Form(h, n, true, poss, kase),
                RealText = Real(n, true, poss, kase),
                Translation = En(n, true, poss, kase),
            };
            if (harmony)
            {
                var f = Nouns.First(x => !x.Back);
                var b = Nouns.First(x => x.Back);
                d.Feature = $"Turkish suffixes change their vowels to match the word they join (vowel harmony): {Segmented(f, true, false, "LOC")} '{En(f, true, false, "LOC")}' but {Segmented(b, true, false, "LOC")} '{En(b, true, false, "LOC")}'.";
                d.NowYouKnow = "The Turkish plural is -ler after e, i, ö, ü and -lar after a, ı, o, u: evler 'houses', kapılar 'doors'.";
            }
            else
            {
                d.Feature = $"Turkish adds suffixes to a word in a fixed order, plural first, then 'my', then the case: {Segmented(n, true, poss, kase)} is '{En(n, true, poss, kase)}', all one word.";
                d.NowYouKnow = "Turkish words can grow very long: Çekoslovakyalılaştıramadıklarımızdanmışsınız, 'you are apparently one of those we could not make Czechoslovak', is a famous single word.";
            }

            void Ex(Noun x, bool pl, bool ps, string k) => d.Example(Cap(En(x, pl, ps, k)), h => Form(h, x, pl, ps, k));
            if (!longChain && !harmony) { Ex(n2, true, false, kase); Ex(n, false, true, null); }
            else if (longChain && !harmony) { Ex(n2, true, true, null); Ex(n2, false, true, kase); Ex(n, true, false, null); }
            else if (!longChain) { Ex(n2, true, false, kase); Ex(n, false, false, kase); Ex(n3, true, false, null); }
            else { Ex(n2, true, true, kase); Ex(n, false, false, kase); Ex(n3, true, true, null); }
            // the other case, in the form it would take on the target word
            d.Extra(W(Form(0, n, true, poss, otherCase)[0].M.Last()), LangConcepts.Vocabulary);
            return d;
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

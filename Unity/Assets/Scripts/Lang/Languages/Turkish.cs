using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Turkish, exaggerated: every idea is a suffix snapped onto the word in a fixed order (plural, "my", case),
    /// and every suffix vowel switches to match the root (vowel harmony, reduced to a strict front/back switch).
    /// </summary>
    public sealed class Turkish : SourceLanguage
    {
        public override string Name => "Turkish";
        public override string Code => "tr";
        public override string Summary => "Ideas snap on as suffixes in a fixed order (plural, my, in/from); suffix vowels match the root (vowel harmony).";
        public override string[] QuizDistractors => new[] { "Hungarian", "Finnish", "Mongolian", "Korean" };
        public override string QuizExplanation => "Turkish: words grow by chains of suffixes in a fixed order, and the suffix vowels harmonise with the root.";
        public override bool NeedsHarmony => true;

        sealed class Noun { public string Key, Tr, En, EnPl, Prep; public bool Back; }
        // no stem ends in a voiceless consonant, so the case suffix always starts with d
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

        // every order of the three suffix slots (plural, possessive, case); index 0 is the real order
        static readonly int[][] Perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        static readonly string[] AllHyps = Enumerable.Range(0, 12)
            .Select(h => h == 0 ? null : h == 6 ? LangConcepts.VowelHarmony : LangConcepts.SuffixOrder).ToArray();

        public Turkish()
        {
            foreach (var n in Nouns) Add("n." + n.Key, MorphKind.Root, n.En).Harmony = n.Back ? 2 : 1;
            foreach (var (s, gloss) in new[] { ("PL", "PL"), ("P1", "my"), ("LOC", "in"), ("ABL", "from") })
            {
                var f = Add(s + ".F", MorphKind.Affix, gloss, LangConcepts.SuffixOrder); f.Group = s; f.Harmony = 1;
                var b = Add(s + ".B", MorphKind.Affix, gloss, LangConcepts.SuffixOrder); b.Group = s; b.Harmony = 2;
            }

            Kind("suffix-order", 0, (r, t) => Chain(r, false, false));
            Kind("long-chain", 1, (r, t) => Chain(r, true, false));
            Kind("vowel-harmony", 2, (r, t) => Chain(r, false, true));
            Kind("chain-harmony", 3, (r, t) => Chain(r, true, true));
        }

        /// <summary>h = suffix-order permutation + 6 * (harmony ignored: always front vowels).</summary>
        static List<Word> Form(int h, Noun n, bool pl, bool poss, string kase)
        {
            string cls = h >= 6 || !n.Back ? "F" : "B";
            var slots = new[] { pl ? "PL" : null, poss ? "P1" : null, kase };
            var m = new List<string> { "n." + n.Key };
            foreach (var i in Perms[h % 6]) if (slots[i] != null) m.Add(slots[i] + "." + cls);
            return P(new Word(m.ToArray()));
        }

        // ---- the real Turkish word, built with the real rules
        const string Vowels = "aeıioöuü";
        static bool IsBack(char v) => "aıou".IndexOf(v) >= 0;
        static char LastVowel(string s) { for (int i = s.Length - 1; i >= 0; i--) if (Vowels.IndexOf(s[i]) >= 0) return s[i]; return 'e'; }

        static List<string> RealParts(Noun n, bool pl, bool poss, string kase)
        {
            var parts = new List<string> { n.Tr };
            string Cur() => string.Concat(parts);
            if (pl) parts.Add(IsBack(LastVowel(Cur())) ? "lar" : "ler");
            if (poss)
            {
                string w = Cur();
                if (Vowels.IndexOf(w[w.Length - 1]) >= 0) parts.Add("m");
                else
                {
                    char v = LastVowel(w);
                    parts.Add("ei".IndexOf(v) >= 0 ? "im" : "aı".IndexOf(v) >= 0 ? "ım" : "öü".IndexOf(v) >= 0 ? "üm" : "um");
                }
            }
            if (kase != null)
            {
                string w = Cur();
                bool back = IsBack(LastVowel(w));
                string d = "fsthşçkp".IndexOf(w[w.Length - 1]) >= 0 ? "t" : "d";
                parts.Add(d + (kase == "LOC" ? (back ? "a" : "e") : (back ? "an" : "en")));
            }
            return parts;
        }

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
                d.Feature = $"Turkish snaps suffixes onto a word in a fixed order, plural first, then 'my', then the case: {Segmented(n, true, poss, kase)} is '{En(n, true, poss, kase)}', all one word.";
                d.NowYouKnow = "Turkish words can grow absurdly long: Çekoslovakyalılaştıramadıklarımızdanmışsınız, 'you are apparently one of those we could not make Czechoslovak', is a famous single word.";
            }

            void Ex(Noun x, bool pl, bool ps, string k) => d.Example(Cap(En(x, pl, ps, k)), h => Form(h, x, pl, ps, k));
            if (!longChain && !harmony) { Ex(n2, true, false, kase); Ex(n, false, true, null); }
            else if (longChain && !harmony) { Ex(n2, true, true, null); Ex(n2, false, true, kase); Ex(n, true, false, null); }
            else if (!longChain) { Ex(n2, true, false, kase); Ex(n, false, false, kase); Ex(n3, true, false, null); }
            else { Ex(n2, true, true, kase); Ex(n, false, false, kase); Ex(n3, true, true, null); }
            d.Extra(W(otherCase + "." + (back ? "B" : "F")), LangConcepts.Vocabulary);
            return d;
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

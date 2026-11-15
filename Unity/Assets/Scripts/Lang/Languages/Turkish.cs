using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;

namespace Fnmw.Lang.Languages
{
    /// <summary>
    /// Turkish, as short subject-place-verb sentences ("Kediler odamda uyuyor", the cats sleep in my room). Ideas are
    /// suffixes added in a fixed order (plural, "my", case), the verb comes last, and each suffix takes the form the
    /// real rules give it: vowel harmony (-ler/-lar, -de/-da, -im/-ım), -m after a vowel, t- after a voiceless consonant.
    /// Tiles are single morphs (stem or suffix). Every puzzle follows a fixed plan of three hints so that each piece of
    /// the target varies on its own across them (see PuzzleBuilder.AllDeducible).
    /// </summary>
    public sealed class Turkish : SourceLanguage
    {
        public override string Name => "Turkish";
        public override string Code => "tr";
        public override string Endonym => "Türkçe";
        public override string Summary => "Verb last; ideas snap on as suffixes in a fixed order (plural, my, in/from); suffix vowels match the word they join (vowel harmony).";
        public override string QuizExplanation => "Turkish: the verb comes last, words grow by chains of suffixes in a fixed order, and the suffix vowels harmonise with the word.";
        public override string[] QuizDistractors => new[] { "Hungarian", "Finnish", "Mongolian", "Korean" };

        sealed class Noun { public string Key, Tr, En, EnPl, Prep; }
        static readonly Noun[] Subjects =
        {
            new Noun { Key = "cat", Tr = "kedi", En = "cat", EnPl = "cats" },
            new Noun { Key = "dog", Tr = "köpek", En = "dog", EnPl = "dogs" },
            new Noun { Key = "bird", Tr = "kuş", En = "bird", EnPl = "birds" },
            new Noun { Key = "mouse", Tr = "fare", En = "mouse", EnPl = "mice" },
            new Noun { Key = "baby", Tr = "bebek", En = "baby", EnPl = "babies" },
            new Noun { Key = "child", Tr = "çocuk", En = "child", EnPl = "children" },
        };
        static readonly Noun[] Places =
        {
            new Noun { Key = "house", Tr = "ev", En = "house", EnPl = "houses", Prep = "in" },
            new Noun { Key = "room", Tr = "oda", En = "room", EnPl = "rooms", Prep = "in" },
            new Noun { Key = "box", Tr = "kutu", En = "box", EnPl = "boxes", Prep = "in" },
            new Noun { Key = "garden", Tr = "bahçe", En = "garden", EnPl = "gardens", Prep = "in" },
            new Noun { Key = "ship", Tr = "gemi", En = "ship", EnPl = "ships", Prep = "on" },
            new Noun { Key = "door", Tr = "kapı", En = "door", EnPl = "doors", Prep = "at" },
            new Noun { Key = "basket", Tr = "sepet", En = "basket", EnPl = "baskets", Prep = "in" },
            new Noun { Key = "bag", Tr = "çanta", En = "bag", EnPl = "bags", Prep = "in" },
        };

        sealed class Verb { public string Tr, En, EnPl, Case; }
        static readonly Verb[] Verbs =
        {
            new Verb { Tr = "uyuyor", En = "sleeps", EnPl = "sleep", Case = "LOC" },
            new Verb { Tr = "oturuyor", En = "sits", EnPl = "sit", Case = "LOC" },
            new Verb { Tr = "yatıyor", En = "lies", EnPl = "lie", Case = "LOC" },
            new Verb { Tr = "bekliyor", En = "waits", EnPl = "wait", Case = "LOC" },
            new Verb { Tr = "oynuyor", En = "plays", EnPl = "play", Case = "LOC" },
            new Verb { Tr = "saklanıyor", En = "hides", EnPl = "hide", Case = "LOC" },
            new Verb { Tr = "geliyor", En = "comes", EnPl = "come", Case = "ABL" },
            new Verb { Tr = "kaçıyor", En = "escapes", EnPl = "escape", Case = "ABL" },
            new Verb { Tr = "çıkıyor", En = "comes out", EnPl = "come out", Case = "ABL" },
        };

        // every real form of each suffix; the morph key is slot + form, e.g. "P1.ım"
        static readonly (string slot, string gloss, string concept, string[] forms)[] Suffixes =
        {
            ("PL", "PL", LangConcepts.SuffixOrder, new[] { "ler", "lar" }),
            ("P1", "my", LangConcepts.SuffixOrder, new[] { "m", "im", "ım", "üm", "um" }),
            ("LOC", "in", LangConcepts.SuffixOrder, new[] { "de", "da", "te", "ta" }),
            ("ABL", "from", LangConcepts.SuffixOrder, new[] { "den", "dan", "ten", "tan" }),
        };

        // rivals: bit 0 = suffixes in the other order; bit 1 = always the front (e/i) forms; bit 2 = always the back (a/ı)
        // forms; bit 3 = English order (verb before the place)
        static readonly string[] AllHyps = Hyps.Bits(LangConcepts.SuffixOrder, LangConcepts.VowelHarmony, LangConcepts.VowelHarmony, LangConcepts.VerbFinal);

        public Turkish()
        {
            foreach (var n in Subjects.Concat(Places)) Add("n." + n.Key, MorphKind.Root, n.Tr, n.En);
            foreach (var v in Verbs) Add("v." + v.Tr, MorphKind.Root, v.Tr, v.En);
            foreach (var (slot, gloss, concept, forms) in Suffixes)
                foreach (var f in forms) Add(slot + "." + f, MorphKind.Affix, f, gloss, concept);

            Kind("case-suffix", 0, (r, t) => Make(r, Extra.None));
            Kind("plural-subject", 1, (r, t) => Make(r, Extra.SubjectPlural));
            Kind("plural-place", 2, (r, t) => Make(r, Extra.PlacePlural));
            Kind("my-place", 3, (r, t) => Make(r, Extra.PlaceMine));
        }

        // ---- the real Turkish rules
        const string Vowels = "aeıioöuü";
        static bool IsBack(char v) => "aıou".IndexOf(v) >= 0;
        static char LastVowel(string s) { for (int i = s.Length - 1; i >= 0; i--) if (Vowels.IndexOf(s[i]) >= 0) return s[i]; return 'e'; }

        /// <summary>The form a suffix takes after the word so far. mode 0 = real harmony, 1 = always front, 2 = always back.</summary>
        static string Suffix(string slot, string word, int mode)
        {
            char v = mode == 1 ? 'e' : mode == 2 ? 'a' : LastVowel(word);
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

        static int Mode(int h) => Hyps.On(h, 1) ? 1 : Hyps.On(h, 2) ? 2 : 0;

        /// <summary>A noun with its suffixes (in the real order: plural, my, case) under hypothesis h.</summary>
        static Word NounW(int h, Noun n, params string[] slots)
        {
            var order = slots.Where(s => s != null).ToList();
            if (Hyps.On(h, 0)) order.Reverse();
            var m = new List<string> { "n." + n.Key };
            string word = n.Tr;
            foreach (var s in order)
            {
                string f = Suffix(s, word, Mode(h));
                m.Add(s + "." + f);
                word += f;
            }
            return new Word(m.ToArray());
        }

        static string RealWord(Noun n, params string[] slots) => string.Concat(NounW(0, n, slots).M.Select(k => k.StartsWith("n.") ? n.Tr : k.Substring(k.IndexOf('.') + 1)));
        static string Segmented(Noun n, params string[] slots) => string.Join("-", NounW(0, n, slots).M.Select(k => k.StartsWith("n.") ? n.Tr : k.Substring(k.IndexOf('.') + 1)));

        enum Extra { None, SubjectPlural, PlacePlural, PlaceMine }

        sealed class Sent
        {
            public Noun S, N; public Verb V; public bool SPl, NPl, NMine;
            public string[] PlaceSlots(string kase) => new[] { NPl ? "PL" : null, NMine ? "P1" : null, kase };
        }

        static List<Word> Render(int h, Sent s)
        {
            var subj = NounW(h, s.S, s.SPl ? "PL" : null);
            var place = NounW(h, s.N, s.PlaceSlots(s.V.Case));
            var verb = W("v." + s.V.Tr);
            return Hyps.On(h, 3) ? P(subj, verb, place) : P(subj, place, verb);
        }

        static string English(Sent s)
        {
            string subj = "The " + (s.SPl ? s.S.EnPl : s.S.En);
            string verb = s.SPl ? s.V.EnPl : s.V.En;
            string place = (s.NMine ? "my " : "the ") + (s.NPl ? s.N.EnPl : s.N.En);
            string prep = s.V.Case == "LOC" ? s.N.Prep : s.V.Tr == "çıkıyor" ? "of" : "from";
            return $"{subj} {verb} {prep} {place}.";
        }

        static string Progressive(Sent s)
        {
            string subj = "The " + (s.SPl ? s.S.EnPl : s.S.En);
            string ing = s.V.EnPl switch { "sit" => "sitting", "lie" => "lying", "come" => "coming", "come out" => "coming out", "escape" => "escaping", "hide" => "hiding", _ => s.V.EnPl + "ing" };
            string place = (s.NMine ? "my " : "the ") + (s.NPl ? s.N.EnPl : s.N.En);
            string prep = s.V.Case == "LOC" ? s.N.Prep : s.V.Tr == "çıkıyor" ? "of" : "from";
            return $"{subj} {(s.SPl ? "are" : "is")} {ing} {prep} {place}.";
        }

        static bool Pluralisable(Noun n) => n.Key != "ship" && n.Key != "door" && n.Key != "house";

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
        string Text(Sent s) => Cap(Render(Render(0, s))) + ".";

        /// <summary>The suffix morphs a place noun gets, i.e. the tiles that must match between two places.</summary>
        static string SuffixSig(Noun n, string[] slots) => string.Join("+", NounW(0, n, slots).M.Skip(1));

        // ---------------------------------------------------------------- plans
        // no extra suffix (4 tiles):
        //   target  S N+C V          Kedi odada uyuyor.
        //   hint 1  S N1+C V         Kedi kutuda uyuyor.
        //   hint 2  S N+C V2         Kedi odada oturuyor.
        //   hint 3  S2 N+C V         Köpek odada uyuyor.
        // one extra suffix X (5 tiles), on the place (plural or "my") or on the subject (plural):
        //   target  S N+X+C V        Kedi odamda uyuyor.
        //   hint 1  S N+C V          Kedi odada uyuyor.
        //   hint 2  S2 N+X+C V2      Köpek odamda oturuyor.
        //   hint 3  S N3+X+C V3      Kedi kutumda yatıyor.
        //   (subject plural: hint 2 S2+X N+C V2, hint 3 S+X N3+C V3)
        Draft Make(Rng rng, Extra extra)
        {
            for (int attempt = 0; ; attempt++)
            {
                var d = TryMake(rng, extra);
                // keep the English captions on two short lines
                if (d != null && (d.TargetMeaning.Length > 33 || d.Examples.Any(e => e.Picture.Length > 33))) d = null;
                if (d != null || attempt > 50) return d;
            }
        }

        Draft TryMake(Rng rng, Extra extra)
        {
            string kase = rng.Chance(0.65f) ? "LOC" : "ABL";
            var verbs = Verbs.Where(v => v.Case == kase).ToList();
            var subs = rng.Sample(Subjects, 2);
            Noun s = subs[0], s2 = subs[1];
            var n = rng.Pick(Places);
            if (extra == Extra.PlacePlural && !Pluralisable(n)) return null;
            bool sPl = extra == Extra.SubjectPlural, nPl = extra == Extra.PlacePlural, nMine = extra == Extra.PlaceMine;
            if (sPl && Suffix("PL", s.Tr, 0) != Suffix("PL", s2.Tr, 0)) return null;   // both subjects take the same plural form

            var full = new[] { nPl ? "PL" : null, nMine ? "P1" : null, kase };
            var bare = new[] { (string)null, null, kase };
            // the case form must not change between the bare place in hint 1 and the suffixed place in the target
            if (extra == Extra.PlacePlural || extra == Extra.PlaceMine)
                if (NounW(0, n, bare).M.Last() != NounW(0, n, full).M.Last()) return null;

            var target = new Sent { S = s, N = n, SPl = sPl, NPl = nPl, NMine = nMine };
            var d = new Draft
            {
                MorphTiles = true,
                HypConcepts = AllHyps,
                OrderConcept = extra == Extra.None || extra == Extra.SubjectPlural ? LangConcepts.VerbFinal : LangConcepts.SuffixOrder,
            };

            if (extra == Extra.None)
            {
                var vv = rng.Sample(verbs, 2);
                var n1s = Places.Where(x => x != n && SuffixSig(x, bare) == SuffixSig(n, bare)).ToList();
                if (n1s.Count == 0) return null;
                var n1 = rng.Pick(n1s);
                target.V = vv[0];
                Ex(d, new Sent { S = s, N = n1, V = vv[0] });
                Ex(d, new Sent { S = s, N = n, V = vv[1] });
                Ex(d, new Sent { S = s2, N = n, V = vv[0] });
            }
            else
            {
                var vv = rng.Sample(verbs, 3);
                target.V = vv[0];
                Ex(d, new Sent { S = s, N = n, V = vv[0] });
                if (sPl)
                {
                    var n3s = Places.Where(x => x != n && SuffixSig(x, bare) == SuffixSig(n, bare)).ToList();
                    if (n3s.Count == 0) return null;
                    Ex(d, new Sent { S = s2, SPl = true, N = n, V = vv[1] });
                    Ex(d, new Sent { S = s, SPl = true, N = rng.Pick(n3s), V = vv[2] });
                }
                else
                {
                    var n3s = Places.Where(x => x != n && (!nPl || Pluralisable(x)) && SuffixSig(x, full) == SuffixSig(n, full)).ToList();
                    if (n3s.Count == 0) return null;
                    Ex(d, new Sent { S = s2, N = n, NPl = nPl, NMine = nMine, V = vv[1] });
                    Ex(d, new Sent { S = s, N = rng.Pick(n3s), NPl = nPl, NMine = nMine, V = vv[2] });
                }
            }

            d.Target = h => Render(h, target);
            d.TargetMeaning = English(target);
            d.RealText = Text(target);
            d.Translation = Progressive(target);
            // the other case, in the form it would take on the target place
            string other = kase == "LOC" ? "ABL" : "LOC";
            d.Extra(W(NounW(0, n, target.PlaceSlots(other)).M.Last()), LangConcepts.Vocabulary);

            string subjWord = sPl ? Segmented(s, "PL") : s.Tr;
            string placeSeg = Segmented(n, target.PlaceSlots(kase));
            string caseName = kase == "LOC" ? "-de/-da 'in, at'" : "-den/-dan 'from'";
            switch (extra)
            {
                case Extra.None:
                    d.Concept = LangConcepts.VerbFinal;
                    d.Concepts = new[] { LangConcepts.SuffixOrder, LangConcepts.VowelHarmony };
                    d.Feature = $"Turkish puts the verb last and says 'in' or 'from' with a suffix on the noun instead of a word before it: {placeSeg} ({caseName}), then {target.V.Tr}.";
                    d.NowYouKnow = "Turkish has no word for 'the' and no prepositions: oda is 'room', odada is 'in the room'.";
                    break;
                case Extra.SubjectPlural:
                    d.Concept = LangConcepts.VowelHarmony;
                    d.Concepts = new[] { LangConcepts.SuffixOrder, LangConcepts.VerbFinal };
                    d.Feature = $"Turkish suffix vowels copy the last vowel of the word (vowel harmony): kedi-ler 'cats' but çocuk-lar 'children', ev-de 'in the house' but oda-da 'in the room'. Here: {subjWord}, {placeSeg}.";
                    d.NowYouKnow = "With a plural subject, Turkish often keeps the verb singular, especially for animals and things: kediler uyuyor.";
                    break;
                case Extra.PlacePlural:
                    d.Concept = LangConcepts.SuffixOrder;
                    d.Concepts = new[] { LangConcepts.VowelHarmony, LangConcepts.VerbFinal };
                    d.Feature = $"Turkish stacks suffixes in a fixed order, plural first, then the case: {placeSeg} is '{(kase == "LOC" ? n.Prep : "from")} the {n.EnPl}', all one word, and every vowel matches the noun.";
                    d.NowYouKnow = "Turkish words can grow very long: Çekoslovakyalılaştıramadıklarımızdanmışsınız is a famous single word.";
                    break;
                default:
                    d.Concept = LangConcepts.SuffixOrder;
                    d.Concepts = new[] { LangConcepts.VowelHarmony, LangConcepts.VerbFinal };
                    d.Feature = $"Turkish says 'my' with a suffix, and it comes before the case: {placeSeg} is '{(kase == "LOC" ? n.Prep : "from")} my {n.En}'. After a vowel 'my' is just -m; after a consonant it is -im, -ım, -üm or -um by vowel harmony.";
                    d.NowYouKnow = "Turkish rarely needs the word benim 'my': the suffix alone says it, as in odamda 'in my room'.";
                    break;
            }
            return d;
        }

        void Ex(Draft d, Sent s) => d.Example(English(s), h => Render(h, s));
    }
}

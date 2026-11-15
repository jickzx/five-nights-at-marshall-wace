using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fnmw.Core;
using Fnmw.Lang;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests.Lang
{
    /// <summary>Speech text for pronunciation, approved hint facts, and the engine's hint leak check.</summary>
    public class SpeechAndHintTests
    {
        static readonly float[] Difficulties = { 0f, 0.35f, 0.65f, 1f };
        const int Seeds = 120;

        static IEnumerable<(LanguageWorld w, LangPuzzle p, BuiltPuzzle b)> Sweep(LanguageEngine eng, int seeds)
        {
            for (int s = 1; s <= seeds; s++)
            {
                var w = eng.Generate(s * 104729);
                for (int i = 0; i < w.Puzzles.Count; i++)
                    foreach (var d in Difficulties)
                    {
                        var p = eng.GetPuzzle(w, i, d);
                        yield return (w, p, LanguageEngine.Inspect(w, p.Id));
                    }
            }
        }

        static bool HasLatin(string s) => s.Any(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));

        // ------------------------------------------------------------------ speech text

        [Test]
        public void SpeechText_IsCleanNativeSentence_ForEveryLanguage()   // L1, L2, L3
        {
            var eng = new LanguageEngine();
            var maxByLang = new Dictionary<string, int>();
            var langs = new HashSet<string>();
            foreach (var (w, p, b) in Sweep(eng, Seeds))
            {
                var r = eng.GetReveal(w, p.Id);
                string tag = $"{b.Language.Name} '{r.RealText}': ";
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.SpeechText), tag + "empty");
                Assert.AreEqual(b.Language.Code, r.SpeechLang, tag);
                Assert.That(r.SpeechText.Length, Is.LessThanOrEqualTo(Speech.MaxLength), tag + "too long");
                Assert.IsFalse(r.SpeechText.IndexOfAny(new[] { '(', ')', '（', '）' }) >= 0, tag + "brackets");
                Assert.AreEqual(r.SpeechText.Trim(), r.SpeechText, tag + "untrimmed");
                switch (b.Language.Code)
                {
                    case "ja": case "zh": case "ar":
                        Assert.IsFalse(HasLatin(r.SpeechText), tag + "romanisation leaked into speech");
                        Assert.IsTrue(Speech.NativeScript(r.SpeechText), tag);
                        Lore_SplitReal(r.RealText, out _, out var reading);
                        Assert.IsFalse(reading.Length > 0 && r.SpeechText.Contains(reading), tag);
                        break;
                    default:
                        Assert.AreEqual(r.RealText, r.SpeechText, tag + "Latin text is spoken as written");
                        break;
                }
                langs.Add(b.Language.Code);
                maxByLang.TryGetValue(b.Language.Code, out int m);
                maxByLang[b.Language.Code] = Math.Max(m, r.SpeechText.Length);
            }
            Assert.AreEqual(7, langs.Count);
            Debug.Log("[Lang] longest SpeechText per language: " + string.Join(", ", maxByLang.Select(kv => kv.Key + "=" + kv.Value)));
        }

        /// <summary>The backend's /v1/hint request bounds (backend/src/validate.ts) must hold for every real puzzle.</summary>
        [Test]
        public void HintRequestFields_FitTheBackendBounds()
        {
            var eng = new LanguageEngine();
            var word = new System.Text.RegularExpressions.Regex(@"^(?=.{1,24}$)[\p{L}\p{M}'’\-¿¡?!.,]+(?: [\p{L}\p{M}'’\-¿¡?!.,]+)*$");
            var meaning = new System.Text.RegularExpressions.Regex(@"^[\p{L}\p{M}0-9 ,.'’?!:;()\-¿¡]{1,100}$");
            int maxWord = 0, maxMeaning = 0, maxWords = 0, maxTiles = 0;
            foreach (var (w, p, b) in Sweep(eng, Seeds))
            {
                string tag = b.Language.Name + ": ";
                Assert.IsTrue(meaning.IsMatch(p.TargetMeaning), tag + p.TargetMeaning);
                Assert.AreEqual(3, p.Examples.Count, tag);
                Assert.That(p.Tiles.Count, Is.InRange(4, 12), tag);
                foreach (var e in p.Examples)
                {
                    Assert.IsTrue(meaning.IsMatch(e.Picture), tag + e.Picture);
                    Assert.That(e.Words.Count, Is.InRange(1, 12), tag);
                    foreach (var t in e.Words) Assert.IsTrue(word.IsMatch(t.Alien) && !Speech.NativeScript(t.Alien), tag + "word '" + t.Alien + "'");
                    maxWords = Math.Max(maxWords, e.Words.Count);
                    maxMeaning = Math.Max(maxMeaning, e.Picture.Length);
                }
                foreach (var t in p.Tiles) { Assert.IsTrue(word.IsMatch(t.Alien) && !Speech.NativeScript(t.Alien), tag + "tile '" + t.Alien + "'"); maxWord = Math.Max(maxWord, t.Alien.Length); }
                maxMeaning = Math.Max(maxMeaning, p.TargetMeaning.Length);
                maxTiles = Math.Max(maxTiles, p.Tiles.Count);
            }
            Debug.Log($"[Lang] hint request maxima: word {maxWord}, meaning {maxMeaning}, words per row {maxWords}, tiles {maxTiles}");
        }

        // the UI's own splitter, restated so this test does not reference Fnmw.UI
        static void Lore_SplitReal(string real, out string phrase, out string reading)
        {
            var m = System.Text.RegularExpressions.Regex.Match(real ?? "", @"^(.*?)\s*[\(（]([^\)）]*)[\)）]\s*$");
            if (m.Success) { phrase = m.Groups[1].Value.Trim(); reading = m.Groups[2].Value.Trim(); }
            else { phrase = real; reading = ""; }
            if (Speech.NativeScript(reading) && !Speech.NativeScript(phrase)) { var t = phrase; phrase = reading; reading = t; }
        }

        [Test]
        public void SpeechClean_HandlesEveryShape()
        {
            Assert.AreEqual("扉を開けてください", Speech.Clean("扉を開けてください (tobira o akete kudasai)"));
            Assert.AreEqual("我看这本书。", Speech.Clean("我看这本书。(Wǒ kàn zhè běn shū.)"));
            Assert.AreEqual("كتب الولد", Speech.Clean("kataba l-waladu (كتب الولد)"));
            Assert.AreEqual("¿Dónde está el gato?", Speech.Clean("¿Dónde está el gato?"));
            Assert.AreEqual("Der Mann kauft das Buch.", Speech.Clean("  Der Mann kauft das Buch.  "));
            Assert.AreEqual("", Speech.Clean(null));
            Assert.AreEqual("Le chat (noir) dort.", Speech.Clean("Le chat (noir) dort."), "a Latin aside is not split");
        }

        // ------------------------------------------------------------------ facts

        [Test]
        public void HintFacts_CoverEveryConcept_AndNameNoLanguage()   // L4
        {
            foreach (var c in LangConcepts.All)
            {
                var e = HintFacts.For(c);
                Assert.IsNotNull(e, c);
                Assert.That(e.Facts.Length, Is.InRange(1, 2), c);
                foreach (var text in e.Facts.Append(e.Focus).Append(e.Offline1))
                {
                    var f = HintSafety.Fold(text);
                    foreach (var l in LanguageEngine.AllLanguages())
                    {
                        foreach (var name in new[] { l.Name, l.Endonym }.Concat(l.QuizDistractors))
                            foreach (var part in HintSafety.Fold(name).Split(' '))
                                if (part.Length >= 3) StringAssert.DoesNotContain(part, f, c + ": " + text);
                    }
                    foreach (var g in HintFacts.Giveaways)
                        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(f, @"\b" + g + @"\b"), c + " names '" + g + "': " + text);
                }
            }
        }

        [Test]
        public void OfflineAndGuidedHints_PassTheirOwnPuzzles()   // L5
        {
            var eng = new LanguageEngine();
            int n = 0;
            foreach (var (w, p, b) in Sweep(eng, Seeds))
            {
                var concepts = new HashSet<string>(LangConcepts.All.Where(c => p.Concepts.Contains(c) || c == p.Concept)) { LangConcepts.Vocabulary };
                foreach (var c in concepts)
                {
                    var ctx = eng.GetHintContext(w, p.Id, c);
                    Assert.AreEqual(2, ctx.OfflineHints.Length);
                    foreach (var h in ctx.OfflineHints)
                        Assert.IsTrue(HintSafety.Check(b, h, out var why), $"{b.Language.Name} {c} offline hint rejected ({why}): {h}");
                    foreach (var (a, bb) in new[] { (1, 2), (1, 3), (2, 3) })
                        for (int level = 1; level <= 2; level++)
                        {
                            var g = HintFacts.Guided(level, c, a, bb);
                            Assert.IsTrue(HintSafety.Check(b, g, out var why), $"{b.Language.Name} {c} guided hint rejected ({why}): {g}");
                        }
                    n++;
                }
            }
            Assert.That(n, Is.GreaterThan(1000));
        }

        // ------------------------------------------------------------------ the leak check

        static (LanguageWorld w, LangPuzzle p, BuiltPuzzle b) First(string code)
        {
            var eng = new LanguageEngine();
            foreach (var t in Sweep(eng, 30)) if (t.b.Language.Code == code) return t;
            throw new Exception("no " + code + " puzzle");
        }

        static bool Safe(BuiltPuzzle b, string text) => HintSafety.Check(b, text, out _);

        [Test]
        public void Rejects_LanguageNames_InAnyCaseOrAccent()   // L6, L7
        {
            var (_, _, b) = First("fr");
            Assert.IsFalse(Safe(b, "This is French word order."));
            Assert.IsFalse(Safe(b, "this is FRENCH."));
            Assert.IsFalse(Safe(b, "Think of Français grammar."));
            Assert.IsFalse(Safe(b, "Think of Francais grammar."));
            Assert.IsFalse(Safe(b, "Like Italian, the describing word follows."), "a quiz distractor");
            Assert.IsFalse(Safe(b, "Unlike Germany, things differ here."));
            Assert.IsFalse(Safe(b, "These are kanji compounds."));
            Assert.IsFalse(Safe(b, "A Romance tongue keeps gender."));
            var (_, _, zh) = First("zh");
            Assert.IsFalse(Safe(zh, "Mandarin needs a measure word here."));
            Assert.IsFalse(Safe(zh, "Read the pinyin carefully."));
        }

        [Test]
        public void Rejects_TwoPuzzleWords_AndTheAnswer()   // L8, L9, L10
        {
            var eng = new LanguageEngine();
            int checkedCount = 0;
            foreach (var (w, p, b) in Sweep(eng, 20))
            {
                var answer = b.AnswerIds.Select(id => p.Tiles.First(t => t.Id == id).Alien.TrimStart('-')).Where(a => a.Any(char.IsLetter)).Distinct().ToList();
                if (answer.Count < 2) continue;
                Assert.IsFalse(Safe(b, $"Use \"{answer[0]}\" and \"{answer[1]}\"."), "two quoted answer words, adjacent");
                Assert.IsFalse(Safe(b, $"Think about \"{answer[0]}\". Much later, think about \"{answer[answer.Count - 1]}\"."), "two quoted, far apart");
                Assert.IsFalse(Safe(b, eng.GetReveal(w, p.Id).AlienText), "the full answer: " + eng.GetReveal(w, p.Id).AlienText);
                Assert.IsFalse(Safe(b, string.Join(" ", answer)), "the answer tiles as text");
                var example = p.Examples[0].Words.Select(t => t.Alien.TrimStart('-')).FirstOrDefault(a => a.Count(char.IsLetter) >= 3);
                if (example != null && !(b.Language.Code == "ja" || b.Language.Code == "zh" || b.Language.Code == "ar") && Safe(b, "Look at the rows."))
                    Assert.IsTrue(Safe(b, $"Look closely at \"{example}\" in row 1."), $"one quoted example word is allowed: {example}");
                checkedCount++;
            }
            Assert.That(checkedCount, Is.GreaterThan(50));
        }

        [Test]
        public void Rejects_NativeScript_Length_AndEmpty()   // L11
        {
            var (_, _, b) = First("ja");
            Assert.IsFalse(Safe(b, "Look at 扉 in the rows."));
            Assert.IsFalse(Safe(b, "Look at the row with книга."), "Cyrillic look-alikes");
            Assert.IsFalse(Safe(b, "Look​ at the rows."), "zero-width characters");
            Assert.IsFalse(Safe(b, new string('a', 161)));
            Assert.IsTrue(Safe(b, "Look at the rows " + new string('a', 160 - 18) + "."));
            Assert.IsFalse(Safe(b, ""));
            Assert.IsFalse(Safe(b, "   "));
            Assert.IsFalse(Safe(b, "Line one.\nLine two."));
        }

        [Test]
        public void Rejects_Instructions_Ordinals_Digits_AndUrls()   // L12
        {
            var (w, p, b) = First("de");
            var word = p.Tiles.Select(t => t.Alien.TrimStart('-')).First(a => a.Count(char.IsLetter) >= 4 && !HintSafety.English.Contains(HintSafety.Fold(a)));
            Assert.IsFalse(Safe(b, "The answer is in front of you."));
            Assert.IsFalse(Safe(b, $"Put '{word}' second."));
            Assert.IsFalse(Safe(b, $"{word} goes last."), "an ordinal next to an unquoted puzzle word");
            Assert.IsFalse(Safe(b, "Press 3 then 1."));
            Assert.IsFalse(Safe(b, "See www.example.com for help."));
            Assert.IsFalse(Safe(b, "Type the words in order."));
            // a meaning of an answer word plus an ordering word paraphrases the answer
            var gloss = b.AnswerIds.Select(id => p.Tiles.First(t => t.Id == id).Gloss).SelectMany(g => g.Split('-')).FirstOrDefault(g => g.Length >= 4 && g.All(char.IsLower) && !HintSafety.GlossStop.Contains(g));
            if (gloss != null) Assert.IsFalse(Safe(b, $"The word for {gloss} comes first."), "gloss + ordinal: " + gloss);
            Assert.IsTrue(Safe(b, "Compare rows 1 and 3: where does the action word sit?"));
        }

        [Test]
        public void ShortEnglishWords_AreNotPuzzleWords()   // L13
        {
            var eng = new LanguageEngine();
            foreach (var (_, _, b) in Sweep(eng, 10))
                Assert.IsTrue(Safe(b, "Look again: a word, no more, can change the row. Do the rows agree?"), b.Language.Name);
        }

        [TestCase("ja", "Start with wa, then ga, then o comes last.", "wa,ga,o")]
        [TestCase("es", "Start with el, then la, then a comes last.", "el,la,a")]
        public void Rejects_OrderingShortOrEnglishLookingTiles(string language, string hint, string forms)
        {
            var eng = new LanguageEngine();
            var candidates = new HashSet<string>(forms.Split(','));
            int checkedCount = 0;
            foreach (var (_, p, b) in Sweep(eng, 20))
            {
                if (b.Language.Code != language) continue;
                var words = p.Tiles.Select(t => t.Alien).Concat(p.Examples.SelectMany(e => e.Words.Select(t => t.Alien)));
                if (!words.Any(w => candidates.Contains(HintSafety.Fold(w)))) continue;
                Assert.IsFalse(Safe(b, hint), language + ": " + hint);
                checkedCount++;
            }
            Assert.That(checkedCount, Is.GreaterThan(0), "must exercise actual matching puzzle words");
        }

        [Test]
        public void UnknownConcept_FallsBackToThePuzzlesOwn()   // L15
        {
            var (w, p, b) = First("tr");
            var ctx = new LanguageEngine().GetHintContext(w, p.Id, "made-up-concept");
            Assert.AreEqual(b.Draft.Concept, ctx.Concept);
            Assert.IsNull(new LanguageEngine().GetHintContext(w, 9999, null));
            Assert.IsFalse(new LanguageEngine().IsHintSafe(w, 9999, "Look at the rows."));
        }

        // ------------------------------------------------------------------ the backend's copy

        [Test]
        public void BackendHintFacts_MatchTheEngine()   // L14
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "backend", "data", "hint-facts.json"));
            Assume.That(File.Exists(path), "backend/data/hint-facts.json is not next to the Unity project: " + path);
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8));
            Assert.AreEqual(HintFacts.Version, root["version"]);
            var concepts = (Dictionary<string, object>)root["concepts"];
            CollectionAssert.AreEquivalent(LangConcepts.All, concepts.Keys);
            foreach (var c in LangConcepts.All)
            {
                var j = (Dictionary<string, object>)concepts[c];
                var e = HintFacts.For(c);
                Assert.AreEqual(e.Focus, j["focus"], c);
                Assert.AreEqual(e.Offline1, j["offline1"], c);
                CollectionAssert.AreEqual(e.Facts, ((List<object>)j["facts"]).Cast<string>(), c);
            }
            CollectionAssert.AreEqual(HintFacts.Giveaways, ((List<object>)root["giveaways"]).Cast<string>());
            CollectionAssert.AreEqual(HintFacts.AnswerPhrases, ((List<object>)root["answer_phrases"]).Cast<string>());
            CollectionAssert.AreEqual(HintFacts.PositionWords, ((List<object>)root["position_words"]).Cast<string>());
            var guided = (Dictionary<string, object>)root["guided"];
            Assert.AreEqual(HintFacts.GuidedLevel1, guided["level1"]);
            Assert.AreEqual(HintFacts.GuidedLevel2, guided["level2"]);
        }

        /// <summary>Just enough JSON for the test above (objects, arrays, strings, numbers, literals).</summary>
        static class MiniJson
        {
            public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                        d[k] = Value(s, ref i); Ws(s, ref i);
                        if (s[i++] == '}') return d;
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++; Ws(s, ref i);
                    if (s[i] == ']') { i++; return l; }
                    while (true) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i++] == ']') return l; }
                }
                if (c == '"') return Str(s, ref i);
                int start = i;
                while (i < s.Length && ",}] \r\n\t".IndexOf(s[i]) < 0) i++;
                return s.Substring(start, i - start);
            }

            static string Str(string s, ref int i)
            {
                var sb = new StringBuilder(); i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
        }
    }
}

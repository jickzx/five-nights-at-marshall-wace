using System.Collections.Generic;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.UI.Tests
{
    public class GlyphTests
    {
        static string Key(int seed, ScriptFamily fam) => System.Convert.ToBase64String(GlyphGen.Raster(GlyphGen.Build(seed, fam), 32));

        [Test]
        public void SameSeed_DrawsTheSameGlyph()
        {
            foreach (ScriptFamily fam in System.Enum.GetValues(typeof(ScriptFamily)))
                Assert.AreEqual(Key(12345, fam), Key(12345, fam), fam.ToString());
        }

        [Test]
        public void EveryGlyph_HasInk_AndStaysInsideItsCell()
        {
            foreach (ScriptFamily fam in System.Enum.GetValues(typeof(ScriptFamily)))
                for (int seed = -50; seed < 150; seed++)
                {
                    var shape = GlyphGen.Build(seed * 7919, fam);
                    var mask = GlyphGen.Raster(shape, 32);
                    int ink = 0;
                    foreach (byte b in mask) if (b != 0) ink++;
                    Assert.That(ink, Is.InRange(24, 700), fam + " seed " + seed);
                    foreach (var stroke in shape.Strokes)
                        foreach (var p in stroke)
                        {
                            Assert.That(p.x, Is.InRange(0f, 1f), fam + " seed " + seed);
                            Assert.That(p.y, Is.InRange(0f, 1f), fam + " seed " + seed);
                        }
                }
        }

        [Test]
        public void DifferentWords_LookDifferent()
        {
            foreach (ScriptFamily fam in System.Enum.GetValues(typeof(ScriptFamily)))
            {
                var seen = new HashSet<string>();
                for (int seed = 0; seed < 60; seed++) seen.Add(Key(seed * 104729 + 3, fam));
                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(54), fam + " repeats too many glyphs");
            }
        }

        [Test]
        public void EachFamily_HasItsOwnHand()
        {
            Assert.AreEqual(ScriptFamily.Block, GlyphGen.FamilyFor("ja"));
            Assert.AreEqual(ScriptFamily.Flow, GlyphGen.FamilyFor("fr"));
            Assert.AreEqual(ScriptFamily.Flow, GlyphGen.FamilyFor("es-MX"));
            Assert.AreEqual(ScriptFamily.Stem, GlyphGen.FamilyFor("fi"));
            Assert.AreNotEqual(Key(99, ScriptFamily.Block), Key(99, ScriptFamily.Flow));
            Assert.DoesNotThrow(() => GlyphGen.FamilyFor(null));
            Assert.DoesNotThrow(() => GlyphGen.FamilyFor("xx"));
        }

        [Test]
        public void WriteOn_AddsInkAsItProgresses()
        {
            var shape = GlyphGen.Build(4242, ScriptFamily.Block);
            int Ink(float p) { int n = 0; foreach (byte b in GlyphGen.Raster(shape, 32, p)) if (b != 0) n++; return n; }
            Assert.AreEqual(0, Ink(0f));
            Assert.That(Ink(0.5f), Is.GreaterThan(0).And.LessThan(Ink(1f)));
        }
    }

    public class LoreTests
    {
        [Test]
        public void SplitReal_SeparatesPhraseFromReading()
        {
            Lore.SplitReal("扉を開けて (tobira o akete)", out var phrase, out var reading);
            Assert.AreEqual("扉を開けて", phrase);
            Assert.AreEqual("tobira o akete", reading);
            Lore.SplitReal("ouvre la porte", out phrase, out reading);
            Assert.AreEqual("ouvre la porte", phrase);
            Assert.AreEqual("", reading);
            Lore.SplitReal(null, out phrase, out reading);
            Assert.AreEqual("", phrase);
        }

        [Test]
        public void Redact_HidesTheLanguageName_UntilTheQuiz()
        {
            string s = Lore.Redact("Japanese puts the verb last, exactly like japanese.", "Japanese", null);
            StringAssert.DoesNotContain("apanese", s);
            StringAssert.Contains("puts the verb last", s);
            Assert.AreEqual("Verb last.", Lore.Redact("Verb last.", "Japanese"));
        }

        [Test]
        public void Fact_PrefersTheEngines_ThenTheLanguage_ThenTheWorld()
        {
            var world = Services.Language.Generate(1);
            StringAssert.Contains("particle", Lore.FactFor(new RevealInfo(), "Japanese", world));
            world.Real = new RealLanguage { Name = "Klingon", Code = "tlh" };
            StringAssert.Contains("Verb always comes last", Lore.FactFor(new RevealInfo(), "Klingon", world));
            Assert.AreEqual("02:05", Lore.Clock(125.9f));
        }

        [Test]
        public void Journal_GroupsPhrasesByLanguage()
        {
            var j = new RunJournal();
            j.Record("Japanese", new RevealInfo { RealText = "扉を開けて (tobira o akete)", Translation = "open the door" }, "fact", false);
            j.Record("japanese", new RevealInfo { RealText = "船を始動", Translation = "start the ship" }, null, true);
            j.Record("French", new RevealInfo { RealText = "ouvre la porte", Translation = "open the door" }, "f", true);
            Assert.AreEqual(2, j.Entries.Count);
            Assert.AreEqual(2, j.Entries[0].Phrases.Count);
            Assert.IsTrue(j.Entries[0].Named);
            Assert.AreEqual("fact", j.Entries[0].Fact);
        }
    }

    public class FontTests
    {
        [Test]
        public void HouseFont_DrawsJapaneseFrenchAndSpanish()
        {
            Assert.IsNotNull(Resources.Load<Font>("UI/Fonts/DotGothic16"), "DotGothic16 must ship under Resources/UI/Fonts");
            foreach (var s in new[] { "扉を開けて 船を始動 カタカナ ひらがな", "Où est la porte fermée ? Ça, c'est l'œil. Noël, garçon, hôtel", "¿Dónde está la puerta? ¡Enciende la nave, niño! pingüino" })
            {
                Assert.IsTrue(FontBook.Covers(FontBook.Pixel, s), s);
                Assert.AreSame(FontBook.Pixel, FontBook.For(s));
            }
        }

        [Test]
        public void FallbackFont_DrawsEveryOtherScript()
        {
            Assert.IsNotNull(Resources.Load<Font>("UI/Fonts/Unifont"), "Unifont must ship under Resources/UI/Fonts");
            foreach (var s in new[] { "문을 열어라", "开门 启动飞船", "افتح الباب", "פתח את הדלת", "दरवाज़ा खोलो", "เปิดประตู", "Άνοιξε την πόρτα", "kapıyı aç, gemiyi çalıştır", "otwórz drzwi, żółć", "mā má mǎ mà", "nyisd ki az ajtót, tűz" })
            {
                Assert.IsTrue(FontBook.Covers(FontBook.Wide, s), s);
                Assert.IsTrue(FontBook.Covers(FontBook.For(s), s), s);
            }
        }

        [Test]
        public void Pictograms_CoverTheStarterVocabulary()
        {
            foreach (var w in new[] { "eye", "door", "fire", "ship", "open", "close", "start", "stop" })
                Assert.IsNotNull(Pictograms.Get(w), w);
            Assert.IsNull(Pictograms.Get("antidisestablishmentarianism"));
        }
    }

    /// <summary>Every screen, reached the way the game reaches it, with the fake engine and scripted keys.</summary>
    public class UiFlowTests
    {
        GameUI _ui;

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            _ui = GameUI.Create();
            Services.UI = _ui;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui) { _ui.Dispose(); Object.DestroyImmediate(_ui.gameObject); }
            GameEvents.ClearAll();
            Services.ResetToFakes();
        }

        void Wait(float seconds) { for (float t = 0; t < seconds; t += 0.05f) _ui.Tick(0.05f, UiInput.None); }
        void Press(UiInput input) { _ui.Tick(0.05f, input); }

        [Test]
        public void FullRun_TitleToEscape_ThroughAWrongAnswer()
        {
            var state = Services.State;
            var lang = Services.Language;
            state.World = lang.Generate(7);
            state.TotalPuzzles = state.World.Puzzles.Count;
            _ui.ShowHud(state);

            bool started = false;
            _ui.ShowTitle(() => started = true);
            Assert.AreEqual("Title", _ui.ScreenName);
            Wait(0.5f);
            Press(UiInput.PressEnter);
            Assert.IsTrue(started, "Enter starts the game");

            _ui.HideAll();
            state.SetPhase(GamePhase.Explore);
            _ui.ShowPrompt("Press E to use console");
            Wait(0.3f);
            Assert.AreEqual("None", _ui.ScreenName);
            Assert.AreEqual("PRESS E TO USE CONSOLE", _ui.PromptShown);

            var puzzle = lang.GetPuzzle(state.World, 0, 0.3f);
            bool continued = false; int chosen = -1; int submits = 0;
            state.SetPhase(GamePhase.Puzzle);
            _ui.ShowPuzzle(puzzle, ids =>
            {
                submits++;
                var result = lang.Check(state.World, puzzle.Id, ids);
                if (!result.Correct) { state.Mistakes++; _ui.ShowAnswerFeedback(result); }
                else _ui.ShowReveal(lang.GetReveal(state.World, puzzle.Id), () => { continued = true; _ui.ShowQuiz(lang.GetQuiz(state.World, puzzle.Id), i => chosen = i); });
            });
            Assert.AreEqual("Puzzle", _ui.ScreenName);
            Wait(0.6f);

            Press(UiInput.PressEnter);
            Assert.AreEqual(0, submits, "an empty phrase is not sent");

            int open = puzzle.Tiles.FindIndex(t => t.Id == "open"), door = puzzle.Tiles.FindIndex(t => t.Id == "door");
            Press(UiInput.Key(open)); Press(UiInput.Key(door));
            Assert.AreEqual(2, _ui.PuzzlePicks);
            Press(UiInput.PressBack);
            Assert.AreEqual(1, _ui.PuzzlePicks, "Backspace takes back one tile");
            Press(UiInput.Key(door));
            Press(UiInput.PressEnter);
            Assert.AreEqual(1, submits);
            Assert.AreEqual("Puzzle", _ui.ScreenName, "a wrong answer keeps you at the console");
            Assert.AreEqual(1, _ui.Cracks, "a wrong answer cracks the glass");
            Wait(1.6f);
            Assert.AreEqual(0, _ui.PuzzlePicks, "the rejected phrase melts away");

            Press(UiInput.Key(door)); Press(UiInput.Key(open));
            Press(UiInput.PressEnter);
            Assert.AreEqual("Morph", _ui.ScreenName, "a right answer starts the reveal");
            Wait(4.5f);
            Assert.AreEqual("Read", _ui.ScreenName);
            Press(UiInput.PressEnter);
            Assert.IsTrue(continued);
            Assert.AreEqual("Quiz", _ui.ScreenName);
            Wait(0.6f);
            Press(UiInput.Key(0));
            Assert.AreEqual("Verdict", _ui.ScreenName);
            Assert.AreEqual(-1, chosen, "the game hears the choice only after the payoff");
            Wait(2f);
            Assert.AreEqual("Payoff", _ui.ScreenName);
            Assert.AreEqual(1, _ui.LanguagesLearned);
            Press(UiInput.PressEnter);
            Assert.AreEqual(0, chosen);

            bool restarted = false;
            state.PuzzlesSolved = 2;
            state.SetPhase(GamePhase.Escaped);
            _ui.ShowEnd(true, state, () => restarted = true);
            Assert.AreEqual("Escape", _ui.ScreenName);
            Wait(3f);
            Press(UiInput.PressEnter);
            Assert.IsTrue(restarted);
        }

        [Test]
        public void Death_FadesToWhite_ThenAcceptsRestart()
        {
            bool restarted = false;
            Services.State.SetPhase(GamePhase.Dead);
            _ui.ShowEnd(false, Services.State, () => restarted = true);
            Assert.AreEqual("Death", _ui.ScreenName);
            Wait(1f);
            Press(UiInput.PressEnter);
            Assert.IsFalse(restarted, "the fade cannot be skipped in its first seconds");
            Wait(3f);
            Press(UiInput.PressEnter);
            Assert.IsTrue(restarted);
        }

        [Test]
        public void Mouse_ClickStartsTheGame_AndQuizAloneStillWorks()
        {
            bool started = false;
            _ui.ShowTitle(() => started = true);
            Wait(0.5f);
            Press(new UiInput { Digit = -1, HasPointer = true, Pointer = Vector2.zero, Click = true });
            Assert.IsTrue(started);

            int chosen = -1;
            Services.State.World = Services.Language.Generate(1);
            _ui.ShowQuiz(Services.Language.GetQuiz(Services.State.World, 1), i => chosen = i);
            Assert.AreEqual("Quiz", _ui.ScreenName);
            Wait(0.6f);
            Press(UiInput.Key(2));
            Wait(2f);
            Press(UiInput.PressEnter);
            Assert.AreEqual(2, chosen);
        }

        [Test]
        public void Mouse_ClickOnAQuizOption_ChoosesIt()
        {
            int chosen = -1;
            Services.State.World = Services.Language.Generate(1);
            _ui.ShowQuiz(Services.Language.GetQuiz(Services.State.World, 1), i => chosen = i);
            Wait(0.6f);
            // option 4 sits in the right column, second row; stage space has its origin at the centre, y up
            var overOption4 = new UiInput { Digit = -1, HasPointer = true, Pointer = new Vector2(326 + 125 - 320, 180 - (234 + 17)), Click = true };
            Press(overOption4);
            Assert.AreEqual("Verdict", _ui.ScreenName);
            Wait(2f);
            Press(new UiInput { Digit = -1, HasPointer = true, Pointer = new Vector2(-300, 170), Click = true });
            Assert.AreEqual(3, chosen, "a click anywhere continues past the payoff");
        }

        [Test]
        public void StageChange_AndHideAll_DoNotThrow()
        {
            Services.State.SetPhase(GamePhase.Explore);
            _ui.ShowHud(Services.State);
            Services.State.SetBeastStage(2);
            Services.State.SetThreat(0.9f);
            Wait(0.5f);
            _ui.HideAll();
            _ui.ShowAnswerFeedback(new AnswerResult { Correct = false, Feedback = "no console is open" });
            Wait(0.2f);
            Assert.AreEqual("None", _ui.ScreenName);
        }
    }
}

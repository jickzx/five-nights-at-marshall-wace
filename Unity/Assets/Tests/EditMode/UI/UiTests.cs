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
            // the engine sometimes leads with the reading: the native script is still the phrase
            Lore.SplitReal("sakana (\u0633\u0643\u0646)", out phrase, out reading);
            Assert.AreEqual("\u0633\u0643\u0646", phrase);
            Assert.AreEqual("sakana", reading);
        }

        [Test]
        public void Redact_HidesTheLanguageName_UntilTheQuiz()
        {
            string s = Lore.Redact("Japanese puts the verb last, exactly like japanese.", "Japanese", null);
            StringAssert.DoesNotContain("apanese", s);
            StringAssert.Contains("puts the verb last", s);
            Assert.AreEqual("Verb last.", Lore.Redact("Verb last.", "Japanese"));
            StringAssert.DoesNotContain("andarin", Lore.Redact("Mandarin almost never puts a number straight onto a noun.", "Mandarin Chinese"));
        }

        [Test]
        public void Arabic_IsJoinedAndReversedForDrawing()
        {
            // s-k-n: start form, middle form, end form, then reversed so a left-to-right renderer draws it right
            Assert.AreEqual("\uFEE6\uFEDC\uFEB3", Rtl.Visual("\u0633\u0643\u0646"));
            // kaf + alef + ta + ba: alef does not join onward, so ta starts a new joined pair
            Assert.AreEqual("\uFE90\uFE97\uFE8E\uFEDB", Rtl.Visual("\u0643\u0627\u062A\u0628"));
            // lam + alef fuse into one glyph
            Assert.AreEqual("\uFEFB", Rtl.Visual("\u0644\u0627"));
            // Latin around it stays where it is
            Assert.AreEqual("a \uFEE6\uFEDC\uFEB3 b", Rtl.Visual("a \u0633\u0643\u0646 b"));
            Assert.AreEqual("plain", Rtl.Visual("plain"));
            Assert.IsTrue(FontBook.Covers(FontBook.Wide, Rtl.Visual("\u0643\u0627\u062A\u0628 \u0645\u0643\u062A\u0628 \u0644\u0627\u0639\u0628")));
        }

        [Test]
        public void Fact_PrefersTheEngines_ThenTheLanguage()
        {
            var world = Services.Language.Generate(1);
            Assert.AreEqual("From the engine.", Lore.FactFor(new RevealInfo { NowYouKnow = " From the engine. " }, "Japanese", world));
            StringAssert.Contains("particle", Lore.FactFor(new RevealInfo(), "Japanese", world));
            Assert.AreEqual("", Lore.FactFor(new RevealInfo(), "Klingon", world));
            Assert.AreEqual("The feature.", Lore.PatternFor(new RevealInfo { Feature = "The feature.", Explanation = "longer" }));
            Assert.AreEqual("longer", Lore.PatternFor(new RevealInfo { Explanation = "longer" }));
            Assert.AreEqual("Which language?", Lore.FirstLine("Which language?\nx = y"));
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

    /// <summary>The real language engine, every language it has, pushed through every screen.</summary>
    public class RealEngineTests
    {
        GameUI _ui;
        Fnmw.Lang.LanguageEngine _engine;

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            _engine = new Fnmw.Lang.LanguageEngine();
            Services.Language = _engine;
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

        static void AssertDrawable(string s, string what)
        {
            if (string.IsNullOrEmpty(s)) return;
            string visual = Rtl.Visual(s);
            Assert.IsTrue(FontBook.Covers(FontBook.For(visual), visual), "a shipped font must draw " + what + ": " + s);
        }

        [Test]
        public void EveryStringTheEngineEmits_HasGlyphsInAShippedFont()
        {
            var languages = new HashSet<string>();
            for (int seed = 1; seed <= 12; seed++)
            {
                var world = _engine.Generate(seed);
                for (int i = 0; i < world.Puzzles.Count; i++)
                    foreach (float difficulty in new[] { 0.1f, 0.9f })
                    {
                        var p = _engine.GetPuzzle(world, i, difficulty);
                        var r = _engine.GetReveal(world, p.Id);
                        var q = _engine.GetQuiz(world, p.Id);
                        languages.Add(r.LanguageName);
                        AssertDrawable(p.TargetMeaning, "meaning"); AssertDrawable(p.AlienName, "alien name");
                        foreach (var e in p.Examples) { AssertDrawable(e.Picture, "example"); foreach (var w in e.Words) AssertDrawable(w.Alien, "word"); }
                        foreach (var t in p.Tiles) AssertDrawable(t.Alien, "tile");
                        AssertDrawable(r.RealText, "real text"); AssertDrawable(r.Translation, "translation");
                        AssertDrawable(r.Feature, "feature"); AssertDrawable(r.NowYouKnow, "fact"); AssertDrawable(r.Explanation, "explanation");
                        AssertDrawable(q.Prompt, "quiz prompt"); AssertDrawable(q.Explanation, "quiz explanation");
                        foreach (var o in q.Options) AssertDrawable(o, "quiz option");
                        AssertDrawable(_engine.Check(world, p.Id, new[] { p.Tiles[0].Id }).Feedback, "feedback");
                    }
            }
            Assert.That(languages.Count, Is.GreaterThanOrEqualTo(7), "expected every source language to be exercised");
        }

        static void AssertOnStage(Rect r, string what)
        {
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(PuzzleLayout.L - 0.5f), what + " left edge " + r);
            Assert.That(r.xMax, Is.LessThanOrEqualTo(PuzzleLayout.R + 0.5f), what + " right edge " + r);
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(PuzzleLayout.Top - 2f), what + " top " + r);
            Assert.That(r.yMax, Is.LessThanOrEqualTo(PuzzleLayout.Foot + 0.5f), what + " bottom " + r);
        }

        static Rect Inset(Rect r) => new Rect(r.x + 0.5f, r.y + 0.5f, r.width - 1f, r.height - 1f);

        [Test]
        public void SentencePuzzles_FitTheConsole()
        {
            int checkedPuzzles = 0;
            float minScale = 1f, minSlotScale = 1f, maxTiles = 0;
            for (int seed = 1; seed <= 24; seed++)
            {
                var world = _engine.Generate(seed);
                Services.State.World = world;
                Services.State.TotalPuzzles = world.Puzzles.Count;
                for (int i = 0; i < world.Puzzles.Count; i++)
                    foreach (float difficulty in new[] { 0.05f, 0.35f, 0.65f, 0.95f })
                    {
                        var p = _engine.GetPuzzle(world, i, difficulty);
                        string tag = "seed " + seed + " puzzle " + i + " d" + difficulty + ": ";
                        Assert.That(p.Tiles.Count, Is.LessThanOrEqualTo(PuzzleLayout.MaxSlots), tag + "more tiles than number keys");
                        var lay = PuzzleLayout.For(p);
                        maxTiles = Mathf.Max(maxTiles, p.Tiles.Count);

                        Assert.IsFalse(lay.TwoCol, tag + "three examples stay in one column");
                        for (int e = 0; e < lay.Rows.Length; e++)
                        {
                            var row = lay.Rows[e];
                            AssertOnStage(row.Plate, tag + "example " + e + " plate");
                            Assert.That(row.Plate.yMax, Is.LessThanOrEqualTo(lay.YAnswer - 4 + 0.5f), tag + "example " + e + " plate runs into the answer");
                            for (int k = 0; k < row.Widths.Length; k++)
                            {
                                var w = row.Word(k);
                                AssertOnStage(w, tag + "example " + e + " word " + k);
                                Assert.That(w.xMin, Is.GreaterThanOrEqualTo(row.Plate.xMax), tag + "word over its plate");
                                Assert.That(w.yMax, Is.LessThanOrEqualTo(lay.YAnswer - 4 + 1f), tag + "example word runs into the answer");
                            }
                            minScale = Mathf.Min(minScale, row.Scale);
                        }

                        AssertOnStage(lay.Slots, tag + "slots");
                        AssertOnStage(lay.Send, tag + "send");
                        Assert.That(lay.Slots.xMax, Is.LessThanOrEqualTo(lay.Send.xMin), tag + "slots run under SEND");

                        // the answer, and every tile at once, fit the slots (with the cursor while not full)
                        var answer = Fnmw.Lang.LanguageEngine.DebugSolution(world, p.Id);
                        foreach (var picks in new[] { answer, p.Tiles.ConvertAll(t => t.Id).ToArray() })
                        {
                            var widths = new List<float>();
                            foreach (var id in picks) widths.Add(PuzzleLayout.WordWidth(p.Tiles.Find(t => t.Id == id)));
                            float room = lay.SlotRoom(widths.Count);
                            PuzzleLayout.Fit(widths, room, out float gap, out float scale);
                            float used = -gap;
                            foreach (var w in widths) used += w + gap;
                            Assert.That(used * scale, Is.LessThanOrEqualTo(room + 0.5f), tag + "placed words overflow the slots");
                            float cursorRight = lay.Slots.x + (used + gap) * scale + 2 + (widths.Count < PuzzleLayout.MaxSlots ? 32 : 0);
                            Assert.That(cursorRight, Is.LessThanOrEqualTo(lay.Send.xMin), tag + "cursor runs under SEND");
                            minSlotScale = Mathf.Min(minSlotScale, scale);
                        }

                        Assert.That(lay.Tiles.Length, Is.EqualTo(p.Tiles.Count));
                        Assert.That(lay.TileRows, Is.LessThanOrEqualTo(2), tag);
                        for (int a = 0; a < lay.Tiles.Length; a++)
                        {
                            AssertOnStage(lay.Tiles[a], tag + "tile " + a);
                            Assert.That(lay.Tiles[a].yMin, Is.GreaterThanOrEqualTo(lay.Slots.yMax - 2f), tag + "tile under the answer row");
                            for (int b = a + 1; b < lay.Tiles.Length; b++)
                                Assert.IsFalse(Inset(lay.Tiles[a]).Overlaps(Inset(lay.Tiles[b])), tag + "tiles " + a + " and " + b + " overlap");
                        }

                        // and the screen itself builds from the same layout, and takes the engine's own answer
                        if (difficulty < 0.1f || difficulty > 0.9f)
                        {
                            int submits = 0;
                            _ui.ShowPuzzle(p, ids => submits++);
                            Wait(0.6f);
                            foreach (var id in answer) Press(UiInput.Key(p.Tiles.FindIndex(t => t.Id == id)));
                            Assert.AreEqual(answer.Length, _ui.PuzzlePicks, tag + "every answer tile places");
                            Press(UiInput.PressEnter);
                            Assert.AreEqual(1, submits, tag);
                            foreach (var rt in _ui.GetComponentsInChildren<RectTransform>(true))
                            {
                                if (!rt.name.StartsWith("Tile") || rt.parent == null || rt.parent.name != "Tiles") continue;
                                int idx = int.Parse(rt.name.Substring(4));
                                var want = lay.Tiles[idx];
                                Assert.That(rt.anchoredPosition.x, Is.EqualTo(Mathf.Round(want.x)).Within(0.01f), tag + rt.name);
                                Assert.That(-rt.anchoredPosition.y, Is.EqualTo(Mathf.Round(want.y)).Within(0.01f), tag + rt.name);
                            }
                            _ui.HideAll();
                        }
                        checkedPuzzles++;
                    }
            }
            Assert.That(checkedPuzzles, Is.GreaterThan(0));
            Debug.Log("[UI] sentence layout: " + checkedPuzzles + " puzzles, max tiles " + maxTiles + ", smallest example scale " + minScale + ", smallest slot scale " + minSlotScale);
        }

        [Test]
        public void FullRun_FiveConsoles_ToTheEscapeSummary()
        {
            var state = Services.State;
            state.World = _engine.Generate(3);
            state.TotalPuzzles = state.World.Puzzles.Count;
            _ui.ShowHud(state);

            for (int i = 0; i < state.TotalPuzzles; i++)
            {
                var puzzle = _engine.GetPuzzle(state.World, i, 0.5f);
                bool revealed = false, continued = false; int chosen = -1;
                state.SetPhase(GamePhase.Puzzle);
                _ui.ShowPuzzle(puzzle, ids =>
                {
                    var result = _engine.Check(state.World, puzzle.Id, ids);
                    if (!result.Correct) { _ui.ShowAnswerFeedback(result); return; }
                    revealed = true;
                    _ui.ShowReveal(_engine.GetReveal(state.World, puzzle.Id), () => { continued = true; _ui.ShowQuiz(_engine.GetQuiz(state.World, puzzle.Id), c => chosen = c); });
                });
                Wait(0.6f);
                foreach (var id in Fnmw.Lang.LanguageEngine.DebugSolution(state.World, puzzle.Id))
                    Press(UiInput.Key(puzzle.Tiles.FindIndex(t => t.Id == id)));
                Press(UiInput.PressEnter);
                Assert.IsTrue(revealed, "console " + (i + 1) + ": the engine's own solution opens the reveal");
                Wait(5f);
                Assert.AreEqual("Read", _ui.ScreenName);
                Press(UiInput.PressEnter);
                Assert.IsTrue(continued);
                Wait(0.6f);
                int right = Fnmw.Lang.LanguageEngine.DebugQuizAnswer(state.World, puzzle.Id);
                Press(UiInput.Key(i % 2 == 0 ? right : (right + 1) % 4));     // alternate right and wrong guesses
                Wait(2.2f);
                Assert.AreEqual("Payoff", _ui.ScreenName);
                Press(UiInput.PressEnter);
                Assert.That(chosen, Is.GreaterThanOrEqualTo(0));
                state.PuzzlesSolved++;
                _ui.HideAll();
            }

            Assert.AreEqual(state.TotalPuzzles, _ui.LanguagesLearned, "one journal entry per language, right or wrong");
            Assert.AreEqual(0, _ui.Cracks, "no wrong answers, no cracks");
            bool restarted = false;
            state.SetPhase(GamePhase.Escaped);
            _ui.ShowEnd(true, state, () => restarted = true);
            Wait(4f);
            Press(UiInput.PressEnter);
            Assert.IsTrue(restarted);
        }
    }
}

using System;
using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Core.Ai;
using Fnmw.Core.Fakes;
using Fnmw.Game.Debrief;
using Fnmw.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Fnmw.Tests
{
    public class RunDebriefTests
    {
        /// <summary>Records only what the debrief touches.</summary>
        sealed class DebriefUi : IGameUI
        {
            public readonly List<string> Debriefs = new List<string>();
            public string Last => Debriefs.Count > 0 ? Debriefs[Debriefs.Count - 1] : null;
            public void ShowTitle(Action onStart) { }
            public void ShowHud(GameState state) { }
            public void ShowPrompt(string text) { }
            public void ShowPuzzle(LangPuzzle puzzle, Action<string[]> onSubmit) { }
            public void ShowAnswerFeedback(AnswerResult result) { }
            public void ShowReveal(RevealInfo reveal, Action onContinue) { }
            public void ShowQuiz(QuizQuestion quiz, Action<int> onChoose) { }
            public void ShowEnd(bool escaped, GameState state, Action onRestart) { }
            public void HideAll() { }
            public void ShowDebrief(string text) => Debriefs.Add(text);
        }

        DebriefUi _ui;
        ScriptedAiText _ai;
        RunDebriefer _d;

        static readonly string[] Langs = { "German", "Japanese", "Turkish" };
        static readonly string[] Concepts = { LangConcepts.VerbFinal, LangConcepts.Particles, LangConcepts.SuffixOrder };

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            _ui = new DebriefUi();
            Services.UI = _ui;
            _ai = new ScriptedAiText();
            Services.Ai = _ai;
            NewRunState();
            _d = new RunDebriefer();
            _d.Attach();
        }

        [TearDown]
        public void TearDown()
        {
            _d.Detach();
            GameEvents.ClearAll();
            Services.ResetToFakes();
        }

        /// <summary>What a scene reload gives the next run: a fresh GameState with a three-console world.</summary>
        static void NewRunState()
        {
            Services.NewState();
            var w = new LanguageWorld { Seed = 7 };
            for (int i = 0; i < Langs.Length; i++)
            {
                w.Puzzles.Add(new LangPuzzle { Id = 100 + i, Concept = Concepts[i] });
                w.Languages.Add(new RealLanguage { Name = Langs[i], Code = "x" + i });
            }
            Services.State.World = w;
            Services.State.TotalPuzzles = Langs.Length;
        }

        static GameState S => Services.State;

        static void Open(int index, float at)
        {
            S.Elapsed = at;
            S.CurrentPuzzleIndex = index;
            S.World.Real = S.World.Languages[index];
            S.SetPhase(GamePhase.Puzzle);
            GameEvents.RaisePuzzleStarted(S.World.Puzzles[index]);
        }

        static void Wrong(int index, string missed, float seconds)
        {
            S.Mistakes++;
            GameEvents.RaiseAnswerSubmitted(new AnswerEvent { PuzzleId = 100 + index, Correct = false, MissedConcept = missed, ResponseTime = seconds });
        }

        static void Right(int index, float seconds, bool quiz)
        {
            GameEvents.RaiseAnswerSubmitted(new AnswerEvent { PuzzleId = 100 + index, Correct = true, ResponseTime = seconds });
            S.PuzzlesSolved++;
            GameEvents.RaiseQuizAnswered(new QuizEvent { PuzzleId = 100 + index, Correct = quiz, RealLanguage = Langs[index] });
            S.SetPhase(GamePhase.Explore);
        }

        /// <summary>German clean, Japanese with two slips, beast to stage 3, then either the ship or Turkish and the beast.</summary>
        static void PlayRun(bool escape)
        {
            S.SetPhase(GamePhase.Explore);
            Open(0, 20f); Right(0, 14f, quiz: true);
            Open(1, 60f); Wrong(1, LangConcepts.Particles, 11f); Wrong(1, LangConcepts.Particles, 9f);
            S.Elapsed = 75f; S.SetBeastStage(2);
            Right(1, 21f, quiz: false);
            S.Elapsed = 130f; S.SetBeastStage(3);
            if (escape)
            {
                Open(2, 140f); Right(2, 30f, quiz: true);
                S.Elapsed = 190f;
                S.SetPhase(GamePhase.Chase);
                S.SetPhase(GamePhase.Escaped);
            }
            else
            {
                Open(2, 140f);
                S.Elapsed = 170f;
                S.SetPhase(GamePhase.Dead);
            }
        }

        static string Json(string text) => JsonUtility.ToJson(new DebriefWriter.Reply { debrief = text });

        // ------------------------------------------------------------------ the log

        [Test]
        public void Log_RecordsAScriptedRun()
        {
            _ai.Available = false;
            PlayRun(escape: false);
            var log = _d.Log;
            Assert.AreEqual(RunOutcome.Caught, log.Outcome);
            Assert.AreEqual(3, log.Consoles.Count);
            CollectionAssert.AreEqual(Langs, log.Languages);
            Assert.AreEqual(LangConcepts.VerbFinal, log.Consoles[0].Concept);
            Assert.IsTrue(log.Consoles[0].Solved);
            Assert.AreEqual(true, log.Consoles[0].QuizCorrect);
            Assert.AreEqual(2, log.Consoles[1].Wrong);
            CollectionAssert.AreEqual(new[] { LangConcepts.Particles, LangConcepts.Particles }, log.Consoles[1].Missed);
            CollectionAssert.AreEqual(new[] { 11f, 9f, 21f }, log.Consoles[1].ResponseTimes);
            Assert.AreEqual(false, log.Consoles[1].QuizCorrect);
            Assert.IsFalse(log.Consoles[2].Solved);
            Assert.AreEqual(2, log.CaughtAtConsole, "caught with the Turkish console open");
            Assert.AreEqual(3, log.HighestStage);
            Assert.AreEqual(130f, log.HighestStageAt);
            Assert.AreEqual(2, log.StageChanges.Count);
            Assert.AreEqual(170f, log.Elapsed);
            Assert.AreEqual(2, log.Mistakes);
            Assert.AreEqual(2, log.Solved);

            string facts = log.Facts();
            StringAssert.Contains("caught by the beast at 2:50", facts);
            StringAssert.Contains("console 2: Japanese, rule particles", facts);
            StringAssert.Contains("2 wrong (missed particles, particles)", facts);
            StringAssert.Contains("highest stage 3 of 3, reached at 2:10", facts);
        }

        [Test]
        public void Log_IgnoresEventsAfterTheEnd_AndResetsForTheNextRun()
        {
            _ai.Available = false;
            PlayRun(escape: true);
            GameEvents.RaiseAnswerSubmitted(new AnswerEvent { PuzzleId = 102, Correct = false, MissedConcept = "word-order" });
            GameEvents.RaiseBeastStageChanged(1);
            Assert.AreEqual(2, _d.Log.Mistakes, "nothing counts after the ship has left");
            Assert.AreEqual(2, _d.Log.StageChanges.Count);

            NewRunState();                 // Restart reloads the scene: a fresh GameState in Title
            S.SetPhase(GamePhase.Explore); // and the next run begins
            Assert.AreEqual(0, _d.Log.Consoles.Count);
            Assert.AreEqual(RunOutcome.None, _d.Log.Outcome);
            Assert.AreEqual(1, _d.Log.HighestStage);
            Assert.AreEqual(0, _d.Log.StageChanges.Count);
            Assert.IsNull(_d.Shown);
        }

        [Test]
        public void Log_ReopenedConsoleKeepsItsHistory()
        {
            var log = new RunLog();
            log.PuzzleStarted(new LangPuzzle { Id = 1, Concept = "particles" }, 0, "Japanese", 5f);
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = false, MissedConcept = "particles", ResponseTime = 4f });
            log.PuzzleStarted(new LangPuzzle { Id = 9, Concept = "particles" }, 0, "Japanese", 30f);   // walked away, came back, rebuilt
            log.Answer(new AnswerEvent { PuzzleId = 9, Correct = true, ResponseTime = 6f });
            Assert.AreEqual(1, log.Consoles.Count);
            Assert.AreEqual(1, log.Consoles[0].Wrong);
            Assert.IsTrue(log.Consoles[0].Solved);
            Assert.AreEqual(5f, log.Consoles[0].StartedAt);
        }

        // ------------------------------------------------------------------ stock

        [Test]
        public void Stock_ForAnEscape()
        {
            _ai.Available = false;
            PlayRun(escape: true);
            string t = _d.Shown;
            Assert.AreEqual("You read German, Japanese and Turkish. You slipped 2 times, mostly on particles at the Japanese console. " +
                            "The beast reached stage 3; you lifted off at 3:10.", t);
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(t, _ui.Last, "the stock account goes up at once");
            Assert.IsTrue(DebriefWriter.Check(Json(t), _d.Log, Langs, out _, out string why), "the stock text passes its own reply check: " + why);
        }

        [Test]
        public void Stock_ForADeath()
        {
            _ai.Available = false;
            PlayRun(escape: false);
            string t = _d.Shown;
            Assert.AreEqual("You read German, Japanese and Turkish. You slipped 2 times, mostly on particles at the Japanese console. " +
                            "The beast reached stage 3 and caught you at the Turkish console at 2:50.", t);
            Assert.IsTrue(DebriefWriter.Check(Json(t), _d.Log, Langs, out _, out string why), why);
        }

        [Test]
        public void Stock_CaughtOnTheShore_WithNoSlips()
        {
            var log = new RunLog();
            log.PuzzleStarted(new LangPuzzle { Id = 1 }, 0, "French", 5f);
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = true, ResponseTime = 8f });
            log.End(false, 65f, 1, 5, caughtInPuzzle: false);
            Assert.AreEqual("You read French. Not one wrong answer. The beast reached stage 1 and caught you at 1:05, 1 of 5 consoles decoded.",
                DebriefWriter.Stock(log));

            var none = new RunLog();
            none.End(false, 12f, 0, 5, false);
            string t = DebriefWriter.Stock(none);
            Assert.AreEqual(2, DebriefWriter.CountSentences(t));
            StringAssert.StartsWith("You never reached a console.", t);
        }

        // ------------------------------------------------------------------ AI paths

        [Test]
        public void Unavailable_KeepsStock_AndSendsNothing()
        {
            _ai.Available = false;
            PlayRun(escape: true);
            Assert.AreEqual(0, _ai.Requests.Count);
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(1, _ui.Debriefs.Count);
        }

        [Test]
        public void FailedRequest_KeepsStock()
        {
            _ai.Fail(AiStatus.Timeout);
            PlayRun(escape: true);
            Assert.AreEqual(1, _ai.Requests.Count);
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(1, _ui.Debriefs.Count, "nothing replaces the stock account");
            StringAssert.Contains("lifted off", _ui.Last);
        }

        [Test]
        public void ValidReply_ReplacesStock_AndTheRequestCarriesOnlyTheLog()
        {
            const string live = "German came easily and Japanese particles tripped you twice. The beast grew to stage 3 while you worked, and you lifted off at 3:10.";
            _ai.Reply(Json(live));
            PlayRun(escape: true);
            Assert.IsTrue(_d.Live);
            Assert.AreEqual(live, _d.Shown);
            Assert.AreEqual(2, _ui.Debriefs.Count, "stock first, then the live one");
            Assert.AreEqual(live, _ui.Last);

            var req = _ai.Requests[0];
            Assert.AreEqual("debrief", req.Feature);
            StringAssert.Contains("\"debrief\"", req.SchemaJson);
            StringAssert.Contains("did not target", req.System);
            Assert.AreEqual("Facts of this run:\n" + _d.Log.Facts(), req.Prompt);
        }

        [Test]
        public void InvalidReply_IsRejected_AndStockStays()
        {
            _ai.Reply(Json("The beast targeted your weakness in Japanese particles. You lifted off at 3:10."));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Debrief\] shown: stock \(reply rejected: beast agency"));
            PlayRun(escape: true);
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(1, _ui.Debriefs.Count);
        }

        [Test]
        public void Check_RejectsWhatTheLogCannotBackUp()
        {
            _ai.Available = false;
            PlayRun(escape: true);
            var log = _d.Log;
            string Why(string text) { DebriefWriter.Check(text, log, Langs, out _, out string why); return why; }

            Assert.IsNull(Why(Json("You read German, Japanese and Turkish. The beast reached stage 3 and you lifted off at 3:10.")), "a fair account passes");
            Assert.AreEqual("not JSON", Why("You read German."));
            Assert.AreEqual("sentences 1", Why(Json("You read German, Japanese and Turkish and lifted off at 3:10.")));
            Assert.AreEqual("sentences 4", Why(Json("You read German. You read Japanese. You read Turkish. You lifted off.")));
            Assert.AreEqual("length", Why(Json(string.Join(" ", System.Linq.Enumerable.Repeat("word", 50)) + ". You lifted off.")));
            Assert.AreEqual("names Spanish", Why(Json("You read Spanish and German. You lifted off at 3:10.")));
            Assert.AreEqual("number 4", Why(Json("You slipped 4 times. You lifted off at 3:10.")));
            Assert.AreEqual("number seven", Why(Json("You slipped seven times. You lifted off.")));
            Assert.AreEqual("says caught", Why(Json("You read German and Japanese. The beast caught you at the ship.")));
            Assert.AreEqual("beast agency", Why(Json("The beast chose Japanese to break you. You lifted off at 3:10.")));
            Assert.IsNull(Why(Json("Not one slip on German. Japanese particles cost you 2 tries before you lifted off.")), "'not one' is not a count");
        }

        [Test]
        public void Check_ForbidsALanguageOfTheWorldThatWasNotReached()
        {
            var reached = new RunLog();
            reached.PuzzleStarted(new LangPuzzle { Id = 1 }, 0, "Mandarin Chinese", 1f);
            reached.End(false, 40f, 0, 3, true);
            var world = new[] { "Mandarin Chinese", "Arabic", "Klingonese" };
            Assert.IsTrue(DebriefWriter.Check(Json("You met Chinese and froze. The beast caught you there."), reached, world, out _, out _), "either word of a met name is fine");
            Assert.IsFalse(DebriefWriter.Check(Json("You met Arabic and froze. The beast caught you there."), reached, world, out _, out string why));
            Assert.AreEqual("names Arabic", why);
            Assert.IsFalse(DebriefWriter.Check(Json("You met Klingonese. The beast caught you there."), reached, world, out _, out why), "names from the world list count too");
        }

        [Test]
        public void StaleReply_IsDroppedAfterRestart()
        {
            _ai.Hold = true;
            _ai.Reply(Json("German was clean and Japanese particles cost you. You lifted off at 3:10."));
            PlayRun(escape: true);
            string stock = _d.Shown;
            _d.Cancel();          // what GameDirector.Restarting does

            // the next run, after the reload, ends in death and also asks
            NewRunState();
            _ai.Reply(Json("You read German and Japanese. The beast caught you at the Turkish console at 2:50."));
            PlayRun(escape: false);
            string stock2 = _d.Shown;
            Assert.AreNotEqual(stock, stock2);

            _ai.Flush();
            Assert.IsTrue(_d.Live);
            Assert.AreEqual("You read German and Japanese. The beast caught you at the Turkish console at 2:50.", _ui.Last, "only the current run's reply lands");
            CollectionAssert.DoesNotContain(_ui.Debriefs, "German was clean and Japanese particles cost you. You lifted off at 3:10.");
        }

        [Test]
        public void Detach_DropsThePendingReply()
        {
            _ai.Hold = true;
            _ai.Reply(Json("German was clean and Japanese particles cost you. You lifted off at 3:10."));
            PlayRun(escape: true);
            _d.Detach();          // the component is destroyed
            _ai.Flush();
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(1, _ui.Debriefs.Count);
        }
    }

    /// <summary>The real end screens print the debrief, before or after they come up, and swap it when it changes.</summary>
    public class DebriefUiTests
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

        [Test]
        public void Escape_ShowsDebriefSentBeforeTheScreen_ThenTheLiveOne()
        {
            var s = Services.State;
            s.TotalPuzzles = 3; s.PuzzlesSolved = 3;
            s.SetPhase(GamePhase.Escaped);
            _ui.ShowDebrief("Stock one. Stock two.");
            Assert.IsNull(_ui.DebriefShown, "no end screen yet");
            _ui.ShowEnd(true, s, () => { });
            Assert.AreEqual("Stock one. Stock two.", _ui.DebriefShown);
            Wait(5f);
            _ui.ShowDebrief("Live one. Live two.");
            Assert.AreEqual("Live one. Live two.", _ui.DebriefShown);
            _ui.ShowDebrief(null);
            Assert.IsNull(_ui.DebriefShown);
        }

        [Test]
        public void Death_ShowsDebriefArrivingAfterTheScreen()
        {
            Services.State.SetPhase(GamePhase.Dead);
            _ui.ShowEnd(false, Services.State, () => { });
            Assert.IsNull(_ui.DebriefShown);
            _ui.ShowDebrief("You read German. The beast caught you at 2:50.");
            Assert.AreEqual("You read German. The beast caught you at 2:50.", _ui.DebriefShown);
            Wait(7f);
            _ui.ShowDebrief("Live. Account.");
            Assert.AreEqual("Live. Account.", _ui.DebriefShown);
        }

        [Test]
        public void NewTitle_ClearsTheLastRunsDebrief()
        {
            _ui.ShowDebrief("Old run. Old account.");
            _ui.ShowTitle(() => { });
            Services.State.SetPhase(GamePhase.Dead);
            _ui.ShowEnd(false, Services.State, () => { });
            Assert.IsNull(_ui.DebriefShown);
        }
    }
}

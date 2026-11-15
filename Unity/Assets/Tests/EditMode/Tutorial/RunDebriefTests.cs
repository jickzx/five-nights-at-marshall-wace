using System;
using System.Collections.Generic;
using System.Reflection;
using Fnmw.Core;
using Fnmw.Core.Ai;
using Fnmw.Core.Fakes;
using Fnmw.Game;
using Fnmw.Game.Debrief;
using Fnmw.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
        bool _demoWas;

        static readonly string[] Langs = { "German", "Japanese", "Turkish" };
        static readonly string[] Concepts = { LangConcepts.VerbFinal, LangConcepts.Particles, LangConcepts.SuffixOrder };

        [SetUp]
        public void SetUp()
        {
            _demoWas = DemoMode.On;
            DemoMode.On = false;
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
            DemoMode.On = _demoWas;
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

        // ------------------------------------------------------------------ fair replies the check used to reject

        [Test]
        public void Check_BeastAgency_OnlyWhenTheBeastOrNoOneChooses()
        {
            _ai.Available = false;
            PlayRun(escape: true);
            var log = _d.Log;
            string Why(string text) { DebriefWriter.Check(Json(text), log, Langs, out _, out string why); return why; }

            Assert.IsNull(Why("You picked the wrong word twice on Japanese. You lifted off at 3:10."), "the player choosing is fair");
            Assert.IsNull(Why("The beast grew to stage 3 while you chose your words. You lifted off at 3:10."));
            Assert.IsNull(Why("Japanese tripped you, and you picked the particles apart. You lifted off at 3:10."));
            Assert.IsNull(Why("The beast picked up speed at 2:10. You lifted off at 3:10."), "picked up is not choosing");

            Assert.AreEqual("beast agency", Why("The beast picked Japanese for you. You lifted off at 3:10."));
            Assert.AreEqual("beast agency", Why("The beast hunted you and chose its moment. You lifted off at 3:10."), "'you' is the object there");
            Assert.AreEqual("beast agency", Why("Japanese, chosen by the beast, tripped you. You lifted off at 3:10."));
            Assert.AreEqual("beast agency", Why("Japanese was chosen to break you. You lifted off at 3:10."), "a passive names no chooser: still a claim");
            Assert.AreEqual("beast agency", Why("It targeted your weak spot. You lifted off at 3:10."));
            Assert.AreEqual("beast agency", Why("The game was rigged against you. You lifted off at 3:10."));
        }

        /// <summary>One console, Japanese, two slips; the beast at stage 3 at 2:10; the ship leaves at 5:58.</summary>
        static RunLog SlowEscape()
        {
            var log = new RunLog();
            log.PuzzleStarted(new LangPuzzle { Id = 1, Concept = LangConcepts.Particles }, 0, "Japanese", 20f);
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = false, MissedConcept = LangConcepts.Particles, ResponseTime = 9f });
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = false, MissedConcept = LangConcepts.Particles, ResponseTime = 8f });
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = true, ResponseTime = 21f });
            log.BeastStage(2, 70f);
            log.BeastStage(3, 130f);
            log.End(true, 358f, 1, 1, false);
            return log;
        }

        [Test]
        public void Check_Numbers_AcceptsHonestRounding_RejectsUnbackedOnes()
        {
            var log = SlowEscape();
            var world = new[] { "Japanese" };
            string Why(string text) { DebriefWriter.Check(Json(text), log, world, out _, out string why); return why; }

            Assert.IsNull(Why("Japanese particles cost you 2 tries. You lifted off after six minutes."), "5:58 rounds to six minutes");
            Assert.IsNull(Why("Japanese particles cost you 2 tries. You lifted off after 5 minutes and change."), "and floors to five");
            Assert.IsNull(Why("The beast reached stage 3 after two minutes. You lifted off at 5:58."), "2:10 is two minutes");
            Assert.IsNull(Why("Japanese took you about 20 seconds in the end. You lifted off at 5:58."), "21 s to the nearest 5 or 10");

            Assert.AreEqual("number seven", Why("Japanese particles cost you 2 tries. You lifted off after seven minutes."));
            Assert.AreEqual("number ten", Why("Japanese particles cost you 2 tries. You lifted off after ten minutes."));
            Assert.AreEqual("number 6", Why("You slipped 6 times on Japanese. You lifted off at 5:58."), "a count is never rounded from a time");
            Assert.AreEqual("time 6:00", Why("Japanese particles cost you 2 tries. You lifted off at 6:00."), "a clock is quoted exactly");
            Assert.AreEqual("number 40", Why("Japanese took you 40 seconds. You lifted off at 5:58."));
        }

        [Test]
        public void Check_FlawlessClaim_RejectedAfterMistakes_UnlessAboutACleanConsole()
        {
            _ai.Available = false;
            PlayRun(escape: true);   // German and Turkish clean, Japanese two slips
            var log = _d.Log;
            string Why(string text) { DebriefWriter.Check(Json(text), log, Langs, out _, out string why); return why; }

            Assert.AreEqual("says flawless", Why("You breezed through it. You lifted off at 3:10."));
            Assert.AreEqual("says flawless", Why("A flawless run, even on Japanese. You lifted off at 3:10."));
            Assert.AreEqual("says flawless", Why("You never slipped once. You lifted off at 3:10."));
            Assert.IsNull(Why("You breezed through German, but Japanese particles tripped you. You lifted off at 3:10."));
        }

        [Test]
        public void Check_SlipClaim_RejectedWithNoMistakes_NegationIsFine()
        {
            var log = new RunLog();
            log.PuzzleStarted(new LangPuzzle { Id = 1 }, 0, "French", 5f);
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = true, ResponseTime = 8f });
            log.End(true, 65f, 1, 1, false);
            var world = new[] { "French" };
            string Why(string text) { DebriefWriter.Check(Json(text), log, world, out _, out string why); return why; }

            Assert.AreEqual("says slipped", Why("You stumbled on French. You lifted off at 1:05."));
            Assert.AreEqual("says slipped", Why("French tripped you up. You lifted off at 1:05."));
            Assert.AreEqual("says slipped", Why("A wrong word slowed you down. You lifted off at 1:05."));
            Assert.IsNull(Why("Not one slip on French. You lifted off at 1:05."));
            Assert.IsNull(Why("You read French without a mistake. You lifted off at 1:05."));
            Assert.IsNull(Why("A flawless run through French. You lifted off at 1:05."), "flawless is true here");
        }

        // ------------------------------------------------------------------ demo mode

        [Test]
        public void Demo_DeathNeverNamesAnUndecodedConsole_StockRequestAndReply()
        {
            DemoMode.On = true;
            _ai.Reply(Json("You read German and Japanese, then froze at the Turkish console. The beast caught you at 2:50."));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Debrief\] shown: stock \(reply rejected: names Turkish\)"));
            PlayRun(escape: false);   // caught with the Turkish console open and undecoded

            Assert.AreEqual("You read German and Japanese. You slipped 2 times, mostly on particles at the Japanese console. " +
                            "The beast reached stage 3 and caught you at 2:50, 2 of 3 consoles decoded.", _d.Shown);
            Assert.IsFalse(_d.Live, "a reply naming the undecoded language is rejected");
            StringAssert.DoesNotContain("Turkish", _ai.Requests[0].Prompt, "the model is never told it");
            StringAssert.Contains("console 3: undecoded language", _ai.Requests[0].Prompt);
            CollectionAssert.DoesNotContain(_ui.Debriefs, null);
            foreach (var shown in _ui.Debriefs) StringAssert.DoesNotContain("Turkish", shown);

            Assert.IsTrue(DebriefWriter.Check(Json("You read German and Japanese. The beast caught you at the third console at 2:50."), _d.Log, Langs, out _, out string why),
                "a fair demo reply still lands: " + why);
        }

        [Test]
        public void Demo_UndecodedLanguageIsHiddenEvenWhenTheWorldListLacksIt()
        {
            var log = new RunLog();
            log.PuzzleStarted(new LangPuzzle { Id = 1, Concept = LangConcepts.Particles }, 0, "Japanese", 5f);
            log.Answer(new AnswerEvent { PuzzleId = 1, Correct = false, MissedConcept = LangConcepts.Particles, ResponseTime = 4f });
            log.End(false, 40f, 0, 3, caughtInPuzzle: true);
            log.HideUndecoded = true;

            string stock = DebriefWriter.Stock(log);
            Assert.AreEqual("You reached 1 console. You slipped 1 time, on particles. The beast reached stage 1 and caught you at 0:40, 0 of 3 consoles decoded.", stock);
            Assert.IsTrue(DebriefWriter.Check(Json(stock), log, null, out _, out string why), why);
            Assert.IsFalse(DebriefWriter.Check(Json("Japanese particles tripped you once. The beast caught you at 0:40."), log, null, out _, out why));
            Assert.AreEqual("names Japanese", why);
            CollectionAssert.AreEqual(new[] { "Japanese" }, log.HiddenLanguages);

            log.HideUndecoded = false;   // outside demo mode it is named as before
            StringAssert.Contains("at the Japanese console", DebriefWriter.Stock(log));
            Assert.IsTrue(DebriefWriter.Check(Json("Japanese particles tripped you once. The beast caught you at 0:40."), log, null, out _, out why), why);
        }

        [Test]
        public void OutsideDemo_DeathStillNamesTheOpenConsole()
        {
            _ai.Reply(Json("You read German and Japanese, then froze at the Turkish console. The beast caught you at 2:50."));
            PlayRun(escape: false);
            Assert.IsTrue(_d.Live, "outside demo mode the language of the console you died at may be named");
            StringAssert.Contains("Turkish", _ai.Requests[0].Prompt);
        }

        // ------------------------------------------------------------------ the component and GameDirector.Restarting

        static FieldInfo RestartingField =>
            typeof(GameDirector).GetField("Restarting", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("GameDirector.Restarting is no longer a field-like event; update this test");

        static int RestartingCount() => (RestartingField.GetValue(null) as Action)?.GetInvocationList().Length ?? 0;
        static void RaiseRestarting() => (RestartingField.GetValue(null) as Action)?.Invoke();   // what GameDirector.Restart does first

        static void Call(object target, string method) =>
            (target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
             ?? throw new MissingMethodException(target.GetType().Name, method)).Invoke(target, null);

        [Test]
        public void Component_HooksRestartingOnAwake_AndUnhooksOnDestroy()
        {
            _d.Detach();   // only the component listens here
            var go = new GameObject("GameDirector");
            var comp = go.AddComponent<RunDebrief>();   // EditMode runs no Awake: call the callbacks as the player would
            int before = RestartingCount();
            bool destroyed = false;
            try
            {
                Call(comp, "Awake");
                Assert.AreEqual(before + 1, RestartingCount(), "Awake subscribes once");

                _ai.Hold = true;
                _ai.Reply(Json("German was clean and Japanese particles cost you. You lifted off at 3:10."));
                PlayRun(escape: true);
                Assert.AreEqual(1, _ui.Debriefs.Count, "the component's debriefer heard the run");
                RaiseRestarting();
                _ai.Flush();
                Assert.IsFalse(comp.Debriefer.Live, "Restarting dropped the pending reply");
                Assert.AreEqual(1, _ui.Debriefs.Count);

                Call(comp, "OnDestroy");
                destroyed = true;
                Assert.AreEqual(before, RestartingCount(), "OnDestroy unsubscribes: a later session's restart cannot reach it");

                NewRunState();
                PlayRun(escape: false);
                Assert.AreEqual(1, _ui.Debriefs.Count, "a destroyed component no longer listens to GameEvents");
                Assert.DoesNotThrow(RaiseRestarting);
            }
            finally
            {
                if (!destroyed) Call(comp, "OnDestroy");
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GameDirector_ClearsRestartingBeforeAPlaySession()
        {
            Action leak = () => Assert.Fail("a subscriber from the last play session fired");
            GameDirector.Restarting += leak;
            try
            {
                var reset = typeof(GameDirector).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(reset);
                Assert.IsNotNull(reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(), "runs on entering play mode");
                reset.Invoke(null, null);
                Assert.AreEqual(0, RestartingCount());
            }
            finally { GameDirector.Restarting -= leak; }
        }

        [Test]
        public void StaleReply_IsDroppedByTheRunCounter_EvenIfItsHandleWasNotCancelled()
        {
            // a client that cancels requests itself (a new run boots) may hand back a handle that still delivers
            var sneaky = new UncancellableAi();
            Services.Ai = sneaky;
            PlayRun(escape: true);
            _d.Cancel();
            sneaky.Deliver(Json("German was clean and Japanese particles cost you. You lifted off at 3:10."));
            Assert.IsFalse(_d.Live);
            Assert.AreEqual(1, _ui.Debriefs.Count);
        }

        /// <summary>Keeps the callback and calls it whatever happened to the handle.</summary>
        sealed class UncancellableAi : IAiText
        {
            Action<AiResponse> _onDone;
            public bool Available => true;
            public AiHandle Request(AiRequest request, Action<AiResponse> onDone) { _onDone = onDone; return new AiHandle(); }
            public void Deliver(string text) => _onDone?.Invoke(new AiResponse { Status = AiStatus.Ok, Text = text });
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
        public void Death_ShowsDebriefSentBeforeTheScreen_AsTheDeathPathDoes()
        {
            Services.State.SetPhase(GamePhase.Dead);
            _ui.ShowDebrief("You read German. The beast caught you at 2:50.");   // the debriefer runs on the phase change
            _ui.HideAll();                                                     // then OnCaught hides the UI for the cutscene
            Assert.IsNull(_ui.DebriefShown);
            _ui.ShowEnd(false, Services.State, () => { });
            Assert.AreEqual("You read German. The beast caught you at 2:50.", _ui.DebriefShown);
        }

        [Test]
        public void Escape_ShowsDebriefArrivingAfterTheScreen()
        {
            var s = Services.State;
            s.TotalPuzzles = 3; s.PuzzlesSolved = 3;
            s.SetPhase(GamePhase.Escaped);
            _ui.ShowEnd(true, s, () => { });
            Assert.IsNull(_ui.DebriefShown);
            Wait(3f);
            _ui.ShowDebrief("Late one. Late two.");
            Assert.AreEqual("Late one. Late two.", _ui.DebriefShown);
        }

        /// <summary>As long as a reply may be (210 characters), with the longest words a debrief is likely to use.</summary>
        const string FullDebrief =
            "You read Mandarin Chinese, Hungarian, Japanese, Arabic and Finnish. You slipped 4 times, mostly on particles at the " +
            "Hungarian console. The beast reached stage 3 and you lifted off at 12:34, barely.";

        static readonly string[] Five = { "Mandarin Chinese", "Hungarian", "Japanese", "Arabic", "Finnish" };

        [TestCase(true)]
        [TestCase(false)]
        public void Escape_FiveTongues_KeepEveryRow_BesideAFullDebrief(bool debriefFirst)
        {
            Assert.That(FullDebrief.Length, Is.LessThanOrEqualTo(DebriefWriter.MaxChars));
            Assert.That(Kit.Lines(FullDebrief, 16, 552), Is.LessThanOrEqualTo(EscapeScreen.DebriefReserve), "the band holds the longest reply");
            foreach (var lang in Five)
                _ui.Journal.Record(lang, new RevealInfo { RealText = "sentence of " + lang.ToLowerInvariant(), Translation = "a meaning" }, "A fact.", true);
            var s = Services.State;
            s.TotalPuzzles = 5; s.PuzzlesSolved = 5; s.Mistakes = 4; s.Elapsed = 754f;
            s.SetPhase(GamePhase.Escaped);

            if (debriefFirst) _ui.ShowDebrief(FullDebrief);
            _ui.ShowEnd(true, s, () => { });
            if (!debriefFirst) { Wait(2f); _ui.ShowDebrief(FullDebrief); }
            Assert.AreEqual(FullDebrief, _ui.DebriefShown);

            var texts = _ui.Front.Root.GetComponentsInChildren<Text>(true);
            var rows = new List<RectTransform>();
            int langs = 0, phrases = 0;
            RectTransform debrief = null;
            foreach (var t in texts)
            {
                if (t.name == "Lang") { langs++; rows.Add(t.rectTransform); }
                else if (t.name == "Real") { phrases++; rows.Add(t.rectTransform); }
                else if (t.name == "Debrief") debrief = t.rectTransform;
            }
            Assert.AreEqual(5, langs, "every tongue keeps its row");
            Assert.AreEqual(5, phrases, "and its phrase");
            Assert.IsNotNull(debrief);
            float debriefTop = -debrief.anchoredPosition.y;
            foreach (var r in rows)
                Assert.That(-r.anchoredPosition.y + r.sizeDelta.y, Is.LessThanOrEqualTo(debriefTop), r.name + " overlaps the debrief");
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

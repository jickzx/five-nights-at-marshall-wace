using System.Linq;
using Fnmw.Core;
using Fnmw.Lang;
using Fnmw.Voice;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests.Voice
{
    /// <summary>The voice module's decisions, with a fake backend, fake audio, a fake UI and a fake clock.
    /// The real language engine supplies puzzles, hint contexts and the leak check.</summary>
    public class VoiceCoreTests
    {
        FakeNet _net;
        FakeAudio _audio;
        FakeUi _ui;
        float _now;
        VoiceCore _core;
        LanguageEngine _engine;
        LangPuzzle _puzzle;

        static VoiceConfig Cfg(bool online = true) => online
            ? new VoiceConfig { BaseUrl = "http://127.0.0.1:8787", Token = "test-game-token-0123456789" }
            : new VoiceConfig();

        [SetUp]
        public void SetUp()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            VoiceCore.ResetStatics();
            _net = new FakeNet();
            _audio = new FakeAudio();
            _ui = new FakeUi();
            Services.Audio = _audio;
            Services.UI = _ui;
            _engine = new LanguageEngine();
            Services.Language = _engine;
            Services.State.World = _engine.Generate(4242);
            Services.State.TotalPuzzles = Services.State.World.Puzzles.Count;
            _now = 1000f;
        }

        [TearDown]
        public void TearDown()
        {
            _core?.Dispose();
            GameEvents.ClearAll();
            Services.ResetToFakes();
            VoiceCore.ResetStatics();
        }

        /// <summary>chatty false pins the mistake-only beast most of these tests were written for; the game runs chatty.</summary>
        VoiceCore Make(VoiceConfig cfg = null, string session = null, bool chatty = false)
        {
            _core = new VoiceCore(cfg ?? Cfg(), _net, () => _now);
            _core.Brain.Chatty = chatty;
            if ((cfg ?? Cfg()).Online && _net.Has("/v1/session")) _net.Respond("/v1/session", FakeNet.Json(session ?? Wire.Session()));
            return _core;
        }

        void Phase(GamePhase to)
        {
            var from = Services.State.Phase;
            Services.State.SetPhase(to);
            _core.OnPhase(from, to);
        }

        void Advance(float seconds) { for (float t = 0; t < seconds; t += 0.1f) { _now += 0.1f; _core.Tick(); } }

        LangPuzzle OpenConsole(int index = 0, float difficulty = 0.5f)
        {
            if (Services.State.Phase == GamePhase.Title) Phase(GamePhase.Explore);
            _puzzle = _engine.GetPuzzle(Services.State.World, index, difficulty);
            Services.State.CurrentPuzzleIndex = index;
            _core.OnPuzzle(_puzzle);
            Phase(GamePhase.Puzzle);
            return _puzzle;
        }

        void Wrong(string concept = LangConcepts.VerbFinal)
        {
            Services.State.Mistakes++;
            _core.OnAnswer(new AnswerEvent { PuzzleId = _puzzle?.Id ?? 1, Correct = false, MissedConcept = concept, ResponseTime = 20f });
        }

        /// <summary>Plays the monster pipeline to the point of audio.</summary>
        void ServeMonster(string text = "You keep stumbling on the same thing.", string source = "live")
        {
            _net.Respond("/v1/monster-line", FakeNet.Json(Wire.Line(text, source)));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
        }

        void RevealAndPayoff()
        {
            Phase(GamePhase.Reveal);
            Phase(GamePhase.Quiz);
            _core.NotifyQuizAnswerLocked(_puzzle.Id);
        }

        // ================================================================== offline and config

        [Test]
        public void V26_NoConfig_MakesZeroRequests_AndStillGivesAuthoredHints()
        {
            Make(Cfg(online: false));
            OpenConsole();
            Wrong(); Wrong(); Advance(30f);
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Assert.IsTrue(_ui.Hint.Visible && !_ui.Hint.Pending && _ui.Hint.Text.Length > 0);
            RevealAndPayoff();
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.AreEqual(PronunciationState.Hidden, _ui.Pron.State, "no backend: no voice cue at all");
            Assert.AreEqual(0, _net.All.Count, "zero network requests");
            Assert.AreEqual(0, _audio.Lines.Count);
            Assert.IsTrue(_core.HintsAvailable);
        }

        [Test]
        public void V29_ConfigRejectsUnsafeUrls_AndTakesFlagsOverEnvOverFile()
        {
            foreach (var bad in new[] { "http://example.com", "https://user:pw@example.com", "not a url", "https://example.com/path", "https://example.com/?q=1", "ftp://127.0.0.1", "javascript:alert(1)", "http://127.0.0.1.evil.com" })
                Assert.IsFalse(VoiceConfig.TryUrl(bad, out _), bad);
            Assert.IsTrue(VoiceConfig.TryUrl("http://127.0.0.1:8787", out var b1)); Assert.AreEqual("http://127.0.0.1:8787", b1);
            Assert.IsTrue(VoiceConfig.TryUrl("https://voice.example.com", out _));
            Assert.IsFalse(VoiceConfig.TryToken("short", out _));
            Assert.IsFalse(VoiceConfig.TryToken("has space in it 1234567", out _));

            var none = VoiceConfig.From(new string[0], _ => null, null);
            Assert.IsFalse(none.Online, "nothing configured: offline");
            var rejected = VoiceConfig.From(new string[0], k => k == "FNMW_VOICE_URL" ? "http://example.com" : k == "FNMW_VOICE_TOKEN" ? "test-game-token-0123456789" : null, null);
            Assert.IsFalse(rejected.Online, "a plain-HTTP remote host is refused");
            var file = "{\"url\":\"https://file.example.com\",\"token\":\"file-token-0123456789abc\",\"volume\":0.5}";
            var fromFile = VoiceConfig.From(new string[0], _ => null, file);
            Assert.AreEqual("https://file.example.com", fromFile.BaseUrl); Assert.AreEqual(0.5f, fromFile.Volume, 1e-4f);
            var fromEnv = VoiceConfig.From(new string[0], k => k == "FNMW_VOICE_URL" ? "https://env.example.com" : null, file);
            Assert.AreEqual("https://env.example.com", fromEnv.BaseUrl);
            var fromArgs = VoiceConfig.From(new[] { "-voiceUrl", "http://localhost:9000", "-voice", "off", "-subtitles", "off" }, k => k == "FNMW_VOICE_URL" ? "https://env.example.com" : null, file);
            Assert.AreEqual("http://localhost:9000", fromArgs.BaseUrl);
            Assert.IsFalse(fromArgs.Enabled); Assert.IsFalse(fromArgs.Subtitles);
            Assert.IsFalse(fromArgs.Identity.Contains("file-token"), "the identity never holds the token");
        }

        [Test]
        public void V31_VoiceDisabled_MakesNoRequests_ButHintsStillWork()
        {
            var cfg = Cfg(); cfg.Enabled = false;
            Make(cfg);
            OpenConsole();
            Assert.IsTrue(_core.HintsAvailable);
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Wrong(); Wrong(); Advance(30f);
            Assert.AreEqual(0, _net.All.Count);
        }

        [Test]
        public void Session_FailsOnce_RetriesOnce_ThenStaysOfflineForTheScene()
        {
            _core = new VoiceCore(Cfg(), _net, () => _now);
            _net.Respond("/v1/session", FakeNet.Timeout());
            Advance(1f);
            Assert.AreEqual(1, _net.Count("/v1/session"));
            Advance(1.5f);
            Assert.AreEqual(2, _net.Count("/v1/session"), "one retry after 2 s");
            _net.Respond("/v1/session", FakeNet.Error(503, "provider_unavailable"));
            Advance(10f);
            Assert.AreEqual(2, _net.Count("/v1/session"));
            OpenConsole();
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Assert.AreEqual(2, _net.All.Count);
        }

        [Test]
        public void Session_IsReusedAcrossARestart_SameBackend()
        {
            Make();
            _core.Dispose();
            _core = new VoiceCore(Cfg(), _net, () => _now);
            Assert.AreEqual(1, _net.Count("/v1/session"), "the static session survives a scene reload");
            Assert.IsTrue(_core.Online);
            _core.Dispose();
            var other = new VoiceConfig { BaseUrl = "http://127.0.0.1:9999", Token = "test-game-token-0123456789" };
            _core = new VoiceCore(other, _net, () => _now);
            Assert.AreEqual(2, _net.Count("/v1/session"), "another backend: a new session");
        }

        // ================================================================== monster dialogue

        [Test]
        public void V1_Monster_Success_PlaysInTheBeastsStage_AndSubtitlesOnlyOnceAudible()
        {
            Make();
            OpenConsole();
            Services.State.SetBeastStage(2);
            Wrong(); Wrong();
            Advance(1.0f);
            Assert.AreEqual(0, _net.Count("/v1/monster-line"), "waits 1.2 s so the stinger lands first");
            Advance(0.4f);
            Assert.AreEqual(1, _net.Count("/v1/monster-line"));
            var req = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line").Req;
            StringAssert.Contains("\"trigger\":\"repeat_concept\"", req.Json);
            StringAssert.Contains("\"concept\":\"verb-final\"", req.Json);
            // the only free text is the sentence's English meaning, which the player can see (the hint route sends it too)
            var meaning = Protocol.MonsterMeaningFor(_puzzle);
            if (meaning != null) StringAssert.Contains("\"meaning\":\"" + meaning + "\"", req.Json);
            else StringAssert.Contains("\"meaning\":null", req.Json);
            Assert.AreEqual("v1.tok", req.Bearer);
            _audio.AutoStart = false;
            ServeMonster();
            var line = _audio.On(VoiceChannel.Monster).Single();
            Assert.AreEqual(2, line.Clip.Stage);
            Assert.IsFalse(line.Clip.Mock);
            Assert.AreEqual(0, _ui.Subtitles.Count, "no subtitle while the audio is still being prepared");
            _audio.Start(line);
            Assert.AreEqual("You keep stumbling on the same thing.", _ui.Subtitle.Text);
            Assert.AreEqual(VoiceSource.Live, _ui.Subtitle.Source);
            Assert.AreEqual(0.2f, _ui.Subtitle.Seconds, 1e-3f, "the subtitle lasts as long as the audio really plays");
            _audio.Finish(line);
            Assert.IsTrue(string.IsNullOrEmpty(_ui.Subtitle.Text), "cleared when the line ends");
            Assert.AreEqual("live", _core.Last["monster"]);
        }

        [Test]
        public void V9_V10_OneRequestForTwoQuickMistakes_ThenCooldown_AndAtMostEightLines()
        {
            Make();
            OpenConsole();
            Wrong(); _now += 0.1f; Wrong();
            Advance(2f);
            Assert.AreEqual(1, _net.Count("/v1/monster-line"));
            ServeMonster();
            Wrong(); Advance(10f);
            Assert.AreEqual(1, _net.Count("/v1/monster-line"), "inside the 25 s cooldown");
            for (int i = 0; i < 12; i++)
            {
                Advance(26f);
                Wrong(); Advance(1.5f);
                if (_net.Has("/v1/monster-line")) ServeMonster("Line number " + new string('a', i + 1) + ".");
            }
            Assert.AreEqual(DialogueBrain.MaxLines, _net.Count("/v1/monster-line"));
        }

        [Test]
        public void Chatty_OpeningAConsole_TheBeastTalksAboutTheSentence()
        {
            Make(chatty: true);
            var p = OpenConsole();
            Advance(1.0f);
            Assert.AreEqual(0, _net.Count("/v1/monster-line"), "a beat after the console opens");
            Advance(0.7f);
            Assert.AreEqual(1, _net.Count("/v1/monster-line"));
            var req = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line").Req;
            StringAssert.Contains("\"trigger\":\"puzzle_start\"", req.Json);
            var meaning = Protocol.MonsterMeaningFor(p);
            if (meaning != null) StringAssert.Contains(meaning, req.Json);
            ServeMonster("A small word to die over.");
            Assert.AreEqual("A small word to die over.", _ui.Subtitle.Text);
        }

        [Test]
        public void Chatty_EveryMistake_SolvedSentences_AndSilence_AllGetLines()
        {
            Make(chatty: true);
            OpenConsole();
            Advance(2f); ServeMonster("Line a.");                           // puzzle_start
            Advance(DialogueBrain.ChattyCooldown);
            Wrong(); Advance(1.5f); ServeMonster("Line b.");                // first_mistake
            Advance(DialogueBrain.ChattyCooldown);
            _core.OnAnswer(new AnswerEvent { PuzzleId = _puzzle.Id, Correct = true, ResponseTime = 5f });
            Wrong(LangConcepts.Particles);                                  // a fresh concept, streak reset: a plain mistake
            Advance(1.5f);
            var req = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line").Req;
            StringAssert.Contains("\"trigger\":\"mistake\"", req.Json);
            ServeMonster("Line c.");
            Advance(DialogueBrain.StalkAfter - 1f);
            Assert.AreEqual(3, _net.Count("/v1/monster-line"), "quiet, but not for long enough yet");
            Advance(1.5f);
            req = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line").Req;
            StringAssert.Contains("\"trigger\":\"stalk\"", req.Json);
        }

        [Test]
        public void Chatty_Brain_SolvedLine_NeverReplacesAWeightierOne_AndLinesAreCapped()
        {
            var b = new DialogueBrain();
            var facts = new RunFacts();
            b.OnStage(2, 0f);
            b.OnAnswer(new AnswerEvent { Correct = true }, facts, 0f);
            Assert.AreEqual("stage_up", b.Waiting.Value.Trigger);
            float t = 100f;
            int lines = 0;
            for (int i = 0; i < 200; i++)
            {
                t += DialogueBrain.ChattyCooldown + 1f;
                b.OnPuzzleStart(t);
                if (b.Take(t + DialogueBrain.PuzzleStartDelay, GamePhase.Puzzle, 0f) != null) lines++;
            }
            Assert.AreEqual(DialogueBrain.ChattyMaxLines, lines);
        }

        [Test]
        public void V11_NoLine_WhileTheTutorialHoldsTheBeast()
        {
            Make();
            OpenConsole();
            Services.State.BeastGrace = 1f;
            Wrong(); Wrong();
            Advance(3f);
            Assert.AreEqual(0, _net.Count("/v1/monster-line"));
        }

        [Test]
        public void V16_LateOrStaleMonsterLines_AreDropped()
        {
            Make();
            OpenConsole();
            Wrong(); Advance(1.5f);
            var inFlight = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line");
            Phase(GamePhase.Reveal);
            Assert.IsFalse(_net.Has("/v1/monster-line"), "solving the puzzle aborts the request");
            inFlight.Done(FakeNet.Json(Wire.Line("You slipped.")));   // and a late callback is ignored anyway
            Assert.AreEqual(0, _net.Count("/v1/speech/"), "not even fetched");
            Assert.AreEqual(0, _audio.Lines.Count);

            Phase(GamePhase.Quiz); Phase(GamePhase.Explore);
            OpenConsole(1);
            Advance(30f);
            Wrong(); Advance(1.5f);
            _net.Respond("/v1/monster-line", FakeNet.Json(Wire.Line("You slipped again.")));
            Advance(9f);   // the audio takes too long
            Assert.IsFalse(_net.Has("/v1/speech/"), "the speech request timed out");
            Assert.AreEqual(0, _audio.Lines.Count);
        }

        [Test]
        public void V17_AResultFromThePreviousRun_IsDiscarded()
        {
            Make();
            OpenConsole();
            Wrong(); Advance(1.5f);
            var oldCore = _core;
            var waiting = _net.Waiting.First(o => o.Req.Path == "/v1/monster-line");
            _net.Waiting.Clear();   // the old transport is gone with its scene; answer its callback by hand
            oldCore.Dispose();
            _core = new VoiceCore(Cfg(), _net, () => _now);   // restart: new epoch
            waiting.Done(FakeNet.Json(Wire.Line("You slipped.")));
            Assert.AreEqual(0, _net.Count("/v1/speech/"));
            Assert.AreEqual(0, _audio.Lines.Count);
        }

        [Test]
        public void V18_DeathOrEscape_CancelsEverything_AndNothingPlaysAfterwards()
        {
            Make();
            OpenConsole();
            Wrong(); Advance(1.5f);
            _core.RequestHint(_puzzle.Id);
            Assert.That(_net.InFlight, Is.GreaterThanOrEqualTo(2));
            Phase(GamePhase.Dead);
            Assert.AreEqual(1, _net.CancelAllCalls);
            Assert.AreEqual(0, _net.InFlight);
            CollectionAssert.IsSubsetOf(new[] { VoiceChannel.Monster, VoiceChannel.Ship, VoiceChannel.Native }, _audio.Stops);
            Advance(10f);
            Assert.AreEqual(0, _audio.Lines.Count);
        }

        [Test]
        public void V3_MonsterFailures_AreSilent_AndNeverRetried()
        {
            Make();
            OpenConsole();
            foreach (var res in new[] { FakeNet.Error(429, "rate_limited"), FakeNet.Error(500, "provider_unavailable"), FakeNet.Error(504, "provider_timeout") })
            {
                Advance(26f);
                Wrong(); Advance(1.5f);
                _net.Respond("/v1/monster-line", res);
                Advance(2f);
            }
            Assert.AreEqual(3, _net.Count("/v1/monster-line"), "no retries");
            Assert.AreEqual(0, _audio.Lines.Count);
            Assert.AreEqual(0, _ui.Subtitles.Count);
        }

        [Test]
        public void V4_ThreeFailuresOpenTheBreaker_ForAMinute_ThenOneProbe()
        {
            Make();
            OpenConsole();
            for (int i = 0; i < 3; i++) { Advance(26f); Wrong(); Advance(1.5f); _net.Respond("/v1/monster-line", FakeNet.Timeout()); }
            int before = _net.All.Count;
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source, "breaker open: offline hint, no request");
            Advance(26f); Wrong(); Advance(1.5f);
            Assert.AreEqual(before, _net.All.Count);
            Advance(40f); Wrong(); Advance(1.5f);
            Assert.AreEqual(before + 1, _net.All.Count, "one probe after a minute");
            ServeMonster();
            Advance(26f); Wrong(); Advance(1.5f);
            Assert.IsTrue(_net.Has("/v1/monster-line"), "success closed it");
        }

        [Test]
        public void V5_V6_MalformedOrUnsafeLines_AreIgnored()
        {
            Make();
            OpenConsole();
            foreach (var body in new[]
            {
                "not json", "{\"id\":\"x\"}", Wire.Line(""), Wire.Line("You slipped.", speech: false), Wire.Line("Go to http now"), Wire.Line("Your Japanese is weak."),
                Wire.Line(new string('a', 200)), "{\"text\":\"You slipped.\",\"source\":\"evil\"}",
            })   // eight: the run's line budget
            {
                Advance(26f); Wrong(); Advance(1.5f);
                _net.Respond("/v1/monster-line", FakeNet.Json(body));
            }
            Assert.AreEqual(0, _net.Count("/v1/speech/"));
            Assert.AreEqual(0, _audio.Lines.Count);
            Assert.IsFalse(TextGuard.MonsterLineOk("Visit www.evil.example"));
            Assert.IsFalse(TextGuard.MonsterLineOk("Call 555 1234."));
            Assert.IsFalse(TextGuard.MonsterLineOk("Je t’attends, 我"));
            Assert.IsTrue(TextGuard.MonsterLineOk("I can wait."));
        }

        [Test]
        public void V8_BadAudio_IsRejected()
        {
            Assert.IsNull(Protocol.Pcm(new byte[3]));
            Assert.IsNull(Protocol.Pcm(new byte[0]));
            Assert.IsNull(Protocol.Pcm(new byte[Protocol.MaxAudioBytes + 2]));
            Assert.AreEqual(-1f, Protocol.Pcm(new byte[] { 0x00, 0x80 })[0], 1e-6f);
            Make();
            OpenConsole();
            Wrong(); Advance(1.5f);
            _net.Respond("/v1/monster-line", FakeNet.Json(Wire.Line("You slipped.")));
            _net.Respond("/v1/speech/", new VoiceResponse { Status = 200, Bytes = new byte[5] });
            Assert.AreEqual(0, _audio.Lines.Count);
        }

        // ================================================================== hints

        [Test]
        public void V1_Hint_ShowsTextAtOnce_ThenSpeaks_IfStillOnScreen()
        {
            Make();
            OpenConsole();
            Wrong(); Wrong();
            _core.RequestHint(_puzzle.Id);
            Assert.IsTrue(_ui.Hint.Pending);
            var req = _net.Waiting.First(o => o.Req.Path == "/v1/hint").Req;
            var answer = LanguageEngine.DebugSolution(Services.State.World, _puzzle.Id);
            StringAssert.Contains("\"level\":1", req.Json);
            StringAssert.DoesNotContain(Services.State.World.Languages[0].Name, req.Json, "no language name");
            StringAssert.DoesNotContain("\"" + Services.State.World.Languages[0].Code + "\"", req.Json, "no language code");
            StringAssert.DoesNotContain("answer", req.Json);
            Assert.That(answer.Length, Is.GreaterThan(0));
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line("Compare rows 1 and 3: where does the action word wait?")));
            Assert.AreEqual("Compare rows 1 and 3: where does the action word wait?", _ui.Hint.Text);
            Assert.AreEqual(VoiceSource.Live, _ui.Hint.Source);
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            Assert.AreEqual(1, _audio.On(VoiceChannel.Ship).Count);
        }

        [Test]
        public void V3_HintFailures_FallBackToTheAuthoredHint_NoLaterThanTheTimeout()
        {
            Make();
            OpenConsole();
            _core.RequestHint(_puzzle.Id);
            Advance(3.6f);
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Assert.IsFalse(_ui.Hint.Pending);
            Advance(5f);
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Error(429, "rate_limited"));
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Assert.AreEqual(2, _ui.Hint.Level);
        }

        [Test]
        public void V7_AHintTheEngineRejects_NeverReachesTheUi()
        {
            Make();
            OpenConsole();
            var answer = LanguageEngine.DebugSolution(Services.State.World, _puzzle.Id).Select(id => _puzzle.Tiles.First(t => t.Id == id).Alien).ToArray();
            string leak = "Try " + string.Join(" ", answer) + ".";
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line(leak)));
            Assert.IsFalse(_ui.Hints.Any(h => h.Text == leak));
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
            Assert.AreEqual(0, _net.Count("/v1/speech/"), "the rejected text's audio is never fetched");
            Assert.AreEqual("unsafe", _core.Last["hint"]);
        }

        [Test]
        public void V12_V13_HammeringH_SendsOneRequest_AndAThirdHintReplaysTheSecond()
        {
            Make();
            OpenConsole();
            for (int i = 0; i < 5; i++) { _core.RequestHint(_puzzle.Id); _now += 0.2f; }
            Assert.AreEqual(1, _net.Count("/v1/hint"));
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line("Compare rows 1 and 2: look at where the action word sits.")));
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(1, _net.Count("/v1/hint"), "4 s cooldown");
            Advance(4.1f);
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line("Look at row 2 again.", speech: false)));
            Assert.AreEqual(2, _ui.Hint.Level);
            Advance(4.1f);
            int shown = _ui.Hints.Count;
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(2, _net.Count("/v1/hint"), "no third request");
            Assert.AreEqual("Look at row 2 again.", _ui.Hint.Text);
            Assert.AreEqual(shown + 1, _ui.Hints.Count);
        }

        [Test]
        public void V14_V15_AHintForAnotherPuzzle_OrAfterLeaving_IsDiscarded()
        {
            Make();
            OpenConsole(0, 0.5f);
            _core.RequestHint(_puzzle.Id);
            int id = _puzzle.Id;
            // the same console rebuilt at another difficulty: same id, other content
            var rebuilt = _engine.GetPuzzle(Services.State.World, 0, 0.05f);
            if (Protocol.PuzzleKey(rebuilt) == Protocol.PuzzleKey(_puzzle)) rebuilt = _engine.GetPuzzle(Services.State.World, 0, 0.95f);
            Assume.That(Protocol.PuzzleKey(rebuilt), Is.Not.EqualTo(Protocol.PuzzleKey(_puzzle)));
            Assert.AreEqual(id, rebuilt.Id);
            var inFlight = _net.Waiting.First(o => o.Req.Path == "/v1/hint");
            _puzzle = rebuilt; _core.OnPuzzle(rebuilt);
            Assert.IsFalse(_ui.Hint.Visible, "the hint line clears with the old content");
            Assert.IsFalse(_net.Has("/v1/hint"), "the old request is aborted");
            inFlight.Done(FakeNet.Json(Wire.Line("Compare rows 1 and 3: where does the action word wait?")));
            Assert.IsFalse(_ui.Hint.Visible, "a late answer for the old content is discarded");

            Advance(5f);
            _core.RequestHint(_puzzle.Id);
            var second = _net.Waiting.First(o => o.Req.Path == "/v1/hint");
            Phase(GamePhase.Explore);   // Esc: walked away
            second.Done(FakeNet.Json(Wire.Line("Compare rows 1 and 3: where does the action word wait?")));
            Assert.IsFalse(_ui.Hint.Visible, "the footer stays clear");
            Assert.AreEqual(0, _audio.Lines.Count);
        }

        [Test]
        public void V27_SessionWithoutHints_GoesStraightToAuthoredHints()
        {
            Make(session: Wire.Session(hint: false));
            OpenConsole();
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(0, _net.Count("/v1/hint"));
            Assert.AreEqual(VoiceSource.Offline, _ui.Hint.Source);
        }

        [Test]
        public void V28_A401_ReSessionsOnce_ASecond401_GoesOffline()
        {
            Make();
            OpenConsole();
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Error(401, "unauthorized"));
            Assert.AreEqual(2, _net.Count("/v1/session"));
            _net.Respond("/v1/session", FakeNet.Json(Wire.Session()));
            Advance(5f);
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Error(401, "unauthorized"));
            Assert.AreEqual(2, _net.Count("/v1/session"), "no second re-session");
            Assert.IsFalse(_core.Online);
            Advance(5f);
            int before = _net.All.Count;
            OpenConsole(1);
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(before, _net.All.Count);
        }

        [Test]
        public void V30_BudgetExhausted_TurnsThatFeatureOffForTheSession()
        {
            Make();
            OpenConsole();
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Error(503, "budget_exhausted"));
            Advance(5f);
            _core.RequestHint(_puzzle.Id);
            Assert.AreEqual(1, _net.Count("/v1/hint"));
            Wrong(); Advance(1.5f);
            Assert.AreEqual(1, _net.Count("/v1/monster-line"), "other features carry on");
        }

        // ================================================================== arbitration

        [Test]
        public void V19_V20_TheShipInterruptsTheMonster_TheMonsterNeverInterruptsTheShip()
        {
            Make();
            OpenConsole();
            Wrong(); Advance(1.5f);
            _core.RequestHint(_puzzle.Id);
            ServeMonster();
            Assert.IsTrue(_audio.Playing(VoiceChannel.Monster));
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line("Compare rows 1 and 3: where does the action word wait?")));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            Assert.IsFalse(_audio.Playing(VoiceChannel.Monster), "the monster stops");
            Assert.IsTrue(_audio.Playing(VoiceChannel.Ship));

            int subs = _ui.Subtitles.Count(s => !string.IsNullOrEmpty(s.Text));
            Advance(26f); Wrong(); Advance(1.5f);
            ServeMonster("You cannot hide.");
            Assert.AreEqual(1, _audio.On(VoiceChannel.Monster).Count, "dropped while the ship speaks");
            Assert.AreEqual(subs, _ui.Subtitles.Count(s => !string.IsNullOrEmpty(s.Text)), "and no subtitle");
        }

        // ================================================================== pronunciation

        [Test]
        public void V21_V25_PrefetchAtTheReveal_PlaysNothing_AndSendsOnlyTheNativeSentence()
        {
            Make();
            OpenConsole();
            Phase(GamePhase.Reveal);
            var req = _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce()));
            var reveal = _engine.GetReveal(Services.State.World, _puzzle.Id);
            StringAssert.Contains("\"text\":\"" + reveal.SpeechText + "\"", req.Json);
            StringAssert.DoesNotContain("(", req.Json);
            if (reveal.RealText.Contains("(")) StringAssert.DoesNotContain(reveal.RealText, req.Json);
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            _core.RequestPronunciation(_puzzle.Id, false);   // too early: still the reveal
            Assert.AreEqual(0, _audio.Lines.Count, "nothing is spoken before the quiz is answered");
            Assert.AreEqual(0, _ui.Prons.Count);
            Phase(GamePhase.Quiz);
            Assert.AreEqual(0, _audio.Lines.Count);
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.AreEqual(0, _audio.Lines.Count, "the Quiz phase alone does not unlock speech");
            _core.NotifyQuizAnswerLocked(_puzzle.Id + 1);
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.AreEqual(0, _audio.Lines.Count, "a choice for another puzzle cannot unlock speech");
            _core.NotifyQuizAnswerLocked(_puzzle.Id);
            _core.RequestPronunciation(_puzzle.Id, false);   // the payoff
            Assert.AreEqual(1, _audio.On(VoiceChannel.Native).Count);
            Assert.AreEqual(PronunciationState.Playing, _ui.Pron.State);
        }

        [Test]
        public void V22_StillLoadingAtThePayoff_PlaysOnceOnArrival_WithAccurateStates()
        {
            Make();
            OpenConsole();
            RevealAndPayoff();
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.AreEqual(PronunciationState.Loading, _ui.Pron.State);
            _audio.AutoStart = false;
            _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce()));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            var line = _audio.On(VoiceChannel.Native).Single();
            Assert.AreEqual(PronunciationState.Preparing, _ui.Pron.State, "processing is not playing");
            _audio.Start(line);
            Assert.AreEqual(PronunciationState.Playing, _ui.Pron.State);
            _audio.Finish(line);
            Assert.AreEqual(PronunciationState.Ready, _ui.Pron.State);
            Assert.AreEqual(1, _audio.Lines.Count, "plays once");
        }

        [Test]
        public void V23_V24_LateAudioIsCachedNotPlayed_AndReplaysNeedNoRequest()
        {
            Make();
            OpenConsole();
            RevealAndPayoff();
            _core.RequestPronunciation(_puzzle.Id, false);
            Phase(GamePhase.Explore);   // payoff dismissed before the audio came
            _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce()));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            Assert.AreEqual(0, _audio.Lines.Count);
            Assert.AreEqual(1, ClipCache.Count, "kept for later");

            // the same sentence again (a restart with the same seed): served from the clip cache
            int calls = _net.All.Count;
            _core.OnPuzzle(_puzzle);
            Phase(GamePhase.Puzzle);
            RevealAndPayoff();
            _core.RequestPronunciation(_puzzle.Id, false);
            _core.RequestPronunciation(_puzzle.Id, true);
            _core.RequestPronunciation(_puzzle.Id, true);
            Assert.AreEqual(calls, _net.All.Count, "zero backend calls");
            Assert.AreEqual(3, _audio.On(VoiceChannel.Native).Count);
            Assert.AreEqual(VoiceSource.Cache, _ui.Pron.Source);
        }

        [Test]
        public void Pronunciation_FailureShowsUnavailable_AndPRetriesAtMostTwice()
        {
            Make();
            OpenConsole();
            RevealAndPayoff();
            _net.Respond("/v1/pronounce", FakeNet.Error(503, "provider_unavailable"));
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.AreEqual(PronunciationState.Unavailable, _ui.Pron.State);
            _core.RequestPronunciation(_puzzle.Id, true);
            Assert.AreEqual(2, _net.Count("/v1/pronounce"));
            _net.Respond("/v1/pronounce", FakeNet.Error(503, "provider_unavailable"));
            _core.RequestPronunciation(_puzzle.Id, true);
            Assert.AreEqual(2, _net.Count("/v1/pronounce"), "no third attempt");
            Assert.AreEqual(PronunciationState.Unavailable, _ui.Pron.State);
        }

        [Test]
        public void ANewConsole_StopsThePreviousSentence()
        {
            Make();
            OpenConsole();
            RevealAndPayoff();
            _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce()));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            _core.RequestPronunciation(_puzzle.Id, false);
            Assert.IsTrue(_audio.Playing(VoiceChannel.Native));
            Phase(GamePhase.Explore);
            OpenConsole(1);
            Assert.IsFalse(_audio.Playing(VoiceChannel.Native));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LeavingThePayoff_CancelsPronunciationStillBeingPrepared(bool openNextConsole)
        {
            Make();
            OpenConsole();
            RevealAndPayoff();
            _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce()));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            _audio.AutoStart = false;
            _core.RequestPronunciation(_puzzle.Id, false);
            var line = _audio.On(VoiceChannel.Native).Single();
            Assert.AreEqual(PronunciationState.Preparing, _ui.Pron.State);
            Phase(GamePhase.Explore);
            if (openNextConsole) OpenConsole(1);
            Assert.IsTrue(line.Ended, "prepared audio must be cancelled before it can start");
            Assert.IsFalse(_audio.Playing(VoiceChannel.Native));
        }

        // ================================================================== mock labelling

        [Test]
        public void MockBackend_IsLabelledMockEverywhere_AndNeverRelabelledLive()
        {
            Make(session: Wire.Session(mock: true));
            Assert.AreEqual("MOCK", _core.Mode);
            StringAssert.Contains("not speech", _core.DebugLine());
            OpenConsole();
            Wrong(); Advance(1.5f);
            ServeMonster("You slipped.", "mock");
            Assert.IsTrue(_audio.On(VoiceChannel.Monster)[0].Clip.Mock);
            Assert.AreEqual(VoiceSource.Mock, _ui.Subtitle.Source);
            _core.RequestHint(_puzzle.Id);
            _net.Respond("/v1/hint", FakeNet.Json(Wire.Line("Compare rows 1 and 3 closely before you build the sentence.", "mock")));
            Assert.AreEqual(VoiceSource.Mock, _ui.Hint.Source);
            Phase(GamePhase.Reveal);
            _net.Respond("/v1/pronounce", FakeNet.Json(Wire.Pronounce("mock")));
            _net.Respond("/v1/speech/", FakeNet.Pcm());
            Phase(GamePhase.Quiz);
            _core.NotifyQuizAnswerLocked(_puzzle.Id);
            _core.RequestPronunciation(_puzzle.Id, false);
            _core.RequestPronunciation(_puzzle.Id, true);
            Assert.AreEqual(VoiceSource.Mock, _ui.Pron.Source, "a cached mock tone is still a mock tone");

            // the backend switches to live with real voices: its config version changes, so the mock clip is not reused
            _core.Dispose();
            VoiceCore.ResetStatics();
            ClipCache.Put("x", new ClipCache.Entry { Samples = new float[10], Rate = 24000, Mock = true });
            _net.All.Clear();
            _core = new VoiceCore(Cfg(), _net, () => _now);
            _net.Respond("/v1/session", FakeNet.Json(Wire.Session(version: "fedcba9876543210")));
            _core.OnPuzzle(_puzzle);
            Phase(GamePhase.Puzzle); Phase(GamePhase.Reveal);
            Assert.AreEqual(1, _net.Count("/v1/pronounce"), "fetched afresh from the live backend");
        }

        [Test]
        public void EveryRealPuzzle_BuildsAValidHintRequest()
        {
            var langs = new System.Collections.Generic.HashSet<string>();
            for (int seed = 1; seed <= 40; seed++)
            {
                var w = _engine.Generate(seed * 31);
                for (int i = 0; i < w.Puzzles.Count; i++)
                    foreach (float d in new[] { 0f, 0.5f, 1f })
                    {
                        var p = _engine.GetPuzzle(w, i, d);
                        var req = Protocol.HintFor(p, 1, 1, LangConcepts.WordOrder, 0);
                        Assert.IsNotNull(req, w.Languages[i].Name + ": " + p.TargetMeaning + " would silently fall back offline");
                        langs.Add(w.Languages[i].Code);
                    }
            }
            Assert.AreEqual(7, langs.Count);
        }

        [Test]
        public void ClipCache_IsBounded()
        {
            for (int i = 0; i < 40; i++) ClipCache.Put("k" + i, new ClipCache.Entry { Samples = new float[24000], Rate = 24000 });
            Assert.That(ClipCache.Count, Is.LessThanOrEqualTo(ClipCache.MaxEntries));
            ClipCache.Put("huge", new ClipCache.Entry { Samples = new float[ClipCache.MaxSamples + 1], Rate = 24000 });
            Assert.IsNull(ClipCache.Get("huge"));
        }
    }
}

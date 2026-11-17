using System;
using System.Collections.Generic;
using Fnmw.Core;
using Fnmw.Core.Ai;
using Fnmw.Core.Fakes;
using NUnit.Framework;

namespace Fnmw.Tests
{
    public class AiScaffoldTests
    {
        [SetUp] public void SetUp() { GameEvents.ClearAll(); Services.ResetToFakes(); }

        [Serializable] class Line { public string line; public int mood; }

        // ---- Services slot ----

        [Test]
        public void DefaultAi_IsUnavailable_AndAnswersDisabledAtOnce()
        {
            Assert.IsFalse(Services.Ai.Available);
            AiResponse got = null;
            var handle = Services.Ai.Request(new AiRequest { Prompt = "x" }, r => got = r);
            Assert.IsNotNull(got);
            Assert.AreEqual(AiStatus.Disabled, got.Status);
            Assert.IsFalse(got.Ok);
            Assert.IsTrue(handle.Done);
        }

        [Test]
        public void ResetToFakes_RestoresTheFakeAi()
        {
            Services.Ai = new ScriptedAiText();
            Services.ResetToFakes();
            Assert.IsInstanceOf<FakeAiText>(Services.Ai);
        }

        // ---- handle ----

        [Test]
        public void CancelledHandle_NeverRunsTheCallback()
        {
            var ai = new ScriptedAiText { Hold = true }.Reply("late");
            int calls = 0;
            var handle = ai.Request(new AiRequest(), _ => calls++);
            handle.Cancel();
            ai.Flush();
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Handle_RunsTheCallbackOnce_AndSwallowsItsExceptions()
        {
            var handle = new AiHandle();
            int calls = 0;
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            handle.Complete(_ => { calls++; throw new InvalidOperationException("boom"); }, new AiResponse());
            handle.Complete(_ => calls++, new AiResponse());
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ScriptedAi_RecordsRequests_AndRepliesInOrder()
        {
            var ai = new ScriptedAiText().Reply("one").Fail(AiStatus.Timeout);
            var seen = new List<AiResponse>();
            ai.Request(new AiRequest { Feature = "a" }, seen.Add);
            ai.Request(new AiRequest { Feature = "b" }, seen.Add);
            ai.Request(new AiRequest { Feature = "c" }, seen.Add);
            Assert.AreEqual(3, ai.Requests.Count);
            Assert.AreEqual("one", seen[0].Text);
            Assert.AreEqual(AiStatus.Timeout, seen[1].Status);
            Assert.AreEqual(AiStatus.BadResponse, seen[2].Status);
        }

        // ---- config ----

        [Test]
        public void Config_WithNothing_IsNotUsable()
        {
            var cfg = AiConfig.From(null, _ => null);
            Assert.IsFalse(cfg.Usable);
            Assert.AreEqual(AiConfig.DefaultModel, cfg.Model);
        }

        [Test]
        public void Config_ReadsTheSecretsFile()
        {
            var cfg = AiConfig.From("{\"openRouterApiKey\":\" k1 \",\"openRouterModel\":\"test/m1:free\"}", null);
            Assert.IsTrue(cfg.Usable);
            Assert.AreEqual("k1", cfg.ApiKey);
            Assert.AreEqual("test/m1:free", cfg.Model);
            Assert.AreEqual(AiConfig.SecretsFileName, cfg.Source);
            Assert.AreEqual("https://openrouter.ai/api/v1/chat/completions", cfg.Endpoint());
            StringAssert.DoesNotContain("k1", cfg.Endpoint());
        }

        [Test]
        public void Config_EnvironmentWinsOverTheFile()
        {
            var env = new Dictionary<string, string> { { AiConfig.EnvKey, "envkey" }, { AiConfig.EnvModel, "test/envmodel:free" } };
            var cfg = AiConfig.From("{\"openRouterApiKey\":\"filekey\",\"openRouterModel\":\"test/filemodel:free\"}", k => env.TryGetValue(k, out var v) ? v : null);
            Assert.AreEqual("envkey", cfg.ApiKey);
            Assert.AreEqual("test/envmodel:free", cfg.Model);
            Assert.AreEqual("environment", cfg.Source);
        }

        [Test]
        public void Config_ProxyNeedsNoKey_AndCarriesTheModel()
        {
            var cfg = AiConfig.From("{\"aiProxyUrl\":\"https://example.test/api/ai\"}", null);
            Assert.IsTrue(cfg.Usable);
            Assert.IsTrue(cfg.UsesProxy);
            Assert.AreEqual("https://example.test/api/ai", cfg.Endpoint());
        }

        [Test]
        public void Config_CanBeSwitchedOff()
        {
            Assert.IsFalse(AiConfig.From("{\"openRouterApiKey\":\"k\",\"aiDisabled\":true}", null).Usable);
            Assert.IsFalse(AiConfig.From("{\"openRouterApiKey\":\"k\"}", k => k == AiConfig.EnvDisabled ? "1" : null).Usable);
        }

        [Test]
        public void Config_SurvivesABrokenFile()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("not valid JSON"));
            Assert.IsFalse(AiConfig.From("{ not json", null).Usable);
        }

        // ---- budget ----

        [Test]
        public void Budget_SpacesOutOneFeature_ButNotAnother()
        {
            var b = new AiBudget { MinIntervalPerFeature = 2f, MaxConcurrent = 5 };
            Assert.IsNull(b.TryStart("taunt", 10f));
            Assert.IsNotNull(b.TryStart("taunt", 11f));
            Assert.IsNull(b.TryStart("hint", 11f));
            Assert.IsNull(b.TryStart("taunt", 12.5f));
            Assert.AreEqual(3, b.Started);
        }

        [Test]
        public void Budget_LimitsConcurrency_UntilEnd()
        {
            var b = new AiBudget { MaxConcurrent = 1, MinIntervalPerFeature = 0f };
            Assert.IsNull(b.TryStart("a", 0f));
            Assert.IsNotNull(b.TryStart("b", 0f));
            b.End();
            Assert.IsNull(b.TryStart("b", 0f));
        }

        [Test]
        public void Budget_StopsAtTheSessionLimit()
        {
            var b = new AiBudget { MaxPerSession = 2, MaxConcurrent = 9, MinIntervalPerFeature = 0f };
            Assert.IsNull(b.TryStart("a", 0f));
            Assert.IsNull(b.TryStart("a", 1f));
            Assert.IsNotNull(b.TryStart("a", 2f));
        }

        // ---- wire ----

        [Test]
        public void Body_UsesChatMessagesAndFreeRouting()
        {
            string body = OpenRouterWire.BuildBody(new AiRequest { Prompt = "hi\nthere", System = "Be brief.", SchemaJson = "{\"type\":\"object\"}" });
            StringAssert.Contains("\"model\":\"openrouter/free\"", body);
            StringAssert.Contains("\"role\":\"system\"", body);
            StringAssert.Contains("\"content\":\"hi\\nthere\"", body);
            StringAssert.Contains("\"response_format\":{\"type\":\"json_schema\"", body);
            StringAssert.Contains("\"max_price\":{\"prompt\":0,\"completion\":0}", body);
            Assert.Throws<ArgumentException>(() => OpenRouterWire.BuildBody(new AiRequest(), "paid/model"));
        }

        [Test]
        public void Reply_ReadsCompletedContent_AndRejectsTruncatedOrInvalidReplies()
        {
            Assert.IsTrue(OpenRouterWire.TryReadText("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"hello\"}}]}", out string text, out _));
            Assert.AreEqual("hello", text);
            Assert.IsFalse(OpenRouterWire.TryReadText("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}", out _, out _));
            Assert.IsFalse(OpenRouterWire.TryReadText("{\"error\":{\"code\":401,\"message\":\"secret\"}}", out _, out string error));
            StringAssert.DoesNotContain("secret", error);
            Assert.IsFalse(OpenRouterWire.TryReadText("<html>", out _, out _));
        }

        // ---- reply checks ----

        [Test]
        public void TryParseJson_ReadsFencedJson_AndRejectsRubbish()
        {
            Assert.IsTrue(AiText.TryParseJson("```json\n{\"line\":\"hello\",\"mood\":2}\n```", out Line a));
            Assert.AreEqual("hello", a.line);
            Assert.AreEqual(2, a.mood);
            Assert.IsFalse(AiText.TryParseJson("not json at all", out Line _));
            Assert.IsFalse(AiText.TryParseJson(null, out Line _));
        }

        [Test]
        public void Clean_MakesOneTidyLine()
        {
            Assert.AreEqual("I can wait.", AiText.Clean("  \"I can\n\n  wait.\"  "));
            Assert.IsNull(AiText.Clean(null));
            Assert.AreEqual("", AiText.Clean("   "));
        }

        [Test]
        public void Fits_ChecksCharsAndWords()
        {
            Assert.IsTrue(AiText.Fits("one two three", 20, 3));
            Assert.IsFalse(AiText.Fits("one two three", 20, 2));
            Assert.IsFalse(AiText.Fits("one two three", 5, 9));
            Assert.IsFalse(AiText.Fits("  ", 5, 9));
        }

        [Test]
        public void MentionsAny_MatchesWholeWords_IgnoringCase()
        {
            Assert.IsTrue(AiText.MentionsAny("This is japanese, I think.", "Japanese"));
            Assert.IsFalse(AiText.MentionsAny("A german shepherd", "man"));
            Assert.IsTrue(AiText.MentionsAny("put -lar after it", "-lar"));
            Assert.IsFalse(AiText.MentionsAny("nothing here", "", null, "  "));
            Assert.IsFalse(AiText.MentionsAny(null, "x"));
        }

        [Test]
        public void MentionsAny_FindsWordsInsideUnspacedScripts()
        {
            Assert.IsTrue(AiText.MentionsAny("これは猫です", "猫"));
            Assert.IsTrue(AiText.MentionsAny("والكتاب", "كتاب"));
            Assert.IsTrue(AiText.MentionsAny("학교에 가요", "학교"));
            Assert.IsTrue(AiText.MentionsAny("say 猫 now", "猫"));
            Assert.IsFalse(AiText.MentionsAny("kapılar", "kapı"), "Latin-script words still need a word boundary");
        }

        [Test]
        public void Clean_KeepsQuotesThatAreNotOneWrappingPair()
        {
            Assert.AreEqual("\"Run,\" I said, \"now.\"", AiText.Clean("\"Run,\" I said, \"now.\""));
        }

        [Test]
        public void StripFences_HandlesAOneLineFence()
        {
            Assert.AreEqual("{\"line\":\"x\"}", AiText.StripFences("```json {\"line\":\"x\"} ```"));
            Assert.AreEqual("{\"line\":\"x\"}", AiText.StripFences("```{\"line\":\"x\"}```"));
        }

        [Test]
        public void Body_NeverWritesNaN_AndEscapesControlCharacters()
        {
            string body = OpenRouterWire.BuildBody(new AiRequest { Prompt = "a\u0001b 猫", Temperature = float.NaN });
            StringAssert.DoesNotContain("NaN", body);
            StringAssert.Contains("a\\u0001b 猫", body);
        }

        [Test]
        public void Config_EnvironmentKeyOverridesAFileProxy()
        {
            var cfg = AiConfig.From("{\"aiProxyUrl\":\"https://example.test/api/ai\"}", k => k == AiConfig.EnvKey ? "envkey" : null);
            Assert.IsFalse(cfg.UsesProxy);
            Assert.AreEqual("envkey", cfg.ApiKey);
        }

        [Test]
        public void Config_InABuild_IgnoresEveryKey_AndNeedsAProxy()
        {
            var env = new Dictionary<string, string> { { AiConfig.EnvKey, "envkey" } };
            var cfg = AiConfig.From("{\"openRouterApiKey\":\"filekey\"}", k => env.TryGetValue(k, out var v) ? v : null, allowDirectKey: false);
            Assert.AreEqual("", cfg.ApiKey);
            Assert.IsFalse(cfg.Usable);
        }

        [Test]
        public void Config_InABuild_KeepsTheFileProxy_EvenWithAnEnvironmentKey()
        {
            var cfg = AiConfig.From("{\"aiProxyUrl\":\"https://example.test/api/ai\",\"openRouterApiKey\":\"filekey\"}",
                k => k == AiConfig.EnvKey ? "envkey" : null, allowDirectKey: false);
            Assert.IsTrue(cfg.Usable);
            Assert.IsTrue(cfg.UsesProxy);
            Assert.AreEqual("", cfg.ApiKey);
            Assert.AreEqual("https://example.test/api/ai", cfg.Endpoint());
        }

        [Test]
        public void HoldsKey_FlagsOnlyANonEmptyKey()
        {
            Assert.IsTrue(AiConfig.HoldsKey("{\"openRouterApiKey\":\"k\"}"));
            Assert.IsFalse(AiConfig.HoldsKey("{\"openRouterApiKey\":\"  \",\"aiProxyUrl\":\"https://example.test\"}"));
            Assert.IsFalse(AiConfig.HoldsKey(null));
            Assert.IsTrue(AiConfig.HoldsKey("{ not json, openRouterApiKey"), "unreadable text that mentions a key must stop the build");
        }

        [Test]
        public void Config_RejectsPaidModels()
        {
            Assert.IsFalse(AiConfig.From("{\"openRouterModel\":\"paid/model\"}", null).Usable);
        }

        [Test]
        public void ExampleSecretsFile_HoldsNoKey()
        {
            string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "secrets.example.json");
            Assert.IsFalse(AiConfig.HoldsKey(System.IO.File.ReadAllText(path)));
        }
    }
}

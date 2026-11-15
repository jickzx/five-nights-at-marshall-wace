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
            var cfg = AiConfig.From("{\"geminiApiKey\":\" k1 \",\"geminiModel\":\"m1\"}", null);
            Assert.IsTrue(cfg.Usable);
            Assert.AreEqual("k1", cfg.ApiKey);
            Assert.AreEqual("m1", cfg.Model);
            Assert.AreEqual(AiConfig.SecretsFileName, cfg.Source);
            StringAssert.EndsWith("/models/m1:generateContent", cfg.Endpoint());
            StringAssert.DoesNotContain("k1", cfg.Endpoint());
        }

        [Test]
        public void Config_EnvironmentWinsOverTheFile()
        {
            var env = new Dictionary<string, string> { { AiConfig.EnvKey, "envkey" }, { AiConfig.EnvModel, "envmodel" } };
            var cfg = AiConfig.From("{\"geminiApiKey\":\"filekey\",\"geminiModel\":\"filemodel\"}", k => env.TryGetValue(k, out var v) ? v : null);
            Assert.AreEqual("envkey", cfg.ApiKey);
            Assert.AreEqual("envmodel", cfg.Model);
            Assert.AreEqual("environment", cfg.Source);
        }

        [Test]
        public void Config_ProxyNeedsNoKey_AndCarriesTheModel()
        {
            var cfg = AiConfig.From("{\"aiProxyUrl\":\"https://example.test/api/ai\"}", null);
            Assert.IsTrue(cfg.Usable);
            Assert.IsTrue(cfg.UsesProxy);
            Assert.AreEqual("https://example.test/api/ai?model=" + AiConfig.DefaultModel, cfg.Endpoint());
        }

        [Test]
        public void Config_CanBeSwitchedOff()
        {
            Assert.IsFalse(AiConfig.From("{\"geminiApiKey\":\"k\",\"aiDisabled\":true}", null).Usable);
            Assert.IsFalse(AiConfig.From("{\"geminiApiKey\":\"k\"}", k => k == AiConfig.EnvDisabled ? "1" : null).Usable);
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
        public void Body_EscapesText_AndCarriesTheSchema()
        {
            string body = GeminiWire.BuildBody(new AiRequest
            {
                System = "Be \"brief\".",
                Prompt = "line one\nline two\\",
                SchemaJson = "{\"type\":\"OBJECT\",\"properties\":{\"line\":{\"type\":\"STRING\"}}}",
                Temperature = 0.5f,
                MaxOutputTokens = 100,
            });
            StringAssert.Contains("\"systemInstruction\":{\"parts\":[{\"text\":\"Be \\\"brief\\\".\"}]}", body);
            StringAssert.Contains("\"text\":\"line one\\nline two\\\\\"", body);
            StringAssert.Contains("\"temperature\":0.5", body);
            StringAssert.Contains("\"maxOutputTokens\":100", body);
            StringAssert.Contains("\"responseMimeType\":\"application/json\"", body);
            StringAssert.Contains("\"responseSchema\":{\"type\":\"OBJECT\"", body);
            Assert.DoesNotThrow(() => UnityEngine.JsonUtility.FromJson<Line>(body), "body must be valid JSON");
        }

        [Test]
        public void Body_WithoutSchemaOrSystem_AsksForPlainText()
        {
            string body = GeminiWire.BuildBody(new AiRequest { Prompt = "hi" });
            StringAssert.DoesNotContain("responseSchema", body);
            StringAssert.DoesNotContain("systemInstruction", body);
        }

        [Test]
        public void Reply_TextIsJoined_AndThoughtPartsSkipped()
        {
            string json = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hidden\",\"thought\":true},{\"text\":\"{\\\"line\\\":\"},{\"text\":\"\\\"hi\\\"}\"}]},\"finishReason\":\"STOP\"}]}";
            Assert.IsTrue(GeminiWire.TryReadText(json, out string text, out string error), error);
            Assert.AreEqual("{\"line\":\"hi\"}", text);
        }

        [Test]
        public void Reply_ErrorsAreReported()
        {
            Assert.IsFalse(GeminiWire.TryReadText("{\"error\":{\"code\":400,\"message\":\"API key not valid\",\"status\":\"INVALID_ARGUMENT\"}}", out _, out string e1));
            StringAssert.Contains("API key not valid", e1);
            Assert.IsFalse(GeminiWire.TryReadText("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", out _, out string e2));
            StringAssert.Contains("SAFETY", e2);
            Assert.IsFalse(GeminiWire.TryReadText("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}", out _, out string e3));
            StringAssert.Contains("MAX_TOKENS", e3);
            Assert.IsFalse(GeminiWire.TryReadText("<html>", out _, out _));
            Assert.IsFalse(GeminiWire.TryReadText("", out _, out _));
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
    }
}

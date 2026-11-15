using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fnmw.Core.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests
{
    public class QsTelemetryTests
    {
        const string Sid = "0b7d6f1e-2c3a-4d5e-8f90-123456789abc";

        static Func<string, string> Env(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return k => d.TryGetValue(k, out var v) ? v : null;
        }

        // ---- config ----

        [Test]
        public void Config_IsOffByDefault()
        {
            var c = QsConfig.From(null, null);
            Assert.IsFalse(c.Enabled);
            Assert.IsFalse(c.Usable);
            Assert.AreEqual("disabled", c.Problem);
        }

        [Test]
        public void Config_NeedsHttpsUnlessLocal()
        {
            string json = "{\"enabled\":true,\"endpointUrl\":\"http://example.com\",\"sessionId\":\"" + Sid + "\",\"ingestToken\":\"t\"}";
            Assert.IsFalse(QsConfig.From(json, null).Usable);
            Assert.IsTrue(QsConfig.From(json.Replace("http://example.com", "https://example.com"), null).Usable);
            Assert.IsTrue(QsConfig.From(json.Replace("http://example.com", "http://192.168.1.20:8000"), null).Usable);
        }

        [Test]
        public void Config_EnvironmentOverridesFile_AndBuildsUrls()
        {
            var c = QsConfig.From("{\"enabled\":false}", Env(QsConfig.EnvEnabled, "1", QsConfig.EnvUrl, "https://qs.example/",
                QsConfig.EnvSession, Sid, QsConfig.EnvToken, "qsi_abc"));
            Assert.IsTrue(c.Usable);
            Assert.AreEqual("https://qs.example/v1/ingest/" + Sid, c.IngestUrl);
            Assert.AreEqual("https://qs.example/v1/consent", c.ConsentTextUrl);
            Assert.AreEqual("environment", c.Source);
        }

        [Test]
        public void Config_RejectsBadSessionAndClampsTiming()
        {
            var c = QsConfig.From("{\"enabled\":true,\"endpointUrl\":\"https://x\",\"sessionId\":\"nope\",\"ingestToken\":\"t\",\"hopSeconds\":9,\"windowSeconds\":1}", null);
            Assert.AreEqual("session id is not a UUID", c.Problem);
            Assert.LessOrEqual(c.HopSeconds, c.WindowSeconds);
        }

        // ---- buffer ----

        [Test]
        public void Buffer_ReturnsHalfOpenWindow_AndDropsOldFrames()
        {
            var b = new QsFrameBuffer(2.0);
            for (int i = 0; i < 400; i++) b.Add(new QsFrame { T = i / 72.0 });
            var w = b.Window(4.0, 5.0);
            Assert.AreEqual(72, w.Count);
            Assert.GreaterOrEqual(w[0].T, 4.0);
            Assert.Less(w[w.Count - 1].T, 5.0);
            Assert.LessOrEqual(b.Count, 2 * 72 + 2);
            b.Add(new QsFrame { T = 1.0 });              // out of order: ignored
            Assert.AreEqual(0, b.Window(0.9, 1.1).Count);
        }

        // ---- wire format ----

        static List<QsFrame> Frames(int n, bool hands)
        {
            var list = new List<QsFrame>();
            for (int i = 0; i < n; i++)
            {
                var pose = new QsPose { Tracked = true, Position = new Vector3(0.1f * i, 1.6f, 0f), Rotation = Quaternion.identity };
                list.Add(new QsFrame { T = 10 + i / 72.0, Head = pose, Left = hands ? pose : QsPose.Untracked, Right = hands ? pose : QsPose.Untracked });
            }
            return list;
        }

        static int Count(string s, string needle) => Regex.Matches(s, Regex.Escape(needle)).Count;

        [Test]
        public void Batch_HasTheServerShape()
        {
            var ev = new List<QsEvent> { new QsEvent { T = 10.25, Type = "answer_submitted", QuestionKey = "p3", PayloadJson = QsWire.Payload(("correct", true)) } };
            string json = QsWire.Batch(7, "p3", 10.0, 11.0, 72f, Frames(3, true), true, ev);
            StringAssert.StartsWith("{\"seq\":7,\"question_key\":\"p3\",\"t_start\":10,\"t_end\":11,\"rate_hz\":72,\"samples\":{\"t\":[", json);
            Assert.AreEqual(1, Count(json, "\"head\":{"));
            Assert.AreEqual(1, Count(json, "\"left\":{"));
            Assert.AreEqual(1, Count(json, "\"right\":{"));
            Assert.AreEqual(3, Count(json, "\"tracked\":["));
            StringAssert.Contains("\"type\":\"answer_submitted\",\"question_key\":\"p3\",\"payload\":{\"correct\":true}", json);
            StringAssert.DoesNotContain(",]", json);
            StringAssert.DoesNotContain("NaN", json);
        }

        [Test]
        public void Batch_CameraSourceSendsHeadOnly_AndEventsOnlyBatchHasNoSamples()
        {
            string head = QsWire.Batch(0, null, 0, 1, 72f, Frames(2, false), false, new List<QsEvent>());
            StringAssert.Contains("\"question_key\":null", head);
            StringAssert.DoesNotContain("\"left\"", head);
            string onlyEvents = QsWire.Batch(0, null, 0, 1, 0, new List<QsFrame>(), false,
                new List<QsEvent> { new QsEvent { T = 1, Type = "death" } });
            StringAssert.StartsWith("{\"events\":[", onlyEvents);
            StringAssert.DoesNotContain("seq", onlyEvents);
        }

        [Test]
        public void Wire_UsesInvariantNumbers_AndEscapes()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.AreEqual("1.5", QsWire.Num(1.5));
                Assert.AreEqual("0", QsWire.Num(double.NaN));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
            Assert.AreEqual("\"a\\\"b\\\\c\\n\"", QsWire.Str("a\"b\\c\n"));
            Assert.AreEqual("p12", QsWire.SafeKey("p12"));
            Assert.IsNull(QsWire.SafeKey("p 12"));
            Assert.IsNull(QsWire.SafeKey("ü"));
        }

        [Test]
        public void Consent_ParsesServerReply_AndBuildsBody()
        {
            Assert.IsTrue(QsWire.TryReadConsent("{\"consent_version\":\"v1\",\"statement\":\"text\",\"statement_sha256\":\"" + new string('a', 64) + "\"}",
                out var version, out var statement, out var sha));
            Assert.AreEqual("v1", version);
            Assert.AreEqual("text", statement);
            Assert.IsFalse(QsWire.TryReadConsent("not json", out _, out _, out _));
            string body = QsWire.Consent("v1", sha, "Quest 3", "0.1");
            StringAssert.Contains("\"accepted\":true", body);
            StringAssert.Contains("\"client\":{\"device\":\"Quest 3\",\"app\":\"0.1\"}", body);
        }

        [Test]
        public void CameraSource_ReportsHeadFromTransform_AndNoHands()
        {
            var go = new GameObject("head");
            try
            {
                go.transform.position = new Vector3(1, 2, 3);
                var src = new CameraPoseSource(() => go.transform);
                var f = src.Sample(4.0);
                Assert.IsTrue(f.Head.Tracked);
                Assert.AreEqual(new Vector3(1, 2, 3), f.Head.Position);
                Assert.IsFalse(f.Left.Tracked);
                Assert.IsFalse(src.HasControllers);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

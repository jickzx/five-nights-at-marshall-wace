using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// JSON for the ingest API, built by hand: JsonUtility cannot write nested arrays or nulls, and this keeps the
    /// per-batch allocation small. Pure, so tests pin the exact wire format (qs-backend/docs/api.md).
    /// </summary>
    public static class QsWire
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Num(double v, int decimals = 5)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            return Math.Round(v, decimals).ToString("0.#####", Inv);
        }

        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Question keys must match ^[A-Za-z0-9_.:-]{1,64}$ on the server; anything else becomes null.</summary>
        public static string SafeKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return null;
            foreach (char c in key)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '_' && c != '.' && c != ':' && c != '-') return null;
            return key;
        }

        public static string PuzzleKey(int puzzleId) => "p" + puzzleId.ToString(Inv);

        public static string Payload(params (string key, object value)[] fields)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(fields[i].key)).Append(':');
                switch (fields[i].value)
                {
                    case null: sb.Append("null"); break;
                    case bool b: sb.Append(b ? "true" : "false"); break;
                    case int n: sb.Append(n.ToString(Inv)); break;
                    case float f: sb.Append(Num(f)); break;
                    case double d: sb.Append(Num(d)); break;
                    default: sb.Append(Str(fields[i].value.ToString())); break;
                }
            }
            return sb.Append('}').ToString();
        }

        static void Vec(StringBuilder sb, Vector3 v) =>
            sb.Append('[').Append(Num(v.x)).Append(',').Append(Num(v.y)).Append(',').Append(Num(v.z)).Append(']');

        static void Quat(StringBuilder sb, Quaternion q) =>
            sb.Append('[').Append(Num(q.x)).Append(',').Append(Num(q.y)).Append(',').Append(Num(q.z)).Append(',').Append(Num(q.w)).Append(']');

        delegate QsPose Pick(QsFrame f);

        static void Device(StringBuilder sb, string name, IList<QsFrame> frames, Pick pick)
        {
            bool anyVelocity = false;
            foreach (var f in frames) if (pick(f).HasVelocity) { anyVelocity = true; break; }
            sb.Append(Str(name)).Append(":{\"pos\":[");
            for (int i = 0; i < frames.Count; i++) { if (i > 0) sb.Append(','); Vec(sb, pick(frames[i]).Position); }
            sb.Append("],\"rot\":[");
            for (int i = 0; i < frames.Count; i++) { if (i > 0) sb.Append(','); Quat(sb, pick(frames[i]).Rotation); }
            sb.Append(']');
            if (anyVelocity)
            {
                sb.Append(",\"vel\":[");
                for (int i = 0; i < frames.Count; i++) { if (i > 0) sb.Append(','); Vec(sb, pick(frames[i]).Velocity); }
                sb.Append(']');
            }
            sb.Append(",\"tracked\":[");
            for (int i = 0; i < frames.Count; i++) { if (i > 0) sb.Append(','); sb.Append(pick(frames[i]).Tracked ? "true" : "false"); }
            sb.Append("]}");
        }

        static void Events(StringBuilder sb, IList<QsEvent> events)
        {
            sb.Append("\"events\":[");
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Str(e.Id.ToString())).Append(",\"t\":").Append(Num(e.T, 4))
                  .Append(",\"type\":").Append(Str(e.Type)).Append(",\"question_key\":").Append(Str(SafeKey(e.QuestionKey)));
                if (!string.IsNullOrEmpty(e.PayloadJson)) sb.Append(",\"payload\":").Append(e.PayloadJson);
                sb.Append('}');
            }
            sb.Append(']');
        }

        /// <summary>One ingest batch: a motion window (when frames is non-empty) plus any pending events.</summary>
        public static string Batch(int seq, string questionKey, double tStart, double tEnd, float rateHz,
                                   IList<QsFrame> frames, bool includeHands, IList<QsEvent> events, string audioJson = null)
        {
            var sb = new StringBuilder(256 + frames.Count * (includeHands ? 260 : 90));
            sb.Append('{');
            if (frames.Count > 0)
            {
                sb.Append("\"seq\":").Append(seq.ToString(Inv))
                  .Append(",\"question_key\":").Append(Str(SafeKey(questionKey)))
                  .Append(",\"t_start\":").Append(Num(tStart, 4)).Append(",\"t_end\":").Append(Num(tEnd, 4));
                if (rateHz > 0) sb.Append(",\"rate_hz\":").Append(Num(rateHz, 2));
                sb.Append(",\"samples\":{\"t\":[");
                for (int i = 0; i < frames.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Num(frames[i].T, 5)); }
                sb.Append("],");
                Device(sb, "head", frames, f => f.Head);
                if (includeHands)
                {
                    sb.Append(',');
                    Device(sb, "left", frames, f => f.Left);
                    sb.Append(',');
                    Device(sb, "right", frames, f => f.Right);
                }
                sb.Append("},");
                if (!string.IsNullOrEmpty(audioJson)) sb.Append("\"audio\":").Append(audioJson).Append(',');
            }
            Events(sb, events);
            return sb.Append('}').ToString();
        }

        public static string Consent(string version, string statementSha256, string deviceModel, string appVersion) =>
            "{\"consent_version\":" + Str(version) + ",\"statement_sha256\":" + Str(statementSha256) +
            ",\"accepted\":true,\"client\":" + Payload(("device", deviceModel), ("app", appVersion)) + "}";

        [Serializable] class DeviceSessionReply { public string session_id; public string ingest_token; public string candidate_pseudonym; }

        /// <summary>Parse POST /v1/device-sessions. Returns false when the reply is not usable.</summary>
        public static bool TryReadDeviceSession(string json, out string sessionId, out string ingestToken, out string pseudonym)
        {
            sessionId = ingestToken = pseudonym = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var r = JsonUtility.FromJson<DeviceSessionReply>(json);
                if (r == null || !Guid.TryParse(r.session_id, out _) || string.IsNullOrEmpty(r.ingest_token)) return false;
                sessionId = r.session_id; ingestToken = r.ingest_token; pseudonym = r.candidate_pseudonym;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static string DeviceSessionRequest(string deviceModel, string appVersion) =>
            "{\"client\":" + Payload(("device", deviceModel), ("app", appVersion)) + "}";

        [Serializable] class ConsentText { public string consent_version; public string statement; public string statement_sha256; }

        /// <summary>Parse GET /v1/consent. Returns false when the reply is not usable.</summary>
        public static bool TryReadConsent(string json, out string version, out string statement, out string sha)
        {
            version = statement = sha = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var c = JsonUtility.FromJson<ConsentText>(json);
                if (c == null || string.IsNullOrEmpty(c.consent_version) || string.IsNullOrEmpty(c.statement_sha256)) return false;
                version = c.consent_version; statement = c.statement; sha = c.statement_sha256;
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Fnmw.Core.Ai
{
    /// <summary>Pure JSON for Gemini's generateContent endpoint: build the request body, read the reply.</summary>
    public static class GeminiWire
    {
        public static string BuildBody(AiRequest r)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            if (!string.IsNullOrEmpty(r.System))
                sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":").Append(Quote(r.System)).Append("}]},");
            sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":").Append(Quote(r.Prompt ?? "")).Append("}]}],");
            sb.Append("\"generationConfig\":{");
            sb.Append("\"temperature\":").Append(Mathf.Clamp(r.Temperature, 0f, 2f).ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"maxOutputTokens\":").Append(Mathf.Clamp(r.MaxOutputTokens, 16, 4096).ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(r.SchemaJson))
                sb.Append(",\"responseMimeType\":\"application/json\",\"responseSchema\":").Append(r.SchemaJson.Trim());
            sb.Append("}}");
            return sb.ToString();
        }

        [Serializable] class Reply { public Candidate[] candidates; public PromptFeedback promptFeedback; public ApiError error; }
        [Serializable] class Candidate { public Content content; public string finishReason; }
        [Serializable] class Content { public Part[] parts; }
        [Serializable] class Part { public string text; public bool thought; }
        [Serializable] class PromptFeedback { public string blockReason; }
        [Serializable] class ApiError { public int code; public string message; public string status; }

        /// <summary>Pull the answer text out of a generateContent reply (or an error body). False with a reason when there is none.</summary>
        public static bool TryReadText(string json, out string text, out string error)
        {
            text = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty reply"; return false; }
            Reply reply;
            try { reply = JsonUtility.FromJson<Reply>(json); }
            catch (Exception ex) { error = "reply is not JSON: " + ex.Message; return false; }
            if (reply == null) { error = "empty reply"; return false; }
            if (reply.error != null && (reply.error.code != 0 || !string.IsNullOrEmpty(reply.error.message)))
            {
                error = "provider error " + reply.error.code + " " + reply.error.status + ": " + reply.error.message;
                return false;
            }
            if (reply.candidates == null || reply.candidates.Length == 0)
            {
                string why = reply.promptFeedback != null ? reply.promptFeedback.blockReason : null;
                error = string.IsNullOrEmpty(why) ? "no candidates" : "blocked: " + why;
                return false;
            }
            var first = reply.candidates[0];
            var sb = new StringBuilder();
            if (first.content != null && first.content.parts != null)
                foreach (var p in first.content.parts)
                    if (p != null && !p.thought && !string.IsNullOrEmpty(p.text)) sb.Append(p.text);
            if (sb.Length == 0)
            {
                error = "no text (finishReason " + (string.IsNullOrEmpty(first.finishReason) ? "unknown" : first.finishReason) + ")";
                return false;
            }
            text = sb.ToString();
            return true;
        }

        /// <summary>A JSON string literal, quotes included.</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder((s?.Length ?? 0) + 2);
            sb.Append('"');
            if (s != null)
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
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

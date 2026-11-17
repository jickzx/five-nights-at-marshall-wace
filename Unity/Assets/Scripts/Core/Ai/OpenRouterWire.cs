using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Fnmw.Core.Ai
{
    /// <summary>Pure JSON for OpenRouter chat completions endpoint: build the request body, read the reply.</summary>
    public static class OpenRouterWire
    {
        public static string BuildBody(AiRequest r, string model = AiConfig.DefaultModel)
        {
            if (!AiConfig.IsFreeModel(model)) throw new ArgumentException("Only free models are allowed", nameof(model));
            var sb = new StringBuilder(512);
            sb.Append("{\"model\":").Append(Quote(model)).Append(",\"messages\":[");
            if (!string.IsNullOrEmpty(r.System))
                sb.Append("{\"role\":\"system\",\"content\":").Append(Quote(r.System)).Append("},");
            sb.Append("{\"role\":\"user\",\"content\":").Append(Quote(r.Prompt ?? "")).Append("}],");
            float temperature = float.IsNaN(r.Temperature) ? 0.8f : Mathf.Clamp(r.Temperature, 0f, 2f);
            sb.Append("\"temperature\":").Append(temperature.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"max_tokens\":").Append(Mathf.Clamp(r.MaxOutputTokens, 16, 4096).ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(r.SchemaJson))
                sb.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"game_reply\",\"strict\":true,\"schema\":")
                    .Append(r.SchemaJson.Trim()).Append("}}");
            sb.Append(",\"provider\":{\"require_parameters\":true,\"max_price\":{\"prompt\":0,\"completion\":0}}}");
            return sb.ToString();
        }

        [Serializable] class Reply { public Choice[] choices; public ApiError error; }
        [Serializable] class Choice { public Message message; public string finish_reason; }
        [Serializable] class Message { public string content; }
        [Serializable] class ApiError { public int code; public string message; }

        public static bool TryReadText(string json, out string text, out string error)
        {
            text = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty reply"; return false; }
            Reply reply;
            try { reply = JsonUtility.FromJson<Reply>(json); }
            catch (Exception) { error = "reply is not JSON"; return false; }
            if (reply?.error != null && (reply.error.code != 0 || !string.IsNullOrEmpty(reply.error.message))) { error = "provider error " + reply.error.code; return false; }
            var first = reply?.choices != null && reply.choices.Length > 0 ? reply.choices[0] : null;
            if (first == null || first.finish_reason != "stop" || string.IsNullOrWhiteSpace(first.message?.content))
            {
                error = "no complete text (finish_reason " + (first?.finish_reason ?? "unknown") + ")";
                return false;
            }
            text = first.message.content;
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

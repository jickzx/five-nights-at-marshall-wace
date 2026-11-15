using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Voice
{
    /// <summary>
    /// Wire format for backend/ (see backend/README.md). JsonUtility is lenient (unknown fields are ignored, a
    /// missing object becomes an empty one), so every parsed field is validated again here before use.
    /// </summary>
    internal static class Protocol
    {
        public const int SampleRate = 24000;
        public const int MaxAudioBytes = 1_500_000;
        public const int MaxJsonBytes = 16 * 1024;
        static readonly Regex Hex32 = new Regex("^[0-9a-f]{32}$");
        static readonly Regex Hex16 = new Regex("^[0-9a-f]{16}$");
        // a "word" may be a short phrase (German examples show "Ich weiß, dass" as one unit)
        static readonly Regex Word = new Regex(@"^(?=.{1,24}$)[\p{L}\p{M}'’\-¿¡?!.,]+(?: [\p{L}\p{M}'’\-¿¡?!.,]+)*$");
        static readonly Regex Meaning = new Regex(@"^[\p{L}\p{M}0-9 ,.'’?!:;()\-¿¡]{1,100}$");

        // ---------------------------------------------------------------- DTOs

        [Serializable] public sealed class SessionReq { public int proto = 1; public string build; }
        [Serializable] public sealed class Features { public bool monster, hint, hint_speech, pronounce; }
        [Serializable] public sealed class SessionRes { public string session; public int expires_in_s; public string mode; public string config_version; public Features features; }
        [Serializable] public sealed class SpeechRef { public string id; public int sample_rate; public string version; public string source; }
        [Serializable] public sealed class LineRes { public string id; public string text; public string source; public string kind; public int[] rows; public SpeechRef speech; }
        [Serializable] public sealed class PronounceRes { public string id; public string source; public SpeechRef speech; }
        [Serializable] public sealed class ErrorBody { public string code; public int retry_after_ms; }
        [Serializable] public sealed class ErrorRes { public ErrorBody error; }

        [Serializable]
        public sealed class MonsterReq
        {
            public int seq; public string trigger; public int stage; public string concept; public int concept_misses, wrong_streak, mistakes, solved, total;
            public string pace, distance, aggression;
        }

        [Serializable] public sealed class HintExample { public string meaning; public string[] words; }
        [Serializable] public sealed class HintReq { public int seq; public int level; public string concept; public int attempts; public string target_meaning; public HintExample[] examples; public string[] tiles; }
        [Serializable] public sealed class PronounceReq { public int seq; public string lang; public string text; }

        // ---------------------------------------------------------------- requests

        /// <summary>JsonUtility writes null strings as ""; the backend wants JSON null for "no concept".</summary>
        public static string MonsterJson(MonsterReq r)
        {
            string json = JsonUtility.ToJson(r);
            return r.concept == null ? json.Replace("\"concept\":\"\"", "\"concept\":null") : json;
        }

        /// <summary>The visible puzzle only: meanings, words and tiles. No answer, no language, no rule text.</summary>
        public static HintReq HintFor(LangPuzzle p, int seq, int level, string concept, int attempts)
        {
            if (p == null || p.Examples == null || p.Examples.Count != 3 || p.Tiles == null || p.Tiles.Count < 4 || p.Tiles.Count > 12) return null;
            var r = new HintReq { seq = seq, level = level, concept = concept, attempts = Mathf.Clamp(attempts, 0, 99), target_meaning = p.TargetMeaning, examples = new HintExample[3] };
            if (r.target_meaning == null || !Meaning.IsMatch(r.target_meaning)) return null;
            for (int i = 0; i < 3; i++)
            {
                var e = p.Examples[i];
                if (e?.Words == null || e.Words.Count < 1 || e.Words.Count > 12 || e.Picture == null || !Meaning.IsMatch(e.Picture)) return null;
                var words = new string[e.Words.Count];
                for (int k = 0; k < words.Length; k++) { words[k] = e.Words[k].Alien; if (words[k] == null || !Word.IsMatch(words[k]) || Native(words[k])) return null; }
                r.examples[i] = new HintExample { meaning = e.Picture, words = words };
            }
            r.tiles = new string[p.Tiles.Count];
            for (int i = 0; i < r.tiles.Length; i++) { r.tiles[i] = p.Tiles[i].Alien; if (r.tiles[i] == null || !Word.IsMatch(r.tiles[i]) || Native(r.tiles[i])) return null; }
            return r;
        }

        static bool Native(string s) { foreach (char c in s) if (char.IsLetter(c) && c >= 0x370 && !(c >= 0x1E00 && c <= 0x1EFF)) return true; return false; }

        // ---------------------------------------------------------------- responses

        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxJsonBytes || json[0] != '{') return null;
            try { return JsonUtility.FromJson<T>(json); } catch (Exception) { return null; }
        }

        public static bool ValidSession(SessionRes s) =>
            s != null && !string.IsNullOrEmpty(s.session) && s.session.Length <= 200 && s.expires_in_s > 0 && s.features != null
            && (s.mode == "live" || s.mode == "mock") && s.config_version != null && Hex16.IsMatch(s.config_version);

        public static bool ValidSpeech(SpeechRef r) =>
            r != null && r.id != null && Hex32.IsMatch(r.id) && r.sample_rate == SampleRate && r.version != null && Hex16.IsMatch(r.version) && SourceOk(r.source);

        public static bool SourceOk(string s) => s == "live" || s == "cache" || s == "mock";

        public static VoiceSource Source(string s, bool sessionMock) =>
            sessionMock || s == "mock" ? VoiceSource.Mock : s == "cache" ? VoiceSource.Cache : VoiceSource.Live;

        public static string ErrorCode(string json) => Parse<ErrorRes>(json)?.error?.code;

        /// <summary>16-bit little-endian mono PCM to floats. Null for empty, odd-length or oversized data.</summary>
        public static float[] Pcm(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 || bytes.Length > MaxAudioBytes) return null;
            var f = new float[bytes.Length / 2];
            for (int i = 0; i < f.Length; i++) f[i] = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 32768f;
            return f;
        }

        public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

        /// <summary>FNV-1a over the puzzle's visible content: a console rebuilt under the same id gets a new key.</summary>
        public static string PuzzleKey(LangPuzzle p)
        {
            if (p == null) return "";
            var sb = new StringBuilder();
            sb.Append(p.Id).Append('|').Append(p.TargetMeaning);
            if (p.Tiles != null) foreach (var t in p.Tiles) sb.Append('|').Append(t.Id).Append('=').Append(t.Alien);
            if (p.Examples != null) foreach (var e in p.Examples) { sb.Append('#').Append(e.Picture); if (e.Words != null) foreach (var w in e.Words) sb.Append(' ').Append(w.Alien); }
            uint h = 2166136261;
            foreach (char c in sb.ToString()) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        public static readonly HashSet<string> Codes = new HashSet<string>
        {
            "unauthorized", "bad_request", "too_large", "not_found", "rate_limited", "invalid_generation", "provider_unavailable",
            "provider_timeout", "disabled", "budget_exhausted",
        };
    }
}

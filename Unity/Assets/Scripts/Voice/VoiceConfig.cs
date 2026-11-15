using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fnmw.Voice
{
    /// <summary>
    /// Where the voice backend is and how loud/visible the voices are. Read in order: command-line flags
    /// (-voiceUrl, -voiceToken, -voice off, -voiceVolume 0.8, -subtitles off), environment variables
    /// (FNMW_VOICE_URL, FNMW_VOICE_TOKEN), then a local JSON file (Unity/UserSettings/fnmw-voice.json in the Editor,
    /// fnmw-voice.json under persistentDataPath in a build). No URL means offline: no request is ever made.
    /// The token is a low-value game token, not a provider key; it is never logged.
    /// </summary>
    internal sealed class VoiceConfig
    {
        public string BaseUrl;     // scheme://host[:port], no path, no credentials
        public string Token;
        public bool Enabled = true;
        public float Volume = 0.9f;
        public bool Subtitles = true;

        public bool Online => Enabled && BaseUrl != null && Token != null;
        /// <summary>Identifies this backend + token for caches, without holding the token itself.</summary>
        public string Identity => BaseUrl == null ? "offline" : BaseUrl + "#" + Hash(Token ?? "");

        [Serializable]
        sealed class FileDto { public string url; public string token; public bool enabled = true; public float volume = 0.9f; public bool subtitles = true; }

        /// <summary>Validates a backend URL: https anywhere, http only to loopback; no userinfo, path, query or fragment.</summary>
        public static bool TryUrl(string raw, out string baseUrl)
        {
            baseUrl = null;
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 200) return false;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var u)) return false;
            if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment)) return false;
            if (u.AbsolutePath != "/" && u.AbsolutePath != "") return false;
            bool loopback = u.Host == "127.0.0.1" || u.Host == "localhost" || u.Host == "[::1]";
            if (u.Scheme == Uri.UriSchemeHttps) { }
            else if (u.Scheme == Uri.UriSchemeHttp && loopback) { }
            else return false;
            baseUrl = u.GetLeftPart(UriPartial.Authority);
            return true;
        }

        public static bool TryToken(string raw, out string token)
        {
            token = null;
            if (string.IsNullOrEmpty(raw) || raw.Length < 16 || raw.Length > 256) return false;
            foreach (char c in raw) if (c < 0x21 || c > 0x7e) return false;
            token = raw;
            return true;
        }

        /// <summary>Pure: builds a config from the three sources (tests pass them in).</summary>
        public static VoiceConfig From(string[] args, Func<string, string> env, string fileJson)
        {
            var c = new VoiceConfig();
            string url = null, token = null;
            if (!string.IsNullOrEmpty(fileJson))
            {
                try
                {
                    var f = JsonUtility.FromJson<FileDto>(fileJson);
                    if (f != null) { url = f.url; token = f.token; c.Enabled = f.enabled; c.Volume = f.volume; c.Subtitles = f.subtitles; }
                }
                catch (Exception) { Debug.LogWarning("[Voice] fnmw-voice.json is not valid JSON; voice stays offline"); }
            }
            url = env?.Invoke("FNMW_VOICE_URL") ?? url;
            token = env?.Invoke("FNMW_VOICE_TOKEN") ?? token;
            var a = Args(args);
            if (a.TryGetValue("-voiceurl", out var v)) url = v;
            if (a.TryGetValue("-voicetoken", out v)) token = v;
            if (a.TryGetValue("-voice", out v)) c.Enabled = v != "off";
            if (a.TryGetValue("-subtitles", out v)) c.Subtitles = v != "off";
            if (a.TryGetValue("-voicevolume", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vol)) c.Volume = vol;
            c.Volume = Mathf.Clamp01(float.IsNaN(c.Volume) ? 0.9f : c.Volume);

            if (url != null || token != null)
            {
                if (!TryUrl(url, out var b) || !TryToken(token, out var t))
                {
                    Debug.LogWarning("[Voice] voice backend config rejected (URL must be https, or http to 127.0.0.1/localhost; token 16-256 printable characters); voice stays offline");
                    return c;
                }
                c.BaseUrl = b; c.Token = t;
            }
            return c;
        }

        static Dictionary<string, string> Args(string[] args)
        {
            var d = new Dictionary<string, string>();
            if (args == null) return d;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                string k = args[i]?.ToLowerInvariant();
                if (k == "-voiceurl" || k == "-voicetoken" || k == "-voice" || k == "-subtitles" || k == "-voicevolume") d[k] = args[i + 1];
            }
            return d;
        }

        /// <summary>Reads the real sources. PlayerPrefs hold player toggles (fnmw.voice.enabled/volume/subtitles).</summary>
        public static VoiceConfig Load()
        {
            string json = null;
            try
            {
                string path = Application.isEditor
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "fnmw-voice.json"))
                    : Path.Combine(Application.persistentDataPath, "fnmw-voice.json");
                if (File.Exists(path) && new FileInfo(path).Length < 4096) json = File.ReadAllText(path);
            }
            catch (Exception) { /* unreadable: offline */ }
            var c = From(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable, json);
            if (PlayerPrefs.HasKey("fnmw.voice.enabled")) c.Enabled &= PlayerPrefs.GetInt("fnmw.voice.enabled") != 0;
            if (PlayerPrefs.HasKey("fnmw.voice.volume")) c.Volume = Mathf.Clamp01(PlayerPrefs.GetFloat("fnmw.voice.volume"));
            if (PlayerPrefs.HasKey("fnmw.voice.subtitles")) c.Subtitles &= PlayerPrefs.GetInt("fnmw.voice.subtitles") != 0;
            return c;
        }

        static string Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h.ToString("x8");
        }
    }
}

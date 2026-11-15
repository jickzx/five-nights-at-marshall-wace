using System;
using System.IO;
using UnityEngine;

namespace Fnmw.Core.Ai
{
    /// <summary>
    /// Where the Gemini key comes from. Environment variables win over secrets.json, which is gitignored.
    /// A key must never be committed or shipped in a build: distributed builds set a proxy URL instead.
    /// </summary>
    public sealed class AiConfig
    {
        public const string DefaultModel = "gemini-3.5-flash-lite";
        public const string EnvKey = "GEMINI_API_KEY";
        public const string EnvModel = "GEMINI_MODEL";
        public const string EnvProxy = "FNMW_AI_PROXY_URL";
        public const string EnvDisabled = "FNMW_AI_DISABLED";
        public const string SecretsFileName = "secrets.json";

        public string ApiKey = "";
        public string Model = DefaultModel;
        /// <summary>When set, requests go here without a key and the proxy adds it server-side.</summary>
        public string ProxyUrl = "";
        public bool Disabled;
        /// <summary>Where the settings came from, for the startup log. Never contains the key.</summary>
        public string Source = "none";

        public bool UsesProxy => !string.IsNullOrEmpty(ProxyUrl);
        public bool Usable => !Disabled && (UsesProxy || !string.IsNullOrEmpty(ApiKey));

        /// <summary>The URL to POST a generateContent body to.</summary>
        public string Endpoint()
        {
            string model = Uri.EscapeDataString(Model);
            if (UsesProxy) return ProxyUrl + (ProxyUrl.Contains("?") ? "&" : "?") + "model=" + model;
            return "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent";
        }

        [Serializable]
        class SecretsFile
        {
            public string geminiApiKey;
            public string geminiModel;
            public string aiProxyUrl;
            public bool aiDisabled;
        }

        /// <summary>Pure: build a config from the text of secrets.json (may be null) and an environment lookup (may be null).</summary>
        public static AiConfig From(string secretsJson, Func<string, string> env)
        {
            var cfg = new AiConfig();
            if (!string.IsNullOrWhiteSpace(secretsJson))
            {
                try
                {
                    var file = JsonUtility.FromJson<SecretsFile>(secretsJson);
                    if (file != null)
                    {
                        if (!string.IsNullOrWhiteSpace(file.geminiApiKey)) { cfg.ApiKey = file.geminiApiKey.Trim(); cfg.Source = SecretsFileName; }
                        if (!string.IsNullOrWhiteSpace(file.geminiModel)) cfg.Model = file.geminiModel.Trim();
                        if (!string.IsNullOrWhiteSpace(file.aiProxyUrl)) { cfg.ProxyUrl = file.aiProxyUrl.Trim(); cfg.Source = SecretsFileName; }
                        cfg.Disabled = file.aiDisabled;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Ai] " + SecretsFileName + " is not valid JSON: " + ex.Message);
                }
            }
            if (env != null)
            {
                string key = env(EnvKey), model = env(EnvModel), proxy = env(EnvProxy), off = env(EnvDisabled);
                if (!string.IsNullOrWhiteSpace(key)) { cfg.ApiKey = key.Trim(); cfg.Source = "environment"; }
                if (!string.IsNullOrWhiteSpace(model)) cfg.Model = model.Trim();
                if (!string.IsNullOrWhiteSpace(proxy)) { cfg.ProxyUrl = proxy.Trim(); cfg.Source = "environment"; }
                if (!string.IsNullOrWhiteSpace(off)) cfg.Disabled = off.Trim() == "1" || off.Trim().ToLowerInvariant() == "true";
            }
            return cfg;
        }

        /// <summary>Resources path of the baked-in key file (Assets/Resources/Ai/secrets.json, gitignored).</summary>
        public const string ResourcePath = "Ai/secrets";

        /// <summary>
        /// Read the key file, then the environment. The file is looked for baked into the app (Resources, so a build
        /// made on a machine that has it needs no setup), then in the Unity project folder, then in the save folder.
        /// </summary>
        public static AiConfig Load()
        {
            string json = null;
            var baked = Resources.Load<TextAsset>(ResourcePath);
            if (baked != null && !string.IsNullOrWhiteSpace(baked.text)) json = baked.text;
            if (json == null)
                foreach (var path in new[]
                {
                    Path.Combine(Application.dataPath, "..", SecretsFileName),
                    Path.Combine(Application.persistentDataPath, SecretsFileName),
                })
                {
                    try
                    {
                        if (File.Exists(path)) { json = File.ReadAllText(path); break; }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Ai] could not read " + SecretsFileName + ": " + ex.Message);
                    }
                }
            return From(json, Environment.GetEnvironmentVariable);
        }
    }
}

using System;
using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// QS sender settings. Off unless explicitly enabled. Read from a gitignored
    /// <c>Assets/Resources/Qs/qs.json</c> (same pattern as the Gemini key) and overridden by environment variables.
    ///
    /// Two ways to say which session to send to:
    /// <list type="bullet">
    /// <item><b>Device key</b> (normal for a headset): an enrolled device's key (<c>qsd_…</c>, from
    /// <c>POST /v1/devices</c>). After each consent the sender asks <c>POST /v1/device-sessions</c> for a fresh
    /// pseudonymous session, so nobody types a token per candidate. The key can do nothing else.</item>
    /// <item><b>Fixed session</b> (testing): a session id and its ingest token.</item>
    /// </list>
    /// Both keys and tokens are credentials: never commit them.
    /// </summary>
    public sealed class QsConfig
    {
        public const string EnvEnabled = "FNMW_QS_ENABLED";
        public const string EnvUrl = "FNMW_QS_URL";
        public const string EnvSession = "FNMW_QS_SESSION";
        public const string EnvToken = "FNMW_QS_TOKEN";
        public const string EnvDeviceKey = "FNMW_QS_DEVICE_KEY";
        public const string EnvMicrophone = "FNMW_QS_MICROPHONE";
        public const string ResourcePath = "Qs/qs";

        public bool Enabled;
        public string EndpointUrl = "";
        public string SessionId = "";
        public string IngestToken = "";
        public string DeviceKey = "";
        /// <summary>Breathing from the microphone, after consent. On unless qs.json says <c>"microphone": false</c>.</summary>
        public bool Microphone = true;
        public float WindowSeconds = 1f;
        public float HopSeconds = 0.5f;
        /// <summary>Calm period after consent used as the person's own baseline. The game should keep play quiet here.</summary>
        public float CalibrationSeconds = 20f;
        /// <summary>Where the settings came from, for the log. Never contains the token.</summary>
        public string Source = "defaults";

        /// <summary>Why the sender will not run, or null when it can.</summary>
        public string Problem
        {
            get
            {
                if (!Enabled) return "disabled";
                if (string.IsNullOrEmpty(EndpointUrl)) return "no endpoint URL";
                if (!SecureOrLocal(EndpointUrl)) return "endpoint must be https (http is allowed only for localhost and LAN addresses)";
                if (UsesDevice) return null;
                if (!Guid.TryParse(SessionId, out _)) return "no device key, and the session id is not a UUID";
                if (string.IsNullOrEmpty(IngestToken)) return "no ingest token";
                return null;
            }
        }

        public bool Usable => Problem == null;
        /// <summary>A device key is set: sessions are started by the headset itself.</summary>
        public bool UsesDevice => !string.IsNullOrEmpty(DeviceKey) && DeviceKey.StartsWith("qsd_");

        string Base => EndpointUrl.TrimEnd('/');
        public string IngestUrlFor(string sessionId) => Base + "/v1/ingest/" + sessionId;
        public string IngestUrl => IngestUrlFor(SessionId);
        public string ConsentUrl => IngestUrl + "/consent";
        public string ConsentTextUrl => Base + "/v1/consent";
        public string DeviceSessionUrl => Base + "/v1/device-sessions";

        public static bool SecureOrLocal(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            if (u.Scheme == Uri.UriSchemeHttps) return true;
            if (u.Scheme != Uri.UriSchemeHttp) return false;
            string h = u.Host;
            return h == "localhost" || h == "127.0.0.1" || h.StartsWith("10.") || h.StartsWith("192.168.") || h.EndsWith(".local");
        }

        [Serializable]
        class FileShape
        {
            public bool enabled = true;      // a qs.json is itself the opt-in; "enabled": false turns it off
            public string endpointUrl;
            public string sessionId;
            public string ingestToken;
            public string deviceKey;
            public bool microphone = true;   // JsonUtility keeps this default when the field is absent
            public float windowSeconds;
            public float hopSeconds;
            public float calibrationSeconds;
        }

        /// <summary>Pure: settings from the text of qs.json (may be null) and an environment lookup (may be null).</summary>
        public static QsConfig From(string json, Func<string, string> env)
        {
            var cfg = new QsConfig();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var f = JsonUtility.FromJson<FileShape>(json);
                    if (f != null)
                    {
                        cfg.Enabled = f.enabled;
                        if (!string.IsNullOrWhiteSpace(f.endpointUrl)) cfg.EndpointUrl = f.endpointUrl.Trim();
                        if (!string.IsNullOrWhiteSpace(f.sessionId)) cfg.SessionId = f.sessionId.Trim();
                        if (!string.IsNullOrWhiteSpace(f.ingestToken)) cfg.IngestToken = f.ingestToken.Trim();
                        if (!string.IsNullOrWhiteSpace(f.deviceKey)) cfg.DeviceKey = f.deviceKey.Trim();
                        cfg.Microphone = f.microphone;
                        if (f.windowSeconds > 0) cfg.WindowSeconds = f.windowSeconds;
                        if (f.hopSeconds > 0) cfg.HopSeconds = f.hopSeconds;
                        if (f.calibrationSeconds > 0) cfg.CalibrationSeconds = f.calibrationSeconds;
                        cfg.Source = "qs.json";
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[QS] qs.json is not valid JSON: " + ex.Message);
                }
            }
            if (env != null)
            {
                string on = env(EnvEnabled), url = env(EnvUrl), sid = env(EnvSession), tok = env(EnvToken);
                string dev = env(EnvDeviceKey), mic = env(EnvMicrophone);
                if (!string.IsNullOrWhiteSpace(dev)) cfg.DeviceKey = dev.Trim();
                if (!string.IsNullOrWhiteSpace(mic)) cfg.Microphone = mic.Trim() == "1" || mic.Trim().ToLowerInvariant() == "true";
                if (!string.IsNullOrWhiteSpace(on)) { cfg.Enabled = on.Trim() == "1" || on.Trim().ToLowerInvariant() == "true"; cfg.Source = "environment"; }
                if (!string.IsNullOrWhiteSpace(url)) cfg.EndpointUrl = url.Trim();
                if (!string.IsNullOrWhiteSpace(sid)) cfg.SessionId = sid.Trim();
                if (!string.IsNullOrWhiteSpace(tok)) cfg.IngestToken = tok.Trim();
            }
            cfg.WindowSeconds = Mathf.Clamp(cfg.WindowSeconds, 0.5f, 4f);
            cfg.HopSeconds = Mathf.Clamp(cfg.HopSeconds, 0.2f, cfg.WindowSeconds);
            cfg.CalibrationSeconds = Mathf.Clamp(cfg.CalibrationSeconds, 5f, 120f);
            return cfg;
        }

        public static QsConfig Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            return From(asset != null ? asset.text : null, Environment.GetEnvironmentVariable);
        }
    }
}

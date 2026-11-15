using System;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace Fnmw.Voice
{
    internal sealed class VoiceRequest
    {
        public string Method = "POST";
        public string Path;          // "/v1/hint": always a constant or a validated id, never player text
        public string Json;          // body, or null
        public string Bearer;        // Authorization token, or null
        public float Timeout = 5f;   // seconds; the core also enforces its own deadline on unscaled time
        public bool Binary;          // expect raw bytes (speech)
        public int MaxBytes = Protocol.MaxJsonBytes;
    }

    internal sealed class VoiceResponse
    {
        public int Status;           // HTTP status, 0 = no response
        public string Error;         // "network", "timeout", "too_large", "aborted" when Status is 0
        public string Text;
        public byte[] Bytes;
    }

    /// <summary>The only way the voice module reaches the network. Callbacks arrive on the main thread.</summary>
    internal interface IVoiceTransport
    {
        int Send(VoiceRequest request, Action<VoiceResponse> done);
        void Cancel(int handle);
        void CancelAll();
        int InFlight { get; }
    }

    /// <summary>
    /// UnityWebRequest to the one configured origin. Redirects are refused, bodies are capped while downloading,
    /// and at most <see cref="MaxInFlight"/> requests run at once (more are refused, not queued).
    /// </summary>
    internal sealed class UnityTransport : IVoiceTransport
    {
        public const int MaxInFlight = 6;
        readonly string _origin;
        readonly Dictionary<int, UnityWebRequest> _live = new Dictionary<int, UnityWebRequest>();
        int _next = 1;

        public UnityTransport(string origin) { _origin = origin; }
        public int InFlight => _live.Count;

        public int Send(VoiceRequest r, Action<VoiceResponse> done)
        {
            if (_live.Count >= MaxInFlight || r.Path == null || !r.Path.StartsWith("/v1/", StringComparison.Ordinal))
            {
                done(new VoiceResponse { Status = 0, Error = "busy" });
                return 0;
            }
            var uwr = new UnityWebRequest(_origin + r.Path, r.Method) { redirectLimit = 0, timeout = (int)Math.Ceiling(r.Timeout) + 1 };
            uwr.downloadHandler = new CappedDownload(r.MaxBytes);
            if (r.Json != null)
            {
                uwr.uploadHandler = new UploadHandlerRaw(Protocol.Utf8(r.Json)) { contentType = "application/json" };
                uwr.SetRequestHeader("Content-Type", "application/json");
            }
            if (r.Bearer != null) uwr.SetRequestHeader("Authorization", "Bearer " + r.Bearer);
            int handle = _next++;
            _live[handle] = uwr;
            UnityWebRequestAsyncOperation op;
            try { op = uwr.SendWebRequest(); }
            catch (Exception)
            {
                // e.g. the player setting that forbids plain HTTP: report it as a network failure
                _live.Remove(handle); uwr.Dispose();
                done(new VoiceResponse { Status = 0, Error = "network" });
                return 0;
            }
            op.completed += _ =>
            {
                if (!_live.Remove(handle)) { uwr.Dispose(); return; }   // cancelled: nobody is listening
                var dl = (CappedDownload)uwr.downloadHandler;
                var res = new VoiceResponse { Status = (int)uwr.responseCode };
                if (dl.Overflow) { res.Status = 0; res.Error = "too_large"; }
                else if (uwr.result == UnityWebRequest.Result.ConnectionError) { res.Status = 0; res.Error = uwr.error != null && uwr.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ? "timeout" : "network"; }
                else if (r.Binary && res.Status == 200) res.Bytes = dl.Data();
                else res.Text = dl.Text();
                uwr.Dispose();
                done(res);
            };
            return handle;
        }

        public void Cancel(int handle)
        {
            if (_live.TryGetValue(handle, out var uwr)) { _live.Remove(handle); uwr.Abort(); }
        }

        public void CancelAll()
        {
            foreach (var uwr in _live.Values) uwr.Abort();
            _live.Clear();
        }

        /// <summary>Stops downloading (and flags it) as soon as a body passes its cap.</summary>
        sealed class CappedDownload : DownloadHandlerScript
        {
            readonly int _max;
            byte[] _buf = new byte[4096];
            int _len;
            public bool Overflow { get; private set; }
            public CappedDownload(int max) : base() { _max = max; }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                if (_len + dataLength > _max) { Overflow = true; return false; }
                if (_len + dataLength > _buf.Length) Array.Resize(ref _buf, Math.Min(_max, Math.Max(_buf.Length * 2, _len + dataLength)));
                Buffer.BlockCopy(data, 0, _buf, _len, dataLength);
                _len += dataLength;
                return true;
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_max) Overflow = true;
            }

            public byte[] Data() { var r = new byte[_len]; Buffer.BlockCopy(_buf, 0, r, 0, _len); return r; }
            public string Text() => System.Text.Encoding.UTF8.GetString(_buf, 0, _len);
        }
    }
}

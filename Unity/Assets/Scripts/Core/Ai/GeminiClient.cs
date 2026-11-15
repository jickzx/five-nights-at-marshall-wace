using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Fnmw.Core.Ai
{
    /// <summary>IAiText over Gemini's generateContent, sent from a coroutine so nothing blocks a frame.</summary>
    public sealed class GeminiClient : IAiText
    {
        readonly AiConfig _config;
        readonly AiBudget _budget;

        public GeminiClient(AiConfig config, AiBudget budget = null)
        {
            _config = config ?? new AiConfig();
            _budget = budget ?? new AiBudget();
        }

        public bool Available => _config.Usable;
        public AiBudget Budget => _budget;

        public AiHandle Request(AiRequest request, Action<AiResponse> onDone)
        {
            var handle = new AiHandle();
            if (request == null) { handle.Complete(onDone, AiResponse.Fail(AiStatus.BadResponse, "null request")); return handle; }
            if (!Available) { handle.Complete(onDone, AiResponse.Fail(AiStatus.Disabled, "no key or proxy configured")); return handle; }
            string refused = _budget.TryStart(request.Feature, Time.realtimeSinceStartup);
            if (refused != null)
            {
                Debug.Log("[Ai] " + request.Feature + " throttled: " + refused);
                handle.Complete(onDone, AiResponse.Fail(AiStatus.Throttled, refused));
                return handle;
            }

            // Everything that can throw (a malformed proxy URL, a rejected header) happens here, outside the
            // coroutine, so a failure still answers the caller and gives the budget slot back.
            UnityWebRequest www = null;
            try
            {
                www = new UnityWebRequest(_config.Endpoint(), UnityWebRequest.kHttpVerbPOST);
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(GeminiWire.BuildBody(request)));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                if (!_config.UsesProxy) www.SetRequestHeader("x-goog-api-key", _config.ApiKey);
                var runner = AiRunner.Get();
                var live = new LiveRequest { Www = www, Handle = handle, Budget = _budget };
                runner.Live.Add(live);
                runner.StartCoroutine(Send(request, live, onDone));
            }
            catch (Exception ex)
            {
                www?.Dispose();
                _budget.End();
                Debug.LogWarning("[Ai] " + request.Feature + " could not be sent: " + ex.Message);
                handle.Complete(onDone, AiResponse.Fail(AiStatus.BadResponse, "could not send: " + ex.Message));
            }
            return handle;
        }

        static IEnumerator Send(AiRequest request, LiveRequest live, Action<AiResponse> onDone)
        {
            float started = Time.realtimeSinceStartup;
            float limit = Mathf.Clamp(request.TimeoutSeconds, 1f, 30f);
            bool timedOut = false;

            var op = live.Www.SendWebRequest();
            while (!op.isDone)
            {
                if (live.Handle.Cancelled) break;
                if (Time.realtimeSinceStartup - started > limit) { timedOut = true; break; }
                yield return null;
            }

            AiResponse response = live.Handle.Cancelled ? null : Read(live.Www, timedOut);
            live.Close();
            if (response == null) yield break;

            response.Seconds = Time.realtimeSinceStartup - started;
            Debug.Log("[Ai] " + request.Feature + " " + response.Status + " in " + Mathf.RoundToInt(response.Seconds * 1000f) + " ms"
                      + (response.Ok ? "" : " (" + response.Error + ")"));
            live.Handle.Complete(onDone, response);
        }

        static AiResponse Read(UnityWebRequest www, bool timedOut)
        {
            if (timedOut) return AiResponse.Fail(AiStatus.Timeout, "no reply in time");
            string body = www.downloadHandler != null ? www.downloadHandler.text : null;
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.DataProcessingError)
                return AiResponse.Fail(AiStatus.NetworkError, www.error);
            if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                GeminiWire.TryReadText(body, out _, out string detail);
                var status = www.responseCode == 429 ? AiStatus.Throttled : AiStatus.BadResponse;
                return AiResponse.Fail(status, "HTTP " + www.responseCode + (string.IsNullOrEmpty(detail) ? "" : ": " + detail));
            }
            if (!GeminiWire.TryReadText(body, out string text, out string error))
                return AiResponse.Fail(AiStatus.BadResponse, error);
            return new AiResponse { Status = AiStatus.Ok, Text = text };
        }
    }

    /// <summary>One request on the wire. Close is safe to call more than once.</summary>
    sealed class LiveRequest
    {
        public UnityWebRequest Www;
        public AiHandle Handle;
        public AiBudget Budget;
        bool _closed;

        /// <summary>Abort if still running, free the request, and give the budget slot back.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            AiRunner.Forget(this);
            try { if (!Www.isDone) Www.Abort(); } catch (Exception) { }
            Www.Dispose();
            Budget.End();
        }
    }

    /// <summary>Hidden object that runs the request coroutines. Survives scene reloads; AiModule clears it on each boot.</summary>
    sealed class AiRunner : MonoBehaviour
    {
        static AiRunner _instance;
        public readonly List<LiveRequest> Live = new List<LiveRequest>();

        public static AiRunner Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[Ai]") { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<AiRunner>();
            return _instance;
        }

        public static void Forget(LiveRequest live)
        {
            if (_instance != null) _instance.Live.Remove(live);
        }

        /// <summary>
        /// Drop every request still in flight: each is aborted and its handle cancelled, so its callback never runs.
        /// A stopped coroutine runs no cleanup of its own, which is why the requests are tracked and closed here.
        /// </summary>
        public static void StopAll()
        {
            if (_instance != null) _instance.CloseAll();
        }

        void CloseAll()
        {
            StopAllCoroutines();
            var live = new List<LiveRequest>(Live);
            Live.Clear();
            foreach (var l in live)
            {
                l.Handle.Cancel();
                l.Close();
            }
        }

        void OnDestroy()
        {
            CloseAll();
            if (_instance == this) _instance = null;
        }
    }
}

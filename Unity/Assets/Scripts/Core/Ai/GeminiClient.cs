using System;
using System.Collections;
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
            AiRunner.Get().StartCoroutine(Send(request, handle, onDone));
            return handle;
        }

        IEnumerator Send(AiRequest request, AiHandle handle, Action<AiResponse> onDone)
        {
            float started = Time.realtimeSinceStartup;
            float limit = Mathf.Clamp(request.TimeoutSeconds, 1f, 30f);
            bool timedOut = false;
            AiResponse response;

            using (var www = new UnityWebRequest(_config.Endpoint(), UnityWebRequest.kHttpVerbPOST))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(GeminiWire.BuildBody(request)));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                if (!_config.UsesProxy) www.SetRequestHeader("x-goog-api-key", _config.ApiKey);

                var op = www.SendWebRequest();
                while (!op.isDone)
                {
                    if (handle.Cancelled) { www.Abort(); break; }
                    if (Time.realtimeSinceStartup - started > limit) { timedOut = true; www.Abort(); break; }
                    yield return null;
                }
                _budget.End();
                if (handle.Cancelled) yield break;
                response = Read(www, timedOut);
            }

            response.Seconds = Time.realtimeSinceStartup - started;
            Debug.Log("[Ai] " + request.Feature + " " + response.Status + " in " + Mathf.RoundToInt(response.Seconds * 1000f) + " ms"
                      + (response.Ok ? "" : " (" + response.Error + ")"));
            handle.Complete(onDone, response);
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

    /// <summary>Hidden object that runs the request coroutines. Survives scene reloads; AiModule clears it on each boot.</summary>
    sealed class AiRunner : MonoBehaviour
    {
        static AiRunner _instance;

        public static AiRunner Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[Ai]") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<AiRunner>();
            return _instance;
        }

        /// <summary>Drop every request still in flight. Their callbacks never run.</summary>
        public static void StopAll()
        {
            if (_instance != null) _instance.StopAllCoroutines();
        }
    }
}

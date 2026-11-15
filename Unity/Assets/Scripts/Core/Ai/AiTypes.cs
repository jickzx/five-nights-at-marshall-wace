// CORE CONTRACT. Changes here must be called out in your PR description.
using System;

namespace Fnmw.Core.Ai
{
    /// <summary>One text-generation request. Build it from game state the player can already see or the game already tracks.</summary>
    public sealed class AiRequest
    {
        /// <summary>Short stable name of the caller, e.g. "taunt", "hint", "debrief". Used for throttling and logs.</summary>
        public string Feature = "misc";
        /// <summary>The role and the rules. Stays the same between calls of one feature.</summary>
        public string System;
        /// <summary>The facts for this call.</summary>
        public string Prompt;
        /// <summary>JSON Schema for the reply (Gemini's subset), as a JSON object string. Null or empty asks for plain text.
        /// The schema fixes the reply's shape only: check length, truth and leaks yourself.</summary>
        public string SchemaJson;
        public int MaxOutputTokens = 512;
        public float Temperature = 0.8f;
        /// <summary>Wall-clock limit, clamped to 1..30 s. After it the callback gets Timeout.</summary>
        public float TimeoutSeconds = 6f;
    }

    public enum AiStatus
    {
        Ok,
        /// <summary>No key or proxy configured, or AI switched off. The normal state of a fresh checkout.</summary>
        Disabled,
        /// <summary>Refused locally by the session budget, or by the provider (HTTP 429).</summary>
        Throttled,
        Timeout,
        NetworkError,
        /// <summary>The provider answered, but with an error, a block, or nothing usable.</summary>
        BadResponse,
    }

    public sealed class AiResponse
    {
        public AiStatus Status;
        /// <summary>The model's text (JSON when a schema was sent). Null unless Status is Ok.</summary>
        public string Text;
        /// <summary>Short reason when Status is not Ok. For logs, never for the player.</summary>
        public string Error;
        /// <summary>Seconds from request to reply.</summary>
        public float Seconds;
        public bool Ok => Status == AiStatus.Ok && !string.IsNullOrEmpty(Text);

        public static AiResponse Fail(AiStatus status, string error) => new AiResponse { Status = status, Error = error };
    }

    /// <summary>Returned by IAiText.Request. Cancel it when the moment has passed (puzzle changed, run restarted, object destroyed).</summary>
    public sealed class AiHandle
    {
        public bool Cancelled { get; private set; }
        /// <summary>True once the callback has run, or the request was cancelled.</summary>
        public bool Done { get; private set; }

        /// <summary>After this the callback never runs. The client also calls it when a new run boots mid-request.</summary>
        public void Cancel() { Cancelled = true; Done = true; }

        /// <summary>For IAiText implementations: run the callback once, unless cancelled. Exceptions are logged, not thrown.</summary>
        public void Complete(Action<AiResponse> onDone, AiResponse response)
        {
            if (Done) return;
            Done = true;
            try { onDone?.Invoke(response); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
        }
    }

    /// <summary>
    /// Text generation behind Services.Ai. The game must never wait on it: apply the answer, move the beast and
    /// show the stock line first, then let the reply improve on that if it arrives in time.
    /// </summary>
    public interface IAiText
    {
        /// <summary>False when no provider is configured. Callers can skip building a prompt.</summary>
        bool Available { get; }

        /// <summary>
        /// Ask for text. onDone runs exactly once on the main thread, unless the handle is cancelled first or a new
        /// run boots while the request is in flight (the handle is then cancelled for you: check handle.Done, do
        /// not wait on the callback). It can run before Request returns (disabled, throttled), so do not assume
        /// it is asynchronous.
        /// </summary>
        AiHandle Request(AiRequest request, Action<AiResponse> onDone);
    }
}

using System;
using System.Collections.Generic;
using Fnmw.Core.Ai;

namespace Fnmw.Core.Fakes
{
    /// <summary>The default Services.Ai: never available, answers Disabled at once.</summary>
    public class FakeAiText : IAiText
    {
        public bool Available => false;

        public AiHandle Request(AiRequest request, Action<AiResponse> onDone)
        {
            var handle = new AiHandle();
            handle.Complete(onDone, AiResponse.Fail(AiStatus.Disabled, "fake"));
            return handle;
        }
    }

    /// <summary>
    /// For tests: records every request and answers from a queue. With Hold set, replies wait until Flush,
    /// which lets a test cancel a handle or change the game state before the answer lands.
    /// </summary>
    public class ScriptedAiText : IAiText
    {
        public bool Available { get; set; } = true;
        public bool Hold;
        public readonly List<AiRequest> Requests = new List<AiRequest>();
        readonly Queue<AiResponse> _replies = new Queue<AiResponse>();
        readonly List<(AiHandle handle, Action<AiResponse> onDone, AiResponse response)> _held =
            new List<(AiHandle, Action<AiResponse>, AiResponse)>();

        public ScriptedAiText Reply(string text)
        {
            _replies.Enqueue(new AiResponse { Status = AiStatus.Ok, Text = text });
            return this;
        }

        public ScriptedAiText Fail(AiStatus status)
        {
            _replies.Enqueue(AiResponse.Fail(status, "scripted"));
            return this;
        }

        public AiHandle Request(AiRequest request, Action<AiResponse> onDone)
        {
            Requests.Add(request);
            var handle = new AiHandle();
            var response = !Available ? AiResponse.Fail(AiStatus.Disabled, "scripted")
                : _replies.Count > 0 ? _replies.Dequeue()
                : AiResponse.Fail(AiStatus.BadResponse, "no scripted reply left");
            if (Hold) _held.Add((handle, onDone, response));
            else handle.Complete(onDone, response);
            return handle;
        }

        /// <summary>Deliver every held reply (cancelled handles stay silent).</summary>
        public void Flush()
        {
            var held = new List<(AiHandle handle, Action<AiResponse> onDone, AiResponse response)>(_held);
            _held.Clear();
            foreach (var h in held) h.handle.Complete(h.onDone, h.response);
        }
    }
}

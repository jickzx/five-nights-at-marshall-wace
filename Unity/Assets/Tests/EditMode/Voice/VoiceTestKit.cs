using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using Fnmw.Voice;

namespace Fnmw.Tests.Voice
{
    /// <summary>A transport that records every request and answers only when told to. No network.</summary>
    internal sealed class FakeNet : IVoiceTransport
    {
        public sealed class Open { public int Handle; public VoiceRequest Req; public Action<VoiceResponse> Done; }
        public readonly List<VoiceRequest> All = new List<VoiceRequest>();
        public readonly List<Open> Waiting = new List<Open>();
        public int Cancelled, CancelAllCalls;
        /// <summary>Answers a request at once when it returns non-null.</summary>
        public Func<VoiceRequest, VoiceResponse> Auto;
        int _next = 1;

        public int InFlight => Waiting.Count;

        public int Send(VoiceRequest r, Action<VoiceResponse> done)
        {
            All.Add(r);
            int h = _next++;
            var auto = Auto?.Invoke(r);
            if (auto != null) { done(auto); return h; }
            Waiting.Add(new Open { Handle = h, Req = r, Done = done });
            return h;
        }

        public void Cancel(int handle) { if (Waiting.RemoveAll(o => o.Handle == handle) > 0) Cancelled++; }
        public void CancelAll() { CancelAllCalls++; Cancelled += Waiting.Count; Waiting.Clear(); }

        public int Count(string prefix) => All.Count(r => r.Path.StartsWith(prefix, StringComparison.Ordinal));
        public bool Has(string prefix) => Waiting.Any(o => o.Req.Path.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>Answers the oldest waiting request whose path starts with prefix.</summary>
        public VoiceRequest Respond(string prefix, VoiceResponse res)
        {
            var o = Waiting.FirstOrDefault(x => x.Req.Path.StartsWith(prefix, StringComparison.Ordinal));
            if (o == null) throw new InvalidOperationException("nothing waiting for " + prefix + "; waiting: " + string.Join(",", Waiting.Select(w => w.Req.Path)));
            Waiting.Remove(o);
            o.Done(res);
            return o.Req;
        }

        public static VoiceResponse Json(string json, int status = 200) => new VoiceResponse { Status = status, Text = json };
        public static VoiceResponse Error(int status, string code) => new VoiceResponse { Status = status, Text = "{\"error\":{\"code\":\"" + code + "\"}}" };
        public static VoiceResponse Pcm(int samples = 4800) => new VoiceResponse { Status = 200, Bytes = new byte[samples * 2] };
        public static VoiceResponse Timeout() => new VoiceResponse { Status = 0, Error = "timeout" };
    }

    /// <summary>Records voice playback; Started fires only when the test says the audio began (or at once with AutoStart).</summary>
    internal sealed class FakeAudio : IAudioDirector
    {
        public sealed class Line { public int Handle; public VoiceClip Clip; public Action<VoicePlayback> Cb; public bool Started, Ended; }
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<VoiceChannel> Stops = new List<VoiceChannel>();
        public bool AutoStart = true;
        int _next = 1;

        public void SetIntensity(float i) { }
        public void PlayStinger(string name) { }

        public int PlayVoice(VoiceClip clip, Action<VoicePlayback> onEvent)
        {
            var l = new Line { Handle = _next++, Clip = clip, Cb = onEvent };
            Lines.Add(l);
            if (AutoStart) Start(l);
            return l.Handle;
        }

        public void Start(Line l)
        {
            l.Started = true;
            l.Cb?.Invoke(new VoicePlayback { Handle = l.Handle, Channel = l.Clip.Channel, Event = VoicePlaybackEvent.Started, Seconds = l.Clip.Samples.Length / (float)l.Clip.SampleRate });
        }

        public void Finish(Line l)
        {
            if (l.Ended) return;
            l.Ended = true;
            l.Cb?.Invoke(new VoicePlayback { Handle = l.Handle, Channel = l.Clip.Channel, Event = VoicePlaybackEvent.Finished });
        }

        public void StopVoice(VoiceChannel channel, float fade = 0.15f)
        {
            Stops.Add(channel);
            foreach (var l in Lines.Where(x => x.Clip.Channel == channel && !x.Ended).ToList())
            {
                l.Ended = true;
                l.Cb?.Invoke(new VoicePlayback { Handle = l.Handle, Channel = channel, Event = VoicePlaybackEvent.Stopped });
            }
        }

        public List<Line> On(VoiceChannel c) => Lines.Where(l => l.Clip.Channel == c).ToList();
        public bool Playing(VoiceChannel c) => Lines.Any(l => l.Clip.Channel == c && l.Started && !l.Ended);
    }

    /// <summary>Records what the voice module shows.</summary>
    internal sealed class FakeUi : IGameUI
    {
        public readonly List<HintView> Hints = new List<HintView>();
        public readonly List<SubtitleLine> Subtitles = new List<SubtitleLine>();
        public readonly List<PronunciationView> Prons = new List<PronunciationView>();
        public HintView Hint => Hints.Count > 0 ? Hints[Hints.Count - 1] : default;
        public SubtitleLine Subtitle => Subtitles.Count > 0 ? Subtitles[Subtitles.Count - 1] : default;
        public PronunciationView Pron => Prons.Count > 0 ? Prons[Prons.Count - 1] : default;

        public void ShowTitle(Action onStart) { }
        public void ShowHud(GameState state) { }
        public void ShowPrompt(string text) { }
        public void ShowPuzzle(LangPuzzle puzzle, Action<string[]> onSubmit) { }
        public void ShowAnswerFeedback(AnswerResult result) { }
        public void ShowReveal(RevealInfo reveal, Action onContinue) { }
        public void ShowQuiz(QuizQuestion quiz, Action<int> onChoose) { }
        public void ShowEnd(bool escaped, GameState state, Action onRestart) { }
        public void HideAll() { }
        public void ShowSubtitle(SubtitleLine line) => Subtitles.Add(line);
        public void ShowHint(HintView hint) => Hints.Add(hint);
        public void ShowPronunciation(PronunciationView view) => Prons.Add(view);
    }

    internal static class Wire
    {
        public const string Hex16 = "0123456789abcdef";
        static int _id;
        public static string Id() => (++_id).ToString("x32");

        public static string Session(bool mock = false, bool monster = true, bool hint = true, bool hintSpeech = true, bool pronounce = true, string version = Hex16) =>
            "{\"session\":\"v1.tok\",\"expires_in_s\":7200,\"mode\":\"" + (mock ? "mock" : "live") + "\",\"config_version\":\"" + version + "\",\"features\":{\"monster\":"
            + B(monster) + ",\"hint\":" + B(hint) + ",\"hint_speech\":" + B(hintSpeech) + ",\"pronounce\":" + B(pronounce) + "}}";

        static string B(bool b) => b ? "true" : "false";

        public static string Speech(string source = "live") => "{\"id\":\"" + Id() + "\",\"sample_rate\":24000,\"version\":\"" + Hex16 + "\",\"source\":\"" + source + "\"}";

        public static string Line(string text, string source = "live", bool speech = true) =>
            "{\"id\":\"" + Id() + "\",\"text\":\"" + text + "\",\"source\":\"" + source + "\",\"kind\":\"generated\",\"rows\":[1,3],\"speech\":" + (speech ? Speech(source) : "null") + "}";

        public static string Pronounce(string source = "live") => "{\"id\":\"" + Id() + "\",\"source\":\"" + source + "\",\"speech\":" + Speech(source) + "}";
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fnmw.Audio.Synth;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// Plays spoken lines on three stoppable sources: the beast's (3D, on the beast) and two 2D ones for the ship
    /// and the native speaker. Processing (VoiceFx) runs on a worker thread; the clip starts on a later frame, and
    /// only then is Started reported, with the real length. A new line on a channel replaces the old one.
    /// </summary>
    internal sealed class VoicePlayer : MonoBehaviour
    {
        public static readonly float[] ChannelGain = { 1f, 0.85f, 0.9f };   // Monster, Ship, Native
        const int MinRate = 8000, MaxRate = 48000;

        sealed class Slot
        {
            public int Handle;
            public VoiceClip Clip;
            public Action<VoicePlayback> Callback;
            public Task<float[]> Job;
            public float[] Ready;
            public bool Preparing, Playing;
            public float EndsAt;
            public AudioClip Unity;
        }

        sealed class Fade { public AudioSource Source; public AudioClip Clip; public float From, T, Len; }

        /// <summary>Where the beast is (null when there is none). Set by the director.</summary>
        internal Func<Transform> Beast = () => null;
        /// <summary>Unscaled seconds. Tests replace it.</summary>
        internal Func<float> Clock = () => Time.unscaledTime;
        /// <summary>Process on the calling thread (tests).</summary>
        internal bool Synchronous;

        readonly Slot[] _slots = { new Slot(), new Slot(), new Slot() };
        readonly AudioSource[] _sources = new AudioSource[3];
        readonly List<Fade> _fades = new List<Fade>();
        int _next = 1;

        public bool IsActive(VoiceChannel ch) { var s = _slots[(int)ch]; return s.Preparing || s.Playing; }
        public bool IsPlaying(VoiceChannel ch) => _slots[(int)ch].Playing;
        internal AudioSource SourceFor(VoiceChannel ch) => _sources[(int)ch];

        public int Play(VoiceClip clip, Action<VoicePlayback> callback)
        {
            if (clip == null || clip.Samples == null || clip.Samples.Length == 0 || clip.SampleRate < MinRate || clip.SampleRate > MaxRate
                || clip.Samples.Length > VoiceFx.MaxSeconds * clip.SampleRate)
            {
                Emit(callback, 0, clip?.Channel ?? VoiceChannel.Ship, VoicePlaybackEvent.Dropped, 0f);
                return 0;
            }
            var ch = clip.Channel;
            if (ch == VoiceChannel.Monster && Beast() == null)
            {
                Emit(callback, 0, ch, VoicePlaybackEvent.Dropped, 0f);   // no beast: a disembodied taunt would be wrong
                return 0;
            }
            Stop(ch, 0.15f);
            var slot = _slots[(int)ch];
            int handle = _next++;
            slot.Handle = handle; slot.Clip = clip; slot.Callback = callback; slot.Preparing = true; slot.Playing = false; slot.Ready = null;
            var samples = clip.Samples; int rate = clip.SampleRate, stage = clip.Stage;
            Func<float[]> work = () => ch == VoiceChannel.Monster ? VoiceFx.Monster(samples, rate, stage)
                : ch == VoiceChannel.Ship ? VoiceFx.Ship(samples, rate) : VoiceFx.Native(samples, rate);
            if (Synchronous) { try { slot.Ready = work(); } catch (Exception) { slot.Ready = Array.Empty<float>(); } slot.Job = null; }
            else slot.Job = Task.Run(work);
            return handle;
        }

        /// <summary>Stops a channel. A line still being prepared is cancelled. Stopped is reported at once.</summary>
        public void Stop(VoiceChannel ch, float fade)
        {
            var slot = _slots[(int)ch];
            if (!slot.Preparing && !slot.Playing) return;
            bool wasPlaying = slot.Playing;
            var cb = slot.Callback; int handle = slot.Handle;
            var unityClip = slot.Unity;
            Reset(slot);
            var src = _sources[(int)ch];
            if (wasPlaying && src)
            {
                if (fade > 0f && src.isPlaying) _fades.Add(new Fade { Source = src, Clip = unityClip, From = src.volume, Len = fade });
                else { src.Stop(); src.clip = null; Kill(unityClip); }
            }
            Emit(cb, handle, ch, VoicePlaybackEvent.Stopped, 0f);
        }

        public void StopAll()
        {
            for (int i = 0; i < 3; i++) Stop((VoiceChannel)i, 0f);
            foreach (var f in _fades) { if (f.Source) { f.Source.Stop(); f.Source.clip = null; } Kill(f.Clip); }
            _fades.Clear();
        }

        void Update() => Tick();

        internal void Tick()
        {
            float now = Clock();
            for (int i = 0; i < 3; i++)
            {
                var slot = _slots[i];
                var ch = (VoiceChannel)i;
                if (slot.Preparing)
                {
                    if (slot.Ready == null && slot.Job != null)
                    {
                        if (!slot.Job.IsCompleted) continue;
                        slot.Ready = slot.Job.Status == TaskStatus.RanToCompletion ? slot.Job.Result : Array.Empty<float>();
                    }
                    if (slot.Ready == null) continue;
                    Begin(slot, ch, now);
                }
                else if (slot.Playing && now >= slot.EndsAt)
                {
                    var cb = slot.Callback; int handle = slot.Handle;
                    var src = _sources[i];
                    if (src && src.clip == slot.Unity) { src.Stop(); src.clip = null; }
                    Kill(slot.Unity);
                    Reset(slot);
                    Emit(cb, handle, ch, VoicePlaybackEvent.Finished, 0f);
                }
            }
            for (int k = _fades.Count - 1; k >= 0; k--)
            {
                var f = _fades[k];
                f.T += Time.unscaledDeltaTime > 0 ? Time.unscaledDeltaTime : 0.02f;
                if (!f.Source || f.T >= f.Len)
                {
                    if (f.Source && f.Source.clip == f.Clip) { f.Source.Stop(); f.Source.clip = null; }
                    Kill(f.Clip);
                    _fades.RemoveAt(k);
                }
                else if (f.Source.clip == f.Clip) f.Source.volume = f.From * (1f - f.T / f.Len);
            }
        }

        void Begin(Slot slot, VoiceChannel ch, float now)
        {
            var data = slot.Ready;
            var cb = slot.Callback; int handle = slot.Handle;
            var src = EnsureSource(ch);
            if (data.Length == 0 || src == null)
            {
                Reset(slot);
                Emit(cb, handle, ch, VoicePlaybackEvent.Dropped, 0f);
                return;
            }
            var clip = AudioClip.Create("fnmw_voice_" + ch, data.Length, 1, slot.Clip.SampleRate, false);
            clip.SetData(data, 0);
            // a fade-out still running on this source belongs to the old clip: cut it so the new line is clean
            _fades.RemoveAll(f => { if (f.Source == src) { Kill(f.Clip); return true; } return false; });
            src.Stop();
            src.clip = clip;
            src.volume = Mathf.Clamp01(slot.Clip.Gain * ChannelGain[(int)ch]);
            src.Play();
            float seconds = data.Length / (float)slot.Clip.SampleRate;
            slot.Unity = clip; slot.Preparing = false; slot.Playing = true; slot.EndsAt = now + seconds; slot.Ready = null; slot.Job = null;
            Emit(cb, handle, ch, VoicePlaybackEvent.Started, seconds);
        }

        AudioSource EnsureSource(VoiceChannel ch)
        {
            var src = _sources[(int)ch];
            if (ch == VoiceChannel.Monster)
            {
                var beast = Beast();
                if (beast == null) return null;
                if (src == null || src.transform.parent != beast)
                {
                    if (src) Kill(src.gameObject);
                    var go = new GameObject("BeastSpeech");
                    go.transform.SetParent(beast, false);
                    go.transform.localPosition = new Vector3(0, 1.6f, 0);
                    src = go.AddComponent<AudioSource>();
                    src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic;
                    src.minDistance = 3f; src.maxDistance = 70f; src.dopplerLevel = 0f; src.spread = 40f;
                    src.playOnAwake = false;
                    _sources[(int)ch] = src;
                }
                return src;
            }
            if (src == null)
            {
                var go = new GameObject(ch == VoiceChannel.Ship ? "ShipVoice" : "NativeVoice");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f; src.playOnAwake = false; src.priority = 16;
                _sources[(int)ch] = src;
            }
            return src;
        }

        static void Reset(Slot s)
        {
            s.Handle = 0; s.Clip = null; s.Callback = null; s.Job = null; s.Ready = null; s.Preparing = false; s.Playing = false; s.Unity = null;
        }

        static void Emit(Action<VoicePlayback> cb, int handle, VoiceChannel ch, VoicePlaybackEvent e, float seconds)
        {
            if (cb == null) return;
            try { cb(new VoicePlayback { Handle = handle, Channel = ch, Event = e, Seconds = seconds }); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        static void Kill(UnityEngine.Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy()
        {
            StopAll();
            for (int i = 0; i < 3; i++) Reset(_slots[i]);
        }
    }
}

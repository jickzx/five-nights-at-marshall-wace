using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Fnmw.Audio.Synth;
using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// Renders every SfxLibrary effect on a worker thread at startup (pure C#, thread-safe), and turns the
    /// buffers into AudioClips on the main thread when first needed. Anything asked for before the worker
    /// reaches it is rendered synchronously (a few ms).
    /// </summary>
    public sealed class SfxBank
    {
        readonly int _sr;
        readonly ConcurrentDictionary<string, float[]> _buffers = new ConcurrentDictionary<string, float[]>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, int> _roundRobin = new Dictionary<string, int>();

        public SfxBank(int sampleRate)
        {
            _sr = sampleRate;
            var worker = new Thread(() =>
            {
                foreach (var (name, v) in SfxLibrary.All()) Get(name, v);
            }) { IsBackground = true, Name = "Fnmw SFX render" };
            worker.Start();
        }

        static string Key(string name, int v) => name + "#" + v;

        public float[] Get(string name, int variant = 0)
        {
            string k = Key(name, variant);
            if (_buffers.TryGetValue(k, out var b)) return b;
            b = SfxLibrary.Render(name, _sr, variant);
            if (b != null) _buffers[k] = b;
            return b;
        }

        /// <summary>Next take of an effect, round-robin across its variants.</summary>
        public int NextVariant(string name)
        {
            int n = SfxLibrary.Variants(name);
            _roundRobin.TryGetValue(name, out int i);
            _roundRobin[name] = (i + 1) % n;
            return i % n;
        }

        /// <summary>Main thread only.</summary>
        public AudioClip Clip(string name, int variant = 0)
        {
            string k = Key(name, variant);
            if (_clips.TryGetValue(k, out var c) && c != null) return c;
            var b = Get(name, variant);
            if (b == null) return null;
            c = AudioClip.Create("fnmw_" + k, b.Length, 1, _sr, false);
            c.SetData(b, 0);
            _clips[k] = c;
            return c;
        }

        public void Destroy()
        {
            foreach (var c in _clips.Values) if (c != null) Object.Destroy(c);
            _clips.Clear();
        }
    }
}

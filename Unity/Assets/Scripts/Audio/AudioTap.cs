using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// Records exactly what the AudioListener hears (score + 3D effects) to a 16-bit WAV, for demo videos.
    /// From the CLI: Fnmw.Audio.AudioTap.StartRecording("/tmp/x.wav"); ... Fnmw.Audio.AudioTap.StopRecording();
    /// A sidecar "&lt;path&gt;.start" holds the UTC time (unix ms) of the first recorded sample, for muxing with frames.
    /// </summary>
    public sealed class AudioTap : MonoBehaviour
    {
        static AudioTap _instance;
        ListenerTap _tap;
        FileStream _file;
        BinaryWriter _w;
        string _path;
        long _bytes;
        int _channels = 2, _sr;
        bool _wroteStart;

        void Awake() => _instance = this;
        void OnDestroy() { Close(); if (_instance == this) _instance = null; }

        public static bool Recording => _instance != null && _instance._file != null;

        public static void StartRecording(string path)
        {
            if (_instance == null) throw new InvalidOperationException("No AudioDirector in the scene");
            _instance.Open(path);
        }

        public static string StopRecording() => _instance?.Close();

        void Open(string path)
        {
            Close();
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null) throw new InvalidOperationException("No AudioListener yet");
            _tap = listener.GetComponent<ListenerTap>() ?? listener.gameObject.AddComponent<ListenerTap>();
            _sr = AudioSettings.outputSampleRate;
            _channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            _file = File.Create(path);
            _w = new BinaryWriter(_file);
            WriteHeader(0);
            _bytes = 0;
            _wroteStart = false;
            _tap.Begin();
        }

        void WriteHeader(long dataBytes)
        {
            _w.Seek(0, SeekOrigin.Begin);
            _w.Write(new[] { 'R', 'I', 'F', 'F' }); _w.Write((int)(36 + dataBytes)); _w.Write(new[] { 'W', 'A', 'V', 'E' });
            _w.Write(new[] { 'f', 'm', 't', ' ' }); _w.Write(16); _w.Write((short)1); _w.Write((short)_channels);
            _w.Write(_sr); _w.Write(_sr * _channels * 2); _w.Write((short)(_channels * 2)); _w.Write((short)16);
            _w.Write(new[] { 'd', 'a', 't', 'a' }); _w.Write((int)dataBytes);
            _w.Seek(0, SeekOrigin.End);
        }

        void Update() => Drain();

        void Drain()
        {
            if (_file == null || _tap == null) return;
            if (!_wroteStart && _tap.FirstSampleUtcMs > 0)
            {
                File.WriteAllText(_path + ".start", _tap.FirstSampleUtcMs.ToString());
                _wroteStart = true;
            }
            int ch = _tap.Channels > 0 ? _tap.Channels : _channels;
            while (_tap.TryRead(out float s, out int sampleIndex))
            {
                // the tap may run with more channels than we record: keep the first two
                if (sampleIndex % ch >= _channels) continue;
                _w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
                _bytes += 2;
            }
        }

        string Close()
        {
            if (_file == null) return null;
            _tap?.End();
            Drain();
            WriteHeader(_bytes);
            _w.Flush();
            _file.Dispose();
            _file = null; _w = null;
            var p = _path;
            Debug.Log($"[Audio] recorded {_bytes / 2 / Math.Max(1, _channels) / (float)_sr:0.0}s to {p}");
            return p;
        }
    }

    /// <summary>Lives on the AudioListener; copies the final mix into a single-producer ring buffer.</summary>
    public sealed class ListenerTap : MonoBehaviour
    {
        readonly float[] _ring = new float[48000 * 2 * 8];
        long _write, _read, _absolute;
        volatile bool _on;
        public volatile int Channels;
        long _firstUtcMs;
        public long FirstSampleUtcMs => Interlocked.Read(ref _firstUtcMs);

        public void Begin() { Interlocked.Exchange(ref _read, Interlocked.Read(ref _write)); _absolute = 0; Interlocked.Exchange(ref _firstUtcMs, 0); _on = true; }
        public void End() => _on = false;

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_on) return;
            Channels = channels;
            if (Interlocked.Read(ref _firstUtcMs) == 0)
                Interlocked.Exchange(ref _firstUtcMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            long w = Interlocked.Read(ref _write);
            for (int i = 0; i < data.Length; i++) _ring[(w + i) % _ring.Length] = data[i];
            Interlocked.Exchange(ref _write, w + data.Length);
        }

        public bool TryRead(out float sample, out int channelIndex)
        {
            long r = Interlocked.Read(ref _read);
            if (r >= Interlocked.Read(ref _write)) { sample = 0; channelIndex = 0; return false; }
            sample = _ring[r % _ring.Length];
            channelIndex = (int)(_absolute++ % Math.Max(1, Channels));
            Interlocked.Exchange(ref _read, r + 1);
            return true;
        }
    }
}

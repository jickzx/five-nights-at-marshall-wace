// Offline processing for spoken lines. Pure C#, no UnityEngine: runs on a worker thread in the game, and in
// Tools~/RenderHarness ("voicefx in.wav outdir") to audition real speech without Unity.
//
// Monster: deeper and harsher at each beast stage (pitch and speed drop together, sub-octave layers, drive,
// a growl tremolo, darker filtering and a longer reverb). Ship: clean and intelligible. Native: untouched apart
// from one gain, because it is a pronunciation reference.
using System;

namespace Fnmw.Audio.Synth
{
    public static class VoiceFx
    {
        /// <summary>-3 dBFS.</summary>
        public const float PeakTarget = 0.70794578f;
        /// <summary>Longest input processed; anything longer is cut, so a bad clip cannot stall a worker.</summary>
        public const float MaxSeconds = 30f;

        public struct MonsterStage
        {
            public float Semitones;      // pitch and speed together
            public float Layer1Semis, Layer1Gain, Layer1DelayMs;
            public float Layer2Semis, Layer2Gain, Layer2DelayMs;
            public float Drive;          // 1 = clean
            public float GrowlDepth, GrowlHz;
            public float LowPassHz;
            public float ShelfDb;        // boost below 180 Hz
            public float ReverbSend, ReverbFeedback, TailSeconds;
        }

        public static MonsterStage StageParams(int stage)
        {
            switch (Math.Max(1, Math.Min(3, stage)))
            {
                case 1: return new MonsterStage { Semitones = -2f, Drive = 1f, LowPassHz = 5000f, ReverbSend = 0.15f, ReverbFeedback = 0.78f, TailSeconds = 0.6f };
                case 2:
                    return new MonsterStage
                    {
                        Semitones = -4f, Layer1Semis = -12f, Layer1Gain = 0.35f, Layer1DelayMs = 12f, Drive = 2f,
                        GrowlDepth = 0.2f, GrowlHz = 38f, LowPassHz = 3800f, ReverbSend = 0.3f, ReverbFeedback = 0.82f, TailSeconds = 1.0f,
                    };
                default:
                    return new MonsterStage
                    {
                        Semitones = -7f, Layer1Semis = -12f, Layer1Gain = 0.45f, Layer1DelayMs = 12f,
                        Layer2Semis = -19f, Layer2Gain = 0.2f, Layer2DelayMs = 23f, Drive = 3.5f,
                        GrowlDepth = 0.4f, GrowlHz = 31f, LowPassHz = 3000f, ShelfDb = 4f, ReverbSend = 0.45f, ReverbFeedback = 0.87f, TailSeconds = 1.6f,
                    };
            }
        }

        static float[] Bound(float[] input, int sr)
        {
            if (input == null || input.Length == 0 || sr <= 0) return Array.Empty<float>();
            int max = (int)(MaxSeconds * sr);
            if (input.Length <= max) return input;
            var cut = new float[max];
            Array.Copy(input, cut, max);
            return cut;
        }

        public static float[] Monster(float[] input, int sr, int stage)
        {
            var x = Bound(input, sr);
            if (x.Length == 0) return Array.Empty<float>();
            var p = StageParams(stage);
            x = Normalize((float[])x.Clone(), 0.5f);   // the same drive bites the same at any input level

            // 1. pitch and speed down together: plain resampling, so it gets slower and deeper at once
            var main = Resample(x, (float)Math.Pow(2.0, p.Semitones / 12.0));
            int tail = (int)(p.TailSeconds * sr);
            var y = new float[main.Length + tail];
            Array.Copy(main, y, main.Length);

            // 2. sub-octave layers, pitch-shifted without changing time so they stay on the words
            if (p.Layer1Gain > 0) MixShifted(y, main, sr, p.Layer1Semis, p.Layer1Gain, p.Layer1DelayMs);
            if (p.Layer2Gain > 0) MixShifted(y, main, sr, p.Layer2Semis, p.Layer2Gain, p.Layer2DelayMs);

            // 3. drive, growl, tone
            var lp = new Svf(); lp.Set(p.LowPassHz, 0.7f, sr);
            var lp2 = new Svf(); lp2.Set(p.LowPassHz, 0.7f, sr);
            var shelf = new Svf(); shelf.Set(180f, 0.7f, sr);
            float shelfGain = Dsp.DbToGain(p.ShelfDb) - 1f;
            for (int i = 0; i < y.Length; i++)
            {
                float s = y[i];
                if (p.Drive > 1f) s = Dsp.SoftClip(s * p.Drive);   // level is restored by the final normalisation
                if (p.GrowlDepth > 0f) s *= 1f - p.GrowlDepth * (0.5f + 0.5f * Dsp.Sin(p.GrowlHz * i / sr));
                lp.Process(s); lp2.Process(lp.Low); s = lp2.Low;
                if (shelfGain != 0f) { shelf.Process(s); s += shelf.Low * shelfGain; }
                y[i] = s;
            }

            // 4. reverb send, with a tail that rings past the words
            var verb = new Reverb(sr) { Feedback = p.ReverbFeedback, Damp = 0.35f };
            for (int i = 0; i < y.Length; i++)
            {
                verb.Process(y[i], y[i], out float wl, out float wr);
                y[i] += p.ReverbSend * 0.5f * (wl + wr);
            }
            return Normalize(TrimTail(y, main.Length), PeakTarget);
        }

        public static float[] Ship(float[] input, int sr)
        {
            var x = Bound(input, sr);
            if (x.Length == 0) return Array.Empty<float>();
            var y = new float[x.Length];
            var hp = new Svf(); hp.Set(150f, 0.7f, sr);
            var presence = new Svf(); presence.Set(3000f, 1.2f, sr);
            float env = 0f, att = 1f - (float)Math.Exp(-1.0 / (0.005 * sr)), rel = 1f - (float)Math.Exp(-1.0 / (0.12 * sr));
            const float threshold = 0.25f, ratio = 3f;
            for (int i = 0; i < x.Length; i++)
            {
                hp.Process(x[i]);
                float s = hp.High;
                presence.Process(s);
                s += presence.Band * 0.4f;                 // about +3 dB around 3 kHz
                float a = Math.Abs(s);
                env += (a > env ? att : rel) * (a - env);
                if (env > threshold) s *= (threshold + (env - threshold) / ratio) / env;
                y[i] = s;
            }
            return Normalize(y, PeakTarget);
        }

        /// <summary>One gain, nothing else: the learner hears the voice as it was recorded.</summary>
        public static float[] Native(float[] input, int sr) => Normalize((float[])Bound(input, sr).Clone(), PeakTarget);

        // ------------------------------------------------------------------ building blocks

        /// <summary>Plays the input at <paramref name="rate"/> (0.5 = an octave down and twice as long), linear interpolation.</summary>
        public static float[] Resample(float[] x, float rate)
        {
            if (x.Length == 0 || rate <= 0f) return Array.Empty<float>();
            int n = Math.Max(1, (int)(x.Length / rate));
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float pos = i * rate;
                int k = (int)pos;
                float f = pos - k;
                float a = x[Math.Min(k, x.Length - 1)], b = x[Math.Min(k + 1, x.Length - 1)];
                y[i] = a + (b - a) * f;
            }
            return y;
        }

        /// <summary>Delay-line pitch shifter (two crossfaded taps), same length as the input.</summary>
        public static float[] PitchShift(float[] x, int sr, float semis)
        {
            var y = new float[x.Length];
            float factor = (float)Math.Pow(2.0, semis / 12.0);
            float window = 0.06f * sr;
            float phase = 0f, step = (1f - factor) / window;
            for (int i = 0; i < x.Length; i++)
            {
                phase += step; phase -= (float)Math.Floor(phase);
                float s = 0f;
                for (int tap = 0; tap < 2; tap++)
                {
                    float ph = phase + tap * 0.5f; ph -= (float)Math.Floor(ph);
                    float d = ph * window;
                    float pos = i - d;
                    int k = (int)Math.Floor(pos);
                    float f = pos - k;
                    float a = k >= 0 && k < x.Length ? x[k] : 0f, b = k + 1 >= 0 && k + 1 < x.Length ? x[k + 1] : 0f;
                    float g = 0.5f - 0.5f * Dsp.Sin(ph + 0.25f);   // Hann window over the tap's sweep
                    s += (a + (b - a) * f) * g;
                }
                y[i] = s;
            }
            return y;
        }

        static void MixShifted(float[] y, float[] main, int sr, float semis, float gain, float delayMs)
        {
            var layer = PitchShift(main, sr, semis);
            int delay = (int)(delayMs * 0.001f * sr);
            for (int i = 0; i < layer.Length && i + delay < y.Length; i++) y[i + delay] += layer[i] * gain;
        }

        /// <summary>Drops the silent end of the reverb tail (keeps at least the words).</summary>
        static float[] TrimTail(float[] y, int keep)
        {
            float peak = 0f;
            foreach (var s in y) peak = Math.Max(peak, Math.Abs(s));
            float floor = peak * 0.001f;
            int end = y.Length;
            while (end > keep && Math.Abs(y[end - 1]) <= floor) end--;
            if (end == y.Length) return y;
            var r = new float[end];
            Array.Copy(y, r, end);
            return r;
        }

        public static float Peak(float[] y)
        {
            float peak = 0f;
            foreach (var s in y) if (!float.IsNaN(s)) peak = Math.Max(peak, Math.Abs(s));
            return peak;
        }

        public static float[] Normalize(float[] y, float target)
        {
            for (int i = 0; i < y.Length; i++) if (float.IsNaN(y[i]) || float.IsInfinity(y[i])) y[i] = 0f;
            float peak = Peak(y);
            if (peak < 1e-6f) return y;
            float g = target / peak;
            for (int i = 0; i < y.Length; i++) y[i] *= g;
            return y;
        }
    }

    /// <summary>How far the score ducks under speech: the quietest wanted level wins.</summary>
    public static class VoiceMix
    {
        public const float ShipOrNative = 0.55f, Monster = 0.75f, None = 1f;
        public const float DownSeconds = 0.12f, UpSeconds = 0.6f;

        public static float DuckTarget(bool monster, bool ship, bool native)
        {
            float t = None;
            if (monster) t = Math.Min(t, Monster);
            if (ship || native) t = Math.Min(t, ShipOrNative);
            return t;
        }

        /// <summary>One step of the duck gain toward its target (fast down, slow up). Audio-thread safe.</summary>
        public static float Step(float gain, float target, int sampleRate)
        {
            float secs = target < gain ? DownSeconds : UpSeconds;
            float k = 1f - (float)Math.Exp(-1.0 / (secs * Math.Max(1, sampleRate)));
            return gain + (target - gain) * k;
        }
    }
}

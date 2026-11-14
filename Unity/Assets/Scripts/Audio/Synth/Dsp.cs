// Pure C# DSP building blocks. No UnityEngine here on purpose: the whole synth can be compiled
// and rendered outside Unity (see Audio/README.md), and every type is safe to use on the audio thread
// (no allocation after construction).
using System;

namespace Fnmw.Audio.Synth
{
    /// <summary>Deterministic xorshift32. Same seed, same sequence, on every platform.</summary>
    public struct Rng
    {
        uint _s;

        public Rng(int seed)
        {
            _s = (uint)seed * 2654435761u ^ 0x9E3779B9u;
            if (_s == 0) _s = 0x1234567u;
            NextUInt(); NextUInt();
        }

        public uint NextUInt()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s;
        }

        /// <summary>[0, 1)</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);
        /// <summary>[-1, 1)</summary>
        public float Bi() => Next01() * 2f - 1f;
        /// <summary>[lo, hiExclusive)</summary>
        public int Range(int lo, int hiExclusive) => hiExclusive <= lo ? lo : lo + (int)(NextUInt() % (uint)(hiExclusive - lo));
        public float Range(float lo, float hi) => lo + (hi - lo) * Next01();
        public bool Chance(float p) => Next01() < p;
    }

    public static class Dsp
    {
        public const float TwoPi = 6.28318530718f;
        const int TableSize = 4096;
        static readonly float[] Sine = BuildSine();

        static float[] BuildSine()
        {
            var t = new float[TableSize + 1];
            for (int i = 0; i <= TableSize; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / TableSize);
            return t;
        }

        /// <summary>Table sine. phase01 is in cycles (any real value).</summary>
        public static float Sin(float phase01)
        {
            phase01 -= (float)Math.Floor(phase01);
            float f = phase01 * TableSize;
            int i = (int)f;
            if (i >= TableSize) i = TableSize - 1;
            float fr = f - i;
            return Sine[i] + (Sine[i + 1] - Sine[i]) * fr;
        }

        public static float MidiToHz(float midi) => 440f * (float)Math.Pow(2.0, (midi - 69f) / 12f);
        public static float DbToGain(float db) => (float)Math.Pow(10.0, db / 20.0);
        public static float Clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;
        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Bounded soft saturation: |out| &lt;= 1 for any input, ~linear below 0.3.</summary>
        public static float SoftClip(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>PolyBLEP correction for a naive sawtooth (phase in [0,1), dt = freq/sr).</summary>
        public static float PolyBlep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        /// <summary>Band-limited saw from a phase in [0,1).</summary>
        public static float Saw(float phase, float dt) => 2f * phase - 1f - PolyBlep(phase, dt);

        /// <summary>Equal-power pan, pan in [-1, 1].</summary>
        public static void Pan(float pan, out float l, out float r)
        {
            float a = (Clamp(pan, -1f, 1f) + 1f) * 0.25f; // 0..0.5 cycles of a quarter wave
            l = Sin(a + 0.25f); // cos
            r = Sin(a);
        }

        /// <summary>Simple linear attack/exponential-ish decay envelope evaluated at t seconds.</summary>
        public static float AD(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            float d = (t - attack) / decay;
            return d > 12f ? 0f : (float)Math.Exp(-d * 4f);
        }

        /// <summary>Attack, hold, release window (all linear, click-free at both ends).</summary>
        public static float AHR(float t, float attack, float hold, float release)
        {
            if (t <= 0f) return 0f;
            if (t < attack) return t / attack;
            if (t < attack + hold) return 1f;
            float r = 1f - (t - attack - hold) / release;
            return r > 0f ? r : 0f;
        }
    }

    /// <summary>One-pole lowpass. Cheap smoothing and tone control.</summary>
    public struct OnePole
    {
        float _z, _a;
        public void SetCutoff(float hz, float sr) => _a = 1f - (float)Math.Exp(-Dsp.TwoPi * Math.Min(hz, sr * 0.45f) / sr);
        public float Process(float x) { _z += _a * (x - _z); return _z; }
        public float HighPass(float x) => x - Process(x);
        public void Reset() => _z = 0f;
    }

    /// <summary>Topology-preserving state variable filter (Cytomic/Simper). Stable under fast modulation.</summary>
    public struct Svf
    {
        float _g, _k, _a1, _a2, _a3, _ic1, _ic2;
        public float Low, Band, High;

        public void Set(float cutoffHz, float q, float sr)
        {
            cutoffHz = Dsp.Clamp(cutoffHz, 10f, sr * 0.45f);
            _g = (float)Math.Tan(Math.PI * cutoffHz / sr);
            _k = 1f / Math.Max(q, 0.05f);
            _a1 = 1f / (1f + _g * (_g + _k));
            _a2 = _g * _a1;
            _a3 = _g * _a2;
        }

        public void Process(float x)
        {
            float v3 = x - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            Low = v2; Band = v1; High = x - _k * v1 - v2;
            if (float.IsNaN(_ic1) || float.IsNaN(_ic2)) { _ic1 = _ic2 = 0f; Low = Band = High = 0f; }
        }

        public void Reset() { _ic1 = _ic2 = 0f; }
    }

    /// <summary>Exponential parameter smoother so gains never jump (no zipper noise, no clicks).</summary>
    public struct Smoother
    {
        public float Value;
        float _coef;
        public void SetTime(float seconds, float sr) => _coef = seconds <= 0f ? 1f : 1f - (float)Math.Exp(-1.0 / (seconds * sr));
        public float Next(float target) { Value += _coef * (target - Value); return Value; }
    }

    /// <summary>Small stereo Schroeder/Freeverb-style reverb: 4 combs + 2 allpasses per side.</summary>
    public sealed class Reverb
    {
        readonly float[][] _comb = new float[8][];
        readonly int[] _ci = new int[8];
        readonly float[] _cf = new float[8];
        readonly float[][] _ap = new float[4][];
        readonly int[] _ai = new int[4];
        public float Feedback = 0.84f, Damp = 0.3f;

        public Reverb(int sampleRate)
        {
            float s = sampleRate / 44100f;
            int[] combs = { 1116, 1277, 1422, 1557 };
            int[] aps = { 556, 341 };
            for (int i = 0; i < 4; i++)
            {
                _comb[i] = new float[(int)(combs[i] * s)];
                _comb[i + 4] = new float[(int)((combs[i] + 23) * s)];
            }
            for (int i = 0; i < 2; i++)
            {
                _ap[i] = new float[(int)(aps[i] * s)];
                _ap[i + 2] = new float[(int)((aps[i] + 23) * s)];
            }
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float l = 0f, r = 0f;
            for (int i = 0; i < 4; i++) { l += Comb(i, inL); r += Comb(i + 4, inR); }
            l = Allpass(0, l); l = Allpass(1, l);
            r = Allpass(2, r); r = Allpass(3, r);
            outL = l * 0.25f; outR = r * 0.25f;
        }

        float Comb(int i, float x)
        {
            var buf = _comb[i];
            int p = _ci[i];
            float y = buf[p];
            _cf[i] = y * (1f - Damp) + _cf[i] * Damp;
            buf[p] = x + _cf[i] * Feedback;
            if (++p >= buf.Length) p = 0;
            _ci[i] = p;
            return y;
        }

        float Allpass(int i, float x)
        {
            var buf = _ap[i];
            int p = _ai[i];
            float b = buf[p];
            buf[p] = x + b * 0.5f;
            if (++p >= buf.Length) p = 0;
            _ai[i] = p;
            return b - x;
        }
    }
}

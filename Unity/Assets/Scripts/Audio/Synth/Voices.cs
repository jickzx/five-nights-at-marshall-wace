// Voice types used by ScoreEngine. All preallocated; nothing here allocates once constructed.
using System;

namespace Fnmw.Audio.Synth
{
    public enum VoiceKind { Koto, Flute, Guitar, Glass, Warm, Bell }
    public enum PadKind { Warm, Choir, Luminous }

    /// <summary>Generic pitched/noisy percussion: thumps, heartbeats, kicks, ticks, droplets.</summary>
    sealed class Perc
    {
        public bool On;
        public int Delay;
        float _t, _cur, _f1, _sweepK, _ph, _env, _decK, _att, _tone, _noise, _gain;
        public float Pan, Send;
        Svf _f;
        float _sr;

        public void Start(float sr, int delay, float f0, float f1, float sweepPerSec, float att, float dec,
            float tone, float noise, float noiseHz, float noiseQ, float gain, float pan, float send)
        {
            On = true; Delay = delay; _sr = sr; _t = 0f; _cur = f0; _f1 = f1;
            _sweepK = (float)Math.Exp(-sweepPerSec / sr);
            _ph = 0f; _env = 0f; _att = Math.Max(att, 0.0005f);
            _decK = (float)Math.Exp(-4.0 / (Math.Max(dec, 0.002f) * sr));
            _tone = tone; _noise = noise; _gain = gain; Pan = pan; Send = send;
            _f.Reset(); _f.Set(noiseHz, noiseQ, sr);
        }

        public float Next(ref Rng rng)
        {
            _t += 1f / _sr;
            if (_t < _att) _env = _t / _att; else _env *= _decK;
            _cur = _f1 + (_cur - _f1) * _sweepK;
            _ph += _cur / _sr; if (_ph >= 1f) _ph -= 1f;
            float s = _tone != 0f ? Dsp.Sin(_ph) * _tone : 0f;
            if (_noise != 0f) { _f.Process(rng.Bi()); s += _f.Band * _noise; }
            if (_t > _att && _env < 1e-4f) On = false;
            return s * _env * _gain;
        }
    }

    /// <summary>Melodic voice for word motifs and bells: Karplus-Strong plucks, flute, FM glass, warm sine.</summary>
    sealed class Voice
    {
        public bool On;
        public int Delay;
        public long Age;
        public VoiceKind Kind;
        public float Pan, Send;
        float _t, _dur, _rel, _hz, _bend, _vel, _ph, _ph2, _sr, _fmIdx;
        readonly float[] _ks;
        int _ksLen, _ksIdx;
        float _ksDecay, _apC, _apX, _apY;

        public Voice(int sr) { _ks = new float[sr / 30 + 4]; }

        public void Start(float sr, int delay, VoiceKind kind, float hz, float dur, float vel, float bendSemis, float pan, float send, ref Rng rng, long age)
        {
            On = true; Delay = delay; Kind = kind; _sr = sr; _t = 0f; _dur = Math.Max(dur, 0.03f);
            _hz = hz; _vel = vel; _bend = bendSemis; Pan = pan; Send = send; _ph = 0f; _ph2 = 0f; Age = age;
            switch (kind)
            {
                case VoiceKind.Koto: _rel = 0.25f; StartKs(hz, 0.9965f, 0.35f, ref rng); break;
                case VoiceKind.Guitar: _rel = 0.2f; StartKs(hz, 0.9982f, 0.55f, ref rng); break;
                case VoiceKind.Flute: _rel = 0.25f; break;
                case VoiceKind.Glass: _rel = 0.9f; _fmIdx = 2.2f; break;
                case VoiceKind.Bell: _rel = 3.5f; _fmIdx = 1.4f; break;
                default: _rel = 0.9f; break;
            }
        }

        void StartKs(float hz, float decay, float bright, ref Rng rng)
        {
            float len = _sr / Math.Max(hz, 40f);
            int n = (int)Math.Floor(len - 0.6f);
            n = Math.Max(2, Math.Min(n, _ks.Length - 1));
            float d = len - 0.5f - n;
            _apC = (1f - d) / (1f + d);
            _apX = _apY = 0f;
            _ksLen = n; _ksIdx = 0; _ksDecay = decay;
            // Excitation: low-passed noise burst, brighter for the guitar.
            float lp = 0f, a = bright;
            float mean = 0f;
            for (int i = 0; i < n; i++) { lp += a * (rng.Bi() - lp); _ks[i] = lp; mean += lp; }
            mean /= n;
            for (int i = 0; i < n; i++) _ks[i] -= mean; // no DC in the string
        }

        public float Next()
        {
            float dt = 1f / _sr;
            _t += dt;
            float s;
            float bend = _bend != 0f ? (float)Math.Pow(2.0, _bend * Math.Min(_t / _dur, 1f) / 12f) : 1f;
            float hz = _hz * bend;
            switch (Kind)
            {
                case VoiceKind.Koto:
                case VoiceKind.Guitar:
                {
                    int next = _ksIdx + 1; if (next >= _ksLen) next = 0;
                    float y0 = _ks[_ksIdx];
                    float avg = 0.5f * (y0 + _ks[next]);
                    float ap = _apC * avg + _apX - _apC * _apY;
                    _apX = avg; _apY = ap;
                    _ks[_ksIdx] = ap * _ksDecay;
                    _ksIdx = next;
                    s = y0 * 1.6f;
                    float relEnv = _t < _dur ? 1f : 1f - (_t - _dur) / _rel;
                    s *= Math.Max(relEnv, 0f) * Dsp.SmoothStep(_t / 0.005f); // 5 ms: a pluck, not a click
                    break;
                }
                case VoiceKind.Flute:
                {
                    float vib = 1f + 0.004f * Dsp.Sin(_t * 5.2f) * Dsp.Clamp01((_t - 0.12f) * 4f);
                    _ph += hz * vib * dt; if (_ph >= 1f) _ph -= 1f;
                    s = Dsp.Sin(_ph) + 0.28f * Dsp.Sin(_ph * 2f) + 0.08f * Dsp.Sin(_ph * 3f);
                    s *= Dsp.AHR(_t, 0.05f, _dur - 0.05f, _rel);
                    break;
                }
                case VoiceKind.Glass:
                case VoiceKind.Bell:
                {
                    float ratio = Kind == VoiceKind.Glass ? 3.5f : 2.0f;
                    _ph2 += hz * ratio * dt; if (_ph2 >= 1f) _ph2 -= 1f;
                    float idx = _fmIdx * (float)Math.Exp(-_t * (Kind == VoiceKind.Glass ? 5f : 1.2f));
                    _ph += hz * dt; if (_ph >= 1f) _ph -= 1f;
                    s = Dsp.Sin(_ph + idx * Dsp.Sin(_ph2) * 0.16f);
                    float tail = Kind == VoiceKind.Glass ? 0.9f : 4f;
                    s *= Dsp.Clamp01(_t / 0.003f) * (float)Math.Exp(-_t / tail * 1.5f) * (_t < _dur ? 1f : Math.Max(0f, 1f - (_t - _dur) / _rel));
                    break;
                }
                default: // Warm
                {
                    _ph += hz * dt; if (_ph >= 1f) _ph -= 1f;
                    _ph2 += hz * 1.0017f * dt; if (_ph2 >= 1f) _ph2 -= 1f;
                    s = 0.6f * (Dsp.Sin(_ph) + Dsp.Sin(_ph2)) + 0.18f * Dsp.Sin(_ph * 2f) + 0.05f * Dsp.Sin(_ph * 3f);
                    s *= Dsp.AHR(_t, 0.03f, _dur - 0.03f, _rel) * 0.8f;
                    break;
                }
            }
            if (_t > _dur + _rel) On = false;
            return s * _vel;
        }
    }

    /// <summary>Sustained chord voice: warm pad, formant choir, luminous wide supersaw. Can glide.</summary>
    sealed class Pad
    {
        public bool On;
        public int Delay;
        public PadKind Kind;
        public float Pan, Send;
        float _hz, _t, _att, _hold, _rel, _gain, _sr, _vowelPh;
        readonly float[] _ph = new float[3];
        Svf _f1, _f2, _f3;
        static readonly float[] Detune = { -0.0045f, 0f, 0.0052f };

        public void Start(float sr, int delay, PadKind kind, float hz, float att, float hold, float rel, float gain, float pan, float send, ref Rng rng)
        {
            On = true; Delay = delay; Kind = kind; _sr = sr; _hz = hz; _t = 0f;
            _att = Math.Max(att, 0.01f); _hold = Math.Max(hold, 0f); _rel = Math.Max(rel, 0.05f);
            _gain = gain; Pan = pan; Send = send;
            for (int i = 0; i < 3; i++) _ph[i] = rng.Next01();
            _vowelPh = rng.Next01();
            _f1.Reset(); _f2.Reset(); _f3.Reset();
            if (kind == PadKind.Warm) _f1.Set(Math.Min(900f, hz * 5f), 0.7f, sr);
            if (kind == PadKind.Luminous) _f1.Set(Math.Min(4200f, hz * 7f), 0.6f, sr);
        }

        public float Next()
        {
            float dt = 1f / _sr;
            _t += dt;
            float env = Dsp.AHR(_t, _att, _hold, _rel);
            if (_t > _att + _hold + _rel) { On = false; return 0f; }
            float vib = Kind == PadKind.Choir ? 1f + 0.0045f * Dsp.Sin(_t * 5.1f + _vowelPh) : 1f;
            float s = 0f;
            for (int i = 0; i < 3; i++)
            {
                float f = _hz * (1f + Detune[i] * (Kind == PadKind.Luminous ? 1.8f : 1f)) * vib;
                float d = f * dt;
                _ph[i] += d; if (_ph[i] >= 1f) _ph[i] -= 1f;
                s += Dsp.Saw(_ph[i], d);
            }
            s *= 0.33f;
            switch (Kind)
            {
                case PadKind.Choir:
                {
                    // Morph slowly between "ah" and "oh" formants: a wordless choir.
                    float m = 0.5f + 0.5f * Dsp.Sin(_t * 0.07f + _vowelPh);
                    _f1.Set(Dsp.Lerp(750f, 450f, m), 6f, _sr);
                    _f2.Set(Dsp.Lerp(1150f, 800f, m), 7f, _sr);
                    _f3.Set(2600f, 8f, _sr);
                    _f1.Process(s); _f2.Process(s); _f3.Process(s);
                    s = (_f1.Band + 0.6f * _f2.Band + 0.15f * _f3.Band) * 1.6f;
                    break;
                }
                case PadKind.Luminous:
                    _f1.Process(s);
                    s = _f1.Low + 0.25f * Dsp.Sin(_ph[1] * 2f);
                    break;
                default:
                    _f1.Process(s);
                    s = _f1.Low;
                    break;
            }
            return s * env * _gain;
        }
    }

    /// <summary>Plays a prerendered mono buffer (stingers), with an optional rate change.</summary>
    sealed class SamplePlayer
    {
        public bool On;
        public int Delay;
        public float Pan, Send;
        float[] _buf;
        double _pos;
        float _rate, _gain;

        public void Start(float[] buf, int delay, float gain, float pan, float send, float rate)
        {
            _buf = buf; Delay = delay; _gain = gain; Pan = pan; Send = send; _rate = rate; _pos = 0; On = buf != null && buf.Length > 1;
        }

        public float Next()
        {
            int i = (int)_pos;
            if (i >= _buf.Length - 1) { On = false; return 0f; }
            float fr = (float)(_pos - i);
            float s = _buf[i] + (_buf[i + 1] - _buf[i]) * fr;
            _pos += _rate;
            return s * _gain;
        }
    }
}

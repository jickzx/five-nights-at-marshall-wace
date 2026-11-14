// Offline-rendered sound effects: every one is synthesised from scratch into a mono float buffer,
// deterministically (same name + variant = same samples), peak-normalised below full scale.
using System;
using System.Collections.Generic;

namespace Fnmw.Audio.Synth
{
    public static class SfxLibrary
    {
        public const float PeakTarget = 0.89f; // -1 dBFS

        /// <summary>Every renderable effect name. Variants are selected with the variant argument.</summary>
        public static readonly string[] Names =
        {
            "wrong", "correct", "beast_lunge", "stage_up_2", "stage_up_3",
            "beast_vocal_1", "beast_vocal_2", "beast_vocal_3", "beast_roar",
            "footstep", "beast_step", "console_on", "console_hum", "static", "ship_power", "begin",
        };

        /// <summary>How many distinct takes exist for a name (round-robin to avoid machine-gun repeats).</summary>
        public static int Variants(string name)
        {
            switch (name)
            {
                case "footstep": return 6;
                case "beast_step": return 3;
                case "beast_vocal_1": case "beast_vocal_2": case "beast_vocal_3": return 3;
                case "static": return 3;
                default: return 1;
            }
        }

        /// <summary>True for loops that must start and end on the same sample value.</summary>
        public static bool IsLoop(string name) => name == "console_hum";

        public static float[] Render(string name, int sr, int variant = 0)
        {
            var rng = new Rng(MotifGenerator.StableHash(name) + variant * 7919);
            float[] b;
            switch (name)
            {
                case "wrong": b = Wrong(sr, ref rng); break;
                case "correct": b = Correct(sr); break;
                case "beast_lunge": b = Lunge(sr, ref rng); break;
                case "stage_up_2": b = StageUp2(sr, ref rng); break;
                case "stage_up_3": b = StageUp3(sr, ref rng); break;
                case "beast_vocal_1": b = Chitter(sr, ref rng); break;
                case "beast_vocal_2": b = Growl(sr, ref rng, 1.4f, 0f); break;
                case "beast_vocal_3": b = Shriek(sr, ref rng); break;
                case "beast_roar": b = Growl(sr, ref rng, 2.2f, 0.6f); break;
                case "footstep": b = Footstep(sr, ref rng, false); break;
                case "beast_step": b = Footstep(sr, ref rng, true); break;
                case "console_on": b = ConsoleOn(sr, ref rng); break;
                case "console_hum": b = ConsoleHum(sr, ref rng); break;
                case "static": b = Static(sr, ref rng); break;
                case "ship_power": b = ShipPower(sr, ref rng); break;
                case "begin": b = Begin(sr, ref rng); break;
                default: return null;
            }
            if (!IsLoop(name)) FadeEdges(b, sr);
            Normalize(b, IsLoop(name) ? 0.5f : PeakTarget);
            return b;
        }

        // ---------- helpers ----------

        static float[] Buf(int sr, float seconds) => new float[(int)(sr * seconds)];

        public static void Normalize(float[] b, float peak)
        {
            float m = 0f;
            for (int i = 0; i < b.Length; i++) { float a = Math.Abs(b[i]); if (a > m) m = a; }
            if (m < 1e-6f) return;
            float g = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] *= g;
        }

        /// <summary>2 ms fade-in and 10 ms fade-out so no effect starts or stops on a step.</summary>
        static void FadeEdges(float[] b, int sr)
        {
            int fi = Math.Min(b.Length / 2, sr / 500), fo = Math.Min(b.Length / 2, sr / 100);
            for (int i = 0; i < fi; i++) b[i] *= (float)i / fi;
            for (int i = 0; i < fo; i++) b[b.Length - 1 - i] *= (float)i / fo;
        }

        // ---------- answer feedback ----------

        // Three detuned saws a semitone apart sliding down an octave, the filter closing: a collapse.
        static float[] Wrong(int sr, ref Rng rng)
        {
            var b = Buf(sr, 1.4f);
            float[] ph = new float[6];
            var f = new Svf();
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float drop = (float)Math.Pow(2.0, -Math.Min(t, 1.1f) / 1.1f); // one octave down
                float s = 0f;
                for (int v = 0; v < 6; v++)
                {
                    float hz = Dsp.MidiToHz(50 + (v % 3) + (v >= 3 ? 0.13f : -0.13f)) * drop;
                    float dt = hz / sr;
                    ph[v] += dt; if (ph[v] >= 1f) ph[v] -= 1f;
                    s += Dsp.Saw(ph[v], dt);
                }
                // bit-crushed grit on the attack
                if (t < 0.25f) s += (float)Math.Round(rng.Bi() * 4f) / 4f * (0.25f - t) * 3f;
                f.Set(3200f * drop * drop + 120f, 1.2f, sr);
                f.Process(s);
                b[i] = f.Low * Dsp.AD(t, 0.004f, 0.55f);
            }
            return b;
        }

        // Open fifth + octave bell arpeggio: consonant, bright, short.
        static float[] Correct(int sr)
        {
            var b = Buf(sr, 1.8f);
            int[] notes = { 62, 69, 74, 78 }; // D A D F#
            float[] starts = { 0f, 0.09f, 0.18f, 0.27f };
            float[] ratios = { 1f, 2f, 3f, 4.07f };
            float[] amps = { 1f, 0.45f, 0.2f, 0.12f };
            for (int n = 0; n < notes.Length; n++)
            {
                float hz = Dsp.MidiToHz(notes[n]);
                int s0 = (int)(starts[n] * sr);
                for (int i = s0; i < b.Length; i++)
                {
                    float t = (float)(i - s0) / sr;
                    float s = 0f;
                    for (int p = 0; p < 4; p++) s += Dsp.Sin(hz * ratios[p] * t) * amps[p] * Dsp.AD(t, 0.003f, 1.1f / (1 + p));
                    b[i] += s * 0.4f;
                }
            }
            return b;
        }

        // ---------- the beast ----------

        // Whoosh (rising band-passed noise) into a snarl, then an impact.
        static float[] Lunge(int sr, ref Rng rng)
        {
            var b = Buf(sr, 1.3f);
            var wf = new Svf(); var sf = new Svf(); var lp = new OnePole(); lp.SetCutoff(180f, sr);
            float ph = 0f, am = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                wf.Set(300f + 5000f * Dsp.SmoothStep(t / 0.45f), 2.5f, sr);
                wf.Process(rng.Bi());
                float whoosh = wf.Band * Dsp.AHR(t, 0.3f, 0.1f, 0.25f);
                float hz = 95f + 60f * (float)Math.Sin(t * 9f);
                ph += hz / sr; if (ph >= 1f) ph -= 1f;
                am += 31f / sr;
                float snarl = Dsp.Saw(ph, hz / sr) * (0.6f + 0.4f * Dsp.Sin(am)) + rng.Bi() * 0.4f;
                sf.Set(900f, 3f, sr); sf.Process(snarl);
                float snarlEnv = Dsp.AHR(t - 0.35f, 0.04f, 0.35f, 0.4f);
                float thud = Dsp.Sin(60f * t - 25f * t * t) * Dsp.AD(t - 0.4f, 0.003f, 0.3f);
                b[i] = whoosh * 0.6f + sf.Band * snarlEnv * 1.4f + lp.Process(thud + rng.Bi() * Dsp.AD(t - 0.4f, 0.001f, 0.05f)) * 1.6f;
            }
            return b;
        }

        static readonly int[] BrassNotes = { 38, 39, 44, 45 };

        // Evolution to stage 2: a bending metallic scrape over a low brass-like blast.
        static float[] StageUp2(int sr, ref Rng rng)
        {
            var b = Buf(sr, 3.0f);
            float[] ph = new float[8];
            var brass = new Svf(); var scrape = new Svf();
            float[] metal = { 1f, 2.76f, 5.40f, 8.93f };
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float s = 0f;
                for (int v = 0; v < 4; v++)
                {
                    float hz = Dsp.MidiToHz(BrassNotes[v]) * (1f + 0.003f * v);
                    float dt = hz / sr; ph[v] += dt; if (ph[v] >= 1f) ph[v] -= 1f;
                    s += Dsp.Saw(ph[v], dt);
                }
                brass.Set(200f + 2200f * Dsp.AD(t, 0.35f, 1.4f), 1.4f, sr); brass.Process(s);
                float m = 0f, bend = 1f - 0.25f * Dsp.SmoothStep(t / 2.5f);
                for (int p = 0; p < 4; p++) m += Dsp.Sin(330f * bend * metal[p] * t) / (1 + p);
                scrape.Set(2500f * bend, 6f, sr); scrape.Process(rng.Bi());
                float scrapeEnv = Dsp.AHR(t, 0.6f, 1.0f, 1.2f) * (0.6f + 0.4f * Dsp.Sin(t * 13f));
                b[i] = brass.Low * Dsp.AHR(t, 0.08f, 1.4f, 1.4f) * 0.5f + (m * 0.3f + scrape.Band * 1.5f) * scrapeEnv;
            }
            return b;
        }

        // Evolution to stage 3: sub drop, ring-modulated shriek, distortion. The nastiest sound in the game.
        static float[] StageUp3(int sr, ref Rng rng)
        {
            var b = Buf(sr, 3.8f);
            float[] ph = new float[6];
            var hp = new OnePole(); hp.SetCutoff(400f, sr);
            float subPh = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float subHz = 25f + 70f * (float)Math.Exp(-t * 1.6f);
                subPh += subHz / sr;
                float sub = Dsp.Sin(subPh) * Dsp.AHR(t, 0.02f, 1.5f, 1.8f);
                float rise = 1f + 0.5f * Dsp.SmoothStep(t / 2.2f);
                float s = 0f;
                for (int v = 0; v < 6; v++)
                {
                    float hz = Dsp.MidiToHz(76 + v * 1.0f + (v % 2) * 0.3f) * rise * (1f + 0.012f * Dsp.Sin(t * (6.3f + v)));
                    float dt = hz / sr; ph[v] += dt; if (ph[v] >= 1f) ph[v] -= 1f;
                    s += Dsp.Saw(ph[v], dt);
                }
                float ring = s * Dsp.Sin(t * 173f);
                float shriek = hp.HighPass(ring) * Dsp.AHR(t - 0.15f, 0.25f, 1.6f, 1.6f);
                float grit = Dsp.SoftClip((sub * 2.5f + shriek * 0.5f + rng.Bi() * 0.15f * Dsp.AD(t, 0.001f, 0.4f)) * 2.2f);
                b[i] = grit;
            }
            return b;
        }

        // Stage 1: insect chitter (resonant click trains) over a low purr.
        static float[] Chitter(int sr, ref Rng rng)
        {
            var b = Buf(sr, 1.1f);
            var res = new Svf(); var purr = new OnePole(); purr.SetCutoff(140f, sr);
            float rate = rng.Range(28f, 45f), next = 0f, resHz = rng.Range(1800f, 2600f);
            float burst = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                if (t >= next) { burst = 1f; next = t + 1f / rate * rng.Range(0.6f, 1.5f); }
                burst *= 0.93f;
                res.Set(resHz * (1f + 0.15f * Dsp.Sin(t * 3f)), 9f, sr);
                res.Process(burst * rng.Bi());
                float env = Dsp.AHR(t, 0.05f, 0.6f, 0.4f);
                float p = purr.Process(rng.Bi()) * (0.5f + 0.5f * Dsp.Sin(t * 27f));
                b[i] = (res.Band * 2f + p * 3f) * env;
            }
            return b;
        }

        // Stage 2 growl / roar: rough saw (amplitude modulated at ~30 Hz) through moving formants.
        static float[] Growl(int sr, ref Rng rng, float seconds, float scream)
        {
            var b = Buf(sr, seconds);
            var f1 = new Svf(); var f2 = new Svf(); var lp = new OnePole(); lp.SetCutoff(3500f, sr);
            float ph = 0f, ph2 = 0f, baseHz = rng.Range(62f, 84f), am = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float u = t / seconds;
                float hz = baseHz * (1f + 0.35f * Dsp.SmoothStep(u * 2f) - 0.25f * Dsp.SmoothStep(u * 1.5f - 0.5f)) * (1f + 0.02f * rng.Bi());
                ph += hz / sr; if (ph >= 1f) ph -= 1f;
                am += rng.Range(24f, 36f) / sr;
                float src = Dsp.Saw(ph, hz / sr) * (0.55f + 0.45f * Dsp.Sin(am)) + rng.Bi() * 0.35f;
                if (scream > 0f)
                {
                    float sh = hz * 8.5f * (1f + 0.03f * Dsp.Sin(t * 7f));
                    ph2 += sh / sr; if (ph2 >= 1f) ph2 -= 1f;
                    src += Dsp.Saw(ph2, sh / sr) * scream * Dsp.SmoothStep((u - 0.2f) * 3f);
                }
                f1.Set(Dsp.Lerp(450f, 750f, u), 4f, sr); f1.Process(src);
                f2.Set(Dsp.Lerp(1100f, 1300f, u), 5f, sr); f2.Process(src);
                b[i] = lp.Process(Dsp.SoftClip((f1.Band + f2.Band * 0.7f) * 1.8f)) * Dsp.AHR(t, 0.12f, seconds * 0.5f, seconds * 0.4f);
            }
            return b;
        }

        // Stage 3 vocalisation: a growl that tears up into a vibrato shriek.
        static float[] Shriek(int sr, ref Rng rng)
        {
            var g = Growl(sr, ref rng, 1.9f, 0.9f);
            var b = Buf(sr, 1.9f);
            var bp = new Svf();
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float hz = 700f * (1f + 0.6f * Dsp.SmoothStep(t / 0.8f)) * (1f + 0.04f * Dsp.Sin(t * 8.5f));
                ph += hz / sr; if (ph >= 1f) ph -= 1f;
                bp.Set(hz * 2f, 3f, sr); bp.Process(Dsp.Saw(ph, hz / sr) + rng.Bi() * 0.3f);
                b[i] = g[i] * 0.8f + bp.Band * 0.9f * Dsp.AHR(t - 0.25f, 0.2f, 0.8f, 0.6f);
            }
            return b;
        }

        // ---------- footsteps ----------

        // Wet step: low thud + squelch (band-passed noise with a fast downward sweep) + splash tail.
        static float[] Footstep(int sr, ref Rng rng, bool beast)
        {
            var b = Buf(sr, beast ? 0.7f : 0.35f);
            var sq = new Svf(); var splash = new Svf(); var lp = new OnePole();
            lp.SetCutoff(beast ? 120f : 260f, sr);
            float thudHz = beast ? rng.Range(38f, 48f) : rng.Range(70f, 95f);
            float sqHz = rng.Range(700f, 1400f) * (beast ? 0.6f : 1f);
            float delay = rng.Range(0.01f, 0.04f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float thud = Dsp.Sin(thudHz * t - thudHz * 0.8f * t * t) * Dsp.AD(t, 0.002f, beast ? 0.25f : 0.08f);
                sq.Set(sqHz * (1f - 0.6f * Dsp.Clamp01((t - delay) / 0.08f)), 4f, sr);
                sq.Process(rng.Bi());
                float squelch = sq.Band * Dsp.AD(t - delay, 0.004f, beast ? 0.12f : 0.06f);
                splash.Set(4500f, 0.8f, sr); splash.Process(rng.Bi());
                float tail = splash.High * Dsp.AD(t - delay * 1.5f, 0.002f, 0.07f) * 0.25f;
                b[i] = lp.Process(thud * 2.5f) * (beast ? 2.5f : 1.2f) + squelch * 1.2f + tail;
            }
            return b;
        }

        // ---------- consoles and the ship ----------

        static float[] ConsoleOn(int sr, ref Rng rng)
        {
            var b = Buf(sr, 1.0f);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float hz = 220f * (float)Math.Pow(2.0, Dsp.SmoothStep(t / 0.35f) * 2.0);
                ph += hz / sr;
                float chirp = (Dsp.Sin(ph) + 0.3f * Dsp.Sin(ph * 2f)) * Dsp.AHR(t, 0.01f, 0.3f, 0.2f);
                float hum = (Dsp.Sin(50f * t) + 0.5f * Dsp.Sin(100f * t) + 0.25f * Dsp.Sin(150f * t)) * Dsp.AHR(t - 0.2f, 0.3f, 0.2f, 0.3f);
                float crackle = rng.Chance(0.01f * Dsp.AD(t, 0.001f, 0.3f)) ? rng.Bi() : 0f;
                b[i] = chirp * 0.5f + hum * 0.4f + crackle * 0.8f;
            }
            return b;
        }

        // Seamless 2 s loop: every partial completes a whole number of cycles in the loop.
        static float[] ConsoleHum(int sr, ref Rng rng)
        {
            float seconds = 2f;
            var b = Buf(sr, seconds);
            int[] harm = { 50, 100, 150, 200, 250, 350 };
            float[] amp = { 1f, 0.7f, 0.35f, 0.3f, 0.12f, 0.08f };
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float s = 0f;
                for (int h = 0; h < harm.Length; h++) s += Dsp.Sin(harm[h] * t) * amp[h];
                s *= 1f + 0.1f * Dsp.Sin(t * 1.5f); // 3 wobbles per loop, also periodic
                b[i] = s;
            }
            return b;
        }

        static float[] Static(int sr, ref Rng rng)
        {
            var b = Buf(sr, 0.7f);
            var bp = new Svf();
            float gate = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                if (rng.Chance(0.004f)) gate = rng.Chance(0.6f) ? 1f : 0f;
                bp.Set(2500f + 1500f * Dsp.Sin(t * 5f), 0.9f, sr);
                bp.Process(rng.Bi());
                float crack = rng.Chance(0.002f) ? rng.Bi() * 2f : 0f;
                b[i] = (bp.Band * gate + crack) * Dsp.AHR(t, 0.01f, 0.45f, 0.2f);
            }
            return b;
        }

        // Capacitors charging: an exponential whine over a turbine rumble, with relay clunks.
        static float[] ShipPower(int sr, ref Rng rng)
        {
            var b = Buf(sr, 3.2f);
            var rumble = new OnePole(); rumble.SetCutoff(90f, sr);
            var turbine = new Svf();
            float ph = 0f;
            float[] clunks = { 0.0f, 0.7f, 1.3f, 1.8f };
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float u = Dsp.Clamp01(t / 2.6f);
                float hz = 180f * (float)Math.Pow(2.0, u * 3.6);
                ph += hz / sr;
                float whine = (Dsp.Sin(ph) + 0.2f * Dsp.Sin(ph * 3f)) * (0.2f + 0.8f * u) * Dsp.AHR(t, 0.3f, 2.4f, 0.4f);
                turbine.Set(400f + 3000f * u, 2f, sr); turbine.Process(rng.Bi());
                float rum = rumble.Process(rng.Bi()) * 6f * Dsp.AHR(t, 0.4f, 2.2f, 0.6f);
                float cl = 0f;
                foreach (float c in clunks) cl += Dsp.Sin(70f * (t - c)) * Dsp.AD(t - c, 0.002f, 0.12f);
                b[i] = whine * 0.35f + turbine.Band * 0.25f * u + rum + cl * 0.6f;
            }
            return b;
        }

        // A deep, distant boom: the planet noticing you.
        static float[] Begin(int sr, ref Rng rng)
        {
            var b = Buf(sr, 3.5f);
            var lp = new OnePole(); lp.SetCutoff(200f, sr);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / sr;
                float hz = 28f + 30f * (float)Math.Exp(-t * 2f);
                ph += hz / sr;
                b[i] = (Dsp.Sin(ph) * 1.2f + lp.Process(rng.Bi()) * 2.5f) * Dsp.AD(t, 0.05f, 2.2f);
            }
            return b;
        }

        /// <summary>Peak absolute value, used by tests and the render harness.</summary>
        public static float Peak(float[] b)
        {
            float m = 0f;
            foreach (float x in b) { float a = Math.Abs(x); if (a > m) m = a; }
            return m;
        }

        public static IEnumerable<(string name, int variant)> All()
        {
            foreach (var n in Names)
                for (int v = 0; v < Variants(n); v++) yield return (n, v);
        }
    }
}

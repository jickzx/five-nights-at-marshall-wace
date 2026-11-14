// The two endings, written as timelines inside the score engine.
// Death: the panic cuts to near silence, a failing heartbeat, then a slow wordless choir with a funeral bell,
//        ending on a Picardy third (D major) like a martyr's transfiguration.
// Escape: the string cluster glides into a D major chord at lift-off, the rain falls away, and a wide,
//         luminous progression rises with bell arpeggios.
using System;

namespace Fnmw.Audio.Synth
{
    public sealed partial class ScoreEngine
    {
        float[] _shipPower;

        struct Cue { public float At; public int[] Chord; public float Hold; }

        // Death: chords in D minor, each held then crossfaded; the last is D major.
        static readonly Cue[] DeathCues =
        {
            new Cue { At = 3.0f,  Chord = new[] { 38, 50, 53, 57, 62 }, Hold = 5.5f },  // Dm
            new Cue { At = 9.5f,  Chord = new[] { 34, 46, 50, 53, 58 }, Hold = 5.0f },  // Bb
            new Cue { At = 15.5f, Chord = new[] { 31, 46, 50, 55, 62 }, Hold = 5.0f },  // Gm/Bb colour
            new Cue { At = 21.5f, Chord = new[] { 33, 45, 50, 52, 57 }, Hold = 2.8f },  // Asus4
            new Cue { At = 25.0f, Chord = new[] { 33, 45, 49, 52, 57 }, Hold = 3.0f },  // A
            new Cue { At = 29.0f, Chord = new[] { 38, 45, 50, 54, 62 }, Hold = 6.0f },  // D major (Picardy)
        };

        // Escape: Dmaj9, Bm9, Gmaj9#11, Asus4, A, then a long D6/9.
        static readonly Cue[] EscapeCues =
        {
            new Cue { At = 2.2f,  Chord = new[] { 38, 45, 54, 61, 64, 69 }, Hold = 3.4f },
            new Cue { At = 6.2f,  Chord = new[] { 35, 42, 50, 57, 61, 66 }, Hold = 3.4f },
            new Cue { At = 10.2f, Chord = new[] { 31, 38, 47, 54, 57, 61 }, Hold = 3.4f },
            new Cue { At = 14.2f, Chord = new[] { 33, 40, 50, 52, 57, 59 }, Hold = 1.6f },
            new Cue { At = 16.2f, Chord = new[] { 33, 40, 49, 52, 57, 61 }, Hold = 1.6f },
            new Cue { At = 18.2f, Chord = new[] { 38, 45, 54, 59, 64, 69, 74 }, Hold = 11f },
        };

        static readonly float[] DeathHeartbeats = { 0.8f, 2.0f, 3.5f, 5.6f };
        public const float DeathLength = 46f, EscapeLength = 42f;

        void BeginEnding(Mood ending, float[] shipPower)
        {
            if (_endT >= 0f) return; // endings are one-shot
            _endT = 0f;
            _mood = ending;
            _shipPower = shipPower;
            if (ending == Mood.Death)
            {
                // The cut: 30 ms is fast enough to read as a cut but long enough to avoid a click.
                for (int i = 0; i < LayerCount; i++) _gain[i].SetTime(0.03f, _sr);
                _gain[(int)L.Ambience].SetTime(1.5f, _sr);
                foreach (var v in _voices) v.On = false;
                foreach (var p in _perc) if (p.Delay > 0) p.On = false;
                _noteCount = 0;
            }
            else
            {
                for (int i = 0; i < LayerCount; i++) _gain[i].SetTime(1.4f, _sr);
                _gain[(int)L.Ambience].SetTime(2.5f, _sr); // rain falls away over ~7 s
                _gain[(int)L.Cluster].SetTime(3.0f, _sr);
                // Literal resolution: the dissonant strings slide onto D major.
                int[] dMajor = { 50, 54, 57, 62, 66 };
                for (int i = 0; i < 5; i++) _clTarget[i] = Dsp.MidiToHz(dMajor[i]);
                _clGlide = 1.6f;
                if (shipPower != null) StartSample(shipPower, 0.8f, 0f, 0, 0.2f, 1f);
            }
        }

        void EndingTimeline(float dt)
        {
            float prev = _endT;
            _endT += dt;
            float t = _endT;
            bool Crossed(float at) => prev < at && t >= at;

            if (_mood == Mood.Death)
            {
                foreach (float hb in DeathHeartbeats)
                    if (Crossed(hb)) Heartbeat(0f, 0.18f * (1f - hb / 7f), 0.22f);
                foreach (var c in DeathCues)
                {
                    if (!Crossed(c.At)) continue;
                    bool last = c.Chord == DeathCues[DeathCues.Length - 1].Chord;
                    float rel = last ? 11f : 3.5f;
                    float att = c.At < 4f ? 5f : 2.5f;
                    for (int i = 0; i < c.Chord.Length; i++)
                    {
                        float pan = Dsp.Lerp(-0.5f, 0.5f, i / (c.Chord.Length - 1f));
                        StartPad(i == 0 ? PadKind.Warm : PadKind.Choir, Dsp.MidiToHz(c.Chord[i]), att, c.Hold, rel,
                            i == 0 ? 0.06f : 0.028f, pan, 0, 0.75f);
                    }
                    // a funeral bell at each change
                    StartVoice(VoiceKind.Bell, Dsp.MidiToHz(c.Chord[0] + 12), 2f, 0.32f, 0f, 0f, 0.7f, 0);
                }
                // keep the near silence: rain only, very low
                _gainTarget[(int)L.Ambience] = t < 3f ? 0.05f : 0.1f;
            }
            else
            {
                foreach (var c in EscapeCues)
                {
                    if (!Crossed(c.At)) continue;
                    bool last = c.Chord == EscapeCues[EscapeCues.Length - 1].Chord;
                    float att = c.At < 3f ? 1.8f : 0.9f;
                    for (int i = 0; i < c.Chord.Length; i++)
                    {
                        float pan = Dsp.Lerp(-0.85f, 0.85f, i / (c.Chord.Length - 1f)) * (i % 2 == 0 ? 1f : -1f);
                        StartPad(PadKind.Luminous, Dsp.MidiToHz(c.Chord[i]), att, c.Hold, last ? 10f : 1.4f, 0.16f, pan, 0, 0.6f);
                    }
                }
                // Bell arpeggio through the chord tones, two octaves up, 8ths at 84 bpm.
                if (t > 2.2f && t < 28f)
                {
                    float eighth = 60f / 84f / 2f;
                    int a = (int)Math.Floor((prev - 2.2f) / eighth), b = (int)Math.Floor((t - 2.2f) / eighth);
                    if (b > a && b >= 0)
                    {
                        var chord = CurrentCue(EscapeCues, t).Chord;
                        int[] order = { 2, 3, 4, 5, 4, 3, 5, 4 };
                        int note = chord[Math.Min(order[b % order.Length], chord.Length - 1)] + 12;
                        float fade = t > 22f ? 1f - (t - 22f) / 6f : 1f;
                        StartVoice(VoiceKind.Glass, Dsp.MidiToHz(note), 0.6f, 0.2f * fade, 0f, (b % 2 == 0 ? -0.6f : 0.6f), 0.7f, 0);
                    }
                }
                if (t > 4f) _gainTarget[(int)L.Cluster] = 0f;
                else _gainTarget[(int)L.Cluster] = 0.6f;
                // lift-off rumble hand-off: the drone holds D under the first chord, then lets go
                _gainTarget[(int)L.Drone] = t < 3f ? 0.5f : 0f;
            }
        }

        static Cue CurrentCue(Cue[] cues, float t)
        {
            var c = cues[0];
            foreach (var x in cues) if (t >= x.At) c = x;
            return c;
        }
    }
}

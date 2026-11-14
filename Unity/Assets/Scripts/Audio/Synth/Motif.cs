// The alien language as music. Each word gets a short melodic motif derived from its glyph seed,
// in a mode (and instrument) chosen by the real language the alien tongue exaggerates.
using System;
using System.Collections.Generic;

namespace Fnmw.Audio.Synth
{
    public enum Timbre { Koto, Flute, Guitar, Glass }

    /// <summary>A scale + instrument + rhythmic feel for one source language.</summary>
    public sealed class LanguageMode
    {
        public string Code, Name;
        public int[] Steps;        // semitones above the tonic, ascending, within one octave
        public Timbre Timbre;
        public int TonicMidi;      // D4 for all, so every motif sits on the score's D drone
        public float[] Rhythm;     // note lengths (in beats) the generator draws from
        public float RestChance;   // chance of a breath between notes (Japanese "ma")
        public float GraceChance;  // chance of a quick grace note before a note (flamenco)

        // In (Miyako-bushi) scale, koto pluck, sparse with silences.
        public static readonly LanguageMode Japanese = new LanguageMode
        {
            Code = "ja", Name = "Japanese", Steps = new[] { 0, 1, 5, 7, 8 }, Timbre = Timbre.Koto, TonicMidi = 62,
            Rhythm = new[] { 1f, 0.5f, 1.5f, 0.5f }, RestChance = 0.25f, GraceChance = 0f,
        };

        // Whole-tone scale (Debussy), breathy glass flute, flowing even notes.
        public static readonly LanguageMode French = new LanguageMode
        {
            Code = "fr", Name = "French", Steps = new[] { 0, 2, 4, 6, 8, 10 }, Timbre = Timbre.Flute, TonicMidi = 62,
            Rhythm = new[] { 0.5f, 0.5f, 0.75f, 0.25f }, RestChance = 0.05f, GraceChance = 0f,
        };

        // Phrygian dominant (flamenco), bright guitar pluck, quick ornaments.
        public static readonly LanguageMode Spanish = new LanguageMode
        {
            Code = "es", Name = "Spanish", Steps = new[] { 0, 1, 4, 5, 7, 8, 10 }, Timbre = Timbre.Guitar, TonicMidi = 62,
            Rhythm = new[] { 0.5f, 0.25f, 0.25f, 0.75f }, RestChance = 0.05f, GraceChance = 0.35f,
        };

        // Anything else: Locrian on glass, the most unstable mode.
        public static readonly LanguageMode Alien = new LanguageMode
        {
            Code = "xx", Name = "Alien", Steps = new[] { 0, 1, 3, 5, 6, 8, 10 }, Timbre = Timbre.Glass, TonicMidi = 62,
            Rhythm = new[] { 0.5f, 1f, 0.5f }, RestChance = 0.1f, GraceChance = 0.1f,
        };

        public static LanguageMode ForCode(string code)
        {
            switch ((code ?? "").Trim().ToLowerInvariant())
            {
                case "ja": case "jp": case "japanese": return Japanese;
                case "fr": case "french": return French;
                case "es": case "spanish": return Spanish;
                default: return Alien;
            }
        }

        /// <summary>MIDI note for a scale degree (degrees beyond the scale wrap into higher/lower octaves).</summary>
        public int Midi(int degree)
        {
            int n = Steps.Length;
            int oct = degree >= 0 ? degree / n : -((-degree + n - 1) / n);
            int step = degree - oct * n;
            return TonicMidi + oct * 12 + Steps[step];
        }

        /// <summary>A consonant chord built only from this mode's notes: root, fifth or third, octave.</summary>
        public int[] WarmChord()
        {
            var notes = new List<int> { TonicMidi - 12, TonicMidi };
            bool has3 = Array.IndexOf(Steps, 4) >= 0, has5 = Array.IndexOf(Steps, 7) >= 0, hasM3 = Array.IndexOf(Steps, 3) >= 0;
            if (has5) notes.Add(TonicMidi - 5);            // fifth below the tonic
            if (has3) notes.Add(TonicMidi + 4);
            else if (hasM3) notes.Add(TonicMidi + 3);
            if (has5) notes.Add(TonicMidi + 7);
            if (!has5 && Array.IndexOf(Steps, 2) >= 0) notes.Add(TonicMidi + 14); // add9 when there is no fifth
            notes.Add(TonicMidi + 12);
            return notes.ToArray();
        }
    }

    public struct MotifNote
    {
        public int Midi;
        public float Start;     // beats from motif start
        public float Length;    // beats
        public float Velocity;  // 0..1
    }

    public sealed class Motif
    {
        public string WordId;
        public LanguageMode Mode;
        public MotifNote[] Notes;
        public float LengthBeats;
    }

    public static class MotifGenerator
    {
        public const int MinMidi = 48, MaxMidi = 96;

        /// <summary>FNV-1a, stable across runtimes (string.GetHashCode is not).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
                return (int)h;
            }
        }

        /// <summary>Deterministic: same (glyphSeed, wordId, language) always yields the same motif.</summary>
        public static Motif Generate(int glyphSeed, string wordId, string languageCode)
        {
            var mode = LanguageMode.ForCode(languageCode);
            var rng = new Rng(unchecked(glyphSeed * 31 + StableHash(wordId) * 17 + StableHash(mode.Code)));
            int n = mode.Steps.Length;
            int count = 3 + rng.Range(0, 3); // 3..5 notes
            var notes = new List<MotifNote>(count + 3);
            int degree = rng.Range(0, n);    // start somewhere in the first octave
            float t = 0f;
            int[] moves = { -2, -1, 1, 1, 2, 3, -3 };
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    degree += moves[rng.Range(0, moves.Length)];
                    degree = Math.Max(-2, Math.Min(n + 4, degree));
                    if (rng.Chance(mode.RestChance)) t += 0.5f;
                }
                float len = mode.Rhythm[rng.Range(0, mode.Rhythm.Length)];
                if (i == count - 1) len = Math.Max(len, 1f); // land on a held note so words feel finished
                if (mode.GraceChance > 0f && rng.Chance(mode.GraceChance))
                {
                    notes.Add(new MotifNote { Midi = Wrap(mode.Midi(degree + 1)), Start = t, Length = 0.125f, Velocity = 0.55f });
                    t += 0.125f;
                }
                float vel = i == 0 ? 0.95f : 0.65f + 0.25f * rng.Next01();
                notes.Add(new MotifNote { Midi = Wrap(mode.Midi(degree)), Start = t, Length = len, Velocity = vel });
                t += len;
            }
            return new Motif { WordId = wordId, Mode = mode, Notes = notes.ToArray(), LengthBeats = t };
        }

        static int Wrap(int midi)
        {
            while (midi < MinMidi) midi += 12;
            while (midi > MaxMidi) midi -= 12;
            return midi;
        }
    }
}

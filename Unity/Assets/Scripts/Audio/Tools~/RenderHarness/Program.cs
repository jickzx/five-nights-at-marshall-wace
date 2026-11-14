using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fnmw.Audio.Synth;

static class P
{
    const int SR = 48000;
    static string Out;

    static void Main(string[] args)
    {
        Out = args.Length > 0 ? args[0] : "out";
        Directory.CreateDirectory(Out);
        Directory.CreateDirectory(Path.Combine(Out, "sfx"));
        var sw = Stopwatch.StartNew();
        double audioSec = 0;

        // ---- states in isolation
        foreach (var (name, mood, level, stage) in new[] {
            ("state_unsettling", Mood.Unsettling, 0.1f, 1), ("state_tense", Mood.Tense, 0.55f, 2), ("state_panic", Mood.Panic, 0.95f, 3) })
        {
            var e = new ScoreEngine(SR);
            var buf = new List<float>();
            var m = new IntensityModel();
            Run(e, buf, 30f, t => { var i = new MoodInput { Intensity = level, Stage = stage, Phase = mood == Mood.Panic ? ScorePhase.Chase : ScorePhase.Explore, BeastDistance = float.PositiveInfinity }; var md = m.Update(i); e.SetTarget(md, m.Level, m.Heat, stage); });
            audioSec += 30; Write(name, buf); Console.WriteLine($"   limiter GR avg {e.TakeAverageGainReductionDb():0.00} dB");
        }

        // ---- the whole arc ending in death
        {
            var e = new ScoreEngine(SR);
            var buf = new List<float>();
            var m = new IntensityModel();
            float total = 0f;
            Run(e, buf, 125f, t =>
            {
                ScorePhase ph; int stage; float inten;
                if (t < 4) { ph = ScorePhase.Title; stage = 1; inten = 0; }
                else if (t < 36) { ph = ScorePhase.Explore; stage = 1; inten = 0.05f + 0.25f * (t - 4) / 32f; }
                else if (t < 68) { ph = ScorePhase.Explore; stage = 2; inten = 0.4f + 0.3f * (t - 36) / 32f; }
                else if (t < 92) { ph = ScorePhase.Chase; stage = 3; inten = 0.85f + 0.15f * (t - 68) / 24f; }
                else { ph = ScorePhase.Dead; stage = 3; inten = 1; }
                var md = m.Update(new MoodInput { Intensity = inten, Stage = stage, Phase = ph, BeastDistance = float.PositiveInfinity });
                if (md == Mood.Death && total == 0f) { e.TriggerDeath(); total = t; }
                e.SetTarget(md, m.Level, m.Heat, stage);
            });
            audioSec += 125; Write("arc_death", buf);
        }

        // ---- panic into escape
        {
            var e = new ScoreEngine(SR);
            var buf = new List<float>();
            var m = new IntensityModel();
            bool fired = false;
            var ship = SfxLibrary.Render("ship_power", SR);
            Run(e, buf, 58f, t =>
            {
                var ph = t < 14 ? ScorePhase.Chase : ScorePhase.Escaped;
                var md = m.Update(new MoodInput { Intensity = 0.95f, Stage = 3, Phase = ph, BeastDistance = float.PositiveInfinity });
                if (md == Mood.Escape && !fired) { e.TriggerEscape(ship); fired = true; }
                e.SetTarget(md, m.Level, m.Heat, 3);
            });
            audioSec += 58; Write("arc_escape", buf);
        }

        // ---- the language as music
        {
            var e = new ScoreEngine(SR);
            var buf = new List<float>();
            var m = new IntensityModel();
            string[] langs = { "ja", "fr", "es" };
            var events = new List<(float t, Action a)>();
            float t0 = 1f;
            foreach (var code in langs)
            {
                var words = new[] { MotifGenerator.Generate(101, "door", code), MotifGenerator.Generate(202, "open", code), MotifGenerator.Generate(303, "ship", code) };
                foreach (var w in words) { var ww = w; events.Add((t0, () => e.PlayMotif(ww, PhraseStyle.Pick))); t0 += 1.6f; }
                var ws = words; var c0 = t0;
                events.Add((t0, () => e.PlayPhrase(ws, PhraseStyle.Wrong))); t0 += 3.5f;
                events.Add((t0, () => e.PlayPhrase(ws, PhraseStyle.Correct))); t0 += 3.5f;
                events.Add((t0, () => e.PlayPhrase(ws, PhraseStyle.Reveal))); t0 += 7f;
            }
            int idx = 0;
            events.Sort((a, b) => a.t.CompareTo(b.t));
            Run(e, buf, t0 + 2f, t =>
            {
                while (idx < events.Count && events[idx].t <= t) events[idx++].a();
                var md = m.Update(new MoodInput { Intensity = 0.1f, Stage = 1, Phase = ScorePhase.Puzzle, BeastDistance = float.PositiveInfinity });
                e.SetTarget(md, m.Level, m.Heat, 1);
            });
            audioSec += t0 + 2; Write("language_motifs", buf);
            foreach (var code in langs)
            {
                var mo = MotifGenerator.Generate(101, "door", code);
                Console.Write($"{code} door: ");
                foreach (var n in mo.Notes) Console.Write($"{n.Midi}@{n.Start} ");
                Console.WriteLine();
            }
        }

        // ---- every stinger
        foreach (var (name, v) in SfxLibrary.All())
        {
            var b = SfxLibrary.Render(name, SR, v);
            var st = new List<float>(b.Length * 2);
            foreach (var x in b) { st.Add(x); st.Add(x); }
            WriteRaw(Path.Combine(Out, "sfx", $"{name}_{v}.wav"), st);
            Console.WriteLine($"sfx {name}_{v}: {b.Length / (float)SR:0.00}s peak {SfxLibrary.Peak(b):0.000} {(HasBad(b) ? "BAD" : "ok")}");
        }

        // ---- CPU: render 60 s of panic (the densest state) and time it
        {
            var e = new ScoreEngine(SR);
            e.SetTarget(Mood.Panic, 1f, 1f, 3);
            var block = new float[1024 * 2];
            for (int i = 0; i < 20; i++) e.Render(block, 2);
            var cw = Stopwatch.StartNew();
            int blocks = SR * 60 / 1024;
            for (int i = 0; i < blocks; i++) e.Render(block, 2);
            cw.Stop();
            Console.WriteLine($"CPU panic: {cw.Elapsed.TotalMilliseconds / 60000.0 * 100:0.00}% of one core (60 s rendered in {cw.Elapsed.TotalMilliseconds:0} ms)");
        }
        Console.WriteLine($"total {audioSec:0}s audio in {sw.Elapsed.TotalSeconds:0.0}s");
    }

    static void Run(ScoreEngine e, List<float> buf, float seconds, Action<float> control)
    {
        // 1024-frame blocks like Unity; control runs per block (~47 fps, close to a game frame)
        var block = new float[1024 * 2];
        int blocks = (int)(seconds * SR / 1024);
        for (int i = 0; i < blocks; i++)
        {
            control(i * 1024f / SR);
            e.Render(block, 2);
            buf.AddRange(block);
        }
    }

    static bool HasBad(float[] b) { foreach (var x in b) if (float.IsNaN(x) || float.IsInfinity(x) || Math.Abs(x) > 1f) return true; return false; }

    static void Write(string name, List<float> buf)
    {
        var a = buf.ToArray();
        float peak = 0; double sum = 0; bool bad = false;
        foreach (var x in a) { if (float.IsNaN(x) || Math.Abs(x) > 1f) bad = true; peak = Math.Max(peak, Math.Abs(x)); sum += x * x; }
        Console.WriteLine($"{name}: {a.Length / 2 / (float)SR:0.0}s peak {peak:0.000} ({20 * Math.Log10(peak + 1e-9):0.0} dBFS) rms {10 * Math.Log10(sum / a.Length + 1e-12):0.0} dBFS {(bad ? "BAD" : "ok")}");
        WriteRaw(Path.Combine(Out, name + ".wav"), buf);
    }

    static void WriteRaw(string path, List<float> st)
    {
        using var f = new BinaryWriter(File.Create(path));
        int n = st.Count;
        f.Write("RIFF".ToCharArray()); f.Write(36 + n * 2); f.Write("WAVE".ToCharArray());
        f.Write("fmt ".ToCharArray()); f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(SR); f.Write(SR * 4); f.Write((short)4); f.Write((short)16);
        f.Write("data".ToCharArray()); f.Write(n * 2);
        foreach (var x in st) f.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767));
    }
}

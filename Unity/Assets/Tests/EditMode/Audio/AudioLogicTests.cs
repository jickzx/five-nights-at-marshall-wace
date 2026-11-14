using System;
using System.Linq;
using Fnmw.Audio.Synth;
using NUnit.Framework;

namespace Fnmw.Tests.Audio
{
    public class IntensityModelTests
    {
        static MoodInput In(float intensity, int stage = 1, ScorePhase phase = ScorePhase.Explore, float threat = 0f) =>
            new MoodInput { Intensity = intensity, Threat = threat, Stage = stage, Phase = phase, BeastDistance = float.PositiveInfinity };

        [Test]
        public void EffectiveLevel_TakesTheLoudestSignal_AndStageFloors()
        {
            Assert.AreEqual(0.2f, IntensityModel.EffectiveLevel(In(0.2f)), 1e-5);
            Assert.AreEqual(0.5f, IntensityModel.EffectiveLevel(In(0.1f, threat: 0.5f)), 1e-5);
            Assert.AreEqual(IntensityModel.Stage2Floor, IntensityModel.EffectiveLevel(In(0f, 2)), 1e-5);
            Assert.AreEqual(IntensityModel.Stage3Floor, IntensityModel.EffectiveLevel(In(0f, 3)), 1e-5);
            Assert.AreEqual(IntensityModel.ChaseFloor, IntensityModel.EffectiveLevel(In(0f, 1, ScorePhase.Chase)), 1e-5);
            Assert.AreEqual(1f, IntensityModel.EffectiveLevel(In(7f)), 1e-5, "clamped");
            var near = In(0f); near.BeastDistance = 15f;
            Assert.AreEqual(0.5f, IntensityModel.EffectiveLevel(near), 1e-5, "proximity counts even without a director");
        }

        [Test]
        public void Bands_MapZeroHalfOne_ToUnsettlingTensePanic()
        {
            Assert.AreEqual(Mood.Unsettling, IntensityModel.Band(0f));
            Assert.AreEqual(Mood.Tense, IntensityModel.Band(0.5f));
            Assert.AreEqual(Mood.Panic, IntensityModel.Band(1f));
        }

        [Test]
        public void Hysteresis_PreventsFlutterAtTheBoundary()
        {
            var m = new IntensityModel();
            Assert.AreEqual(Mood.Unsettling, m.Update(In(0.30f)));
            Assert.AreEqual(Mood.Tense, m.Update(In(0.40f)));
            Assert.AreEqual(Mood.Tense, m.Update(In(0.31f)), "just below the enter threshold stays tense");
            Assert.AreEqual(Mood.Unsettling, m.Update(In(0.20f)));
            Assert.AreEqual(Mood.Panic, m.Update(In(0.80f)));
            Assert.AreEqual(Mood.Panic, m.Update(In(0.66f)), "panic holds until it drops below the exit threshold");
            Assert.AreEqual(Mood.Tense, m.Update(In(0.50f)));
        }

        [Test]
        public void Phases_TitleChaseAndTerminalEndings()
        {
            var m = new IntensityModel();
            Assert.AreEqual(Mood.Title, m.Update(In(0.9f, 1, ScorePhase.Title)));
            Assert.AreEqual(Mood.Panic, m.Update(In(0f, 1, ScorePhase.Chase)));
            Assert.AreEqual(Mood.Death, m.Update(In(0f, 3, ScorePhase.Dead)));
            Assert.AreEqual(Mood.Death, m.Update(In(0f, 1, ScorePhase.Explore)), "endings are terminal");
            m.Reset();
            Assert.AreEqual(Mood.Escape, m.Update(In(1f, 3, ScorePhase.Escaped)));
        }

        [Test]
        public void Heat_RisesWithinTheBand()
        {
            var m = new IntensityModel();
            m.Update(In(0.4f)); float lo = m.Heat;
            m.Update(In(0.65f)); float hi = m.Heat;
            Assert.AreEqual(Mood.Tense, m.Mood);
            Assert.Greater(hi, lo);
            Assert.That(lo, Is.InRange(0f, 1f));
            Assert.That(hi, Is.InRange(0f, 1f));
        }
    }

    public class MoodSchedulerTests
    {
        [Test]
        public void Escalation_LandsOnTheNextBeat()
        {
            var s = new MoodScheduler();
            s.Request(Mood.Unsettling, 0); s.OnStep(16);
            Assert.AreEqual(Mood.Unsettling, s.Current);
            s.Request(Mood.Tense, 17);
            Assert.AreEqual(20, s.PendingAtStep);
            Assert.IsFalse(s.OnStep(18)); Assert.IsFalse(s.OnStep(19));
            Assert.IsTrue(s.OnStep(20));
            Assert.AreEqual(Mood.Tense, s.Current);
        }

        [Test]
        public void CalmingDown_WaitsForTheBar()
        {
            var s = new MoodScheduler();
            s.Request(Mood.Panic, 0); s.OnStep(4);
            s.Request(Mood.Tense, 21);
            Assert.AreEqual(32, s.PendingAtStep);
            for (long st = 22; st < 32; st++) Assert.IsFalse(s.OnStep(st));
            Assert.IsTrue(s.OnStep(32));
        }

        [Test]
        public void Endings_AreImmediate_AndFinal()
        {
            var s = new MoodScheduler();
            Assert.IsTrue(s.Request(Mood.Death, 5));
            Assert.AreEqual(Mood.Death, s.Current);
            Assert.IsFalse(s.Request(Mood.Unsettling, 6));
            Assert.AreEqual(Mood.Death, s.Current);
        }

        [Test]
        public void ChangingYourMind_CancelsThePendingSwitch()
        {
            var s = new MoodScheduler();
            s.Request(Mood.Unsettling, 0); s.OnStep(16);
            s.Request(Mood.Tense, 17);
            s.Request(Mood.Unsettling, 18);
            Assert.IsFalse(s.HasPending);
            Assert.IsFalse(s.OnStep(20));
            Assert.AreEqual(Mood.Unsettling, s.Current);
        }

        [Test]
        public void NextBoundary_IsStrictlyAfter()
        {
            Assert.AreEqual(16, MoodScheduler.NextBoundary(0, 16));
            Assert.AreEqual(16, MoodScheduler.NextBoundary(15, 16));
            Assert.AreEqual(32, MoodScheduler.NextBoundary(16, 16));
            Assert.AreEqual(8, MoodScheduler.NextBoundary(5, 4));
        }
    }

    public class MotifTests
    {
        [Test]
        public void SameWord_SameMotif()
        {
            var a = MotifGenerator.Generate(1234, "door", "ja");
            var b = MotifGenerator.Generate(1234, "door", "ja");
            CollectionAssert.AreEqual(a.Notes.Select(n => (n.Midi, n.Start, n.Length)).ToArray(), b.Notes.Select(n => (n.Midi, n.Start, n.Length)).ToArray());
        }

        [Test]
        public void DifferentWords_UsuallyDiffer()
        {
            int distinct = Enumerable.Range(0, 40)
                .Select(i => string.Join(",", MotifGenerator.Generate(i * 7919, "w" + i, "fr").Notes.Select(n => n.Midi)))
                .Distinct().Count();
            Assert.Greater(distinct, 30);
        }

        [TestCase("ja")] [TestCase("fr")] [TestCase("es")] [TestCase("fi")] [TestCase(null)]
        public void Notes_StayInTheLanguagesModeAndRange(string code)
        {
            var mode = LanguageMode.ForCode(code);
            for (int seed = 0; seed < 200; seed++)
            {
                var m = MotifGenerator.Generate(seed, "tile" + seed, code);
                Assert.That(m.Notes.Length, Is.InRange(3, 10));
                Assert.Greater(m.LengthBeats, 0f);
                foreach (var n in m.Notes)
                {
                    Assert.That(n.Midi, Is.InRange(MotifGenerator.MinMidi, MotifGenerator.MaxMidi));
                    int pc = ((n.Midi - mode.TonicMidi) % 12 + 12) % 12;
                    CollectionAssert.Contains(mode.Steps, pc, $"{code}: {n.Midi} is outside {mode.Name}");
                    Assert.Greater(n.Length, 0f);
                    Assert.That(n.Velocity, Is.InRange(0f, 1f));
                }
            }
        }

        [Test]
        public void Languages_HaveDistinctModesAndInstruments()
        {
            var ja = LanguageMode.ForCode("ja"); var fr = LanguageMode.ForCode("fr"); var es = LanguageMode.ForCode("es");
            Assert.AreNotEqual(ja.Timbre, fr.Timbre); Assert.AreNotEqual(fr.Timbre, es.Timbre); Assert.AreNotEqual(ja.Timbre, es.Timbre);
            CollectionAssert.AreNotEqual(ja.Steps, fr.Steps); CollectionAssert.AreNotEqual(fr.Steps, es.Steps);
            Assert.AreNotEqual(string.Join(",", MotifGenerator.Generate(5, "door", "ja").Notes.Select(n => n.Midi)),
                               string.Join(",", MotifGenerator.Generate(5, "door", "es").Notes.Select(n => n.Midi)));
        }

        [Test]
        public void WarmChord_IsBuiltFromTheMode()
        {
            foreach (var code in new[] { "ja", "fr", "es", "xx" })
            {
                var mode = LanguageMode.ForCode(code);
                foreach (int midi in mode.WarmChord())
                {
                    int pc = ((midi - mode.TonicMidi) % 12 + 12) % 12;
                    Assert.IsTrue(Array.IndexOf(mode.Steps, pc) >= 0 || pc == 7, $"{code} chord note {midi}");
                }
            }
        }

        [Test]
        public void StableHash_IsStable()
        {
            Assert.AreEqual(MotifGenerator.StableHash("door"), MotifGenerator.StableHash("door"));
            Assert.AreEqual(unchecked((int)2166136261u), MotifGenerator.StableHash(""));
        }
    }

    public class BufferRangeTests
    {
        const int SR = 48000;

        static void AssertClean(float[] b, float ceiling, string what)
        {
            Assert.IsNotNull(b, what);
            for (int i = 0; i < b.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(b[i]) || float.IsInfinity(b[i]), $"{what}: bad sample at {i}");
                Assert.LessOrEqual(Math.Abs(b[i]), ceiling, $"{what}: sample {i} = {b[i]}");
            }
        }

        [Test]
        public void EveryStinger_RendersWithinRange()
        {
            foreach (var (name, v) in SfxLibrary.All())
            {
                var b = SfxLibrary.Render(name, SR, v);
                AssertClean(b, SfxLibrary.PeakTarget + 1e-4f, name);
                Assert.Greater(SfxLibrary.Peak(b), 0.1f, $"{name} is silent");
                if (!SfxLibrary.IsLoop(name))
                {
                    Assert.Less(Math.Abs(b[0]), 0.01f, $"{name} starts with a click");
                    Assert.Less(Math.Abs(b[b.Length - 1]), 0.01f, $"{name} ends with a click");
                }
            }
        }

        [Test]
        public void Stingers_AreDeterministic()
        {
            CollectionAssert.AreEqual(SfxLibrary.Render("wrong", SR), SfxLibrary.Render("wrong", SR));
            CollectionAssert.AreNotEqual(SfxLibrary.Render("footstep", SR, 0), SfxLibrary.Render("footstep", SR, 1));
        }

        [Test]
        public void ConsoleHum_LoopsSeamlessly()
        {
            var b = SfxLibrary.Render("console_hum", SR);
            float jump = Math.Abs(b[0] - b[b.Length - 1]);
            float typicalStep = 0f;
            for (int i = 1; i < b.Length; i++) typicalStep = Math.Max(typicalStep, Math.Abs(b[i] - b[i - 1]));
            Assert.LessOrEqual(jump, typicalStep * 1.01f + 1e-4f);
        }

        [TestCase(Mood.Title)] [TestCase(Mood.Unsettling)] [TestCase(Mood.Tense)] [TestCase(Mood.Panic)]
        public void Score_StaysBelowFullScale(Mood mood)
        {
            var e = new ScoreEngine(SR);
            var block = new float[1024 * 2];
            float peak = 0f;
            for (int i = 0; i < SR * 6 / 1024; i++)
            {
                e.SetTarget(mood, mood == Mood.Panic ? 1f : 0.5f, 1f, 3);
                e.Render(block, 2);
                AssertClean(block, 0.95f, mood.ToString());
                peak = Math.Max(peak, e.LastPeak);
            }
            Assert.Greater(peak, 0.01f, "score is silent");
        }

        [Test]
        public void Score_EndingsAndMotifs_StayBelowFullScale()
        {
            var e = new ScoreEngine(SR);
            var block = new float[512 * 2];
            var words = new[] { MotifGenerator.Generate(1, "a", "es"), MotifGenerator.Generate(2, "b", "es") };
            e.SetTarget(Mood.Panic, 1f, 1f, 3);
            for (int i = 0; i < 100; i++) e.Render(block, 2);
            e.PlayPhrase(words, PhraseStyle.Wrong);
            e.PlayPhrase(words, PhraseStyle.Reveal, 1f);
            e.TriggerEscape(SfxLibrary.Render("ship_power", SR));
            for (int i = 0; i < SR * 8 / 512; i++) { e.Render(block, 2); AssertClean(block, 0.95f, "escape"); }
            Assert.AreEqual(Mood.Escape, e.CurrentMood);
        }

        [Test]
        public void Death_CutsToNearSilence()
        {
            var e = new ScoreEngine(SR);
            var block = new float[1024 * 2];
            e.SetTarget(Mood.Panic, 1f, 1f, 3);
            for (int i = 0; i < SR * 4 / 1024; i++) e.Render(block, 2);
            float before = 0f; int nb = 0;
            for (int i = 0; i < SR * 2 / 1024; i++) { e.Render(block, 2); before += Rms(block); nb++; }
            before /= nb;
            e.TriggerDeath();
            for (int i = 0; i < SR / 1024; i++) e.Render(block, 2); // 1 s for the cut and the reverb tail
            float after = 0f; int na = 0;
            for (int i = 0; i < SR / 1024; i++) { e.Render(block, 2); after += Rms(block); na++; }
            after /= na;
            Assert.Less(after, before * 0.1f, $"mean RMS before {before} after {after}: should be at least 20 dB down");
        }

        [Test]
        public void MoodChanges_AreQuantisedToTheGrid()
        {
            var e = new ScoreEngine(SR);
            var block = new float[64 * 2];
            e.SetTarget(Mood.Unsettling, 0.1f, 0f, 1);
            for (int i = 0; i < SR * 4 / 64; i++) e.Render(block, 2);
            Assert.AreEqual(Mood.Unsettling, e.CurrentMood);
            e.SetTarget(Mood.Tense, 0.5f, 0.5f, 2);
            long switchedAt = -1;
            for (int i = 0; i < SR * 4 / 64 && switchedAt < 0; i++)
            {
                e.Render(block, 2);
                if (e.CurrentMood == Mood.Tense) switchedAt = e.StepCount;
            }
            Assert.GreaterOrEqual(switchedAt, 0, "never switched");
            Assert.AreEqual(0, switchedAt % MoodScheduler.StepsPerBeat, $"switched on step {switchedAt}, not a beat");
        }

        static float Rms(float[] b)
        {
            double s = 0; foreach (var x in b) s += x * x;
            return (float)Math.Sqrt(s / b.Length);
        }
    }
    public class MasterLimiterTests
    {
        [Test]
        public void Knee_IsTransparentBelowThreshold_AndNeverReachesFullScale()
        {
            Assert.AreEqual(0.5f, Fnmw.Audio.MasterLimiter.Knee(0.5f), 1e-6);
            Assert.AreEqual(-0.79f, Fnmw.Audio.MasterLimiter.Knee(-0.79f), 1e-6);
            float prev = 0f;
            for (float x = 0f; x < 8f; x += 0.01f)
            {
                float y = Fnmw.Audio.MasterLimiter.Knee(x);
                Assert.GreaterOrEqual(y, prev - 1e-6f, "monotonic");
                Assert.LessOrEqual(y, 0.97f, "below full scale");
                Assert.AreEqual(-y, Fnmw.Audio.MasterLimiter.Knee(-x), 1e-6, "symmetric");
                prev = y;
            }
        }
    }
}

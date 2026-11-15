using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fnmw.Audio;
using Fnmw.Audio.Synth;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.Tests.Audio
{
    /// <summary>Voice processing (pure) and the voice player (start/stop events, no-beast drop).</summary>
    public class VoiceFxTests
    {
        const int SR = 24000;

        /// <summary>A voice-like test signal: a 140 Hz buzz with vibrato through two formant bands, in syllables.</summary>
        static float[] Voice(float seconds = 1.5f)
        {
            int n = (int)(seconds * SR);
            var y = new float[n];
            var f1 = new Svf(); f1.Set(700f, 4f, SR);
            var f2 = new Svf(); f2.Set(2200f, 5f, SR);
            float phase = 0f;
            var rng = new Fnmw.Audio.Synth.Rng(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float hz = 140f + 6f * (float)Math.Sin(2 * Math.PI * 5 * t);
                phase += hz / SR; phase -= (float)Math.Floor(phase);
                float src = 2f * phase - 1f + 0.05f * rng.Bi();
                f1.Process(src); f2.Process(src);
                float syllable = (float)Math.Max(0, Math.Sin(Math.PI * t * 4));
                y[i] = (f1.Band + 0.6f * f2.Band) * syllable * 0.4f;
            }
            return y;
        }

        static float Centroid(float[] y)
        {
            const int N = 2048;
            double num = 0, den = 0;
            var re = new double[N]; var im = new double[N];
            for (int start = 0; start + N <= y.Length; start += N)
            {
                for (int i = 0; i < N; i++) { re[i] = y[start + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)); im[i] = 0; }
                Fft(re, im);
                for (int k = 1; k < N / 2; k++)
                {
                    double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    num += mag * k * SR / (double)N; den += mag;
                }
            }
            return den > 0 ? (float)(num / den) : 0f;
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < len / 2; k++)
                    {
                        double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                        double ur = re[i + k], ui = im[i + k];
                        double vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi, vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi; re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    }
            }
        }

        static bool AllFinite(float[] y) { foreach (var s in y) if (float.IsNaN(s) || float.IsInfinity(s)) return false; return true; }

        [Test]
        public void Monster_GetsDeeperLongerAndDarker_WithStage()   // U1
        {
            var input = Voice();
            var outs = new float[4][];
            for (int s = 1; s <= 3; s++) outs[s] = VoiceFx.Monster(input, SR, s);
            float c0 = Centroid(input), c1 = Centroid(outs[1]), c2 = Centroid(outs[2]), c3 = Centroid(outs[3]);
            UnityEngine.Debug.Log($"[VoiceFx] centroid in {c0:0} s1 {c1:0} s2 {c2:0} s3 {c3:0} Hz; length in {input.Length} s1 {outs[1].Length} s2 {outs[2].Length} s3 {outs[3].Length}");
            Assert.That(c1, Is.LessThan(c0), "stage 1 is darker than the raw voice");
            Assert.That(c2, Is.LessThan(c1), "stage 2 darker than stage 1");
            Assert.That(c3, Is.LessThan(c2), "stage 3 darker than stage 2");
            Assert.That(outs[1].Length, Is.GreaterThan(input.Length));
            Assert.That(outs[2].Length, Is.GreaterThan(outs[1].Length));
            Assert.That(outs[3].Length, Is.GreaterThan(outs[2].Length));
            for (int s = 1; s <= 3; s++)
            {
                Assert.IsTrue(AllFinite(outs[s]), "stage " + s);
                Assert.That(VoiceFx.Peak(outs[s]), Is.LessThanOrEqualTo(VoiceFx.PeakTarget + 1e-4f), "peak at -3 dBFS");
                Assert.That(VoiceFx.Peak(outs[s]), Is.GreaterThan(0.6f));
            }
        }

        [Test]
        public void Native_IsTheInputUpToOneGain()   // U2
        {
            var input = Voice();
            var y = VoiceFx.Native(input, SR);
            Assert.AreEqual(input.Length, y.Length);
            float g = VoiceFx.PeakTarget / VoiceFx.Peak(input);
            for (int i = 0; i < y.Length; i++) Assert.AreEqual(input[i] * g, y[i], 1e-5f, "sample " + i);
            Assert.AreNotSame(input, y, "the caller's buffer is not modified");
        }

        [Test]
        public void Ship_IsCleanSameLength_AndUnlikeTheMonster()   // U3
        {
            var input = Voice();
            var ship = VoiceFx.Ship(input, SR);
            var m1 = VoiceFx.Monster(input, SR, 1);
            Assert.AreEqual(input.Length, ship.Length);
            Assert.AreNotEqual(ship.Length, m1.Length);
            Assert.That(Math.Abs(Centroid(ship) - Centroid(input)), Is.LessThan(Math.Abs(Centroid(m1) - Centroid(input))), "the ship stays close to the voice");
            Assert.IsTrue(AllFinite(ship));
        }

        [Test]
        public void EdgeInputs_DoNotThrow_AndFinishInBoundedTime()   // U4
        {
            foreach (var input in new[] { new float[0], new float[] { 0.5f }, null })
            {
                Assert.DoesNotThrow(() => VoiceFx.Monster(input, SR, 3));
                Assert.DoesNotThrow(() => VoiceFx.Ship(input, SR));
                Assert.DoesNotThrow(() => VoiceFx.Native(input, SR));
            }
            var longIn = new float[SR * 40];
            for (int i = 0; i < longIn.Length; i++) longIn[i] = 0.3f * (float)Math.Sin(i * 0.05);
            var sw = Stopwatch.StartNew();
            var y = VoiceFx.Monster(longIn, SR, 3);
            sw.Stop();
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(15), "40 s of input (cut to 30 s) at stage 3");
            Assert.That(y.Length, Is.LessThan(SR * 60), "input is capped at 30 s");
            UnityEngine.Debug.Log($"[VoiceFx] 30 s stage 3 in {sw.Elapsed.TotalMilliseconds:0} ms");
        }

        [Test]
        public void DuckTarget_QuietestWins()   // U5
        {
            Assert.AreEqual(1f, VoiceMix.DuckTarget(false, false, false));
            Assert.AreEqual(0.75f, VoiceMix.DuckTarget(true, false, false));
            Assert.AreEqual(0.55f, VoiceMix.DuckTarget(false, true, false));
            Assert.AreEqual(0.55f, VoiceMix.DuckTarget(false, false, true));
            Assert.AreEqual(0.55f, VoiceMix.DuckTarget(true, true, false));
            float g = 1f;
            for (int i = 0; i < SR / 2; i++) g = VoiceMix.Step(g, 0.55f, SR);
            Assert.AreEqual(0.55f, g, 0.01f, "down within half a second");
        }
    }

    public class VoicePlayerTests
    {
        GameObject _go, _beast;
        VoicePlayer _player;
        float _now;
        readonly List<VoicePlayback> _events = new List<VoicePlayback>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("VoicePlayerTest");
            _player = _go.AddComponent<VoicePlayer>();
            _player.Synchronous = true;
            _player.Clock = () => _now;
            _now = 100f;
            _events.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_go);
            if (_beast) UnityEngine.Object.DestroyImmediate(_beast);
        }

        static VoiceClip Clip(VoiceChannel ch, float seconds = 1f, int stage = 1)
        {
            var s = new float[(int)(seconds * 24000)];
            for (int i = 0; i < s.Length; i++) s[i] = 0.4f * Mathf.Sin(i * 0.05f);
            return new VoiceClip { Samples = s, SampleRate = 24000, Channel = ch, Stage = stage };
        }

        [Test]
        public void Monster_WithNoBeast_IsDropped_AndNothingPlays()   // U6
        {
            int h = _player.Play(Clip(VoiceChannel.Monster), e => _events.Add(e));
            Assert.AreEqual(0, h);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(VoicePlaybackEvent.Dropped, _events[0].Event);
            Assert.IsFalse(_player.IsActive(VoiceChannel.Monster));
        }

        [Test]
        public void Started_IsReportedOnlyWhenPlaybackBegins_WithTheProcessedLength()
        {
            _beast = new GameObject("Beast");
            _player.Beast = () => _beast.transform;
            int h = _player.Play(Clip(VoiceChannel.Monster, 1f, 3), e => _events.Add(e));
            Assert.That(h, Is.GreaterThan(0));
            Assert.AreEqual(0, _events.Count, "processing is not playing");
            Assert.IsTrue(_player.IsActive(VoiceChannel.Monster));
            Assert.IsFalse(_player.IsPlaying(VoiceChannel.Monster));
            _player.Tick();
            Assert.AreEqual(VoicePlaybackEvent.Started, _events[0].Event);
            Assert.That(_events[0].Seconds, Is.GreaterThan(1.5f), "stage 3 is slower than the raw line");
            var src = _player.SourceFor(VoiceChannel.Monster);
            Assert.AreEqual(_beast.transform, src.transform.parent, "3D on the beast");
            Assert.AreEqual(1f, src.spatialBlend);
            _now += _events[0].Seconds + 0.01f;
            _player.Tick();
            Assert.AreEqual(VoicePlaybackEvent.Finished, _events[1].Event);
            Assert.IsFalse(_player.IsActive(VoiceChannel.Monster));
        }

        [Test]
        public void Native_PlaysTheCleanVoice_In2D()
        {
            var clip = Clip(VoiceChannel.Native, 0.5f);
            _player.Play(clip, e => _events.Add(e));
            _player.Tick();
            Assert.AreEqual(VoicePlaybackEvent.Started, _events[0].Event);
            Assert.AreEqual(0.5f, _events[0].Seconds, 1e-3f, "no time stretch on the native voice");
            var src = _player.SourceFor(VoiceChannel.Native);
            Assert.AreEqual(0f, src.spatialBlend);
            var data = new float[src.clip.samples];
            src.clip.GetData(data, 0);
            float g = VoiceFx.PeakTarget / VoiceFx.Peak(clip.Samples);
            for (int i = 0; i < data.Length; i += 97) Assert.AreEqual(clip.Samples[i] * g, data[i], 1e-4f);
        }

        [Test]
        public void StopAll_DuringSpeechAndPreparation_StopsEveryChannel()   // U7
        {
            _beast = new GameObject("Beast");
            _player.Beast = () => _beast.transform;
            _player.Play(Clip(VoiceChannel.Ship), e => _events.Add(e));
            _player.Tick();
            _player.Play(Clip(VoiceChannel.Monster), e => _events.Add(e));   // still preparing (not ticked)
            _player.StopAll();
            Assert.IsFalse(_player.IsActive(VoiceChannel.Ship));
            Assert.IsFalse(_player.IsActive(VoiceChannel.Monster));
            Assert.AreEqual(2, _events.FindAll(e => e.Event == VoicePlaybackEvent.Stopped).Count);
            _player.Tick();
            Assert.IsFalse(_events.Exists(e => e.Event == VoicePlaybackEvent.Started && e.Channel == VoiceChannel.Monster), "a cancelled line never starts");
            Assert.IsFalse(_player.SourceFor(VoiceChannel.Ship).isPlaying);
        }

        [Test]
        public void ANewLine_ReplacesItsChannel_AndBadClipsAreDropped()
        {
            _player.Play(Clip(VoiceChannel.Ship), e => _events.Add(e));
            _player.Tick();
            _player.Play(Clip(VoiceChannel.Ship, 0.3f), e => _events.Add(e));
            Assert.AreEqual(VoicePlaybackEvent.Stopped, _events[1].Event);
            _player.Tick();
            Assert.AreEqual(VoicePlaybackEvent.Started, _events[2].Event);
            Assert.AreEqual(0, _player.Play(new VoiceClip { Samples = new float[10], SampleRate = 1000, Channel = VoiceChannel.Ship }, e => _events.Add(e)));
            Assert.AreEqual(0, _player.Play(new VoiceClip { Samples = new float[0], SampleRate = 24000, Channel = VoiceChannel.Ship }, e => _events.Add(e)));
            Assert.AreEqual(VoicePlaybackEvent.Dropped, _events[_events.Count - 1].Event);
        }
    }
}

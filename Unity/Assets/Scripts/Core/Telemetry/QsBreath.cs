using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fnmw.Core.Telemetry
{
    /// <summary>Breathing features for one send, computed on the headset. Null fields mean "not measurable".</summary>
    public struct QsBreathFeatures
    {
        public double? RateBpm, Regularity, Amp, SinceLastBreathS, VoiceFraction;
        public int DeepBreaths;
        public bool Any => Amp.HasValue || RateBpm.HasValue;

        /// <summary>The "audio" object of an ingest batch (qs-backend/docs/api.md). Numbers only, never samples.</summary>
        public string ToJson()
        {
            var inv = CultureInfo.InvariantCulture;
            string N(double? v, int d = 4) => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)
                ? Math.Round(v.Value, d).ToString("0.######", inv) : "null";
            return new StringBuilder("{")
                .Append("\"breath_rate_bpm\":").Append(N(RateBpm, 2))
                .Append(",\"breath_regularity\":").Append(N(Regularity, 3))
                .Append(",\"breath_amp\":").Append(N(Amp, 6))
                .Append(",\"deep_breaths\":").Append(DeepBreaths.ToString(inv))
                .Append(",\"since_last_breath_s\":").Append(N(SinceLastBreathS, 2))
                .Append(",\"voice_fraction\":").Append(N(VoiceFraction, 3))
                .Append('}').ToString();
        }
    }

    /// <summary>
    /// Breathing from the microphone, on the device. A line-by-line port of qs-backend/qs/audio.py (the reference,
    /// unit-tested on synthetic signals); keep the two in step. Raw audio goes in, 20 ms at a time, and is reduced
    /// to one RMS value per frame immediately; only the numbers in <see cref="QsBreathFeatures"/> ever leave the
    /// headset. Pure C#, no Unity calls, so it is testable in EditMode.
    /// </summary>
    public sealed class QsBreath
    {
        public const double EnvFs = 50.0;
        public const double HistoryS = 15.0;
        const double MinHistoryS = 8.0, SmoothS = 0.3, LagMinS = 1.2, LagMaxS = 7.5, MinRegularity = 0.3;
        const double DeepFactor = 1.8, NoiseFraction = 0.2, HoldNoRateS = 6.0, PeakFraction = 0.5, MajorFraction = 0.75;
        const double VoiceFactor = 3.0, PeakSeparationS = 0.8, Eps = 1e-6;

        readonly int _frame;                     // audio samples per 20 ms envelope frame
        double _sumSq; int _n;                   // the frame being filled
        readonly List<double> _env = new List<double>();

        public QsBreath(int sampleRate) { _frame = Math.Max(1, (int)Math.Round(sampleRate / EnvFs)); }

        public int EnvelopeCount => _env.Count;

        /// <summary>Feed raw mic samples (any count). They are folded into the envelope and not kept.</summary>
        public void Push(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = samples[i];
                _sumSq += x * x;
                if (++_n == _frame) { PushEnvelope(Math.Sqrt(_sumSq / _n)); _sumSq = 0; _n = 0; }
            }
        }

        /// <summary>Feed envelope values directly (tests, or a source that already gives RMS per 20 ms).</summary>
        public void PushEnvelope(double rms)
        {
            _env.Add(rms);
            int max = (int)(HistoryS * EnvFs);
            if (_env.Count > max + 50) _env.RemoveRange(0, _env.Count - max);
        }

        public void Clear() { _env.Clear(); _sumSq = 0; _n = 0; }

        public QsBreathFeatures Features(double hopS = 0.5)
        {
            int maxN = (int)(HistoryS * EnvFs);
            int start = Math.Max(0, _env.Count - maxN);
            var env = _env.GetRange(start, _env.Count - start).ToArray();
            return Compute(env, EnvFs, hopS);
        }

        // ---- the algorithm (mirrors audio.breath_features) -------------------------------------------------

        public static QsBreathFeatures Compute(double[] env, double envFs, double hopS)
        {
            var o = new QsBreathFeatures();
            if (env.Length < MinHistoryS * envFs) return o;
            int n = env.Length;
            int hop = Math.Max(1, (int)Math.Round(hopS * envFs));

            double p90env = Percentile(env, 90);
            int loud = 0;
            for (int i = n - hop; i < n; i++) if (env[i] > VoiceFactor * Math.Max(p90env, Eps)) loud++;
            o.VoiceFraction = (double)loud / hop;

            var sm = Smooth(env, Math.Max(1, (int)Math.Round(SmoothS * envFs)));
            o.Amp = Percentile(sm, 90) - Percentile(sm, 10);

            // breath peaks
            double floor = Percentile(sm, 10);
            var peaks = Peaks(sm, Math.Max(1, (int)(PeakSeparationS * envFs)));
            var heights = new List<double>();
            foreach (int i in peaks) heights.Add(sm[i] - floor);
            if (heights.Count > 0)
            {
                double thr = Math.Max(NoiseFraction * Percentile(heights.ToArray(), 90), 1e-5);
                for (int k = peaks.Count - 1; k >= 0; k--) if (heights[k] < thr) { peaks.RemoveAt(k); heights.RemoveAt(k); }
            }
            int? majorGap = null;
            if (peaks.Count > 0)
            {
                double refH = Percentile(heights.ToArray(), 75);
                int lo = n - 2 * hop, hi = n - hop, deep = 0;
                if (peaks.Count >= 4)
                    for (int k = 0; k < peaks.Count; k++) if (peaks[k] >= lo && peaks[k] < hi && heights[k] >= DeepFactor * refH) deep++;
                o.DeepBreaths = deep;
                int last = -1;
                for (int k = 0; k < peaks.Count; k++) if (heights[k] >= PeakFraction * refH) last = peaks[k];
                if (last >= 0) o.SinceLastBreathS = (n - 1 - last) / envFs;
                var major = new List<int>();
                for (int k = 0; k < peaks.Count; k++) if (heights[k] >= MajorFraction * refH) major.Add(peaks[k]);
                if (major.Count >= 3)
                {
                    var gaps = new double[major.Count - 1];
                    for (int k = 1; k < major.Count; k++) gaps[k - 1] = major[k] - major[k - 1];
                    majorGap = (int)Math.Round(Percentile(gaps, 50));
                }
            }
            else o.SinceLastBreathS = n / envFs;

            // repetition of the detrended log envelope
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Log(sm[i] + Eps);
            Detrend(x);
            if (Std(x) > 0.02)
            {
                var ac = Autocorr(x);
                int lo = (int)(LagMinS * envFs), hi = Math.Min((int)(LagMaxS * envFs), n / 2);
                int best = -1; double bestR = -1;
                for (int i = Math.Max(lo, 1); i < hi; i++)
                    if (ac[i] >= ac[i - 1] && ac[i] >= ac[i + 1] && ac[i] > bestR) { best = i; bestR = ac[i]; }
                if (best > 0 && bestR >= MinRegularity)
                {
                    if (majorGap.HasValue && Math.Abs(majorGap.Value - 2 * best) < Math.Abs(majorGap.Value - best) && 2 * best + 1 < ac.Length)
                    {
                        int j = Math.Max(2 * best - 3, 1);
                        for (int i = j; i < Math.Min(2 * best + 4, ac.Length - 1); i++) if (ac[i] > ac[j]) j = i;
                        best = j; bestR = ac[j];
                    }
                    double a = ac[best - 1], b = ac[best], c = ac[best + 1], den = a - 2 * b + c;
                    double d = den != 0 ? 0.5 * (a - c) / den : 0;
                    o.RateBpm = 60.0 * envFs / (best + d);
                    o.Regularity = bestR;
                }
            }
            if (o.SinceLastBreathS.HasValue && o.SinceLastBreathS.Value > HoldNoRateS) { o.RateBpm = null; o.Regularity = null; }
            return o;
        }

        static double[] Smooth(double[] x, int w)
        {
            var o = new double[x.Length];
            if (w <= 1) { Array.Copy(x, o, x.Length); return o; }
            // same as numpy convolve(mode="same") normalised by the overlap: centred, edges average what exists
            int left = w / 2, right = w - 1 - left;
            for (int i = 0; i < x.Length; i++)
            {
                double s = 0; int c = 0;
                for (int k = i - right; k <= i + left; k++) if (k >= 0 && k < x.Length) { s += x[k]; c++; }
                o[i] = s / c;
            }
            return o;
        }

        static List<int> Peaks(double[] x, int minSep)
        {
            var cand = new List<int>();
            for (int i = 1; i < x.Length - 1; i++) if (x[i] >= x[i - 1] && x[i] > x[i + 1]) cand.Add(i);
            cand.Sort((a, b) => x[b].CompareTo(x[a]));
            var kept = new List<int>();
            foreach (int i in cand)
            {
                bool ok = true;
                foreach (int j in kept) if (Math.Abs(i - j) < minSep) { ok = false; break; }
                if (ok) kept.Add(i);
            }
            kept.Sort();
            return kept;
        }

        static void Detrend(double[] x)
        {
            int n = x.Length; double km = (n - 1) / 2.0, xm = 0;
            foreach (var v in x) xm += v;
            xm /= n;
            double num = 0, den = 0;
            for (int i = 0; i < n; i++) { double k = i - km; num += k * (x[i] - xm); den += k * k; }
            double slope = den > 0 ? num / den : 0;
            for (int i = 0; i < n; i++) x[i] = x[i] - xm - (i - km) * slope;
        }

        static double Std(double[] x)
        {
            double m = 0; foreach (var v in x) m += v; m /= x.Length;
            double s = 0; foreach (var v in x) s += (v - m) * (v - m);
            return Math.Sqrt(s / x.Length);
        }

        /// <summary>Normalised autocorrelation for lags 0..n-1 (direct sum; n is at most 750, called twice a second).</summary>
        static double[] Autocorr(double[] x)
        {
            int n = x.Length, maxLag = Math.Min(n - 1, (int)(LagMaxS * EnvFs) * 2 + 8);
            var ac = new double[maxLag + 1];
            for (int lag = 0; lag <= maxLag; lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < n; i++) s += x[i] * x[i + lag];
                ac[lag] = s;
            }
            double z = ac[0] > 0 ? ac[0] : 1;
            for (int i = 0; i <= maxLag; i++) ac[i] /= z;
            return ac;
        }

        /// <summary>Linear-interpolated percentile, numpy's default method.</summary>
        static double Percentile(double[] x, double q)
        {
            if (x.Length == 0) return double.NaN;
            var s = (double[])x.Clone();
            Array.Sort(s);
            double pos = (s.Length - 1) * q / 100.0;
            int i = (int)Math.Floor(pos);
            if (i >= s.Length - 1) return s[s.Length - 1];
            return s[i] + (s[i + 1] - s[i]) * (pos - i);
        }
    }
}

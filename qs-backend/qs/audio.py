"""Breathing features from microphone audio: the reference implementation of what the headset computes.

The Quest computes these on the device (``Unity/Assets/Scripts/Core/Telemetry/QsBreath.cs`` is a line-by-line
port of this file) and sends only the numbers below. **Raw audio never leaves the headset** and the backend
never receives or stores it. This module exists so the algorithm can be unit-tested on synthetic signals and
so ``qs/sim.py`` produces exactly what a headset would send.

Pipeline (all on the headset)
-----------------------------
1. ``envelope``: mic samples (16 kHz or whatever the device gives) become a 50 Hz RMS envelope, one value per
   20 ms frame. The samples are discarded as soon as the frame is computed.
2. ``breath_features``: the last ``HISTORY_S`` (15 s) of envelope become, every hop (0.5 s):

   ========================  =====  ==========================================================================
   ``breath_rate_bpm``       1/min  Period of the strongest repetition of the smoothed log envelope between 1.2 s
                                    and 7.5 s (8-50 breaths/min), from its normalised autocorrelation. ``None``
                                    when nothing repeats clearly (autocorrelation peak below 0.3), e.g. talking.
   ``breath_regularity``     0-1    That autocorrelation peak: how regular the breathing is.
   ``breath_amp``            RMS    p90 - p10 of the smoothed envelope over the history: how loud (deep) the
                                    breath sounds are. Only meaningful relative to the same person's baseline.
   ``deep_breaths``          count  Breath peaks at least 1.8x the 75th-percentile breath peak of the history
                                    (sighs, gasps, deliberate deep breaths), counted in the hop before the
                                    newest, so every peak is counted exactly once, half a second late.
   ``since_last_breath_s``   s      Time since the last breath peak at least half that reference height. Long
                                    gaps mean the breath sound stopped (breath-holding, or the mic lost it);
                                    beyond 6 s the rate is reported as ``None``.
   ``voice_fraction``        0-1    Share of 20 ms frames in the newest hop louder than 6x the history's median:
                                    speech or loud noise, which makes the other numbers unreliable.
   ========================  =====  ==========================================================================

Caveat: a headset mic hears both inhale and exhale. When the two sound alike the strongest repetition can be
the half-cycle, which would double the rate. Picking the *largest* autocorrelation peak (not the first) prefers
the full cycle whenever exhale and inhale differ in loudness, which they usually do. None of this has been
validated on a real Quest microphone yet.
"""
from __future__ import annotations

import math

import numpy as np

ENV_FS = 50.0                 # envelope rate: one RMS value per 20 ms
HISTORY_S = 15              # envelope kept for the rate estimate
MIN_HISTORY_S = 8.0           # below this nothing is reported
SMOOTH_S = 0.3                # moving average before looking for repetition
LAG_MIN_S, LAG_MAX_S = 1.2, 7.5   # 50 .. 6 breaths per minute
MIN_REGULARITY = 0.3
DEEP_FACTOR = 1.8
NOISE_FRACTION = 0.2
HOLD_NO_RATE_S = 6.0
PEAK_FRACTION = 0.5
MAJOR_FRACTION = 0.75
VOICE_FACTOR = 3.0
PEAK_SEPARATION_S = 0.8
EPS = 1e-6


def envelope(audio: np.ndarray, fs: float, frame_s: float = 1.0 / ENV_FS) -> np.ndarray:
    """RMS of consecutive frames of ``frame_s`` seconds (the tail that does not fill a frame is dropped)."""
    x = np.asarray(audio, dtype=float).ravel()
    n = int(round(fs * frame_s))
    if n <= 0 or len(x) < n:
        return np.zeros(0)
    k = len(x) // n
    frames = x[: k * n].reshape(k, n)
    return np.sqrt((frames ** 2).mean(axis=1))


def _smooth(x: np.ndarray, n: int) -> np.ndarray:
    if n <= 1 or len(x) < 2:
        return x.copy()
    kernel = np.ones(n)
    return np.convolve(x, kernel, mode="same") / np.convolve(np.ones(len(x)), kernel, mode="same")


def _peaks(x: np.ndarray, min_sep: int) -> list[int]:
    """Local maxima, greedily keeping the highest when two are closer than ``min_sep`` samples."""
    cand = [i for i in range(1, len(x) - 1) if x[i] >= x[i - 1] and x[i] > x[i + 1]]
    cand.sort(key=lambda i: -x[i])
    kept: list[int] = []
    for i in cand:
        if all(abs(i - j) >= min_sep for j in kept):
            kept.append(i)
    return sorted(kept)


def _r(v: float | None, digits: int = 4) -> float | None:
    if v is None or not math.isfinite(v):
        return None
    return float(f"{v:.{digits}g}")


def empty() -> dict:
    return {"breath_rate_bpm": None, "breath_regularity": None, "breath_amp": None, "deep_breaths": 0,
            "since_last_breath_s": None, "voice_fraction": None}


def breath_features(env: np.ndarray, env_fs: float = ENV_FS, hop_s: float = 0.5) -> dict:
    """Breathing features from the trailing envelope history (newest sample last). See the module docstring."""
    env = np.asarray(env, dtype=float)
    max_n = int(HISTORY_S * env_fs)
    if len(env) > max_n:
        env = env[-max_n:]
    if len(env) < MIN_HISTORY_S * env_fs:
        return empty()
    hop_n = max(1, int(round(hop_s * env_fs)))
    out = empty()
    out["voice_fraction"] = _r(float(np.mean(env[-hop_n:] > VOICE_FACTOR * max(float(np.percentile(env, 90)), EPS))), 3)

    sm = _smooth(env, max(1, int(round(SMOOTH_S * env_fs))))
    out["breath_amp"] = _r(float(np.percentile(sm, 90) - np.percentile(sm, 10)))

    # breath peaks: deep breaths in the hop before the newest (a peak at the very end is not a peak yet, so
    # counting one hop late counts every peak exactly once), and time since the last ordinary breath.
    # Peaks smaller than a fifth of the loud ones (90th percentile, so one sigh does not move it) are noise.
    floor = float(np.percentile(sm, 10))
    peaks = _peaks(sm, max(1, int(PEAK_SEPARATION_S * env_fs)))
    heights = np.array([sm[i] - floor for i in peaks]) if peaks else np.zeros(0)
    keep = heights >= max(NOISE_FRACTION * float(np.percentile(heights, 90)), 1e-5) if len(heights) else np.zeros(0, bool)
    peaks = [i for i, k in zip(peaks, keep) if k]
    heights = heights[keep] if len(heights) else heights
    if len(peaks):
        ref = float(np.percentile(heights, 75))
        lo, hi = len(sm) - 2 * hop_n, len(sm) - hop_n
        out["deep_breaths"] = int(sum(1 for i, h in zip(peaks, heights)
                                      if lo <= i < hi and h >= DEEP_FACTOR * ref and len(peaks) >= 4))
        ordinary = [i for i, h in zip(peaks, heights) if h >= PEAK_FRACTION * ref]
        if ordinary:
            out["since_last_breath_s"] = _r((len(sm) - 1 - ordinary[-1]) / env_fs, 3)
    else:
        out["since_last_breath_s"] = _r(len(sm) / env_fs, 3)
    major = [i for i, h in zip(peaks, heights) if len(peaks) and h >= MAJOR_FRACTION * float(np.percentile(heights, 75))]
    major_gap = float(np.median(np.diff(major))) if len(major) >= 3 else None

    # repetition of the log envelope (log so a loud sigh does not dominate the period estimate)
    x = np.log(sm + EPS)
    k = np.arange(len(x), dtype=float)
    k -= k.mean()
    x = x - x.mean() - k * ((k * (x - x.mean())).sum() / (k * k).sum())
    if float(np.std(x)) > 0.02:
        nfft = 1 << int(math.ceil(math.log2(2 * len(x))))
        spec = np.fft.rfft(x, nfft)
        ac = np.fft.irfft(spec * np.conj(spec), nfft)[: len(x)]
        ac = ac / ac[0] if ac[0] > 0 else ac
        lo, hi = int(LAG_MIN_S * env_fs), min(int(LAG_MAX_S * env_fs), len(x) // 2)
        best, best_r = None, -1.0
        for i in range(max(lo, 1), hi):
            if ac[i] >= ac[i - 1] and ac[i] >= ac[i + 1] and ac[i] > best_r:
                best, best_r = i, float(ac[i])
        if best is not None and best_r >= MIN_REGULARITY:
            # Octave check: the strongest repetition can be half a breath (exhale, then inhale). If the loud
            # (exhale) peaks are spaced nearer twice that lag, the breath is the double lag.
            if major_gap is not None and abs(major_gap - 2 * best) < abs(major_gap - best) and 2 * best + 1 < len(ac):
                j = max(range(max(2 * best - 3, 1), min(2 * best + 4, len(ac) - 1)), key=lambda i: ac[i])
                best, best_r = j, float(ac[j])
            # parabolic interpolation of the peak for sub-sample lag
            a, b, c = ac[best - 1], ac[best], ac[best + 1]
            d = 0.5 * (a - c) / (a - 2 * b + c) if (a - 2 * b + c) != 0 else 0.0
            d = max(-0.5, min(0.5, d))     # only a refinement within one sample; never trust it further
            out["breath_rate_bpm"] = _r(60.0 * env_fs / (best + d))
            out["breath_regularity"] = _r(best_r, 3)

    if out["since_last_breath_s"] is not None and out["since_last_breath_s"] > HOLD_NO_RATE_S:
        out["breath_rate_bpm"] = out["breath_regularity"] = None   # no breath sound lately: no rate to give
    return out


# ---- synthetic breathing (tests and qs/sim.py) ----------------------------------------------------------

def synth_envelope(t: np.ndarray, rate_bpm: np.ndarray, amp: np.ndarray, noise: float = 0.002,
                   seed: int = 0, sigh_at: list[float] | None = None) -> np.ndarray:
    """A plausible breath-sound envelope sampled at times ``t``: a loud exhale and a quieter inhale per cycle.

    ``rate_bpm`` and ``amp`` are per-sample arrays, so rate can change smoothly (phase is integrated). An
    ``amp`` of 0 is silence (breath held). ``sigh_at`` adds one deep breath (a 1.6 s exhale at 3.5x a calm breath) at each time."""
    rng = np.random.default_rng(seed)
    t = np.asarray(t, float)
    dt = np.diff(t, prepend=t[0])
    phase = np.cumsum(np.asarray(rate_bpm, float) / 60.0 * dt)
    frac = phase % 1.0
    exhale = np.where(frac < 0.45, np.sin(np.pi * frac / 0.45) ** 2, 0.0)
    inhale = np.where((frac >= 0.55) & (frac < 0.9), 0.55 * np.sin(np.pi * (frac - 0.55) / 0.35) ** 2, 0.0)
    a = np.asarray(amp, float)
    env = 0.02 * a * (exhale + inhale)
    for ts in sigh_at or []:
        d = (t - ts) / 1.6                      # a sigh is its own long, loud breath out
        env = env + np.where((d >= 0) & (d < 1), 0.02 * 3.5 * np.sin(np.pi * d) ** 2, 0.0)
    return np.abs(env + noise * (1 + 0.3 * rng.standard_normal(len(t))))

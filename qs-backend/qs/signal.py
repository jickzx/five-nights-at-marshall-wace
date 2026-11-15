"""Pure signal processing for QS. No I/O, no globals: every function takes arrays and returns numbers.

Conventions
-----------
* Time ``t`` is in seconds on the headset's monotonic clock, strictly increasing after cleaning.
* Positions are metres in Unity tracking space (left-handed, y up). Rotations are unit quaternions
  stored as ``(x, y, z, w)``, Unity's order.
* Frequencies are in Hz. Powers are mean-square values of the input signal's units within a band, so
  a sine of amplitude ``A`` contributes ``A**2 / 2`` (Parseval), whatever the window length.
"""
from __future__ import annotations

import math

import numpy as np

# Below this many samples a window carries too little signal to say anything.
MIN_SAMPLES = 16


def clean_series(t: np.ndarray, x: np.ndarray, valid: np.ndarray | None = None) -> tuple[np.ndarray, np.ndarray]:
    """Drop invalid / non-finite / duplicate-time samples and sort by time.

    ``x`` is (n, d). ``valid`` is an optional boolean mask (tracking flag)."""
    t = np.asarray(t, dtype=float)
    x = np.asarray(x, dtype=float)
    if x.ndim == 1:
        x = x[:, None]
    keep = np.isfinite(t) & np.all(np.isfinite(x), axis=1)
    if valid is not None:
        keep &= np.asarray(valid, dtype=bool)
    t, x = t[keep], x[keep]
    order = np.argsort(t, kind="stable")
    t, x = t[order], x[order]
    if len(t) > 1:
        uniq = np.concatenate(([True], np.diff(t) > 1e-6))
        t, x = t[uniq], x[uniq]
    return t, x


def estimate_rate(t: np.ndarray) -> float:
    """Median sample rate in Hz (robust to the odd dropped frame). 0 when it cannot be estimated."""
    if len(t) < 2:
        return 0.0
    dt = np.median(np.diff(t))
    return float(1.0 / dt) if dt > 0 else 0.0


def resample_uniform(t: np.ndarray, x: np.ndarray, fs: float) -> tuple[np.ndarray, np.ndarray]:
    """Linearly interpolate (n, d) samples onto a uniform grid at ``fs`` Hz spanning [t0, t_end]."""
    if len(t) < 2 or fs <= 0:
        return t.copy(), x.copy()
    n = int(math.floor((t[-1] - t[0]) * fs)) + 1
    tu = t[0] + np.arange(n) / fs
    xu = np.column_stack([np.interp(tu, t, x[:, j]) for j in range(x.shape[1])])
    return tu, xu


def detrend_linear(x: np.ndarray) -> np.ndarray:
    """Remove a least-squares line from each column (kills drift and slow reaches leaking into low bins)."""
    n = x.shape[0]
    if n < 2:
        return x - x.mean(axis=0, keepdims=True)
    k = np.arange(n, dtype=float)
    k -= k.mean()
    denom = float((k * k).sum())
    xm = x.mean(axis=0, keepdims=True)
    slope = (k[:, None] * (x - xm)).sum(axis=0, keepdims=True) / denom
    return x - xm - k[:, None] * slope


def periodogram(x: np.ndarray, fs: float) -> tuple[np.ndarray, np.ndarray]:
    """One-sided Hann-windowed PSD of each column, summed over columns.

    Returns (freqs, psd) with psd in units**2 / Hz, scaled so that ``sum(psd) * df`` equals the mean-square
    of the (detrended) signal."""
    x = detrend_linear(np.asarray(x, dtype=float))
    n = x.shape[0]
    w = np.hanning(n)
    s2 = float((w * w).sum())
    spec = np.fft.rfft(x * w[:, None], axis=0)
    psd = (np.abs(spec) ** 2) / (fs * s2)
    if n % 2 == 0:
        psd[1:-1] *= 2.0
    else:
        psd[1:] *= 2.0
    freqs = np.fft.rfftfreq(n, d=1.0 / fs)
    return freqs, psd.sum(axis=1)


def band_power(x: np.ndarray, fs: float, lo: float, hi: float) -> float:
    """Mean-square power of ``x`` (n, d) between ``lo`` and ``hi`` Hz, summed over columns."""
    if x.shape[0] < MIN_SAMPLES or fs <= 0:
        return float("nan")
    freqs, psd = periodogram(x, fs)
    df = fs / x.shape[0]
    band = (freqs >= lo) & (freqs <= hi)
    return float(psd[band].sum() * df)


def dominant_frequency(x: np.ndarray, fs: float, lo: float, hi: float) -> float:
    """Frequency of the largest PSD bin within [lo, hi] Hz (nan if none)."""
    if x.shape[0] < MIN_SAMPLES or fs <= 0:
        return float("nan")
    freqs, psd = periodogram(x, fs)
    band = (freqs >= lo) & (freqs <= hi)
    if not band.any():
        return float("nan")
    return float(freqs[band][np.argmax(psd[band])])


def derivative(x: np.ndarray, fs: float) -> np.ndarray:
    """Central-difference time derivative of each column."""
    if x.shape[0] < 2:
        return np.zeros_like(x)
    return np.gradient(x, 1.0 / fs, axis=0)


def moving_average(x: np.ndarray, n: int) -> np.ndarray:
    """Centred moving average of each column over ``n`` samples, same length (edges average what exists).

    Used before taking a speed: the magnitude of a noisy derivative is biased upward (sensor jitter gets
    rectified into apparent movement), and averaging over ~100 ms removes most of that bias."""
    if n <= 1 or x.shape[0] < 2:
        return x
    kernel = np.ones(n)
    num = np.column_stack([np.convolve(x[:, j], kernel, mode="same") for j in range(x.shape[1])])
    den = np.convolve(np.ones(x.shape[0]), kernel, mode="same")[:, None]
    return num / den


def speed(v: np.ndarray) -> np.ndarray:
    """Vector magnitude of each row."""
    return np.linalg.norm(v, axis=1)


# ---- rotations -------------------------------------------------------------------------------------

def normalize_quats(q: np.ndarray) -> np.ndarray:
    """Normalise and make the sign continuous (q and -q are the same rotation)."""
    q = np.asarray(q, dtype=float)
    norms = np.linalg.norm(q, axis=1, keepdims=True)
    norms[norms == 0] = 1.0
    q = q / norms
    for i in range(1, len(q)):
        if np.dot(q[i], q[i - 1]) < 0:
            q[i] = -q[i]
    return q


def angular_speed(t: np.ndarray, q: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Angular speed (deg/s) between consecutive quaternions, timestamped at the interval midpoints."""
    if len(t) < 2:
        return np.array([]), np.array([])
    q = normalize_quats(q)
    dots = np.abs(np.sum(q[1:] * q[:-1], axis=1)).clip(0.0, 1.0)
    angle = 2.0 * np.degrees(np.arccos(dots))
    dt = np.diff(t)
    return 0.5 * (t[1:] + t[:-1]), angle / dt


def forward_vectors(q: np.ndarray) -> np.ndarray:
    """Rotate Unity's forward axis (0, 0, 1) by each (x, y, z, w) quaternion."""
    q = normalize_quats(q)
    x, y, z, w = q[:, 0], q[:, 1], q[:, 2], q[:, 3]
    fx = 2.0 * (x * z + w * y)
    fy = 2.0 * (y * z - w * x)
    fz = 1.0 - 2.0 * (x * x + y * y)
    return np.column_stack([fx, fy, fz])


def yaw_degrees(q: np.ndarray) -> np.ndarray:
    """Unwrapped heading (deg) of the forward vector about the vertical axis."""
    f = forward_vectors(q)
    yaw = np.arctan2(f[:, 0], f[:, 2])
    return np.degrees(np.unwrap(yaw))


def count_reversals(series: np.ndarray, min_excursion: float) -> int:
    """Turning points of a 1-D series that swing back by at least ``min_excursion`` (a zig-zag filter).

    For yaw in degrees this counts changes of looking direction: starting centred, a scan
    left-right-left is 2 reversals. Jitter smaller than ``min_excursion`` never counts."""
    if len(series) < 3:
        return 0
    reversals, direction = 0, 0
    lo = hi = extreme = float(series[0])
    for v in map(float, series[1:]):
        if direction == 0:
            lo, hi = min(lo, v), max(hi, v)
            if v - lo >= min_excursion:
                direction, extreme = 1, v
            elif hi - v >= min_excursion:
                direction, extreme = -1, v
        elif direction == 1:
            if v > extreme:
                extreme = v
            elif extreme - v >= min_excursion:
                reversals, direction, extreme = reversals + 1, -1, v
        else:
            if v < extreme:
                extreme = v
            elif v - extreme >= min_excursion:
                reversals, direction, extreme = reversals + 1, 1, v
    return reversals

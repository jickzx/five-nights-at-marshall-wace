"""QS feature extraction: raw pose samples -> window features -> per-question QS records.

Everything here is pure (dicts and arrays in, dicts out) so it can be unit-tested on synthetic signals.
The exact definition of every number is written down in ``docs/qs-schema.md``; keep the two in step.

Pipeline
--------
1. ``window_features``: one window of samples (normally 1.0 s, sent every 0.5 s) becomes a flat dict
   of numbers. Raw samples are not kept after this step (see docs/design.md for why).
2. ``compute_baseline``: the windows from the calm calibration period at session start become a
   per-feature median and MAD, so each person is compared with themselves.
3. ``relative_to_baseline``: a window's features expressed as ratios to that person's baseline.
4. ``question_record``: all windows and game events of one question become one QS record.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Sequence

import numpy as np

from . import signal as sig

QS_VERSION = "qs-0.2"   # 0.2: optional breathing features from the headset mic, and possible states

TREMOR_BAND = (4.0, 12.0)       # Hz: physiological and enhanced physiological tremor live here
RESTLESS_BAND = (0.5, 3.0)      # Hz: small slow repetitive movement (shifting, fiddling)
YAW_REVERSAL_DEG = 10.0         # a head turn must swing back this far to count as a scan reversal
MIN_COVERAGE = 0.6              # fraction of the window a device must be tracked for its features to count
MAX_RATE_HZ = 120.0             # grid used for resampling never exceeds this
SPEED_SMOOTH_S = 0.1            # speeds are taken after a 100 ms moving average (see signal.moving_average)

# Freezing: a window is "still" when head and both hands are all below this fraction of the person's own
# calibration median AND below a small absolute ceiling. The ceiling stops someone who fidgeted through
# calibration from having ordinary calm movement counted as freezing.
# 0.5 rather than lower because controller jitter leaves a floor of apparent hand speed (about 2 mm/s after
# smoothing) that is a sizeable fraction of a calm baseline, so a lower fraction is unreachable in practice.
FREEZE_FRACTION = 0.5
FREEZE_CEILING = {"head_ang_speed_mean": 3.0, "hand_speed_mean": 0.01}  # deg/s, m/s

# A startle is looked for in the head and hands within this time after a sudden game stimulus.
STARTLE_EVENTS = frozenset({"monster_appeared", "monster_lunge", "monster_stage", "jumpscare", "answer_wrong_fx"})
STARTLE_WINDOW_S = 1.5
STARTLE_PRE_S = 2.0

# Hesitation: a gap of at least this long between inputs while a question is open.
HESITATION_GAP_S = 3.0
ACTION_EVENTS = frozenset({"tile_placed", "tile_removed", "answer_cleared", "answer_submitted", "hint_used"})
CHANGE_EVENTS = frozenset({"tile_removed", "answer_cleared"})
QUESTION_END_EVENTS = frozenset({"question_abandoned", "death", "session_end"})

# Features that get a baseline and a ratio. Hand features are per side.
BASELINE_FEATURES = (
    "head_ang_speed_mean",
    "head_yaw_scan_rate",
    "head_trans_speed_mean",
    "left_tremor_rms",
    "right_tremor_rms",
    "left_restless_rms",
    "right_restless_rms",
    "left_hand_speed_mean",
    "right_hand_speed_mean",
    "audio_breath_rate_bpm",
    "audio_breath_amp",
)

# Breathing features the headset computes from its microphone (qs/audio.py documents each; raw audio never
# leaves the device). Stored in a window's features with an ``audio_`` prefix when the batch carries them
# and the session's consent covers the microphone.
AUDIO_FIELDS = ("breath_rate_bpm", "breath_regularity", "breath_amp", "deep_breaths", "since_last_breath_s", "voice_fraction")

# Ratios are capped so one twitch against a near-zero baseline cannot dominate an average.
RATIO_CAP = 50.0
# Floors stop a perfectly still calibration from producing infinite ratios (units as the feature).
BASELINE_FLOOR = {
    "head_ang_speed_mean": 1.0,       # deg/s
    "head_yaw_scan_rate": 1.0,        # deg/s
    "head_trans_speed_mean": 0.005,   # m/s
    "left_tremor_rms": 0.5,           # mm/s
    "right_tremor_rms": 0.5,
    "left_restless_rms": 1.0,         # mm/s
    "right_restless_rms": 1.0,
    "left_hand_speed_mean": 0.005,    # m/s
    "right_hand_speed_mean": 0.005,
    "audio_breath_rate_bpm": 6.0,     # breaths/min
    "audio_breath_amp": 0.0005,       # RMS, full scale 1
}


@dataclass
class DeviceTrack:
    """Samples of one tracked device inside a window. Arrays are aligned with ``t``."""
    t: np.ndarray
    pos: np.ndarray | None          # (n, 3) metres
    rot: np.ndarray | None          # (n, 4) quaternion x, y, z, w
    vel: np.ndarray | None = None   # (n, 3) m/s as reported by the runtime, optional
    tracked: np.ndarray | None = None


def _num(x: float) -> float | None:
    """JSON-safe float: nan/inf become None, everything else rounded to 5 significant-ish digits."""
    if x is None or not math.isfinite(x):
        return None
    return float(f"{x:.5g}")


def _coverage(t_clean: np.ndarray, t0: float, t1: float, fs: float) -> float:
    span = t1 - t0
    if span <= 0 or fs <= 0 or len(t_clean) == 0:
        return 0.0
    return float(min(1.0, len(t_clean) / (span * fs + 1)))


def _grid_rate(t: np.ndarray) -> float:
    fs = sig.estimate_rate(t)
    return min(fs, MAX_RATE_HZ) if fs > 0 else 0.0


def hand_features(track: DeviceTrack | None, t0: float, t1: float, nominal_fs: float) -> dict[str, float | None]:
    """Tremor, restlessness and speed for one controller."""
    empty = {"tremor_rms": None, "tremor_peak_hz": None, "restless_rms": None,
             "hand_speed_mean": None, "hand_speed_peak": None, "hand_speed_peak_t": None, "coverage": 0.0}
    if track is None:
        return empty
    source = track.pos if track.pos is not None else track.vel
    if source is None:
        return empty
    t, x = sig.clean_series(track.t, source, track.tracked)
    cov = _coverage(t, t0, t1, nominal_fs or sig.estimate_rate(t))
    fs = _grid_rate(t)
    if len(t) < sig.MIN_SAMPLES or cov < MIN_COVERAGE or fs <= 0:
        return {**empty, "coverage": round(cov, 3)}
    tu, xu = sig.resample_uniform(t, x, fs)
    # Velocity from the pose by default: it is what every runtime gives, and runtime-reported velocity
    # may be filtered or predicted. Reported velocity is used only when there is no position.
    v = sig.derivative(xu, fs) if track.pos is not None else xu
    tremor_ms2 = sig.band_power(v, fs, *TREMOR_BAND)
    restless_ms2 = sig.band_power(v, fs, *RESTLESS_BAND)
    sp = sig.speed(sig.moving_average(v, max(1, round(SPEED_SMOOTH_S * fs))))
    i = int(np.argmax(sp))
    return {
        "tremor_rms": _num(math.sqrt(tremor_ms2) * 1000.0),          # mm/s
        "tremor_peak_hz": _num(sig.dominant_frequency(v, fs, *TREMOR_BAND)),
        "restless_rms": _num(math.sqrt(restless_ms2) * 1000.0),      # mm/s
        "hand_speed_mean": _num(float(sp.mean())),                    # m/s
        "hand_speed_peak": _num(float(sp[i])),
        "hand_speed_peak_t": _num(float(tu[i])),
        "coverage": round(cov, 3),
    }


def head_features(track: DeviceTrack | None, t0: float, t1: float, nominal_fs: float) -> dict[str, float | None]:
    """Rotation speed, scanning and translation for the headset."""
    empty = {"head_ang_speed_mean": None, "head_ang_speed_peak": None, "head_ang_speed_peak_t": None,
             "head_yaw_scan_rate": None, "head_yaw_reversals": None, "head_trans_speed_mean": None,
             "head_coverage": 0.0}
    if track is None or track.rot is None:
        return empty
    t, q = sig.clean_series(track.t, track.rot, track.tracked)
    cov = _coverage(t, t0, t1, nominal_fs or sig.estimate_rate(t))
    if len(t) < sig.MIN_SAMPLES or cov < MIN_COVERAGE:
        return {**empty, "head_coverage": round(cov, 3)}
    tm, w = sig.angular_speed(t, q)
    # A single bad frame (tracking hiccup) can produce an absurd spike; clip at a physical maximum.
    w = np.minimum(w, 1500.0)
    w = sig.moving_average(w[:, None], max(1, round(SPEED_SMOOTH_S * sig.estimate_rate(t)))).ravel()
    yaw = sig.yaw_degrees(q)
    duration = max(t[-1] - t[0], 1e-6)
    out = {
        "head_ang_speed_mean": _num(float(w.mean())),
        "head_ang_speed_peak": _num(float(w.max())),
        "head_ang_speed_peak_t": _num(float(tm[int(np.argmax(w))])),
        "head_yaw_scan_rate": _num(float(np.abs(np.diff(yaw)).sum() / duration)),
        "head_yaw_reversals": sig.count_reversals(yaw, YAW_REVERSAL_DEG),
        "head_trans_speed_mean": None,
        "head_coverage": round(cov, 3),
    }
    if track.pos is not None:
        tp, p = sig.clean_series(track.t, track.pos, track.tracked)
        if len(tp) >= sig.MIN_SAMPLES:
            fs = _grid_rate(tp)
            _, pu = sig.resample_uniform(tp, p, fs)
            v = sig.moving_average(sig.derivative(pu, fs), max(1, round(SPEED_SMOOTH_S * fs)))
            out["head_trans_speed_mean"] = _num(float(sig.speed(v).mean()))
    return out


def window_features(t0: float, t1: float, head: DeviceTrack | None, left: DeviceTrack | None,
                    right: DeviceTrack | None, nominal_fs: float = 0.0,
                    audio: Mapping[str, Any] | None = None) -> dict[str, Any]:
    """All features of one window, flat, JSON-safe. ``audio`` is the headset's breathing features, if any."""
    f: dict[str, Any] = {"t_start": _num(t0), "t_end": _num(t1)}
    f.update(head_features(head, t0, t1, nominal_fs))
    for side, track in (("left", left), ("right", right)):
        for k, v in hand_features(track, t0, t1, nominal_fs).items():
            f[f"{side}_{k}"] = v
    if audio:
        for k in AUDIO_FIELDS:
            v = audio.get(k)
            f[f"audio_{k}"] = (int(v) if k == "deep_breaths" else _num(float(v))) if v is not None else None
    return f


# ---- baseline --------------------------------------------------------------------------------------

def compute_baseline(windows: Sequence[Mapping[str, Any]], min_windows: int = 10) -> dict[str, Any]:
    """Median and MAD of each baseline feature over the calibration windows.

    Returns {"status": "ok" | "short" | "missing", "windows": n, "features": {name: {"median", "mad", "n"}}}.
    "short" means fewer than ``min_windows`` usable windows: ratios are still given but should be read with care."""
    feats: dict[str, dict[str, float]] = {}
    for name in BASELINE_FEATURES:
        vals = np.array([w[name] for w in windows if w.get(name) is not None], dtype=float)
        if len(vals) == 0:
            continue
        med = float(np.median(vals))
        mad = float(np.median(np.abs(vals - med)))
        feats[name] = {"median": _num(med), "mad": _num(mad), "n": int(len(vals))}
    n = len(windows)
    status = "missing" if not feats else ("ok" if n >= min_windows else "short")
    return {"status": status, "windows": n, "features": feats}


def _ratio(value: float | None, name: str, baseline: Mapping[str, Any] | None) -> float | None:
    if value is None or not baseline:
        return None
    b = (baseline.get("features") or {}).get(name)
    if not b or b.get("median") is None:
        return None
    denom = max(float(b["median"]), BASELINE_FLOOR.get(name, 1e-6))
    return _num(min(float(value) / denom, RATIO_CAP))


def relative_to_baseline(features: Mapping[str, Any], baseline: Mapping[str, Any] | None) -> dict[str, float | None]:
    """Each baseline feature of a window as a ratio to the person's own calibration median (1.0 = as calm)."""
    return {name: _ratio(features.get(name), name, baseline) for name in BASELINE_FEATURES}


def is_freeze(features: Mapping[str, Any], baseline: Mapping[str, Any] | None) -> bool | None:
    """True when head and both hands are all much stiller than this person's own calm baseline."""
    head = features.get("head_ang_speed_mean")
    hands = [features.get("left_hand_speed_mean"), features.get("right_hand_speed_mean")]
    if head is None or any(h is None for h in hands):
        return None
    bf = (baseline or {}).get("features") or {}
    if not all(bf.get(k) for k in ("head_ang_speed_mean", "left_hand_speed_mean", "right_hand_speed_mean")):
        return None   # no personal baseline: never fall back to an absolute threshold

    def still(value: float, name: str, ceiling_key: str) -> bool:
        limit = FREEZE_CEILING[ceiling_key]
        b = bf.get(name)
        if b and b.get("median") is not None:
            limit = min(limit, FREEZE_FRACTION * float(b["median"]))
        return value < limit

    return (still(head, "head_ang_speed_mean", "head_ang_speed_mean")
            and still(hands[0], "left_hand_speed_mean", "hand_speed_mean")
            and still(hands[1], "right_hand_speed_mean", "hand_speed_mean"))


# ---- per-question records --------------------------------------------------------------------------

def _mean(xs: Iterable[float | None]) -> float | None:
    vals = [x for x in xs if x is not None]
    return _num(float(np.mean(vals))) if vals else None


def _pct(xs: Iterable[float | None], q: float) -> float | None:
    vals = [x for x in xs if x is not None]
    return _num(float(np.percentile(vals, q))) if vals else None


def question_spans(events: Sequence[Mapping[str, Any]]) -> dict[str, tuple[float, float | None]]:
    """question_key -> (t_shown, t_end or None while open), from the session's events in time order.

    A question ends at a correct answer_submitted, an answer_submitted marked ``payload.final`` (an answer
    the game does not let you retry, such as the one-shot quiz), an abandon/death/session_end, or when
    another question is shown. A wrong, non-final answer keeps it open (the game leaves the puzzle up)."""
    spans: dict[str, tuple[float, float | None]] = {}
    current: str | None = None
    for e in sorted(events, key=lambda e: e["t"]):
        typ, key, t = e["type"], e.get("question_key"), float(e["t"])
        if typ == "question_shown" and key:
            if current and current != key and spans[current][1] is None:
                spans[current] = (spans[current][0], t)
            if key not in spans:
                spans[key] = (t, None)
            elif spans[key][1] is not None:
                spans[key] = (spans[key][0], None)   # shown again after it closed: reopen, keep first shown time
            current = key
        elif current and ((typ == "answer_submitted" and key in (None, current)
                           and ((e.get("payload") or {}).get("correct") is True or (e.get("payload") or {}).get("final") is True))
                          or (typ in QUESTION_END_EVENTS and key in (None, current))):
            if spans[current][1] is None:
                spans[current] = (spans[current][0], t)
            current = None
    return spans


def _startles(windows: Sequence[Mapping[str, Any]], events: Sequence[Mapping[str, Any]],
              baseline: Mapping[str, Any] | None) -> list[dict[str, Any]]:
    """For each sudden stimulus, the peak head rotation and hand speed in the 1.5 s after it, relative to
    the level just before it (or the person's baseline when that is higher)."""
    out = []
    bf = (baseline or {}).get("features") or {}
    for e in events:
        if e["type"] not in STARTLE_EVENTS:
            continue
        te = float(e["t"])
        pre = [w for w in windows if w.get("t_end") is not None and te - STARTLE_PRE_S <= w["t_end"] <= te]

        def level(name: str) -> float:
            vals = [w.get(name) for w in pre if w.get(name) is not None]
            base = (bf.get(name) or {}).get("median")
            cands = [float(np.median(vals))] if vals else []
            if base is not None:
                cands.append(float(base))
            return max(cands + [BASELINE_FLOOR.get(name, 1e-6)])

        head_peak, head_t = None, None
        hand_peak = None
        for w in windows:
            pt = w.get("head_ang_speed_peak_t")
            if pt is not None and te <= pt <= te + STARTLE_WINDOW_S and w.get("head_ang_speed_peak") is not None:
                if head_peak is None or w["head_ang_speed_peak"] > head_peak:
                    head_peak, head_t = w["head_ang_speed_peak"], pt
            for side in ("left", "right"):
                ht = w.get(f"{side}_hand_speed_peak_t")
                hv = w.get(f"{side}_hand_speed_peak")
                if ht is not None and hv is not None and te <= ht <= te + STARTLE_WINDOW_S:
                    hand_peak = hv if hand_peak is None else max(hand_peak, hv)
        hand_level = max(level("left_hand_speed_mean"), level("right_hand_speed_mean"))
        out.append({
            "event_type": e["type"],
            "t_event": _num(te),
            "head_peak_deg_s": _num(head_peak) if head_peak is not None else None,
            "head_peak_ratio": _num(min(head_peak / level("head_ang_speed_mean"), RATIO_CAP)) if head_peak is not None else None,
            "hand_peak_m_s": _num(hand_peak) if hand_peak is not None else None,
            "hand_peak_ratio": _num(min(hand_peak / hand_level, RATIO_CAP)) if hand_peak is not None else None,
            "latency_s": _num(head_t - te) if head_t is not None else None,
        })
    return out


def question_record(question_key: str, windows: Sequence[Mapping[str, Any]], events: Sequence[Mapping[str, Any]],
                    baseline: Mapping[str, Any] | None, span: tuple[float, float | None] | None = None,
                    now_t: float | None = None) -> dict[str, Any]:
    """One QS record: everything measured about how a person moved and acted on one question.

    ``windows`` are window feature dicts (any order, may include other questions: they are filtered by time
    span when ``span`` is given, else by their own question_key). ``events`` are the session's events."""
    events = sorted(events, key=lambda e: e["t"])
    if span is None:
        span = question_spans(events).get(question_key, (None, None))  # type: ignore[assignment]
    t_shown, t_end = span if span else (None, None)
    closed = t_end is not None
    if t_shown is not None:
        hi = t_end if t_end is not None else math.inf
        q_events = [e for e in events if t_shown <= float(e["t"]) <= hi]
        q_windows = [w for w in windows if w.get("t_end") is not None and w["t_end"] > t_shown
                     and (w.get("t_start") or 0) < hi]
    else:
        q_events = [e for e in events if e.get("question_key") == question_key]
        q_windows = [w for w in windows if w.get("question_key") == question_key]
    q_windows = sorted(q_windows, key=lambda w: w["t_start"])

    # ---- behaviour from game events
    actions = [e for e in q_events if e["type"] in ACTION_EVENTS]
    submits = [e for e in q_events if e["type"] == "answer_submitted"]
    correct_flags = [(e.get("payload") or {}).get("correct") for e in submits]
    first_action = actions[0]["t"] if actions else None
    first_submit = submits[0]["t"] if submits else None
    end_for_gaps = t_end if t_end is not None else now_t
    gaps: list[float] = []
    if first_action is not None:
        times = [float(e["t"]) for e in actions]
        if end_for_gaps is not None:
            times.append(float(end_for_gaps))
        gaps = [b - a for a, b in zip(times, times[1:]) if b - a >= HESITATION_GAP_S]
    if any(f is True for f in correct_flags):
        outcome = "correct"
    elif any(e["type"] == "death" for e in q_events):
        outcome = "died"
    elif not closed:
        outcome = "open"
    else:
        outcome = "incorrect" if submits else "abandoned"

    behaviour = {
        "time_to_first_action_s": _num(first_action - t_shown) if first_action is not None and t_shown is not None else None,
        "response_latency_s": _num(first_submit - t_shown) if first_submit is not None and t_shown is not None else None,
        "duration_s": _num((t_end if t_end is not None else (now_t or t_shown or 0)) - t_shown) if t_shown is not None else None,
        "submissions": len(submits),
        "incorrect_submissions": sum(1 for f in correct_flags if f is False),
        "answer_changes": sum(1 for e in q_events if e["type"] in CHANGE_EVENTS),
        "hesitations": len(gaps),
        "hesitation_total_s": _num(sum(gaps)) if gaps else 0.0,
        "hints_used": sum(1 for e in q_events if e["type"] == "hint_used"),
    }

    # ---- motion from windows
    rel = [relative_to_baseline(w, baseline) for w in q_windows]
    freeze_flags = [is_freeze(w, baseline) for w in q_windows]
    hop = _median_hop(q_windows)
    freeze_runs = _runs(freeze_flags)
    fidget = _fidget_index(q_windows, q_events, baseline)
    motion = {
        "windows": len(q_windows),
        "left_tremor_rms_mm_s": _mean(w.get("left_tremor_rms") for w in q_windows),
        "right_tremor_rms_mm_s": _mean(w.get("right_tremor_rms") for w in q_windows),
        "left_tremor_ratio": _pct((r["left_tremor_rms"] for r in rel), 50),
        "right_tremor_ratio": _pct((r["right_tremor_rms"] for r in rel), 50),
        "left_tremor_ratio_p90": _pct((r["left_tremor_rms"] for r in rel), 90),
        "right_tremor_ratio_p90": _pct((r["right_tremor_rms"] for r in rel), 90),
        "head_ang_speed_deg_s": _mean(w.get("head_ang_speed_mean") for w in q_windows),
        "head_ang_speed_ratio": _pct((r["head_ang_speed_mean"] for r in rel), 50),
        "head_scan_rate_deg_s": _mean(w.get("head_yaw_scan_rate") for w in q_windows),
        "head_scan_ratio": _pct((r["head_yaw_scan_rate"] for r in rel), 50),
        "head_yaw_reversals_per_min": _reversals_per_min(q_windows, hop),
        "restless_ratio": _pct((_mean([r["left_restless_rms"], r["right_restless_rms"]]) for r in rel), 50),
        "fidget_index": fidget,
        "freeze_s": _num(sum(1 for f in freeze_flags if f) * hop) if q_windows else None,
        "longest_freeze_s": _num(max(freeze_runs, default=0) * hop) if q_windows else None,
        "freeze_fraction": _num(sum(1 for f in freeze_flags if f) / max(1, sum(1 for f in freeze_flags if f is not None))) if q_windows else None,
    }
    startles = _startles(windows, q_events, baseline)
    motion["startles"] = startles
    motion["startle_max_head_ratio"] = _num(max((s["head_peak_ratio"] for s in startles if s["head_peak_ratio"] is not None), default=float("nan")))

    a_windows = [w for w in q_windows if any(w.get(f"audio_{k}") is not None for k in AUDIO_FIELDS)]
    audio = None
    if a_windows:
        a_rel = [relative_to_baseline(w, baseline) for w in a_windows]
        hold = [w.get("audio_since_last_breath_s") for w in a_windows if w.get("audio_since_last_breath_s") is not None]
        audio = {
            "windows": len(a_windows),
            "breath_rate_bpm": _pct((w.get("audio_breath_rate_bpm") for w in a_windows), 50),
            "breath_rate_bpm_p90": _pct((w.get("audio_breath_rate_bpm") for w in a_windows), 90),
            "breath_rate_ratio": _pct((r["audio_breath_rate_bpm"] for r in a_rel), 50),
            "breath_rate_ratio_p90": _pct((r["audio_breath_rate_bpm"] for r in a_rel), 90),
            "breath_amp_ratio": _pct((r["audio_breath_amp"] for r in a_rel), 50),
            # counted by the headset in non-overlapping hops, so a plain sum counts each deep breath once
            "deep_breaths": int(sum(w.get("audio_deep_breaths") or 0 for w in a_windows)),
            "longest_breath_hold_s": _num(max(hold)) if hold else None,
            "voice_fraction": _mean(w.get("audio_voice_fraction") for w in a_windows),
        }

    head_cov = [w.get("head_coverage") for w in q_windows]
    hand_cov = [min(w.get("left_coverage") or 0, w.get("right_coverage") or 0) for w in q_windows]
    quality = {
        "baseline_status": (baseline or {}).get("status", "missing"),
        "head_coverage": _mean(head_cov),
        "hand_coverage": _mean(hand_cov),
    }
    record = {
        "qs_version": QS_VERSION,
        "question_key": question_key,
        "status": "closed" if closed else "open",
        "outcome": outcome,
        "t_shown": _num(t_shown) if t_shown is not None else None,
        "t_end": _num(t_end) if t_end is not None else None,
        "behaviour": behaviour,
        "motion": motion,
        "audio": audio,
        "quality": quality,
    }
    from .states import record_states   # states builds on this module's constants; imported here to avoid a cycle
    record["states"] = record_states(record)
    return record


def _median_hop(windows: Sequence[Mapping[str, Any]]) -> float:
    starts = [w["t_start"] for w in windows if w.get("t_start") is not None]
    if len(starts) < 2:
        return 0.5
    return float(np.median(np.diff(sorted(starts)))) or 0.5


def _runs(flags: Sequence[bool | None]) -> list[int]:
    runs, n = [], 0
    for f in flags:
        if f:
            n += 1
        else:
            if n:
                runs.append(n)
            n = 0
    if n:
        runs.append(n)
    return runs


def _reversals_per_min(windows: Sequence[Mapping[str, Any]], hop: float) -> float | None:
    # Windows overlap (1 s windows every 0.5 s), so sum over windows and scale by window/hop overlap.
    vals = [w.get("head_yaw_reversals") for w in windows if w.get("head_yaw_reversals") is not None]
    if not vals:
        return None
    lengths = [w["t_end"] - w["t_start"] for w in windows if w.get("t_end") is not None]
    win = float(np.median(lengths)) if lengths else 1.0
    overlap = max(win / hop, 1.0) if hop > 0 else 1.0
    minutes = len(vals) * hop / 60.0
    return _num(sum(vals) / overlap / minutes) if minutes > 0 else None


def _fidget_index(windows: Sequence[Mapping[str, Any]], events: Sequence[Mapping[str, Any]],
                  baseline: Mapping[str, Any] | None) -> float | None:
    """Fraction of windows with hand movement in the 0.5-3 Hz band above 2x the person's calibration
    median while no input action happened in that window: movement that is not doing the task."""
    if not windows:
        return None
    action_times = [float(e["t"]) for e in events if e["type"] in ACTION_EVENTS]
    flagged = counted = 0
    for w in windows:
        r = relative_to_baseline(w, baseline)
        vals = [v for v in (r["left_restless_rms"], r["right_restless_rms"]) if v is not None]
        if not vals:
            continue
        counted += 1
        acting = any(w["t_start"] <= t <= w["t_end"] for t in action_times)
        if not acting and max(vals) >= 2.0:
            flagged += 1
    return _num(flagged / counted) if counted else None


def session_summary(records: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
    """Roll-up of a session's QS records for the per-candidate view. Observations only: no score."""
    closed = [r for r in records if r.get("status") == "closed"]

    def m(path: tuple[str, str]) -> float | None:
        return _mean((r.get(path[0]) or {}).get(path[1]) for r in records)

    return {
        "questions": len(records),
        "closed": len(closed),
        "correct": sum(1 for r in records if r.get("outcome") == "correct"),
        "mean_response_latency_s": m(("behaviour", "response_latency_s")),
        "mean_time_to_first_action_s": m(("behaviour", "time_to_first_action_s")),
        "total_answer_changes": sum((r.get("behaviour") or {}).get("answer_changes", 0) for r in records),
        "total_hesitations": sum((r.get("behaviour") or {}).get("hesitations", 0) for r in records),
        "median_left_tremor_ratio": m(("motion", "left_tremor_ratio")),
        "median_right_tremor_ratio": m(("motion", "right_tremor_ratio")),
        "mean_head_ang_speed_ratio": m(("motion", "head_ang_speed_ratio")),
        "total_freeze_s": _num(sum((r.get("motion") or {}).get("freeze_s") or 0 for r in records)),
        "startles": sum(len((r.get("motion") or {}).get("startles") or []) for r in records),
        "states": {k: sum(1 for r in records for st in r.get("states") or [] if st["key"] == k)
                   for k in sorted({st["key"] for r in records for st in r.get("states") or []})},
    }

"""Possible states: plain-language readings of the QS features, for one window (live) or one question (record).

Rules, not a model. Every rule compares a feature with **the same person's calm calibration** (a ratio, 1.0 =
as calm as at the start) and fires only above a stated multiple. Each state carries:

* ``key`` and ``label``: what it might be ("startle / fright", "signs of stress", "shaking (tremor)" …);
* ``likelihood``: ``possible`` or ``likely``. Never more certain than that;
* ``evidence``: the features behind it, their values, the multiple of baseline, in words;
* ``near_event``: the game event it followed (for example the monster appearing), when there is one;
* ``could_also_be``: ordinary explanations that produce the same signal.

What states are not: a diagnosis, a judgement of the person, or a hiring signal. They describe moments and
questions. There is no state about the candidate as a whole and no overall score. The thresholds below are
reasoned defaults checked only on synthetic signals (``tests/test_states.py``); they have not been validated
on real people.
"""
from __future__ import annotations

from typing import Any, Iterable, Mapping, Sequence

from .features import BASELINE_FLOOR, STARTLE_EVENTS

# --- thresholds (multiples of the person's own calibration median) -----------------------------------
TREMOR_POSSIBLE, TREMOR_LIKELY = 2.0, 3.5
TREMOR_MIN_MM_S = 4.0            # below this the 4-12 Hz band is controller jitter, whatever the ratio
RESTLESS_POSSIBLE, RESTLESS_LIKELY = 2.5, 4.0
SCAN_POSSIBLE, SCAN_LIKELY = 3.0, 5.0
STARTLE_POSSIBLE, STARTLE_LIKELY = 4.0, 8.0
STARTLE_HAND_LIKELY = 4.0
STARTLE_AFTER_S = 1.5            # a reaction must peak within this time after the stimulus
FREEZE_AFTER_STARTLE_S = 6.0     # stillness this soon after a stimulus reads as freezing rather than focus
BREATH_FAST_POSSIBLE, BREATH_FAST_LIKELY = 1.35, 1.6
BREATH_FAST_MIN_BPM = 20.0
BREATH_DEEP_RATIO = 1.6          # breath sound this much louder than calibration
BREATH_HOLD_POSSIBLE_S, BREATH_HOLD_LIKELY_S = 7.0, 10.0
VOICE_MAX = 0.3                  # more speech than this in a window and the breathing numbers are not used
CALM_MAX_RATIO = 1.5

COULD_ALSO_BE = {
    "startle": "a real-world noise or touch, or adjusting the headset.",
    "stress": "excitement, physical effort, or discomfort with the headset.",
    "restless": "looking for something in the game, stretching, or an uncomfortable headset.",
    "freeze": "concentrating hard or reading carefully.",
    "tremor": "cold, fatigue, caffeine, a medical tremor, or a loose grip on the controller.",
    "fast_breathing": "physical effort, talking, a cold or blocked nose.",
    "deep_breath": "a deliberate breath to settle, a yawn, or the mic catching a cough.",
    "breath_hold": "concentration, or the microphone losing the breath sound (headset moved, room noise).",
    "calm": "a person who shows little outwardly whatever they feel.",
}

LABELS = {
    "startle": "startle / fright",
    "stress": "signs of stress",
    "restless": "restless / a lot of movement",
    "freeze": "freezing",
    "tremor": "shaking (tremor)",
    "fast_breathing": "fast breathing",
    "deep_breath": "deep breath or sigh",
    "breath_hold": "holding breath",
    "calm": "calm",
}

# Order on screen: the most specific first.
ORDER = ["startle", "stress", "tremor", "freeze", "breath_hold", "fast_breathing", "deep_breath", "restless", "calm"]

EVENT_WORDS = {
    "monster_appeared": "the monster appeared", "monster_lunge": "the monster lunged",
    "monster_stage": "the monster changed", "jumpscare": "the jump scare", "answer_wrong_fx": "a wrong answer cracked the glass",
}


def _state(key: str, likelihood: str, evidence: list[dict], question_key: str | None = None,
           near_event: dict | None = None, t: float | None = None) -> dict[str, Any]:
    return {"key": key, "label": LABELS[key], "likelihood": likelihood, "evidence": evidence,
            "question_key": question_key, "near_event": near_event, "t": t,
            "could_also_be": COULD_ALSO_BE[key]}


def _ev(feature: str, value: Any, ratio: float | None, text: str, unit: str = "") -> dict:
    return {"feature": feature, "value": value, "unit": unit, "ratio": None if ratio is None else round(ratio, 2), "text": text}


def _x(r: float) -> str:
    return f"{r:.1f}×"


def _base(baseline: Mapping[str, Any] | None, name: str) -> float | None:
    b = ((baseline or {}).get("features") or {}).get(name) or {}
    m = b.get("median")
    return None if m is None else max(float(m), BASELINE_FLOOR.get(name, 1e-6))


def _max(*vals: float | None) -> float | None:
    v = [x for x in vals if x is not None]
    return max(v) if v else None


def sort_states(states: Iterable[dict]) -> list[dict]:
    return sorted(states, key=lambda s: (ORDER.index(s["key"]), 0 if s["likelihood"] == "likely" else 1))


def _event_ref(e: Mapping[str, Any], t_ref: float | None) -> dict:
    te = float(e["t"])
    return {"type": e["type"], "t": round(te, 2), "dt": None if t_ref is None else round(t_ref - te, 2),
            "text": EVENT_WORDS.get(e["type"], e["type"].replace("_", " "))}


# ---- per window (live) ------------------------------------------------------------------------------

def window_states(features: Mapping[str, Any], rel: Mapping[str, Any] | None, baseline: Mapping[str, Any] | None,
                  events: Sequence[Mapping[str, Any]] = (), freeze: bool | None = None) -> list[dict]:
    """States for one window. ``rel`` are the window's ratios to baseline (``features.relative_to_baseline``);
    ``events`` are recent game events (any order; only stimuli up to a few seconds before the window matter).
    Without a baseline there is nothing to compare with, so the result is empty."""
    if not baseline or baseline.get("status") == "missing" or not rel:
        return []
    qk = features.get("question_key")
    t0, t1 = features.get("t_start"), features.get("t_end")
    out: list[dict] = []
    stimuli = sorted((e for e in events if e.get("type") in STARTLE_EVENTS), key=lambda e: e["t"])

    # startle: a head (or hand) peak right after a sudden stimulus, far above the calm level
    head_base = _base(baseline, "head_ang_speed_mean")
    hand_base = _max(_base(baseline, "left_hand_speed_mean"), _base(baseline, "right_hand_speed_mean"))
    pk, pk_t = features.get("head_ang_speed_peak"), features.get("head_ang_speed_peak_t")
    startle_event = None
    if pk is not None and pk_t is not None and head_base:
        for e in reversed(stimuli):
            if 0 <= pk_t - float(e["t"]) <= STARTLE_AFTER_S:
                startle_event = e
                break
    if startle_event is not None:
        head_r = pk / head_base
        hand_pk = _max(features.get("left_hand_speed_peak"), features.get("right_hand_speed_peak"))
        hand_r = hand_pk / hand_base if hand_pk is not None and hand_base else None
        if head_r >= STARTLE_POSSIBLE:
            ev = [_ev("head_ang_speed_peak", pk, head_r, f"head turned {_x(head_r)} faster than their calm level", "deg/s")]
            if hand_r is not None and hand_r >= 2:
                ev.append(_ev("hand_speed_peak", hand_pk, hand_r, f"hands jerked {_x(hand_r)} faster than calm", "m/s"))
            likely = head_r >= STARTLE_LIKELY or (hand_r is not None and hand_r >= STARTLE_HAND_LIKELY)
            out.append(_state("startle", "likely" if likely else "possible", ev, qk,
                              _event_ref(startle_event, pk_t), pk_t))

    # shaking: 4-12 Hz hand motion well above calibration and above controller jitter
    tremor = None
    for side in ("left", "right"):
        r, v = rel.get(f"{side}_tremor_rms"), features.get(f"{side}_tremor_rms")
        if r is not None and v is not None and v >= TREMOR_MIN_MM_S and r >= TREMOR_POSSIBLE and (tremor is None or r > tremor[1]):
            tremor = (side, r, v, features.get(f"{side}_tremor_peak_hz"))
    if tremor:
        side, r, v, hz = tremor
        hz_txt = f" around {hz:.0f} Hz" if hz else ""
        out.append(_state("tremor", "likely" if r >= TREMOR_LIKELY else "possible",
                          [_ev(f"{side}_tremor_rms", v, r, f"{side} hand shaking{hz_txt}, {_x(r)} their calm level", "mm/s")],
                          qk, None, t1))

    # restless: slow repetitive hand movement or a lot of head scanning
    rest = _max(rel.get("left_restless_rms"), rel.get("right_restless_rms"))
    scan = rel.get("head_yaw_scan_rate")
    rest_ev, rest_likely = [], False
    if rest is not None and rest >= RESTLESS_POSSIBLE:
        rest_ev.append(_ev("restless_rms", None, rest, f"slow hand movement {_x(rest)} their calm level"))
        rest_likely |= rest >= RESTLESS_LIKELY
    if scan is not None and scan >= SCAN_POSSIBLE:
        rest_ev.append(_ev("head_yaw_scan_rate", features.get("head_yaw_scan_rate"), scan, f"looking around {_x(scan)} as much as when calm", "deg/s"))
        rest_likely |= scan >= SCAN_LIKELY
    if rest_ev:
        out.append(_state("restless", "likely" if rest_likely else "possible", rest_ev, qk, None, t1))

    # freezing: head and both hands far stiller than calm (features.is_freeze), worth more right after a stimulus
    if freeze:
        recent = [e for e in stimuli if t1 is not None and 0 <= t1 - float(e["t"]) <= FREEZE_AFTER_STARTLE_S]
        ev = [_ev("freeze", True, None, "head and both hands below half their calm movement")]
        out.append(_state("freeze", "likely" if recent else "possible", ev, qk,
                          _event_ref(recent[-1], t1) if recent else None, t1))

    out.extend(_breath_states(features, rel, qk, t1, stimuli))
    out = _with_stress(out, qk, t1)
    if not out:
        out = _calm(rel, features, qk, t1)
    return sort_states(out)


def _breath_states(features, rel, qk, t, stimuli) -> list[dict]:
    voice = features.get("audio_voice_fraction")
    if voice is not None and voice > VOICE_MAX:
        return []      # talking: the breathing numbers do not mean breathing
    out = []
    rate, rate_r = features.get("audio_breath_rate_bpm"), rel.get("audio_breath_rate_bpm")
    if rate is not None and rate_r is not None and rate >= BREATH_FAST_MIN_BPM and rate_r >= BREATH_FAST_POSSIBLE:
        out.append(_state("fast_breathing", "likely" if rate_r >= BREATH_FAST_LIKELY else "possible",
                          [_ev("audio_breath_rate_bpm", rate, rate_r, f"breathing {rate:.0f} times a minute, {_x(rate_r)} their calm rate", "/min")],
                          qk, None, t))
    deep, amp_r = features.get("audio_deep_breaths") or 0, rel.get("audio_breath_amp")
    if deep >= 1 or (amp_r is not None and amp_r >= BREATH_DEEP_RATIO):
        ev = []
        if deep >= 1:
            ev.append(_ev("audio_deep_breaths", deep, None, "a breath much deeper than the ones around it"))
        if amp_r is not None and amp_r >= BREATH_DEEP_RATIO:
            ev.append(_ev("audio_breath_amp", features.get("audio_breath_amp"), amp_r, f"breath sounds {_x(amp_r)} as loud as when calm"))
        out.append(_state("deep_breath", "possible", ev, qk, None, t))
    hold = features.get("audio_since_last_breath_s")
    if hold is not None and hold >= BREATH_HOLD_POSSIBLE_S and features.get("audio_breath_amp") is not None:
        recent = [e for e in stimuli if t is not None and 0 <= t - float(e["t"]) <= hold + 2]
        out.append(_state("breath_hold", "likely" if hold >= BREATH_HOLD_LIKELY_S else "possible",
                          [_ev("audio_since_last_breath_s", hold, None, f"no breath heard for {hold:.0f} s", "s")],
                          qk, _event_ref(recent[-1], t) if recent else None, t))
    return out


def _with_stress(states: list[dict], qk, t) -> list[dict]:
    """Signs of stress: breathing changes together with shaking or restlessness (likely), or shaking and
    restlessness together without breathing data (possible). Built only from states already found."""
    keys = {s["key"]: s for s in states}
    breath = [keys[k] for k in ("fast_breathing", "deep_breath") if k in keys]
    motor = [keys[k] for k in ("tremor", "restless") if k in keys]
    if breath and motor:
        level = "likely"
    elif "tremor" in keys and "restless" in keys:
        level = "possible"
    else:
        return states
    ev = [e for s in breath + motor for e in s["evidence"]]
    near = next((s["near_event"] for s in states if s.get("near_event")), None)
    return states + [_state("stress", level, ev, qk, near, t)]


def _calm(rel, features, qk, t) -> list[dict]:
    vals = [v for v in rel.values() if v is not None]
    if not vals or max(vals) > CALM_MAX_RATIO:
        return []
    has_audio = rel.get("audio_breath_rate_bpm") is not None
    return [_state("calm", "likely" if has_audio else "possible",
                   [_ev("all", None, max(vals), f"every measure within {_x(CALM_MAX_RATIO)} of their calm calibration")],
                   qk, None, t)]


# ---- per question (record) --------------------------------------------------------------------------

def record_states(record: Mapping[str, Any]) -> list[dict]:
    """States for one question, from its QS record (``features.question_record``). Pure: record in, list out."""
    m = record.get("motion") or {}
    a = record.get("audio") or {}
    qk = record.get("question_key")
    if (record.get("quality") or {}).get("baseline_status") in (None, "missing"):
        return []
    out: list[dict] = []

    startles = [s for s in m.get("startles") or [] if s.get("head_peak_ratio") is not None]
    strong = max(startles, key=lambda s: s["head_peak_ratio"], default=None)
    if strong and strong["head_peak_ratio"] >= STARTLE_POSSIBLE:
        r, hr = strong["head_peak_ratio"], strong.get("hand_peak_ratio")
        ev = [_ev("motion.startle_max_head_ratio", strong.get("head_peak_deg_s"), r,
                  f"head turned {_x(r)} faster than just before, {strong.get('latency_s') or 0:.1f} s after the stimulus", "deg/s")]
        if hr is not None and hr >= 2:
            ev.append(_ev("motion.startles.hand_peak_ratio", strong.get("hand_peak_m_s"), hr, f"hands jerked {_x(hr)} faster", "m/s"))
        likely = r >= STARTLE_LIKELY or (hr is not None and hr >= STARTLE_HAND_LIKELY)
        out.append(_state("startle", "likely" if likely else "possible", ev, qk,
                          _event_ref({"type": strong["event_type"], "t": strong["t_event"]}, None), strong.get("t_event")))

    p90 = _max(m.get("left_tremor_ratio_p90"), m.get("right_tremor_ratio_p90"))
    med = _max(m.get("left_tremor_ratio"), m.get("right_tremor_ratio"))
    mm = _max(m.get("left_tremor_rms_mm_s"), m.get("right_tremor_rms_mm_s"))
    if p90 is not None and p90 >= TREMOR_POSSIBLE + 0.5 and (mm or 0) >= TREMOR_MIN_MM_S * 0.75:
        ev = [_ev("motion.tremor_ratio_p90", None, p90, f"hands shook up to {_x(p90)} their calm level")]
        if med is not None:
            ev.append(_ev("motion.tremor_ratio", None, med, f"typically {_x(med)} through the question"))
        out.append(_state("tremor", "likely" if (med or 0) >= TREMOR_POSSIBLE + 0.5 else "possible", ev, qk, None, None))

    rr, fi, sr = m.get("restless_ratio"), m.get("fidget_index"), m.get("head_scan_ratio")
    ev, likely = [], False
    if rr is not None and rr >= 2.0:
        ev.append(_ev("motion.restless_ratio", rr, rr, f"slow hand movement {_x(rr)} their calm level")); likely |= rr >= 3.0
    if fi is not None and fi >= 0.3:
        ev.append(_ev("motion.fidget_index", fi, None, f"moving without using the controls {fi:.0%} of the time")); likely |= fi >= 0.5
    if sr is not None and sr >= 2.5:
        ev.append(_ev("motion.head_scan_ratio", sr, sr, f"looked around {_x(sr)} as much as when calm")); likely |= sr >= 4.0
    if ev:
        out.append(_state("restless", "likely" if likely else "possible", ev, qk, None, None))

    lf = m.get("longest_freeze_s")
    if lf is not None and lf >= 2.0:
        after = strong if strong and strong["head_peak_ratio"] >= STARTLE_POSSIBLE else None
        out.append(_state("freeze", "likely" if lf >= 4.0 or after else "possible",
                          [_ev("motion.longest_freeze_s", lf, None, f"held almost completely still for {lf:.1f} s", "s")], qk,
                          _event_ref({"type": after["event_type"], "t": after["t_event"]}, None) if after else None, None))

    if (a.get("voice_fraction") or 0) <= VOICE_MAX:
        br, brr = a.get("breath_rate_bpm"), a.get("breath_rate_ratio_p90")
        if br is not None and brr is not None and brr >= BREATH_FAST_POSSIBLE and (a.get("breath_rate_bpm_p90") or br) >= BREATH_FAST_MIN_BPM:
            out.append(_state("fast_breathing", "likely" if brr >= BREATH_FAST_LIKELY else "possible",
                              [_ev("audio.breath_rate_ratio_p90", a.get("breath_rate_bpm_p90"), brr,
                                   f"breathing up to {a.get('breath_rate_bpm_p90') or br:.0f} a minute, {_x(brr)} their calm rate", "/min")], qk, None, None))
        deep, ampr = a.get("deep_breaths") or 0, a.get("breath_amp_ratio")
        if deep >= 1 or (ampr is not None and ampr >= BREATH_DEEP_RATIO):
            ev = []
            if deep:
                ev.append(_ev("audio.deep_breaths", deep, None, f"{deep} deep breath{'s' if deep > 1 else ''} or sigh{'s' if deep > 1 else ''}"))
            if ampr is not None and ampr >= BREATH_DEEP_RATIO:
                ev.append(_ev("audio.breath_amp_ratio", None, ampr, f"breath sounds {_x(ampr)} as loud as when calm"))
            out.append(_state("deep_breath", "likely" if deep >= 2 else "possible", ev, qk, None, None))
        hold = a.get("longest_breath_hold_s")
        if hold is not None and hold >= BREATH_HOLD_POSSIBLE_S:
            near = _event_ref({"type": strong["event_type"], "t": strong["t_event"]}, None) if strong else None
            out.append(_state("breath_hold", "likely" if hold >= BREATH_HOLD_LIKELY_S else "possible",
                              [_ev("audio.longest_breath_hold_s", hold, None, f"no breath heard for {hold:.0f} s", "s")], qk, near, None))

    out = _with_stress(out, qk, None)
    if not out:
        ratios = [v for v in (med, rr, sr, m.get("head_ang_speed_ratio"), a.get("breath_rate_ratio")) if v is not None]
        if ratios and max(ratios) <= CALM_MAX_RATIO:
            out = [_state("calm", "likely" if a.get("breath_rate_ratio") is not None else "possible",
                          [_ev("all", None, max(ratios), f"every measure within {_x(CALM_MAX_RATIO)} of their calm calibration")], qk, None, None)]
    return sort_states(out)


def compact(states: Sequence[Mapping[str, Any]]) -> list[dict]:
    """The small form broadcast with every live window (evidence text only, no raw feature names)."""
    return [{"key": s["key"], "label": s["label"], "likelihood": s["likelihood"],
             "evidence": [e["text"] for e in s["evidence"]], "near_event": s.get("near_event"),
             "could_also_be": s["could_also_be"], "question_key": s.get("question_key"), "t": s.get("t")}
            for s in states]

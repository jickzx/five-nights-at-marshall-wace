"""Possible states (qs/states.py) on synthetic signals with known answers."""
import math

import numpy as np
import pytest

from qs import features as F
from qs import states as S
from qs.sim import quat_yaw, simulate_batches, track

FS = 72.0
CALM_AUDIO = {"breath_rate_bpm": 13.0, "breath_regularity": 0.8, "breath_amp": 0.017, "deep_breaths": 0,
              "since_last_breath_s": 1.2, "voice_fraction": 0.0}


def window(t0, tremor_mm=0.15, sway_mm=1.0, yaw=None, audio=None, seed=0):
    """One 1 s window of a seated person: gentle head scan, hands with a small sway and tremor."""
    rng = np.random.default_rng(seed)
    t = t0 + np.arange(int(FS)) / FS
    yaw_deg = 5 * np.sin(2 * math.pi * 0.23 * t) if yaw is None else yaw(t)
    head = track(t, pos=np.tile([0, 1.6, 0], (len(t), 1)) + rng.normal(0, 0.0002, (len(t), 3)), rot=quat_yaw(yaw_deg))
    hands = []
    for i, x in enumerate((-0.2, 0.2)):
        pos = np.zeros((len(t), 3))
        pos[:, 0] = x + tremor_mm / 1000 * np.sin(2 * math.pi * (8.0 + i) * t)
        pos[:, 1] = 1.0 + sway_mm / 1000 * np.sin(2 * math.pi * 1.2 * t)
        pos += rng.normal(0, 0.0001, pos.shape)
        hands.append(track(t, pos=pos, rot=np.tile([0, 0, 0, 1.0], (len(t), 1))))
    return F.window_features(t[0], t[-1] + 1 / FS, head, hands[0], hands[1], FS, audio=audio)


@pytest.fixture(scope="module")
def baseline():
    return F.compute_baseline([window(i * 0.5, audio=CALM_AUDIO, seed=i) for i in range(30)])


def states_of(feats, baseline, events=()):
    rel = F.relative_to_baseline(feats, baseline)
    return S.window_states(feats, rel, baseline, events, F.is_freeze(feats, baseline))


def keys(states):
    return {s["key"]: s["likelihood"] for s in states}


def test_calibration_level_window_is_calm(baseline):
    st = states_of(window(40.0, audio=CALM_AUDIO, seed=99), baseline)
    assert keys(st) == {"calm": "likely"}
    assert "could also be" not in st[0]["label"] and st[0]["could_also_be"]


def test_calm_without_audio_is_only_possible(baseline):
    assert keys(states_of(window(40.0, seed=98), baseline)) == {"calm": "possible"}


def test_8hz_shake_well_above_baseline_is_shaking(baseline):
    st = states_of(window(40.0, tremor_mm=1.0, seed=5), baseline)
    tremor = next(s for s in st if s["key"] == "tremor")
    assert tremor["likelihood"] == "likely"
    assert "8 Hz" in tremor["evidence"][0]["text"] or "9 Hz" in tremor["evidence"][0]["text"]
    assert tremor["evidence"][0]["ratio"] > S.TREMOR_LIKELY
    assert "medical tremor" in tremor["could_also_be"] and "cold" in tremor["could_also_be"]


def test_still_hand_gives_no_shaking(baseline):
    st = states_of(window(40.0, tremor_mm=0.0, sway_mm=0.0, seed=6), baseline)
    assert "tremor" not in keys(st)


def test_startle_spike_right_after_monster_appeared_is_fright(baseline):
    # a 45 degree flinch peaking ~0.3 s after the monster appears at t = 40.2
    flinch = lambda t: 5 * np.sin(2 * math.pi * 0.23 * t) + 45 * np.exp(-0.5 * ((t - 40.5) / 0.06) ** 2)  # noqa: E731
    feats = window(40.0, yaw=flinch, seed=7)
    st = states_of(feats, baseline, [{"t": 40.2, "type": "monster_appeared"}])
    s = next(s for s in st if s["key"] == "startle")
    assert s["likelihood"] == "likely" and s["near_event"]["type"] == "monster_appeared"
    assert 0 <= s["near_event"]["dt"] <= S.STARTLE_AFTER_S
    assert "faster than their calm level" in s["evidence"][0]["text"]
    # the same flinch with no stimulus before it is not called a startle
    assert "startle" not in keys(states_of(feats, baseline, []))
    # nor when the stimulus came after the movement
    assert "startle" not in keys(states_of(feats, baseline, [{"t": 40.9, "type": "monster_appeared"}]))


def test_fast_breathing_plus_tremor_is_likely_stress(baseline):
    fast = {**CALM_AUDIO, "breath_rate_bpm": 27.0}
    st = keys(states_of(window(40.0, tremor_mm=1.0, audio=fast, seed=8), baseline))
    assert st["stress"] == "likely" and st["fast_breathing"] == "likely" and "tremor" in st


def test_fast_breathing_alone_is_not_stress(baseline):
    st = keys(states_of(window(40.0, audio={**CALM_AUDIO, "breath_rate_bpm": 27.0}, seed=9), baseline))
    assert "fast_breathing" in st and "stress" not in st


def test_talking_suppresses_breathing_states(baseline):
    st = keys(states_of(window(40.0, audio={**CALM_AUDIO, "breath_rate_bpm": 30.0, "voice_fraction": 0.8}, seed=10), baseline))
    assert "fast_breathing" not in st


def test_breath_hold_after_a_scare_names_the_event(baseline):
    held = {**CALM_AUDIO, "breath_rate_bpm": None, "since_last_breath_s": 11.0}
    st = states_of(window(50.0, audio=held, seed=11), baseline, [{"t": 41.0, "type": "monster_appeared"}])
    s = next(s for s in st if s["key"] == "breath_hold")
    assert s["likelihood"] == "likely" and s["near_event"]["type"] == "monster_appeared"


def test_no_baseline_means_no_states():
    feats = window(0.0, tremor_mm=1.0)
    assert S.window_states(feats, F.relative_to_baseline(feats, None), None) == []


def test_record_states_follow_the_script():
    from tests.conftest import World
    from tests.test_api import run_session
    w = World()
    _, session, _ = run_session(w)
    recs = {r["question_key"]: r["record"] for r in w.client.get(f"/v1/sessions/{session['id']}/qs", headers=w.h()).json()}
    p3 = keys(recs["p3"]["states"])
    assert p3["startle"] == "likely" and "freeze" in p3 and "breath_hold" in p3
    startle = next(s for s in recs["p3"]["states"] if s["key"] == "startle")
    assert startle["near_event"]["type"] == "monster_appeared"
    assert "startle" in keys(recs["p5"]["states"])
    for r in recs.values():
        for s in r["states"]:
            assert s["likelihood"] in ("possible", "likely") and s["evidence"] and s["could_also_be"]
            assert s["question_key"] == r["question_key"]


def test_live_windows_carry_states():
    from tests.conftest import World
    w = World()
    _, session, token = w.new_session()
    w.consent(session["id"], token)
    for b in simulate_batches():
        w.post(session["id"], token, b)
    live = [m["payload"] for m in w.backend.broadcaster.sent if m["event"] == "window"]
    assert all("states" in p for p in live)
    seen = {s["key"] for p in live for s in p["states"]}
    assert {"startle", "freeze", "calm", "fast_breathing", "tremor"} <= seen
    assert any(p["features"]["audio_breath_rate_bpm"] for p in live)


LABEL_VERDICTS = ("hire", "reject", "suitab", "score", "diagnos", "disorder", "liar", "honest")


def test_states_never_judge_the_person():
    text = " ".join(list(S.LABELS.values()) + list(S.COULD_ALSO_BE.values())).lower()
    for word in LABEL_VERDICTS:
        assert word not in text

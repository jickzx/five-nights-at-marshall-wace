"""Breathing features (qs/audio.py, ported to the headset as QsBreath.cs) on synthetic signals."""
import numpy as np
import pytest

from qs import audio as A
from qs.models import AudioFeatures

T = np.arange(0, 15, 1 / A.ENV_FS)


def env(rate, amp=1.0, **kw):
    return A.synth_envelope(T, np.full(len(T), float(rate)), np.full(len(T), float(amp)) if np.isscalar(amp) else amp, **kw)


@pytest.mark.parametrize("rate", [9, 13, 18, 24, 32, 40])
def test_breath_rate_is_recovered(rate):
    f = A.breath_features(env(rate, seed=rate))
    assert f["breath_rate_bpm"] == pytest.approx(rate, rel=0.05)
    assert f["breath_regularity"] > 0.5 and f["voice_fraction"] == 0


def test_rate_from_raw_audio_through_the_envelope():
    fs = 16000
    t = np.arange(0, 15, 1 / fs)
    shape = np.interp(t, T, env(15, noise=0.0005))
    noise = np.random.default_rng(0).standard_normal(len(t))
    f = A.breath_features(A.envelope(noise * shape * 1.7, fs))
    assert f["breath_rate_bpm"] == pytest.approx(15, rel=0.05)


def test_breath_hold_is_detected_and_rate_withdrawn():
    f = A.breath_features(env(14, amp=np.where(T > 6, 0.0, 1.0), seed=3))
    assert f["since_last_breath_s"] > 8 and f["breath_rate_bpm"] is None


def test_louder_breathing_raises_amplitude():
    assert A.breath_features(env(14, amp=1.8))["breath_amp"] > 1.5 * A.breath_features(env(14, amp=1.0))["breath_amp"]


def test_a_sigh_is_counted_once_across_overlapping_sends():
    total = 0
    for end in np.arange(9.0, 25.0, 0.5):
        t = np.arange(0, end, 1 / A.ENV_FS)
        e = A.synth_envelope(t, np.full(len(t), 14.0), np.ones(len(t)), seed=4, sigh_at=[17.0])
        total += A.breath_features(e)["deep_breaths"]
    assert total == 1


def test_no_sigh_no_deep_breaths():
    total = 0
    for end in np.arange(9.0, 25.0, 0.5):
        t = np.arange(0, end, 1 / A.ENV_FS)
        total += A.breath_features(A.synth_envelope(t, np.full(len(t), 14.0), np.ones(len(t)), seed=4))["deep_breaths"]
    assert total == 0


def test_loud_bursts_count_as_voice():
    e = env(14, seed=5)
    e[-25:] = 0.3          # half a second of speech-level sound
    assert A.breath_features(e)["voice_fraction"] > 0.5


def test_short_history_reports_nothing():
    assert A.breath_features(env(14)[: int(4 * A.ENV_FS)]) == A.empty()


def test_wire_model_has_no_field_for_audio_samples():
    assert set(AudioFeatures.model_fields) == {"breath_rate_bpm", "breath_regularity", "breath_amp", "deep_breaths",
                                               "since_last_breath_s", "voice_fraction"}
    with pytest.raises(ValueError):
        AudioFeatures.model_validate({"samples": [0.1, 0.2]})


def test_rate_is_always_in_range_over_a_whole_simulated_session():
    from qs.sim import simulate_batches
    for seed, intensity in [(7, 1.0), (11, 0.6), (23, 1.4), (31, 0.85), (47, 1.1)]:
        for b in simulate_batches(seed=seed, intensity=intensity):
            r = b["audio"]["breath_rate_bpm"]
            assert r is None or 6 <= r <= 60, (seed, b["t_end"], r)
            AudioFeatures.model_validate(b["audio"])

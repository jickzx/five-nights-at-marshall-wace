"""Signal processing on synthetic signals with known answers."""
import math

import numpy as np
import pytest

from qs import features as F
from qs import signal as sig
from qs.sim import quat_yaw, still_hand, track

FS = 90.0


def tvec(seconds=1.0, fs=FS, jitter=0.0, seed=0):
    t = np.arange(int(seconds * fs)) / fs
    if jitter:
        t = t + np.random.default_rng(seed).normal(0, jitter, len(t))
        t.sort()
    return t


def test_band_power_of_sine_matches_parseval():
    t = tvec(2.0)
    a = 0.3
    x = (a * np.sin(2 * math.pi * 8.0 * t))[:, None]
    p = sig.band_power(x, FS, 4.0, 12.0)
    assert p == pytest.approx(a * a / 2, rel=0.08)


def test_dominant_frequency_finds_the_sine():
    t = tvec(1.0)
    x = np.sin(2 * math.pi * 9.0 * t)[:, None]
    assert sig.dominant_frequency(x, FS, 4, 12) == pytest.approx(9.0, abs=1.0)


def test_8hz_tremor_registers_and_still_hand_does_not():
    t = tvec(1.0)
    shake = np.zeros((len(t), 3))
    shake[:, 0] = 0.001 * np.sin(2 * math.pi * 8.0 * t)       # 1 mm at 8 Hz
    shaking = F.hand_features(track(t, pos=shake), t[0], t[-1], FS)
    still = F.hand_features(still_hand(t, noise_m=0.0001), t[0], t[-1], FS)
    # Velocity amplitude 2*pi*8*1mm = 50.3 mm/s -> RMS 35.5 mm/s.
    assert shaking["tremor_rms"] == pytest.approx(35.5, rel=0.15)
    assert shaking["tremor_peak_hz"] == pytest.approx(8.0, abs=1.0)
    assert still["tremor_rms"] < 0.15 * shaking["tremor_rms"]


def test_slow_reach_is_not_tremor_but_is_movement():
    t = tvec(1.0)
    reach = np.zeros((len(t), 3))
    reach[:, 2] = 0.2 * (1 - np.cos(math.pi * t / t[-1])) / 2    # smooth 20 cm reach over 1 s
    f = F.hand_features(track(t, pos=reach), t[0], t[-1], FS)
    assert f["hand_speed_mean"] > 0.1
    assert f["tremor_rms"] < 5.0       # mm/s: a deliberate reach leaks very little into 4-12 Hz


def test_2hz_sway_is_restlessness_not_tremor():
    t = tvec(2.0)
    sway = np.zeros((len(t), 3))
    sway[:, 1] = 0.01 * np.sin(2 * math.pi * 2.0 * t)
    f = F.hand_features(track(t, pos=sway), t[0], t[-1], FS)
    assert f["restless_rms"] > 5 * f["tremor_rms"]


def test_jittered_timestamps_still_measure_tremor():
    t = tvec(1.0, jitter=0.0015)
    shake = np.zeros((len(t), 3))
    shake[:, 0] = 0.001 * np.sin(2 * math.pi * 8.0 * t)
    f = F.hand_features(track(t, pos=shake), t[0], t[-1], FS)
    assert f["tremor_rms"] == pytest.approx(35.5, rel=0.2)


def test_sensor_jitter_does_not_read_as_hand_speed():
    t = tvec(1.0, fs=72.0)
    f = F.hand_features(still_hand(t, noise_m=0.0001), t[0], t[-1], 72.0)
    assert f["hand_speed_mean"] < 0.004          # m/s; unsmoothed this jitter reads as ~9 mm/s


def test_reported_velocity_is_used_only_without_position():
    t = tvec(1.0)
    v = np.zeros((len(t), 3))
    v[:, 0] = 0.0503 * np.cos(2 * math.pi * 8.0 * t)
    f = F.hand_features(F.DeviceTrack(t=t, pos=None, rot=None, vel=v), t[0], t[-1], FS)
    assert f["tremor_rms"] == pytest.approx(35.5, rel=0.15)


def test_untracked_controller_gives_no_features():
    t = tvec(1.0)
    tr = still_hand(t)
    tr.tracked = np.zeros(len(t), dtype=bool)
    tr.tracked[:20] = True
    f = F.hand_features(tr, t[0], t[-1], FS)
    assert f["tremor_rms"] is None and f["coverage"] < F.MIN_COVERAGE


def test_head_angular_speed_and_scan():
    t = tvec(2.0)
    yaw = 30.0 * np.sin(2 * math.pi * 1.0 * t)                  # look left/right 1 Hz, +-30 deg
    f = F.head_features(track(t, rot=quat_yaw(yaw)), t[0], t[-1], FS)
    # mean |d yaw/dt| of A sin(wt) = 2*A*w/pi = 4*A*f = 120 deg/s
    assert f["head_yaw_scan_rate"] == pytest.approx(120, rel=0.05)
    assert f["head_ang_speed_mean"] == pytest.approx(120, rel=0.05)
    assert f["head_yaw_reversals"] in (3, 4)                     # 2 s of 1 Hz scanning


def test_head_still_reads_near_zero():
    t = tvec(1.0)
    f = F.head_features(track(t, rot=quat_yaw(np.full(len(t), 15.0))), t[0], t[-1], FS)
    assert f["head_ang_speed_mean"] < 0.01
    assert f["head_yaw_reversals"] == 0


def test_reversals_ignore_small_jitter():
    rng = np.random.default_rng(1)
    assert sig.count_reversals(rng.normal(0, 1.0, 500), 10.0) == 0
    assert sig.count_reversals(np.array([0, -20, 0, 20, 0, -20.0]), 10.0) == 2   # turns at -20 and +20


def test_quaternion_sign_flip_is_not_a_spin():
    t = tvec(1.0)
    q = quat_yaw(np.full(len(t), 10.0))
    q[::2] *= -1
    _, w = sig.angular_speed(t, q)
    assert w.max() < 1e-6


def test_baseline_and_ratio_and_freeze():
    calm = [{"head_ang_speed_mean": 10.0 + i % 3, "left_hand_speed_mean": 0.05, "right_hand_speed_mean": 0.05,
             "left_tremor_rms": 4.0, "right_tremor_rms": 4.0} for i in range(20)]
    b = F.compute_baseline(calm)
    assert b["status"] == "ok"
    assert b["features"]["head_ang_speed_mean"]["median"] == pytest.approx(11.0)
    r = F.relative_to_baseline({"left_tremor_rms": 12.0}, b)
    assert r["left_tremor_rms"] == pytest.approx(3.0)
    frozen = {"head_ang_speed_mean": 1.0, "left_hand_speed_mean": 0.003, "right_hand_speed_mean": 0.002}
    moving = {"head_ang_speed_mean": 9.0, "left_hand_speed_mean": 0.04, "right_hand_speed_mean": 0.05}
    assert F.is_freeze(frozen, b) is True
    assert F.is_freeze(moving, b) is False


def test_freeze_is_relative_to_the_person():
    """The same absolute stillness is a freeze for a mover but not for someone who is always that still."""
    window = {"head_ang_speed_mean": 1.5, "left_hand_speed_mean": 0.006, "right_hand_speed_mean": 0.006}
    mover = F.compute_baseline([{"head_ang_speed_mean": 20.0, "left_hand_speed_mean": 0.08,
                                 "right_hand_speed_mean": 0.08}] * 12)
    statue = F.compute_baseline([{"head_ang_speed_mean": 1.6, "left_hand_speed_mean": 0.006,
                                  "right_hand_speed_mean": 0.007}] * 12)
    assert F.is_freeze(window, mover) is True
    assert F.is_freeze(window, statue) is False


def test_ratio_is_capped_and_floored():
    b = F.compute_baseline([{"left_tremor_rms": 0.0}] * 12)
    r = F.relative_to_baseline({"left_tremor_rms": 1000.0}, b)
    assert r["left_tremor_rms"] == F.RATIO_CAP

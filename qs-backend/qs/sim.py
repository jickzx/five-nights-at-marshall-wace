"""Synthetic sessions: a simulated person and a scripted game, producing the batches a headset would send.

Used by the tests and by scripts/make_demo_fixture.py (the dashboard demo data). Deterministic given a seed."""
from __future__ import annotations

import math

import numpy as np

from .features import DeviceTrack


def quat_yaw(yaw_deg: np.ndarray, pitch_deg: np.ndarray | None = None) -> np.ndarray:
    """Unity (x, y, z, w) quaternions for a heading about +y, optionally followed by a pitch about +x."""
    yaw = np.radians(np.asarray(yaw_deg, dtype=float))
    qy = np.column_stack([np.zeros_like(yaw), np.sin(yaw / 2), np.zeros_like(yaw), np.cos(yaw / 2)])
    if pitch_deg is None:
        return qy
    p = np.radians(np.asarray(pitch_deg, dtype=float))
    qx = np.column_stack([np.sin(p / 2), np.zeros_like(p), np.zeros_like(p), np.cos(p / 2)])
    return quat_mul(qy, qx)


def quat_mul(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    ax, ay, az, aw = a.T
    bx, by, bz, bw = b.T
    return np.column_stack([
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    ])


def track(t, pos=None, rot=None) -> DeviceTrack:
    return DeviceTrack(t=np.asarray(t, float), pos=None if pos is None else np.asarray(pos, float),
                       rot=None if rot is None else np.asarray(rot, float))


def still_hand(t, noise_m=0.0001, seed=0) -> DeviceTrack:
    rng = np.random.default_rng(seed)
    pos = np.array([0.2, 1.0, 0.3]) + rng.normal(0, noise_m, (len(t), 3))
    return track(t, pos=pos, rot=np.tile([0, 0, 0, 1.0], (len(t), 1)))


class Performer:
    """A synthetic person playing a session at ``fs`` Hz. Call ``motion(t)`` for each frame time.

    Knobs are set over time by the scenario: ``tremor_mm`` (8-10 Hz shake amplitude), ``scan_deg``
    (head look-around amplitude), ``fidget_mm`` (slow 1-2 Hz hand sway), ``freeze`` (0..1 damping of
    all movement), plus ``startle_at`` times that produce a 0.4 s flinch."""

    def __init__(self, seed=7):
        self.rng = np.random.default_rng(seed)
        self.tremor_mm = 0.15
        self.scan_deg = 6.0
        self.fidget_mm = 1.0
        self.freeze = 0.0
        self.startle_at: list[float] = []
        self.reach_at: list[float] = []
        self._phase = self.rng.uniform(0, 2 * math.pi, 8)

    def _startle(self, t: float) -> float:
        s = 0.0
        for ts in self.startle_at:
            d = t - ts - 0.18                     # ~180 ms latency
            if 0 <= d < 0.6:
                s += math.sin(math.pi * d / 0.6) ** 2
        return s

    def _reach(self, t: float) -> float:
        s = 0.0
        for tr in self.reach_at:
            d = t - tr
            if 0 <= d < 0.7:
                s += (1 - math.cos(2 * math.pi * d / 0.7)) / 2
        return s

    def motion(self, t: float) -> dict:
        p = self._phase
        k = 1.0 - 0.92 * self.freeze
        st = self._startle(t)
        yaw = k * (self.scan_deg * math.sin(2 * math.pi * 0.23 * t + p[0])
                   + 0.4 * self.scan_deg * math.sin(2 * math.pi * 0.61 * t + p[1])) + 25.0 * st
        pitch = -8.0 + k * 2.0 * math.sin(2 * math.pi * 0.17 * t + p[2]) + 6.0 * st
        head = np.array([0.0, 1.62, 0.0]) + k * 0.01 * np.array([math.sin(0.3 * t), 0.2 * math.sin(0.5 * t), 0])
        head = head + self.rng.normal(0, 0.0002, 3)
        hands = {}
        for i, (side, x) in enumerate((("left", -0.22), ("right", 0.22))):
            tremor = self.tremor_mm / 1000.0 * k * math.sin(2 * math.pi * (8.5 + i) * t + p[3 + i])
            fid = self.fidget_mm / 1000.0 * k * math.sin(2 * math.pi * (1.2 + 0.4 * i) * t + p[5 + i])
            reach = 0.18 * self._reach(t) if side == "right" else 0.0
            pos = np.array([x + tremor, 1.05 + fid + 0.06 * st, 0.35 + reach]) + self.rng.normal(0, 0.0001, 3)
            hands[side] = pos
        rot = quat_yaw(np.array([yaw]), np.array([pitch]))[0]
        return {"head_pos": head, "head_rot": rot, "left_pos": hands["left"], "right_pos": hands["right"]}


# ---------------------------------------------------------------- scripted session ----------------------

def _eid(rng: np.random.Generator) -> str:
    import uuid
    return str(uuid.UUID(bytes=rng.bytes(16), version=4))


def demo_script() -> tuple[list[tuple[float, str, str | None, dict | None]], list[tuple[float, float, dict]]]:
    """A five-question session with calibration, hesitation, a monster scare, a freeze and a death.

    Returns (events, phases): events are (t, type, question_key, payload); phases are (t0, t1, knobs) that
    set the performer's behaviour over time."""
    ev: list[tuple[float, str, str | None, dict | None]] = [
        (0.2, "session_start", None, {"device": "simulated", "rate_hz": 72}),
        (1.0, "calibration_start", None, None),
        (26.0, "calibration_end", None, None),
        # q1: quick and clean
        (27.0, "question_shown", "p1", None),
        (31.2, "tile_placed", "p1", None), (33.0, "tile_placed", "p1", None), (34.4, "tile_placed", "p1", None),
        (36.0, "answer_submitted", "p1", {"correct": True}),
        # q2: long hesitation, answer changes, one wrong answer
        (40.0, "question_shown", "p2", None),
        (49.5, "tile_placed", "p2", None), (51.0, "tile_placed", "p2", None), (53.5, "tile_removed", "p2", None),
        (55.0, "tile_placed", "p2", None), (60.5, "answer_cleared", "p2", None), (63.0, "tile_placed", "p2", None),
        (64.2, "tile_placed", "p2", None), (65.5, "answer_submitted", "p2", {"correct": False}),
        (65.6, "answer_wrong_fx", "p2", None),
        (69.0, "tile_placed", "p2", None), (70.4, "tile_placed", "p2", None), (72.0, "answer_submitted", "p2", {"correct": True}),
        # q3: the monster appears mid-question
        (76.0, "question_shown", "p3", None),
        (80.0, "tile_placed", "p3", None),
        (84.0, "monster_appeared", "p3", {"stage": 2}),
        (84.5, "chase_start", None, None),
        (93.0, "tile_placed", "p3", None), (94.5, "tile_placed", "p3", None), (96.0, "answer_submitted", "p3", {"correct": True}),
        (97.0, "chase_end", None, None),
        # q4: hint, heavy looking around
        (100.0, "question_shown", "p4", None),
        (106.0, "hint_used", "p4", None),
        (109.0, "tile_placed", "p4", None), (111.0, "tile_placed", "p4", None),
        (112.5, "answer_submitted", "p4", {"correct": False}), (112.6, "answer_wrong_fx", "p4", None),
        (113.0, "monster_stage", None, {"stage": 3}),
        (118.0, "tile_placed", "p4", None), (119.0, "tile_placed", "p4", None),
        (121.0, "answer_submitted", "p4", {"correct": True}),
        # q5: caught
        (124.0, "question_shown", "p5", None),
        (130.0, "tile_placed", "p5", None),
        (134.0, "monster_lunge", "p5", None),
        (135.2, "death", None, None),
        (137.0, "session_end", None, None),
    ]
    phases = [
        (0.0, 26.0, {"tremor_mm": 0.15, "scan_deg": 5.0, "fidget_mm": 0.8, "freeze": 0.0}),
        (26.0, 40.0, {"tremor_mm": 0.18, "scan_deg": 7.0, "fidget_mm": 1.0, "freeze": 0.0}),
        (40.0, 49.0, {"tremor_mm": 0.30, "scan_deg": 3.0, "fidget_mm": 4.0, "freeze": 0.0}),
        (49.0, 76.0, {"tremor_mm": 0.35, "scan_deg": 6.0, "fidget_mm": 2.5, "freeze": 0.0}),
        (76.0, 84.0, {"tremor_mm": 0.25, "scan_deg": 6.0, "fidget_mm": 1.0, "freeze": 0.0}),
        (84.0, 85.0, {"tremor_mm": 0.6, "scan_deg": 14.0, "fidget_mm": 1.0, "freeze": 0.0}),
        (85.0, 90.0, {"tremor_mm": 0.5, "scan_deg": 2.0, "fidget_mm": 0.3, "freeze": 1.0}),
        (90.0, 100.0, {"tremor_mm": 0.55, "scan_deg": 18.0, "fidget_mm": 1.5, "freeze": 0.0}),
        (100.0, 124.0, {"tremor_mm": 0.45, "scan_deg": 22.0, "fidget_mm": 2.0, "freeze": 0.0}),
        (124.0, 140.0, {"tremor_mm": 0.6, "scan_deg": 16.0, "fidget_mm": 1.5, "freeze": 0.0}),
    ]
    return ev, phases


def simulate_batches(seed: int = 7, rate_hz: float = 72.0, window_s: float = 1.0, hop_s: float = 0.5,
                     jitter_s: float = 0.0008, intensity: float = 1.0) -> list[dict]:
    """The batches a headset would send for ``demo_script``: one window every ``hop_s`` with the events
    that happened since the previous send. Same JSON shape as the Unity sender (see docs/api.md)."""
    rng = np.random.default_rng(seed)
    perf = Performer(seed)
    events, phases = demo_script()
    perf.startle_at = [t for t, typ, _, _ in events if typ in ("monster_appeared", "monster_lunge", "monster_stage", "answer_wrong_fx")]
    perf.reach_at = [t for t, typ, _, _ in events if typ in ("tile_placed", "tile_removed", "answer_submitted")]
    end = max(t for t, *_ in events)
    n = int(end * rate_hz) + 1
    ts = np.arange(n) / rate_hz + rng.normal(0, jitter_s, n)
    ts.sort()
    frames = []
    for t in ts:
        for a, b, knobs in phases:
            if a <= t < b:
                for k, v in knobs.items():
                    # intensity scales how strongly this simulated person reacts after calibration
                    scale = intensity if (a >= 26.0 and k in ("tremor_mm", "scan_deg", "fidget_mm")) else 1.0
                    setattr(perf, k, v * scale)
        frames.append(perf.motion(float(t)))
    ids = [_eid(rng) for _ in events]

    def question_at(t: float) -> str | None:
        current = None
        for (te, typ, key, payload) in events:
            if te > t:
                break
            if typ == "question_shown":
                current = key
            elif (typ == "answer_submitted" and (payload or {}).get("correct")) or typ in ("death", "session_end"):
                current = None
        return current

    batches = []
    seq = 0
    sent_until = -1.0
    t_send = window_s
    while t_send <= end + hop_s:
        t0, t1 = t_send - window_s, t_send
        idx = np.where((ts >= t0) & (ts < t1))[0]
        batch: dict = {"seq": seq, "question_key": question_at(t1), "t_start": round(t0, 4), "t_end": round(t1, 4),
                       "rate_hz": rate_hz, "events": []}
        if len(idx):
            r = lambda a: [[round(float(x), 5) for x in row] for row in a]  # noqa: E731
            batch["samples"] = {
                "t": [round(float(ts[i]), 5) for i in idx],
                "head": {"pos": r([frames[i]["head_pos"] for i in idx]), "rot": r([frames[i]["head_rot"] for i in idx])},
                "left": {"pos": r([frames[i]["left_pos"] for i in idx])},
                "right": {"pos": r([frames[i]["right_pos"] for i in idx])},
            }
        for (te, typ, key, payload), eid in zip(events, ids):
            if sent_until < te <= t1:
                batch["events"].append({"id": eid, "t": te, "type": typ, "question_key": key, "payload": payload})
        sent_until = t1
        batches.append(batch)
        if any(e["type"] == "session_end" for e in batch["events"]):
            break   # the sender stops after it reports the end of the session
        seq += 1
        t_send += hop_s
    return batches

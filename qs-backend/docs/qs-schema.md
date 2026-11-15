# The QS record (version `qs-0.1`)

A **QS record** describes how one person moved and acted while one question was on screen. There is one record per question per session. Every motion value in it is measured against that same person's calm calibration at the start of the session, never against other people or a fixed threshold.

QS measures movement and timing. It does not measure feelings, ability, honesty or health, and no field in it is a score or a decision. Hand motion in the 4–12 Hz band, for example, rises with caffeine, cold, fatigue, an unfamiliar controller and many ordinary medical conditions.

The code is the specification of last resort: `qs/signal.py` (signal processing), `qs/features.py` (features and records). This document and that code must change together.

## Pipeline

```
headset, 72-90 Hz            ingest (per batch)                    per question
pose of head + 2 hands  ->   one window: 1.0 s, every 0.5 s   ->   QS record
+ game events                -> window features (raw discarded)    (recomputed when its events arrive)
                             -> ratio to own baseline
```

1. **Window features.** Each batch carries one window of samples (default 1.0 s, a new one every 0.5 s, so windows overlap by half). The API computes the window features below and stores them. **Raw samples are not stored** (see `design.md` for the storage and privacy reasons).
2. **Baseline.** When the `calibration_end` event arrives, the windows between `calibration_start` and `calibration_end` (default: the first 20 s after consent, while the game keeps play quiet) become the baseline: the median and the median absolute deviation (MAD) of each baseline feature.
3. **Ratios.** A window's ratio for feature *f* is `f / max(baseline_median(f), floor(f))`, capped at 50. `1.0` means "the same as this person's calibration". The floors stop a perfectly still calibration from producing huge ratios.
4. **Records.** A question's record is built from its game events and the windows that overlap its time span.

### Conventions

| | |
|---|---|
| Time | Seconds on the headset's monotonic session clock (`t`). 0 is when recording started after consent. |
| Position | Metres, Unity tracking space (left-handed, y up). |
| Rotation | Unit quaternion `(x, y, z, w)`. |
| Ratios | Dimensionless, ×baseline. Medians across windows unless stated. |
| Missing | `null`: not measurable (device untracked for >40 % of a window, no baseline, no such event). |

## Window features (`qs_motion_windows.features`)

Before anything else, samples are cleaned (non-finite, untracked and duplicate-time samples dropped) and linearly resampled onto a uniform grid at the window's median sample rate (capped at 120 Hz). A device needs at least 16 samples and 60 % coverage of the window, or its features are `null`.

**Velocity** comes from differentiating position (central differences), not from the runtime's reported velocity: every runtime gives pose, and reported velocity may be filtered or predicted. Reported velocity is used only when a device sends no position.

**Band power** uses a one-sided Hann-windowed periodogram of the linearly detrended signal, scaled so that the sum over a band equals the mean-square of the signal in that band (a sine of amplitude *A* contributes *A²/2*). Values are summed over the three axes. With 1 s windows the frequency resolution is 1 Hz.

**Speeds** are taken after a 100 ms centred moving average. Without it, sensor jitter is rectified into apparent movement (0.1 mm of position noise at 72 Hz reads as about 9 mm/s of hand speed).

| Field | Unit | Definition |
|---|---|---|
| `left_tremor_rms`, `right_tremor_rms` | mm/s | √(band power of hand velocity in **4–12 Hz**) × 1000. Physiological and enhanced physiological tremor sit in this band. A deliberate reach puts almost no energy here; a 1 mm, 8 Hz shake gives about 35 mm/s. |
| `left_tremor_peak_hz`, `right_…` | Hz | Frequency of the largest periodogram bin in 4–12 Hz. |
| `left_restless_rms`, `right_…` | mm/s | √(band power of hand velocity in **0.5–3 Hz**) × 1000: slow, small, repetitive movement (shifting, fiddling). |
| `left_hand_speed_mean`, `right_…` | m/s | Mean smoothed hand speed. |
| `left_hand_speed_peak`, `…_peak_t` | m/s, s | Highest smoothed hand speed and when it happened (used for startle). |
| `left_coverage`, `right_coverage` | 0–1 | Share of the window the controller was tracked. |
| `head_ang_speed_mean`, `head_ang_speed_peak`, `…_peak_t` | deg/s, s | Angle between consecutive head rotations divided by time (single-frame spikes clipped at 1500 deg/s), smoothed; mean, maximum and time of maximum. |
| `head_yaw_scan_rate` | deg/s | Total absolute change of heading (yaw of the forward vector, unwrapped) divided by the window length: how much the person turns their head side to side. |
| `head_yaw_reversals` | count | Changes of looking direction where the heading swings back by at least 10° (a zig-zag filter, so jitter does not count). Starting centred, a left-right-left scan is 2. |
| `head_trans_speed_mean` | m/s | Mean smoothed speed of the head position (leaning, stepping). |
| `head_coverage` | 0–1 | Share of the window the headset was tracked. |

**Baseline features** (get a median, MAD and ratio): `head_ang_speed_mean`, `head_yaw_scan_rate`, `head_trans_speed_mean`, and per hand `tremor_rms`, `restless_rms`, `hand_speed_mean`.

`qs_motion_windows.rel` holds the ratios of those features for that window (null before the baseline exists).

**Low-motion ("freeze") window** (judged only when a baseline exists; without one it is `null`, never an absolute threshold): head angular speed and both hand speeds are each below **half** of the person's own calibration median **and** below an absolute ceiling (3 deg/s for the head, 10 mm/s for hands). The half is not lower because controller jitter leaves about 2 mm/s of apparent hand speed even after smoothing, a sizeable fraction of a calm baseline. The ceiling stops someone who fidgeted through calibration from having ordinary calm movement counted as freezing.

## Baseline (`qs_sessions.baseline`)

```json
{"status": "ok", "windows": 50, "t_start": 0.0, "t_end": 20.0,
 "features": {"right_tremor_rms": {"median": 2.41, "mad": 0.38, "n": 50}, "...": {}}}
```

`status` is `ok` (≥10 calibration windows), `short` (fewer; ratios are given but are less stable) or `missing` (no calibration: every ratio is `null`, raw values are still reported).

## Question spans

From the session's events in time order: a question **opens** at `question_shown` and **closes** at the first of: a correct `answer_submitted`; an `answer_submitted` with `payload.final: true` (an answer the game does not let you retry: the sender marks the one-shot quiz this way); `question_abandoned` for it (the sender sends this when the player walks away from a console or a chase starts); `death`; `session_end`; or another question being shown. A wrong, non-final answer leaves it open (the game keeps the same puzzle up). A question shown again after closing reopens with its first shown time kept.

## The record (`qs_records.record`)

```json
{
  "qs_version": "qs-0.1",
  "question_key": "p3",
  "status": "closed",
  "outcome": "correct",
  "t_shown": 76.0, "t_end": 96.0,
  "behaviour": {"time_to_first_action_s": 4.0, "response_latency_s": 20.0, "duration_s": 20.0,
                "submissions": 1, "incorrect_submissions": 0, "answer_changes": 0,
                "hesitations": 2, "hesitation_total_s": 14.5, "hints_used": 0},
  "motion": {"windows": 41, "left_tremor_rms_mm_s": 3.1, "right_tremor_rms_mm_s": 3.4,
             "left_tremor_ratio": 1.9, "right_tremor_ratio": 2.0, "left_tremor_ratio_p90": 3.2, "right_tremor_ratio_p90": 3.6,
             "head_ang_speed_deg_s": 11.2, "head_ang_speed_ratio": 1.4, "head_scan_rate_deg_s": 9.8, "head_scan_ratio": 1.5,
             "head_yaw_reversals_per_min": 14.0, "restless_ratio": 1.1, "fidget_index": 0.05,
             "freeze_s": 4.5, "longest_freeze_s": 4.5, "freeze_fraction": 0.22,
             "startles": [{"event_type": "monster_appeared", "t_event": 84.0, "head_peak_deg_s": 118.0,
                           "head_peak_ratio": 16.9, "hand_peak_m_s": 0.31, "hand_peak_ratio": 9.4, "latency_s": 0.41}],
             "startle_max_head_ratio": 16.9},
  "quality": {"baseline_status": "ok", "head_coverage": 0.99, "hand_coverage": 0.99}
}
```

### `status`, `outcome`

`status` is `open` while the question is on screen and `closed` after. `outcome` is what the game recorded: `correct`, `incorrect` (closed with at least one answer and none correct), `abandoned` (closed with no answer), `died` (the game's monster caught the player during it), or `open`.

### `behaviour` (from game events)

Input actions are `tile_placed`, `tile_removed`, `answer_cleared`, `answer_submitted`, `hint_used`.

| Field | Unit | Definition |
|---|---|---|
| `time_to_first_action_s` | s | First input action − `t_shown`. |
| `response_latency_s` | s | First `answer_submitted` − `t_shown`. |
| `duration_s` | s | `t_end` (or the latest time seen, while open) − `t_shown`. |
| `submissions` | count | `answer_submitted` events. |
| `incorrect_submissions` | count | Those with `payload.correct == false`. |
| `answer_changes` | count | `tile_removed` + `answer_cleared` events: changing an answer before sending it. |
| `hesitations` | count | Gaps of **≥3 s** between consecutive input actions (and from the last action to the end), counted from the first action on. The wait before the first action is `time_to_first_action_s`, not a hesitation. |
| `hesitation_total_s` | s | Sum of those gaps. |
| `hints_used` | count | `hint_used` events. |

### `motion` (from windows overlapping the question)

| Field | Unit | Definition |
|---|---|---|
| `windows` | count | Windows overlapping the question's span. |
| `left_tremor_rms_mm_s`, `right_…` | mm/s | Mean of window `tremor_rms`. |
| `left_tremor_ratio`, `right_…` | ×baseline | Median of window ratios. |
| `left_tremor_ratio_p90`, `right_…` | ×baseline | 90th percentile of window ratios (brief bursts). |
| `head_ang_speed_deg_s`, `head_ang_speed_ratio` | deg/s, × | Mean value; median ratio. |
| `head_scan_rate_deg_s`, `head_scan_ratio` | deg/s, × | Mean value; median ratio. |
| `head_yaw_reversals_per_min` | 1/min | Window reversals summed, divided by the window overlap factor (window length / hop) and by the question's minutes. |
| `restless_ratio` | × | Median over windows of the mean of left and right 0.5–3 Hz ratios. |
| `fidget_index` | 0–1 | Share of windows where either hand's 0.5–3 Hz ratio is **≥2** and **no input action** happened in that window: movement that is not doing the task. |
| `freeze_s`, `longest_freeze_s` | s | Low-motion windows × hop; longest continuous run × hop. |
| `freeze_fraction` | 0–1 | Low-motion windows / windows where it could be judged. |
| `startles` | list | One entry per sudden stimulus during the question (`monster_appeared`, `monster_lunge`, `monster_stage`, `jumpscare`, `answer_wrong_fx`): the highest head angular-speed peak and hand-speed peak whose time falls within **1.5 s after** the event, each divided by the higher of the level in windows ending in the **2 s before** it and the person's baseline median; `latency_s` is the head peak's time minus the event's. |
| `startle_max_head_ratio` | × | Largest `head_peak_ratio` in `startles`. |

### `quality`

`baseline_status` (as above), and the mean `head_coverage` and `hand_coverage` (the lower of the two hands per window) over the question's windows. Read every other field in the light of these.

## Session roll-up (`GET /v1/candidates/{id}/summary`)

Per session: counts of questions, closed and correct; mean response latency and time to first action; total answer changes, hesitations and low-motion seconds; mean of the per-question tremor and head ratios; number of startles. Observations only: there is no overall score.

## Validation

`tests/test_signal.py` checks the definitions on synthetic signals with known answers: an 8 Hz, 1 mm shake reads 35.5 mm/s ± 15 % and peaks at 8 Hz; a still controller with 0.1 mm noise reads below 15 % of that; a smooth 20 cm reach reads under 5 mm/s of tremor; a 2 Hz sway lands in the 0.5–3 Hz band, not 4–12 Hz; a ±30° 1 Hz head scan reads 120 deg/s; jittered timestamps and quaternion sign flips do not change results; the same stillness is a freeze for a mover and not for someone who is always that still. `tests/test_api.py` runs a full scripted session (`qs/sim.py`) through the API and checks each record against the script.

What is **not** validated: these thresholds against real Quest data. The band edges, the 50 % freeze fraction, the 3 s hesitation gap and the startle windows are reasoned defaults and need checking against recordings from real headsets and real people before anyone relies on them.

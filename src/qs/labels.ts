// What each measured number is, in plain words. These label numbers; the possible states (states.ts) are where
// QS says what the numbers might mean. qs.test.ts fails if verdict or diagnostic words creep into either.

export interface FeatureLabel { short: string; long: string; unit: string }

export const FEATURES: Record<string, FeatureLabel> = {
  "behaviour.time_to_first_action_s": { short: "First input", long: "Seconds from the question appearing to the first tile placed", unit: "s" },
  "behaviour.response_latency_s": { short: "First answer", long: "Seconds from the question appearing to the first answer sent", unit: "s" },
  "behaviour.duration_s": { short: "Time on question", long: "Seconds the question was open", unit: "s" },
  "behaviour.submissions": { short: "Answers sent", long: "Number of answers sent", unit: "" },
  "behaviour.incorrect_submissions": { short: "Incorrect answers", long: "Answers the game marked incorrect", unit: "" },
  "behaviour.answer_changes": { short: "Answer changes", long: "Tiles removed or cleared before sending", unit: "" },
  "behaviour.hesitations": { short: "Pauses ≥3 s", long: "Gaps of 3 s or more between inputs after the first input", unit: "" },
  "behaviour.hesitation_total_s": { short: "Paused time", long: "Total length of those pauses", unit: "s" },
  "behaviour.hints_used": { short: "Hints", long: "Hints opened", unit: "" },
  "motion.left_tremor_ratio": { short: "4–12 Hz motion L", long: "Left-hand motion energy in the 4–12 Hz band, relative to the person's own calibration", unit: "×" },
  "motion.right_tremor_ratio": { short: "4–12 Hz motion R", long: "Right-hand motion energy in the 4–12 Hz band, relative to the person's own calibration", unit: "×" },
  "motion.left_tremor_ratio_p90": { short: "4–12 Hz L (p90)", long: "90th percentile of the left-hand 4–12 Hz ratio", unit: "×" },
  "motion.right_tremor_ratio_p90": { short: "4–12 Hz R (p90)", long: "90th percentile of the right-hand 4–12 Hz ratio", unit: "×" },
  "motion.head_ang_speed_ratio": { short: "Head rotation", long: "Head rotation speed relative to calibration", unit: "×" },
  "motion.head_scan_ratio": { short: "Head scanning", long: "Side-to-side head turning relative to calibration", unit: "×" },
  "motion.head_yaw_reversals_per_min": { short: "Look reversals", long: "Changes of looking direction of 10° or more per minute", unit: "/min" },
  "motion.restless_ratio": { short: "0.5–3 Hz motion", long: "Slow small hand movement (0.5–3 Hz band) relative to calibration", unit: "×" },
  "motion.fidget_index": { short: "Motion without input", long: "Share of windows with 0.5–3 Hz hand motion above 2× calibration and no game input", unit: "" },
  "motion.freeze_s": { short: "Low-motion time", long: "Seconds with head and both hands below half their calibration level", unit: "s" },
  "motion.longest_freeze_s": { short: "Longest low-motion", long: "Longest continuous low-motion stretch", unit: "s" },
  "motion.freeze_fraction": { short: "Low-motion share", long: "Share of the question spent in low motion", unit: "" },
  "motion.startle_max_head_ratio": { short: "Peak after stimulus", long: "Highest head-rotation peak within 1.5 s of a sudden game event, relative to the level just before", unit: "×" },
  "audio.breath_rate_bpm": { short: "Breathing rate", long: "Typical breaths per minute during the question (from the headset microphone)", unit: "/min" },
  "audio.breath_rate_ratio": { short: "Breathing vs calm", long: "Breathing rate relative to the person's own calibration", unit: "×" },
  "audio.breath_rate_ratio_p90": { short: "Fastest breathing", long: "Fastest breathing (90th percentile) relative to calibration", unit: "×" },
  "audio.breath_amp_ratio": { short: "Breath loudness", long: "How loud the breath sounds were relative to calibration", unit: "×" },
  "audio.deep_breaths": { short: "Deep breaths", long: "Deep breaths or sighs", unit: "" },
  "audio.longest_breath_hold_s": { short: "Longest no-breath gap", long: "Longest time with no breath heard", unit: "s" },
  "audio.voice_fraction": { short: "Speech", long: "Share of the time the microphone heard speech-level sound", unit: "" },
  "quality.baseline_status": { short: "Baseline", long: "Whether calibration produced a usable baseline", unit: "" },
  "quality.head_coverage": { short: "Head tracking", long: "Share of samples with the headset tracked", unit: "" },
  "quality.hand_coverage": { short: "Hand tracking", long: "Share of samples with both controllers tracked", unit: "" },
  outcome: { short: "Game outcome", long: "What the game recorded at the end of the question", unit: "" },
};

export const EVENT_LABELS: Record<string, string> = {
  session_start: "Session start", calibration_start: "Calibration", calibration_end: "Calibration done",
  question_shown: "Question", tile_placed: "Tile", tile_removed: "Tile removed", answer_cleared: "Cleared",
  answer_submitted: "Answer", answer_wrong_fx: "Glass cracks", hint_used: "Hint", monster_appeared: "Monster appears",
  monster_stage: "Monster evolves", monster_lunge: "Monster lunges", jumpscare: "Jump scare", chase_start: "Chase",
  chase_end: "Chase over", death: "Caught", question_abandoned: "Left question", session_end: "Session end",
  tracking_lost: "Tracking lost", tracking_restored: "Tracking back",
};

export const OUTCOME_LABELS: Record<string, string> = {
  correct: "Answered correctly", incorrect: "Not solved", abandoned: "Left unanswered", died: "Caught by monster", open: "In progress",
};

export function fmt(v: unknown, unit = ""): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "number") {
    if (Number.isInteger(v) && unit !== "×") return unit ? `${v} ${unit}` : String(v);
    const s = Math.abs(v) >= 100 ? v.toFixed(0) : Math.abs(v) >= 10 ? v.toFixed(1) : v.toFixed(2);
    if (unit === "×") return `${s}×`;
    if (unit === "" && v <= 1 && v >= 0 && !Number.isInteger(v)) return `${Math.round(v * 100)}%`;
    return unit ? `${s} ${unit}` : s;
  }
  return String(v);
}

// Plain-language reading of the possible states the backend infers (qs-backend/qs/states.py).
// Pure functions only, so the sentences the employer reads are unit-tested (qs.test.ts).
import type { LiveState, LiveWindow, Likelihood, RecordState, StateKey } from "./types";

/** How each state is said in a sentence about the candidate ("Likely startled or frightened"). */
export const PHRASE: Record<StateKey, string> = {
  startle: "startled or frightened",
  stress: "showing signs of stress",
  restless: "restless, moving a lot",
  freeze: "freezing",
  tremor: "shaking",
  fast_breathing: "breathing fast",
  deep_breath: "taking a deep breath",
  breath_hold: "holding their breath",
  calm: "calm",
};

/** Short chip text ("Possible fright"). */
export const CHIP: Record<StateKey, string> = {
  startle: "startle / fright",
  stress: "signs of stress",
  restless: "restless",
  freeze: "freezing",
  tremor: "shaking",
  fast_breathing: "fast breathing",
  deep_breath: "deep breath",
  breath_hold: "held breath",
  calm: "calm",
};

/** Colour family: alarm (sudden, freezing), arousal (sustained activation), calm. */
export type Family = "alarm" | "arousal" | "calm";
export const FAMILY: Record<StateKey, Family> = {
  startle: "alarm", freeze: "alarm", breath_hold: "alarm",
  stress: "arousal", tremor: "arousal", restless: "arousal", fast_breathing: "arousal", deep_breath: "arousal",
  calm: "calm",
};

// Same order as the backend: the most specific first.
export const ORDER: StateKey[] = ["startle", "stress", "tremor", "freeze", "breath_hold", "fast_breathing", "deep_breath", "restless", "calm"];

type AnyState = Pick<LiveState, "key" | "likelihood">;

export function rank(a: AnyState, b: AnyState): number {
  const o = ORDER.indexOf(a.key) - ORDER.indexOf(b.key);
  return o !== 0 ? o : (a.likelihood === "likely" ? 0 : 1) - (b.likelihood === "likely" ? 0 : 1);
}

export function likelyWord(l: Likelihood): string { return l === "likely" ? "Likely" : "Possibly"; }

export function chipText(s: AnyState): string {
  return `${s.likelihood === "likely" ? "Likely" : "Possible"} ${CHIP[s.key]}`;
}

/** "Likely startled or frightened". Calm reads "Likely calm" / "Possibly calm" as well. */
export function headline(s: AnyState): string {
  return `${likelyWord(s.likelihood)} ${PHRASE[s.key]}`;
}

export function questionName(key: string | null | undefined): string {
  if (!key) return "between questions";
  const m = /^p(\d+)(-quiz)?$/.exec(key);
  if (!m) return key;
  return m[2] ? `console ${m[1]} quiz` : `console ${m[1]}`;
}

/** The sentence under the headline: the strongest evidence, the game event it followed, the question. */
export function because(s: LiveState): string {
  const ev = s.evidence[0] ?? "";
  const first = ev ? ev[0].toUpperCase() + ev.slice(1) : "";
  const near = s.near_event ? `, after ${s.near_event.text}` : "";
  const where = s.question_key ? ` on ${questionName(s.question_key)}` : "";
  return `${first}${near}${where}.`;
}

/**
 * What to say right now. Window states flicker (each is one second of data), so the page reports the strongest
 * state seen in the last few windows, and the most recent window it appeared in. Returns null before a baseline.
 */
export function current(windows: LiveWindow[], span = 6): { state: LiveState; window: LiveWindow; all: LiveState[] } | null {
  const recent = windows.slice(-span).filter((w) => (w.states ?? []).length);
  if (!recent.length) return null;
  let best: { state: LiveState; window: LiveWindow } | null = null;
  for (const w of recent) for (const s of w.states!) {
    if (!best || rank(s, best.state) < 0 || (rank(s, best.state) === 0 && w.seq > best.window.seq)) best = { state: s, window: w };
  }
  // every distinct state in the span, strongest first, one per key
  const seen = new Map<StateKey, LiveState>();
  for (const w of recent) for (const s of w.states!) {
    const prev = seen.get(s.key);
    if (!prev || (s.likelihood === "likely" && prev.likelihood !== "likely")) seen.set(s.key, s);
  }
  const all = [...seen.values()].sort(rank);
  return best ? { ...best, all } : null;
}

/** The strongest state of a question, for its row. */
export function topOf(states: RecordState[] | undefined): RecordState | null {
  return [...(states ?? [])].sort(rank)[0] ?? null;
}

/** The ratio to calm for a live vital, and the words for it ("2.1× their calm rate"). */
export function vsCalm(ratio: number | null | undefined, noun: string): string {
  if (ratio == null) return "measuring";
  if (ratio < 0.8) return `below their calm ${noun}`;
  if (ratio <= 1.25) return `about their calm ${noun}`;
  return `${ratio.toFixed(1)}× their calm ${noun}`;
}

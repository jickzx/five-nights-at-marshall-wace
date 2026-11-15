// Shapes of the QS API and Realtime payloads (qs-backend/docs/api.md, qs-backend/docs/qs-schema.md).

export type SessionStatus = "created" | "live" | "ended";

export interface Session {
  id: string;
  status: SessionStatus;
  candidate_id: string;
  candidate_pseudonym: string | null;
  question_set_id?: string | null;
  device_id?: string | null;
  created_at?: string | null;
  started_at?: string | null;
  ended_at?: string | null;
}

export type Num = number | null;

export type Likelihood = "possible" | "likely";
export type StateKey = "startle" | "stress" | "restless" | "freeze" | "tremor" | "fast_breathing" | "deep_breath" | "breath_hold" | "calm";

export interface NearEvent { type: string; t: number; dt: number | null; text: string }

/** A possible state as broadcast with each window (qs/states.py compact form): evidence is already in words. */
export interface LiveState {
  key: StateKey;
  label: string;
  likelihood: Likelihood;
  evidence: string[];
  near_event: NearEvent | null;
  could_also_be: string;
  question_key: string | null;
  t: number | null;
}

/** A possible state in a QS record: evidence carries the feature, value and ratio behind each sentence. */
export interface RecordState extends Omit<LiveState, "evidence"> {
  evidence: { feature: string; value: unknown; unit: string; ratio: number | null; text: string }[];
}

/** A motion window as broadcast on qs:session:<id> (event "window") and returned by /windows. */
export interface LiveWindow {
  seq: number;
  t_start: number;
  t_end: number;
  question_key: string | null;
  features: Record<string, Num>;
  rel: Record<string, Num> | null;
  freeze: boolean | null;
  states?: LiveState[];
}

export interface QsEvent {
  t: number;
  type: string;
  question_key: string | null;
  payload: Record<string, unknown> | null;
}

export interface Startle {
  event_type: string;
  t_event: Num;
  head_peak_deg_s: Num;
  head_peak_ratio: Num;
  hand_peak_m_s: Num;
  hand_peak_ratio: Num;
  latency_s: Num;
}

export interface QsRecord {
  qs_version: string;
  question_key: string;
  status: "open" | "closed";
  outcome: string;
  t_shown: Num;
  t_end: Num;
  behaviour: Record<string, Num>;
  motion: Record<string, Num | Startle[]> & { startles?: Startle[] };
  audio?: Record<string, Num> | null;
  states?: RecordState[];
  quality: { baseline_status: string; head_coverage: Num; hand_coverage: Num };
}

export interface Evidence { question_key: string; feature: string; value: unknown }
export interface Observation {
  statement: string;
  question_keys: string[];
  features: string[];
  states?: { key: StateKey; label: string; likelihood: Likelihood }[];
  direction: string;
  evidence: Evidence[];
}
export interface Summary {
  observations: Observation[];
  data_quality: Observation[];
  limitations: string[];
}
export interface SummaryResult {
  status: "ok" | "unavailable" | "rejected" | "demo" | "idle" | "loading";
  reason?: string | null;
  model?: string | null;
  summary: Summary | null;
}

export interface Baseline { status: string; windows: number }

/** One Realtime broadcast message (or a replayed one in demo mode). */
export type QsMessage =
  | { event: "window"; payload: LiveWindow }
  | { event: "event"; payload: QsEvent }
  | { event: "record"; payload: QsRecord }
  | { event: "baseline"; payload: Baseline }
  | { event: "summary"; payload: SummaryResult };

export interface Question { key: string; prompt: string | null }

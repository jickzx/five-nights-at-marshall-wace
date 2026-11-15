// The one place session state changes. Live Realtime messages and the demo replay both go through
// applyMessage, so the demo exercises exactly the code a real session uses.
import type { Baseline, LiveWindow, QsEvent, QsMessage, QsRecord, Session, SummaryResult } from "./types";

export const MAX_WINDOWS = 1200; // ten minutes at two windows a second

export interface SessionView {
  session: Session;
  windows: LiveWindow[];        // ordered by seq, de-duplicated
  events: QsEvent[];            // ordered by t
  records: Map<string, QsRecord>;
  baseline: Baseline | null;
  summary: SummaryResult;
  lastSeq: number;
}

export function emptyView(session: Session): SessionView {
  return { session, windows: [], events: [], records: new Map(), baseline: null,
    summary: { status: "idle", summary: null }, lastSeq: -1 };
}

function insertWindow(view: SessionView, w: LiveWindow) {
  if (view.windows.length === 0 || w.seq > view.windows[view.windows.length - 1].seq) {
    view.windows.push(w);
  } else if (!view.windows.some((x) => x.seq === w.seq)) {
    // Late or out-of-order delivery (reconnect catch-up): keep seq order.
    const i = view.windows.findIndex((x) => x.seq > w.seq);
    view.windows.splice(i, 0, w);
  } else {
    return;
  }
  if (view.windows.length > MAX_WINDOWS) view.windows.splice(0, view.windows.length - MAX_WINDOWS);
  view.lastSeq = Math.max(view.lastSeq, w.seq);
}

function insertEvent(view: SessionView, e: QsEvent) {
  if (view.events.some((x) => x.t === e.t && x.type === e.type && x.question_key === e.question_key)) return;
  const i = view.events.findIndex((x) => x.t > e.t);
  if (i < 0) view.events.push(e); else view.events.splice(i, 0, e);
}

/** Apply one message; returns what changed so the UI repaints only that. */
export function applyMessage(view: SessionView, msg: QsMessage): "window" | "event" | "record" | "baseline" | "summary" | null {
  switch (msg.event) {
    case "window": insertWindow(view, msg.payload); return "window";
    case "event":
      insertEvent(view, msg.payload);
      if (msg.payload.type === "session_end") view.session = { ...view.session, status: "ended" };
      else if (view.session.status === "created") view.session = { ...view.session, status: "live" };
      return "event";
    case "record": view.records.set(msg.payload.question_key, msg.payload); return "record";
    case "baseline": view.baseline = msg.payload; return "baseline";
    case "summary":
      // a stored good summary is never replaced by a failed attempt
      if (view.summary.status === "ok" && msg.payload.status !== "ok") return null;
      view.summary = msg.payload; return "summary";
    default: return null;
  }
}

export function orderedRecords(view: SessionView): QsRecord[] {
  return [...view.records.values()].sort((a, b) => (a.t_shown ?? Infinity) - (b.t_shown ?? Infinity));
}

/** The time span of each question, for shading the timeline. */
export function questionBands(view: SessionView): { key: string; t0: number; t1: number | null }[] {
  const bands: { key: string; t0: number; t1: number | null }[] = [];
  for (const r of orderedRecords(view)) if (r.t_shown != null) bands.push({ key: r.question_key, t0: r.t_shown, t1: r.t_end });
  // A question that is shown but has no record yet (record arrives on the next event).
  for (const e of view.events) {
    if (e.type === "question_shown" && e.question_key && !bands.some((b) => b.key === e.question_key))
      bands.push({ key: e.question_key, t0: e.t, t1: null });
  }
  return bands.sort((a, b) => a.t0 - b.t0);
}

// Demo mode: replays a session that was produced by running synthetic motion through the real QS
// pipeline (qs-backend/scripts/make_demo_fixture.py). Messages are delivered on a timer in the same
// shape Supabase Realtime delivers them, and go through the same store code.
import type { QsSource, SessionSnapshot } from "./source";
import type { QsMessage, QsRecord, Question, Session, SummaryResult, Baseline } from "./types";

export interface FixtureSession {
  session: Session;
  consent: unknown;
  baseline: (Baseline & Record<string, unknown>) | null;
  records: QsRecord[];
  candidate_summary: Record<string, unknown>;
  stream?: { at: number; event: QsMessage["event"]; payload: unknown }[];
  summary?: SummaryResult;
}

export interface Fixture {
  question_set: { id: string; name: string; questions: Question[] };
  sessions: FixtureSession[];
}

export interface Clock {
  now(): number;                                  // ms
  every(ms: number, fn: () => void): () => void;  // returns cancel
}

export const realClock: Clock = {
  now: () => performance.now(),
  every: (ms, fn) => { const id = setInterval(fn, ms); return () => clearInterval(id); },
};

/** Replay speed: one second of session per second of wall time, sped up during the quiet calibration. */
export class DemoSource implements QsSource {
  readonly mode = "demo" as const;
  private live: FixtureSession;
  private startedAt: number;
  private sessionWatchers = new Set<() => void>();
  private announced = false;

  constructor(private fixture: Fixture, private clock: Clock = realClock, private loop = true,
              private calibrationSpeed = 4) {
    this.live = fixture.sessions.find((s) => s.stream) ?? fixture.sessions[0];
    this.startedAt = clock.now();
  }

  /** Session-time (s) of the replay right now. Calibration (first 26 s) plays faster so the demo gets going. */
  sessionTime(): number {
    const wall = (this.clock.now() - this.startedAt) / 1000;
    const fastEnd = 26 / this.calibrationSpeed;
    const t = wall < fastEnd ? wall * this.calibrationSpeed : 26 + (wall - fastEnd);
    return t;
  }

  private duration(): number {
    const s = this.live.stream ?? [];
    return s.length ? s[s.length - 1].at : 0;
  }

  async questionPrompts(): Promise<Record<string, Question>> {
    return Object.fromEntries(this.fixture.question_set.questions.map((q) => [q.key, q]));
  }

  async listSessions(): Promise<Session[]> {
    return this.fixture.sessions.map((s, i) => {
      if (s !== this.live) return { ...s.session, status: "ended" as const, started_at: minutesAgo(18 + i * 23) };
      const t = this.sessionTime();
      const status = t >= this.duration() ? "ended" : "live";
      return { ...s.session, status, started_at: minutesAgo(t / 60) };
    });
  }

  async load(sessionId: string): Promise<SessionSnapshot> {
    const s = this.fixture.sessions.find((x) => x.session.id === sessionId);
    if (!s) throw new Error("unknown demo session");
    if (s !== this.live) {
      return { session: { ...s.session, status: "ended" }, windows: [], events: [], records: s.records,
        baseline: s.baseline, summary: { status: "idle", summary: null } };
    }
    // Live session: start empty; everything arrives through subscribe(), exactly like a real live view
    // that connected at the start of the session.
    return { session: { ...s.session, status: "live" }, windows: [], events: [], records: [], baseline: null,
      summary: { status: "idle", summary: null } };
  }

  subscribe(sessionId: string, onMessage: (m: QsMessage) => void, onReset?: () => void): () => void {
    if (sessionId !== this.live.session.id) return () => {};
    const stream = this.live.stream ?? [];
    let i = 0;
    const tick = () => {
      const t = this.sessionTime();
      while (i < stream.length && stream[i].at <= t) {
        const m = stream[i++];
        onMessage({ event: m.event, payload: m.payload } as QsMessage);
      }
      if (i >= stream.length) {
        if (!this.announced) { this.announced = true; this.sessionWatchers.forEach((f) => f()); }
        if (this.loop && t > this.duration() + 12) {
          // Start the replay again so the page keeps moving during a demo.
          this.startedAt = this.clock.now();
          i = 0;
          this.announced = false;
          onReset?.();
          this.sessionWatchers.forEach((f) => f());
        }
      }
    };
    tick();
    return this.clock.every(100, tick);
  }

  watchSessions(onChange: () => void): () => void {
    this.sessionWatchers.add(onChange);
    return () => this.sessionWatchers.delete(onChange);
  }

  async requestSummary(sessionId: string): Promise<SummaryResult> {
    const s = this.fixture.sessions.find((x) => x.session.id === sessionId);
    if (s?.summary) return s.summary;
    return { status: "unavailable", reason: "The demo includes a summary for the featured session only.", summary: null };
  }

  fixtureSession(sessionId: string): FixtureSession | undefined {
    return this.fixture.sessions.find((x) => x.session.id === sessionId);
  }
}

function minutesAgo(m: number): string {
  return new Date(Date.now() - m * 60_000).toISOString();
}

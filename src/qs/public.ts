// Public mode: the demo employer's sessions from the API's read-only public view (GET /v1/public/...), with no
// sign-in. Live updates come from polling (Realtime channels are private to signed-in employers).
import type { QsConfig, QsSource, SessionSnapshot } from "./source";
import type { QsMessage, Question, Session, SummaryResult } from "./types";

const POLL_MS = 1000;

export class PublicSource implements QsSource {
  readonly mode = "live" as const;
  constructor(private cfg: QsConfig) {}

  private async get<T>(path: string): Promise<T> {
    const r = await fetch(`${this.cfg.apiUrl}/v1/public${path}`);
    if (!r.ok) throw new Error(`${path.split("?")[0]}: HTTP ${r.status}`);
    return r.json() as Promise<T>;
  }

  async questionPrompts(): Promise<Record<string, Question>> {
    const out: Record<string, Question> = {};
    for (const s of await this.get<{ questions: Question[] }[]>("/question-sets")) for (const q of s.questions) out[q.key] ??= q;
    return out;
  }

  listSessions(): Promise<Session[]> { return this.get<Session[]>("/sessions?limit=50"); }

  async load(id: string): Promise<SessionSnapshot> {
    const [session, records, windows, events] = await Promise.all([
      this.get<Session & { baseline: SessionSnapshot["baseline"] }>(`/sessions/${id}`),
      this.get<{ record: SessionSnapshot["records"][number] }[]>(`/sessions/${id}/qs`),
      this.get<SessionSnapshot["windows"]>(`/sessions/${id}/windows?tail=true&limit=1200`),
      this.get<SessionSnapshot["events"]>(`/sessions/${id}/events`),
    ]);
    return { session, windows, events, records: records.map((r) => r.record), baseline: session.baseline ?? null,
      summary: await this.storedSummary(id) };
  }

  async storedSummary(id: string): Promise<SummaryResult> {
    try {
      const row = await this.get<{ status: SummaryResult["status"]; summary: SummaryResult["summary"]; model: string | null; error: string | null }>(`/sessions/${id}/summary`);
      return { status: row.status, summary: row.summary, model: row.model, reason: row.error };
    } catch {
      return { status: "idle", summary: null };
    }
  }

  subscribe(sessionId: string, onMessage: (m: QsMessage) => void): () => void {
    let lastSeq = -1, stopped = false, ticks = 0, seenSummary = false;
    const id = encodeURIComponent(sessionId);
    const tick = async () => {
      if (stopped) return;
      try {
        const windows = await this.get<SessionSnapshot["windows"]>(`/sessions/${id}/windows?after_seq=${lastSeq}&limit=500`);
        for (const w of windows) { lastSeq = Math.max(lastSeq, w.seq); onMessage({ event: "window", payload: w }); }
        if (ticks++ % 2 === 0) {
          const [events, records] = await Promise.all([
            this.get<SessionSnapshot["events"]>(`/sessions/${id}/events`),
            this.get<{ record: SessionSnapshot["records"][number] }[]>(`/sessions/${id}/qs`),
          ]);
          events.forEach((e) => onMessage({ event: "event", payload: e }));
          records.forEach((r) => onMessage({ event: "record", payload: r.record }));
          if (!seenSummary && events.some((e) => e.type === "session_end")) {
            const s = await this.storedSummary(sessionId);
            if (s.status !== "idle") { seenSummary = true; onMessage({ event: "summary", payload: s }); }
          }
        }
      } catch { /* network blip: try again next tick */ }
      if (!stopped) setTimeout(tick, POLL_MS);
    };
    void tick();
    return () => { stopped = true; };
  }

  watchSessions(onChange: () => void): () => void {
    const t = setInterval(onChange, 5000);
    return () => clearInterval(t);
  }

  async requestSummary(sessionId: string): Promise<SummaryResult> {
    // The server writes the summary when a session ends; the public view only reads it.
    const s = await this.storedSummary(sessionId);
    return s.status === "idle" ? { status: "unavailable", reason: "The summary is written when the session ends.", summary: null } : s;
  }
}

// Live mode: the employer API for reads, Supabase Realtime Broadcast (private channels) for updates.
// Only the publishable key and the signed-in employer's own session token are ever in the browser.
import type { SupabaseClient } from "@supabase/supabase-js";
import type { QsConfig, QsSource, SessionSnapshot } from "./source";
import type { QsMessage, Question, Session, SummaryResult } from "./types";

export class LiveSource implements QsSource {
  readonly mode = "live" as const;

  private constructor(private cfg: QsConfig, private sb: SupabaseClient) {}

  /** The Supabase client is loaded only in live mode, so the demo bundle stays small. */
  static async create(cfg: QsConfig): Promise<LiveSource> {
    const { createClient } = await import("@supabase/supabase-js");
    const sb = createClient(cfg.supabaseUrl, cfg.publishableKey, { auth: { persistSession: true } });
    return new LiveSource(cfg, sb);
  }

  async signedIn(): Promise<boolean> {
    const { data } = await this.sb.auth.getSession();
    return !!data.session;
  }

  async signIn(email: string, password: string): Promise<string | null> {
    const { error } = await this.sb.auth.signInWithPassword({ email, password });
    return error ? error.message : null;
  }

  async signOut(): Promise<void> { await this.sb.auth.signOut(); }

  private async token(): Promise<string> {
    const { data } = await this.sb.auth.getSession();
    if (!data.session) throw new Error("not signed in");
    return data.session.access_token;
  }

  private async get<T>(path: string, init?: RequestInit): Promise<T> {
    const r = await fetch(this.cfg.apiUrl + path, {
      ...init,
      headers: { Authorization: `Bearer ${await this.token()}`, "Content-Type": "application/json", ...(init?.headers ?? {}) },
    });
    if (!r.ok) throw new Error(`${path}: HTTP ${r.status}`);
    return r.json() as Promise<T>;
  }

  async questionPrompts(): Promise<Record<string, Question>> {
    const sets = await this.get<{ questions: Question[] }[]>("/v1/question-sets");
    const out: Record<string, Question> = {};
    for (const s of sets) for (const q of s.questions) out[q.key] ??= q;
    return out;
  }

  listSessions(): Promise<Session[]> {
    return this.get<Session[]>("/v1/sessions?limit=50");
  }

  async load(id: string): Promise<SessionSnapshot> {
    const [session, records, windows, events] = await Promise.all([
      this.get<Session & { baseline: SessionSnapshot["baseline"] }>(`/v1/sessions/${id}`),
      this.get<{ record: SessionSnapshot["records"][number] }[]>(`/v1/sessions/${id}/qs`),
      this.get<SessionSnapshot["windows"]>(`/v1/sessions/${id}/windows?tail=true&limit=1200`),
      this.get<SessionSnapshot["events"]>(`/v1/sessions/${id}/events`),
    ]);
    const summary = await this.storedSummary(id);
    return { session, windows, events, records: records.map((r) => r.record), baseline: session.baseline ?? null, summary };
  }

  /** The stored summary (ok, or a failed attempt with its reason), or idle when none has been written yet. */
  async storedSummary(id: string): Promise<SummaryResult> {
    try {
      const row = await this.get<{ status: SummaryResult["status"]; summary: SummaryResult["summary"]; model: string | null; error: string | null }>(`/v1/sessions/${id}/summary`);
      return { status: row.status, summary: row.summary, model: row.model, reason: row.error };
    } catch {
      return { status: "idle", summary: null };
    }
  }

  subscribe(sessionId: string, onMessage: (m: QsMessage) => void): () => void {
    const channel = this.sb.channel(`qs:session:${sessionId}`, { config: { private: true } });
    let lastSeq = -1;
    let joined = false;
    let closed = false;
    const forward = (m: QsMessage) => {
      if (m.event === "window") lastSeq = Math.max(lastSeq, m.payload.seq);
      onMessage(m);
    };
    for (const event of ["window", "event", "record", "baseline", "summary"] as const) {
      channel.on("broadcast", { event }, (msg) => forward({ event, payload: msg.payload } as QsMessage));
    }
    // Broadcasts sent while the socket was down are not replayed by Realtime, so after every rejoin
    // fetch what was missed from the API. The store drops anything already seen.
    const catchUp = async () => {
      const id = encodeURIComponent(sessionId);
      const [windows, events, records] = await Promise.all([
        this.get<SessionSnapshot["windows"]>(`/v1/sessions/${id}/windows?after_seq=${lastSeq}&limit=5000`),
        this.get<SessionSnapshot["events"]>(`/v1/sessions/${id}/events`),
        this.get<{ record: SessionSnapshot["records"][number] }[]>(`/v1/sessions/${id}/qs`),
      ]);
      if (closed) return;
      windows.forEach((w) => forward({ event: "window", payload: w }));
      events.forEach((e) => forward({ event: "event", payload: e }));
      records.forEach((r) => forward({ event: "record", payload: r.record }));
    };
    void this.token().then((t) => this.sb.realtime.setAuth(t)).then(() => channel.subscribe((status) => {
      if (status !== "SUBSCRIBED") return;
      if (joined) void catchUp().catch(() => {});
      joined = true;
    }));
    return () => { closed = true; void this.sb.removeChannel(channel); };
  }

  watchSessions(onChange: () => void): () => void {
    // One channel per employer the user belongs to; the policy only lets members join.
    const channels: ReturnType<SupabaseClient["channel"]>[] = [];
    void this.get<{ employers: { employer_id: string }[] }>("/v1/me").then(async (me) => {
      await this.sb.realtime.setAuth(await this.token());
      for (const e of me.employers) {
        const ch = this.sb.channel(`qs:employer:${e.employer_id}`, { config: { private: true } });
        ch.on("broadcast", { event: "session" }, () => onChange()).subscribe();
        channels.push(ch);
      }
    }).catch(() => {});
    return () => channels.forEach((c) => void this.sb.removeChannel(c));
  }

  async requestSummary(sessionId: string, refresh = false): Promise<SummaryResult> {
    try {
      return await this.get<SummaryResult>(`/v1/sessions/${sessionId}/summary${refresh ? "?refresh=true" : ""}`, { method: "POST" });
    } catch (e) {
      return { status: "unavailable", reason: e instanceof Error ? e.message : "request failed", summary: null };
    }
  }
}

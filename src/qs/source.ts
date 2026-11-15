// Where session data comes from. Both sources hand the page the same things, and both deliver live
// updates as QsMessage objects that go through store.applyMessage.
import type { QsEvent, QsMessage, QsRecord, Question, Session, SummaryResult, LiveWindow, Baseline } from "./types";

export interface SessionSnapshot {
  session: Session;
  windows: LiveWindow[];
  events: QsEvent[];
  records: QsRecord[];
  baseline: Baseline | null;
  summary: SummaryResult;
}

export interface QsSource {
  readonly mode: "demo" | "live";
  questionPrompts(): Promise<Record<string, Question>>;
  listSessions(): Promise<Session[]>;
  load(sessionId: string): Promise<SessionSnapshot>;
  /** Live updates for one session. Returns an unsubscribe function. onReset: start the view over (demo loop). */
  subscribe(sessionId: string, onMessage: (m: QsMessage) => void, onReset?: () => void): () => void;
  /** Session list changes (new session, started, ended). Returns an unsubscribe function. */
  watchSessions(onChange: () => void): () => void;
  requestSummary(sessionId: string): Promise<SummaryResult>;
}

export interface QsConfig {
  apiUrl: string;
  supabaseUrl: string;
  publishableKey: string;
  demo: boolean;
}

/** Demo mode is on whenever no API URL is configured, or when ?demo is in the address. */
export function readConfig(env: Record<string, string | undefined>, search: string): QsConfig {
  const apiUrl = (env.VITE_QS_API_URL ?? "").trim().replace(/\/+$/, "");
  const supabaseUrl = (env.VITE_SUPABASE_URL ?? "").trim().replace(/\/+$/, "");
  const publishableKey = (env.VITE_SUPABASE_PUBLISHABLE_KEY ?? "").trim();
  const forced = new URLSearchParams(search).has("demo");
  return { apiUrl, supabaseUrl, publishableKey, demo: forced || !apiUrl };
}

/** Refuse to run with anything that looks like a server-side secret in the browser bundle. */
export function assertBrowserSafeKey(key: string): void {
  if (key.startsWith("sb_secret_")) throw new Error("A Supabase secret key was configured for the browser. Use the publishable key.");
  const parts = key.split(".");
  if (parts.length === 3) {
    try {
      const claims = JSON.parse(atob(parts[1].replace(/-/g, "+").replace(/_/g, "/")));
      if (claims.role === "service_role") throw new Error("The service_role key was configured for the browser. Use the publishable (anon) key.");
    } catch (e) {
      if (e instanceof Error && e.message.includes("service_role")) throw e;
    }
  }
}

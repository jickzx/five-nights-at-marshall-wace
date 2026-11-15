// QS employer page. Picks a data source (live API + Realtime, or the demo replay with ?demo), keeps one
// SessionView per selected session via store.applyMessage, and paints it as plain statements first and numbers
// second (small print and the expandable question details).
import "./qs.css";
import { COLORS, ribbon, sparkline } from "./charts";
import { DemoSource, type Fixture } from "./demo";
import { FEATURES, OUTCOME_LABELS, fmt } from "./labels";
import { assertBrowserSafeKey, readConfig, type QsSource } from "./source";
import { because, chipText, current, FAMILY, headline, questionName, topOf, vsCalm } from "./states";
import { applyMessage, emptyView, orderedRecords, type SessionView } from "./store";
import type { LiveWindow, Observation, QsMessage, QsRecord, Question, RecordState, Session, SummaryResult } from "./types";

const $ = <T extends HTMLElement = HTMLElement>(id: string) => document.getElementById(id) as T;

export function esc(s: unknown): string {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
}

const state = {
  source: null as QsSource | null,
  sessions: [] as Session[],
  selected: null as string | null,
  view: null as SessionView | null,
  prompts: {} as Record<string, Question>,
  unsubscribe: (() => {}) as () => void,
  open: new Set<string>(),
  nums: new Set<string>(),          // questions whose "The numbers" panel is open (kept across live re-renders)
  dirty: { now: true, records: true, summary: true },
  totalSeconds: 140,
  summaryWait: 0 as number,
};

function show(...ids: string[]) { for (const id of ids) $(id).hidden = false; }

// ---------------------------------------------------------------- sessions

function ago(iso?: string | null): string {
  if (!iso) return "";
  const m = Math.max(0, (Date.now() - Date.parse(iso)) / 60000);
  return m < 1 ? "just now" : m < 60 ? `${Math.round(m)} min ago` : m < 48 * 60 ? `${Math.round(m / 60)} h ago` : `${Math.round(m / 1440)} days ago`;
}

function paintSessions() {
  const nav = $("sessions");
  if (!state.sessions.length) {
    nav.innerHTML = `<p class="empty">No sessions yet. A session appears here as soon as a candidate agrees to start in the headset.</p>`;
    return;
  }
  nav.innerHTML = `<ul>${state.sessions.map((s) => {
    const what = s.status === "live" ? "Live now" : s.status === "ended" ? `Finished ${ago(s.ended_at ?? s.started_at)}` : "Waiting for consent";
    const src = s.device_id ? "Headset" : "";
    return `<li><button type="button" data-session="${esc(s.id)}" aria-pressed="${s.id === state.selected}" class="${s.status}">
      <span class="pseudo">${esc(s.candidate_pseudonym ?? "Candidate")}</span>
      <span class="meta">${s.status === "live" ? `<i class="dot" aria-hidden="true"></i>` : ""}${esc(what)}${src ? `<span class="src">${src}</span>` : ""}</span>
    </button></li>`;
  }).join("")}</ul>`;
}

async function refreshSessions() {
  if (!state.source) return;
  try { state.sessions = await state.source.listSessions(); } catch { return; }
  if (state.selected && state.view) {
    const s = state.sessions.find((x) => x.id === state.selected);
    if (s && s.status !== state.view.session.status) { state.view.session = { ...state.view.session, ...s }; markAll(); }
  }
  paintSessions();
}

let selectToken = 0;

async function select(id: string) {
  if (!state.source) return;
  const source = state.source;
  const token = ++selectToken;
  state.unsubscribe();
  state.selected = id;
  state.open.clear();
  state.nums.clear();
  paintSessions();
  // Subscribe before loading, and hold messages until the snapshot is in, so nothing broadcast while the
  // snapshot loads is lost. The store drops duplicates, so overlap between the two is harmless.
  let view: SessionView | null = null;
  const held: QsMessage[] = [];
  const apply = (m: QsMessage) => {
    if (!view) { held.push(m); return; }
    const changed = applyMessage(view, m);
    if (changed === "window" || changed === "event" || changed === "baseline") state.dirty.now = true;
    if (changed === "record") { state.dirty.records = true; state.dirty.now = true; }
    if (changed === "summary") state.dirty.summary = true;
    if (changed === "event" && m.event === "event" && m.payload.type === "session_end") onEnded(view);
  };
  const unsubscribe = source.subscribe(id, (m) => { if (token === selectToken) apply(m); }, () => {
    if (token === selectToken && view) { view = emptyView({ ...view.session, status: "live" }); state.view = view; markAll(); }
  });
  state.unsubscribe = unsubscribe;
  let snap;
  try {
    snap = await source.load(id);
  } catch (e) {
    if (token === selectToken) $("who").textContent = `Could not load this session: ${(e as Error).message}`;
    return;
  }
  if (token !== selectToken) { unsubscribe(); return; }
  const v = emptyView(snap.session);
  v.baseline = snap.baseline;
  v.summary = snap.summary;
  for (const w of snap.windows) applyMessage(v, { event: "window", payload: w });
  for (const e of snap.events) applyMessage(v, { event: "event", payload: e });
  for (const r of snap.records) applyMessage(v, { event: "record", payload: r });
  view = v;
  for (const m of held.splice(0)) apply(m);
  state.view = v;
  if (v.session.status === "ended" && v.summary.status === "idle") onEnded(v);
  markAll();
}

/** When a session ends the server writes the summary (Gemini Flash) on its own; wait for it, then ask once. */
function onEnded(view: SessionView) {
  const source = state.source;
  if (!source || source.mode === "demo") return;
  window.clearTimeout(state.summaryWait);
  let tries = 0;
  const poll = async () => {
    if (state.view !== view || view.summary.status === "ok" || view.summary.status === "unavailable" || view.summary.status === "rejected") return;
    tries++;
    const got = await source.storedSummary?.(view.session.id);
    if (state.view !== view) return;
    if (got && got.status !== "idle") { view.summary = got; state.dirty.summary = true; return; }
    if (tries < 6) { state.summaryWait = window.setTimeout(poll, 5000); return; }
    view.summary = { status: "loading", summary: null }; state.dirty.summary = true;
    view.summary = await source.requestSummary(view.session.id);
    if (state.view === view) state.dirty.summary = true;
  };
  view.summary = view.summary.status === "idle" ? { status: "loading", summary: null } : view.summary;
  state.dirty.summary = true;
  state.summaryWait = window.setTimeout(poll, 3000);
}

function markAll() { state.dirty.now = state.dirty.records = state.dirty.summary = true; }

// ---------------------------------------------------------------- now

function lastValue(view: SessionView, pick: (w: LiveWindow) => number | null | undefined, n = 6): number | null {
  for (let i = view.windows.length - 1, k = 0; i >= 0 && k < n; i--, k++) {
    const v = pick(view.windows[i]);
    if (v != null) return v;
  }
  return null;
}

function paintNow() {
  const view = state.view;
  if (!view) return;
  const s = view.session;
  const last = view.windows.at(-1);
  const q = last?.question_key;
  const minutes = last ? Math.max(1, Math.round(last.t_end / 60)) : 0;
  $("who").textContent = s.status === "live"
    ? `${s.candidate_pseudonym ?? "Candidate"}, live for ${minutes} min, ${q ? `on ${questionName(q)}` : "between questions"}`
    : s.status === "ended" ? `${s.candidate_pseudonym ?? "Candidate"}, finished ${ago(s.ended_at)}` : `${s.candidate_pseudonym ?? "Candidate"}, waiting for consent`;

  const now = s.status === "live" ? current(view.windows) : null;
  const h = $("headline"), b = $("because"), also = $("also"), chips = $("chips");
  h.className = "headline";
  if (s.status === "ended") {
    const recs = orderedRecords(view);
    const tops = recs.map((r) => topOf(r.states)).filter((x): x is RecordState => !!x && x.key !== "calm");
    h.textContent = "Session finished.";
    b.textContent = recs.length
      ? `${recs.length} question${recs.length > 1 ? "s" : ""}, ${recs.filter((r) => r.outcome === "correct").length} answered correctly. ${tops.length ? `Strongest moments: ${tops.slice(0, 3).map((t) => `${chipText(t).toLowerCase()} on ${questionName(t.question_key)}`).join(", ")}.` : "No strong states on any question."}`
      : "No questions were reached.";
    also.textContent = "";
    chips.innerHTML = "";
  } else if (s.status !== "live" || !view.windows.length) {
    h.textContent = "Waiting for the candidate.";
    b.textContent = "Nothing is recorded until they agree to the consent statement in the headset.";
    also.textContent = ""; chips.innerHTML = "";
  } else if (!now) {
    h.textContent = "Calibrating.";
    b.textContent = "For the first 20 seconds QS learns how this person moves and breathes when settled. Everything after is read against that.";
    also.textContent = ""; chips.innerHTML = "";
  } else {
    h.textContent = headline(now.state);
    h.classList.add(FAMILY[now.state.key]);
    b.textContent = because(now.state);
    also.textContent = now.state.key === "calm" ? "" : `Could also be ${now.state.could_also_be}`;
    chips.innerHTML = now.all.filter((x) => x.key !== now.state.key)
      .map((x) => `<li class="chip ${FAMILY[x.key]} ${x.likelihood}" title="${esc(x.evidence.join("; "))}">${esc(chipText(x))}</li>`).join("");
  }

  // vitals: words first, number small
  const vitals: [string, (w: LiveWindow) => number | null, (w: LiveWindow) => number | null, string][] = [
    ["breath", (w) => w.rel?.audio_breath_rate_bpm ?? null, (w) => w.features.audio_breath_rate_bpm ?? null, COLORS.arousal],
    ["hands", (w) => Math.max(w.rel?.left_tremor_rms ?? 0, w.rel?.right_tremor_rms ?? 0) || null, () => null, COLORS.alarm],
    ["head", (w) => w.rel?.head_yaw_scan_rate ?? null, (w) => w.features.head_yaw_scan_rate ?? null, COLORS.calm],
  ];
  for (const [key, ratio, raw, color] of vitals) {
    const el = document.querySelector<HTMLElement>(`[data-vital="${key}"]`)!;
    const r = lastValue(view, ratio), v = lastValue(view, raw);
    const noun = key === "breath" ? "rate" : key === "hands" ? "steadiness" : "level";
    let words = vsCalm(r, noun), small = "";
    if (key === "breath") {
      const hold = lastValue(view, (w) => w.features.audio_since_last_breath_s ?? null, 2);
      const any = view.windows.some((w) => w.features.audio_breath_amp != null);
      if (!any) { words = "No microphone data"; small = "Headset sends motion only"; }
      else if (hold != null && hold >= 7) { words = `No breath heard for ${Math.round(hold)} s`; small = ""; }
      else small = v != null ? `${v.toFixed(0)} breaths a minute` : "";
    } else if (key === "hands") {
      const cov = lastValue(view, (w) => Math.max(w.features.left_coverage ?? 0, w.features.right_coverage ?? 0), 2);
      if (!cov) { words = "Controllers not tracked"; small = "Head only on this device"; }
      else { words = r == null ? "measuring" : r >= 2 ? `Shaking, ${r.toFixed(1)}× their calm level` : r <= 1.25 ? "Steady" : `A little shakier than calm`; small = "4–12 Hz hand motion"; }
    } else {
      small = v != null ? `${v.toFixed(0)}° per second of turning` : "";
      words = r == null ? "measuring" : vsCalm(r, "level");
    }
    el.querySelector(".v")!.textContent = words;
    el.querySelector(".n")!.textContent = small;
    sparkline(el.querySelector("canvas")!, view.windows, ratio, color);
  }
  const lastT = view.windows.at(-1)?.t_end ?? 0;
  if (lastT > state.totalSeconds) state.totalSeconds = Math.ceil(lastT / 30) * 30;
  ribbon($("ribbon") as HTMLCanvasElement, view, state.totalSeconds);
}

// ---------------------------------------------------------------- questions

const DETAIL_FIELDS = ["behaviour.time_to_first_action_s", "behaviour.response_latency_s", "behaviour.hesitations",
  "behaviour.answer_changes", "behaviour.hints_used", "motion.right_tremor_ratio", "motion.left_tremor_ratio",
  "motion.head_scan_ratio", "motion.restless_ratio", "motion.longest_freeze_s", "motion.startle_max_head_ratio",
  "audio.breath_rate_bpm", "audio.breath_rate_ratio_p90", "audio.deep_breaths", "audio.longest_breath_hold_s",
  "quality.head_coverage", "quality.hand_coverage"];

function valueOf(r: QsRecord, f: string): unknown {
  const [g, k] = f.split(".");
  return ((r as unknown as Record<string, Record<string, unknown> | null>)[g] ?? {})[k];
}

const sentence = (t: string) => (t ? t[0].toUpperCase() + t.slice(1) : t);

function stateLine(s: RecordState): string {
  const near = s.near_event ? ` After ${esc(s.near_event.text)}.` : "";
  return `<li><span class="chip ${FAMILY[s.key]} ${s.likelihood}">${esc(chipText(s))}</span>
    <span>${esc(sentence(s.evidence.map((e) => e.text).join("; ")))}.${near} <span class="could">Could also be ${esc(s.could_also_be)}</span></span></li>`;
}

function questionRow(r: QsRecord): string {
  const prompt = state.prompts[r.question_key]?.prompt;
  const states = r.states ?? [];
  const open = state.open.has(r.question_key);
  const dur = r.behaviour.duration_s;
  const top = topOf(states);
  const nums = DETAIL_FIELDS.map((f) => [f, valueOf(r, f)] as const).filter(([, v]) => v != null)
    .map(([f, v]) => `<div><dt>${esc(FEATURES[f]?.long ?? f)}</dt><dd>${esc(fmt(v, FEATURES[f]?.unit ?? ""))}</dd></div>`).join("");
  return `<li class="q ${r.status}">
    <button type="button" class="qhead" data-q="${esc(r.question_key)}" aria-expanded="${open}">
      <span class="qname">${esc(questionName(r.question_key))}</span>
      <span class="qout">${esc(OUTCOME_LABELS[r.outcome] ?? r.outcome)}${dur != null ? `, ${Math.round(dur)} s` : ""}</span>
      <span class="qtop">${top ? `<span class="chip ${FAMILY[top.key]} ${top.likelihood}">${esc(chipText(top))}</span>` : `<span class="muted">No reading yet</span>`}${states.length > 1 ? `<span class="more">+${states.length - 1}</span>` : ""}</span>
    </button>
    <div class="qbody" ${open ? "" : "hidden"}>
      ${prompt ? `<p class="prompt">${esc(prompt)}</p>` : ""}
      ${states.length ? `<ul class="slist">${states.map(stateLine).join("")}</ul>` : `<p class="muted">No possible states: not enough data yet, or no baseline.</p>`}
      <details data-nums="${esc(r.question_key)}" ${state.nums.has(r.question_key) ? "open" : ""}><summary>The numbers</summary><dl class="nums">${nums}</dl>
        <p class="fine">Ratios compare with this person's own calm start: 1× is as calm as then.</p></details>
    </div>
  </li>`;
}

function paintRecords() {
  const recs = state.view ? orderedRecords(state.view) : [];
  $("question-list").innerHTML = recs.length ? recs.map(questionRow).join("")
    : `<li class="empty">Questions appear here as the candidate reaches each console.</li>`;
}

// ---------------------------------------------------------------- summary

function observation(o: Observation): string {
  const chips = (o.states ?? []).map((s) => `<span class="chip ${FAMILY[s.key]} ${s.likelihood}">${esc(chipText(s))}</span>`).join("");
  const ev = o.evidence.filter((e) => e.value != null)
    .map((e) => `${esc(questionName(e.question_key))}: ${esc(FEATURES[e.feature]?.long ?? e.feature)} ${esc(fmt(e.value, FEATURES[e.feature]?.unit ?? ""))}`).join("; ");
  return `<li><p>${esc(o.statement)}</p>${chips ? `<p class="ochips">${chips}</p>` : ""}${ev ? `<p class="fine">${ev}</p>` : ""}</li>`;
}

function paintSummary() {
  const view = state.view;
  const body = $("summary-body");
  if (!view) { body.innerHTML = ""; return; }
  const s: SummaryResult = view.summary;
  const ended = view.session.status === "ended";
  const demo = state.source?.mode === "demo";
  const closed = orderedRecords(view).filter((r) => r.status === "closed").length;
  const button = `<button type="button" id="make-summary" class="quiet" ${closed ? "" : "disabled"}>${demo ? "Show the demo summary" : s.summary ? "Write it again" : "Write the summary now"}</button>`;
  let html: string;
  if (s.status === "loading") html = `<p class="lede">Gemini Flash is writing the summary from the question records and their states.</p>`;
  else if (s.summary) {
    html = `<p class="byline">${s.status === "demo" ? "Written for the demo and checked like a real summary; not generated by Gemini." : `Written by ${esc(s.model ?? "Gemini")} from the records below. Every statement cites them; numbers come from the records, not the model.`}</p>
      <ul class="obs">${s.summary.observations.map(observation).join("")}</ul>
      ${s.summary.data_quality.length ? `<h3>What limits this reading</h3><ul class="obs">${s.summary.data_quality.map(observation).join("")}</ul>` : ""}
      <ul class="limits">${s.summary.limitations.map((l) => `<li>${esc(l)}</li>`).join("")}</ul>`;
  } else if (s.status === "unavailable" || s.status === "rejected") {
    html = `<p class="notice"><b>Summary unavailable.</b> ${esc(s.reason ?? s.status)}</p><p class="fine">The states and question records above do not depend on it.</p>`;
  } else html = `<p class="lede">${ended ? "Waiting for the summary." : "When the session ends, Gemini Flash writes a short summary from the question records, their states and evidence."}</p>`;
  body.innerHTML = `${html}<p class="actions">${button}</p>`;
  $("make-summary")?.addEventListener("click", async () => {
    if (!state.view || !state.source) return;
    const v = state.view;
    const had = v.summary;
    v.summary = { status: "loading", summary: null };
    paintSummary();
    const got = await state.source.requestSummary(v.session.id, !!had.summary);
    v.summary = got.status !== "ok" && had.status === "ok" ? had : got;
    if (state.view === v) paintSummary();
  });
}

// ---------------------------------------------------------------- sign in (live mode only)

function showSignIn(source: import("./live").LiveSource) {
  show("intro");
  const el = $("signin");
  el.innerHTML = `<form>
      <label>Work email<input name="email" type="email" autocomplete="username" required></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
      <button type="submit">Sign in</button>
      <p class="fine" role="alert"></p>
    </form>`;
  el.querySelector("form")!.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = new FormData(e.target as HTMLFormElement);
    const alert = el.querySelector("[role=alert]")!;
    alert.textContent = "Signing in…";
    const err = await source.signIn(String(f.get("email")), String(f.get("password")));
    if (err) { alert.textContent = `Sign-in failed: ${err}. Check the email and password.`; return; }
    $("intro").hidden = true;
    await start(source);
  });
}

// ---------------------------------------------------------------- boot

async function start(source: QsSource) {
  state.source = source;
  show("sessions", "now", "vitals", "timeline", "questions", "summary");
  if (source.mode === "live") {
    const out = $("signout");
    out.hidden = false;
    out.onclick = async () => { await (source as import("./live").LiveSource).signOut(); location.reload(); };
  }
  state.prompts = await source.questionPrompts().catch(() => ({}));
  await refreshSessions();
  source.watchSessions(() => void refreshSessions());
  const first = state.sessions.find((s) => s.status === "live") ?? state.sessions[0];
  if (first) await select(first.id); else { $("headline").textContent = "No sessions yet."; $("because").textContent = "Start the game in an enrolled headset, or run the simulated feed."; }
  $("sessions").addEventListener("click", (e) => {
    const b = (e.target as HTMLElement).closest<HTMLButtonElement>("button[data-session]");
    if (b) void select(b.dataset.session!);
  });
  $("question-list").addEventListener("click", (e) => {
    const b = (e.target as HTMLElement).closest<HTMLButtonElement>("button[data-q]");
    if (!b) return;
    const k = b.dataset.q!;
    if (state.open.has(k)) state.open.delete(k); else state.open.add(k);
    state.dirty.records = true;
  });
  $("question-list").addEventListener("toggle", (e) => {
    const d = e.target as HTMLDetailsElement;
    const k = d.dataset?.nums;
    if (!k) return;
    if (d.open) state.nums.add(k); else state.nums.delete(k);
  }, true);
  setInterval(() => void refreshSessions(), source.mode === "demo" ? 5000 : 20000);
}

let lastNow = 0;
function frame(t: number) {
  // the live sentence repaints at most four times a second so it can be read
  if (state.dirty.now && t - lastNow > 250) { state.dirty.now = false; lastNow = t; paintNow(); }
  if (state.dirty.records) { state.dirty.records = false; paintRecords(); }
  if (state.dirty.summary) { state.dirty.summary = false; paintSummary(); }
  requestAnimationFrame(frame);
}

async function boot() {
  const cfg = readConfig(import.meta.env as Record<string, string | undefined>, location.search);
  const mode = $("mode");
  addEventListener("resize", () => { state.dirty.now = true; });
  requestAnimationFrame(frame);

  if (cfg.demo) {
    mode.className = "mode demo";
    mode.textContent = "Demo: a simulated session";
    const fixture = (await import("./demo/fixture.json")).default as unknown as Fixture;
    const last = fixture.sessions.find((s) => s.stream)?.stream?.at(-1)?.at;
    if (last) state.totalSeconds = last;
    await start(new DemoSource(fixture));
    return;
  }
  mode.className = "mode live";
  mode.textContent = "Live";
  try {
    assertBrowserSafeKey(cfg.publishableKey);
  } catch (e) {
    mode.textContent = "Misconfigured";
    show("intro");
    $("signin").innerHTML = `<p class="notice">${esc((e as Error).message)}</p>`;
    return;
  }
  const { LiveSource } = await import("./live");
  const live = await LiveSource.create(cfg);
  if (await live.signedIn()) await start(live); else showSignIn(live);
}

void boot();

// QS Data Pipeline page. Picks a data source (demo replay or live API + Realtime), keeps one
// SessionView per selected session via store.applyMessage, and paints it.
import "./qs.css";
import { drawDial, drawTimeline, drawTrace, SERIES } from "./charts";
import { DemoSource, type Fixture } from "./demo";
import { EMPLOYERS, EMPLOYERS_ARE_PLACEHOLDERS } from "./employers";
import { glyphSvg, pipeline, starfield, tide } from "./graphics";
import { FEATURES, LIVE_SIGNALS, OUTCOME_LABELS, PRINCIPLES, fmt } from "./labels";
import { PAL } from "./palette";
import { assertBrowserSafeKey, readConfig, type QsSource } from "./source";
import { applyMessage, emptyView, orderedRecords, type SessionView } from "./store";
import type { Observation, QsMessage, QsRecord, Question, Session, SummaryResult } from "./types";

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
  hidden: new Set<string>(),
  dirty: { charts: true, records: true, summary: true },
  totalSeconds: 140,
};

// ---------------------------------------------------------------- static parts

function paintStatic() {
  $("brand-glyph").innerHTML = glyphSvg(3, 18, PAL.blood);
  $("glyph-row").innerHTML = Array.from({ length: 9 }, (_, i) => glyphSvg(i * 13 + 5, 30, i % 3 === 1 ? PAL.blood : PAL.rustHot)).join("");
  $("employers-note").textContent = EMPLOYERS_ARE_PLACEHOLDERS
    ? "Placeholder names. These are invented sample employers, not customers, shown until the team adds its real list."
    : "";
  $("employer-list").innerHTML = EMPLOYERS.map((e) => `<li>
      ${e.logo ? `<img src="${esc(e.logo)}" alt="" width="44" height="44">` : glyphSvg(e.mark, 44, PAL.rustHot)}
      <span class="name">${esc(e.name)}</span><span class="sector">${esc(e.sector)}</span>
      ${EMPLOYERS_ARE_PLACEHOLDERS ? `<span class="ph">Sample</span>` : ""}</li>`).join("");
  $("principle-list").innerHTML = PRINCIPLES.map(([t, d]) => `<li><b>${esc(t)}</b>${esc(d)}</li>`).join("");
  $("legend").innerHTML = SERIES.map((s) => `<button type="button" data-series="${s.key}" aria-pressed="true"><i style="background:${s.color}"></i>${esc(s.label)}</button>`).join("");
  $("legend").addEventListener("click", (e) => {
    const b = (e.target as HTMLElement).closest("button");
    if (!b) return;
    const k = b.dataset.series!;
    if (state.hidden.has(k)) state.hidden.delete(k); else state.hidden.add(k);
    b.setAttribute("aria-pressed", String(!state.hidden.has(k)));
    state.dirty.charts = true;
  });
  const dials = [
    ["tremorR", LIVE_SIGNALS.tremorR, PAL.blood], ["tremorL", LIVE_SIGNALS.tremorL, PAL.rustHot],
    ["head", LIVE_SIGNALS.head, PAL.bone], ["scan", LIVE_SIGNALS.scan, PAL.amber],
  ] as const;
  $("dials").innerHTML = dials.map(([k, s]) => `<div class="panel dial" data-dial="${k}">
      <canvas aria-hidden="true"></canvas><div class="v" aria-live="off">—</div><div class="l">${esc(s.label)}</div><div class="n">${esc(s.note)}</div></div>`).join("");
}

// ---------------------------------------------------------------- sessions

function relTime(iso?: string | null): string {
  if (!iso) return "not started";
  const m = Math.max(0, (Date.now() - Date.parse(iso)) / 60000);
  return m < 1 ? "started just now" : m < 60 ? `started ${Math.round(m)} min ago` : `started ${Math.round(m / 60)} h ago`;
}

function paintSessions() {
  const list = $("session-list");
  if (!state.sessions.length) { list.innerHTML = `<li class="empty">No sessions yet.</li>`; return; }
  list.innerHTML = state.sessions.map((s) => `<li class="${s.id === state.selected ? "selected" : ""}">
      <span class="dot ${s.status === "live" ? "live" : ""}" title="${esc(s.status)}"></span>
      <span class="pseudo">${esc(s.candidate_pseudonym ?? "—")}</span>
      <span class="meta qset">${s.status === "live" ? "Live now" : s.status === "ended" ? "Finished" : "Waiting for consent"}</span>
      <span class="meta when">${esc(relTime(s.started_at))}</span>
      <button type="button" data-session="${esc(s.id)}" aria-pressed="${s.id === state.selected}">${s.id === state.selected ? "Viewing" : "View"}</button>
    </li>`).join("");
  $("stat-live").textContent = String(state.sessions.filter((s) => s.status === "live").length);
}

async function refreshSessions() {
  if (!state.source) return;
  state.sessions = await state.source.listSessions();
  if (state.selected) {
    const s = state.sessions.find((x) => x.id === state.selected);
    if (s && state.view) state.view.session = { ...state.view.session, status: s.status };
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
  paintSessions();
  // Subscribe before loading, and hold messages until the snapshot is in, so nothing broadcast while the
  // snapshot loads is lost. The store drops duplicates, so overlap between the two is harmless.
  let view: SessionView | null = null;
  const held: QsMessage[] = [];
  const apply = (m: QsMessage) => {
    if (!view) { held.push(m); return; }
    const changed = applyMessage(view, m);
    if (changed === "window" || changed === "event") state.dirty.charts = true;
    if (changed === "record") { state.dirty.records = true; state.dirty.charts = true; }
  };
  const unsubscribe = source.subscribe(id, (m) => { if (token === selectToken) apply(m); }, () => {
    if (token === selectToken && view) { view = emptyView({ ...view.session, status: "live" }); state.view = view; markAll(); }
  });
  state.unsubscribe = unsubscribe;
  let snap;
  try {
    snap = await source.load(id);
  } catch (e) {
    if (token === selectToken) $("live-who").textContent = `— could not load (${esc((e as Error).message)})`;
    return;
  }
  if (token !== selectToken) { unsubscribe(); return; }   // another session was picked meanwhile
  const v = emptyView(snap.session);
  v.baseline = snap.baseline;
  v.summary = snap.summary;
  for (const w of snap.windows) applyMessage(v, { event: "window", payload: w });
  for (const e of snap.events) applyMessage(v, { event: "event", payload: e });
  for (const r of snap.records) applyMessage(v, { event: "record", payload: r });
  view = v;
  for (const m of held.splice(0)) apply(m);
  state.view = v;
  $("live-who").textContent = `— ${snap.session.candidate_pseudonym ?? ""}`;
  markAll();
}

function markAll() { state.dirty.charts = state.dirty.records = state.dirty.summary = true; }

// ---------------------------------------------------------------- live charts

function latestRatio(view: SessionView, key: string): number | null {
  for (let i = view.windows.length - 1, n = 0; i >= 0 && n < 6; i--, n++) {
    const v = view.windows[i].rel?.[key];
    if (v != null) return v;
  }
  return null;
}

function paintCharts() {
  const view = state.view;
  if (!view) return;
  drawTrace($("trace") as HTMLCanvasElement, view, 60, state.hidden);
  drawTimeline($("timeline") as HTMLCanvasElement, view, state.totalSeconds);
  const keys: Record<string, string> = { tremorR: "right_tremor_rms", tremorL: "left_tremor_rms", head: "head_ang_speed_mean", scan: "head_yaw_scan_rate" };
  const colors: Record<string, string> = { tremorR: PAL.blood, tremorL: PAL.rustHot, head: PAL.bone, scan: PAL.amber };
  document.querySelectorAll<HTMLElement>("[data-dial]").forEach((el) => {
    const k = el.dataset.dial!;
    const v = latestRatio(view, keys[k]);
    drawDial(el.querySelector("canvas")!, v, colors[k]);
    el.querySelector(".v")!.textContent = v == null ? (view.baseline || view.windows.length > 60 ? "—" : "calibrating") : fmt(v, "×");
  });
  $("stat-windows").textContent = String(view.windows.length ? view.lastSeq + 1 : 0);
}

// ---------------------------------------------------------------- records

const BAR_FEATURES = ["motion.left_tremor_ratio", "motion.right_tremor_ratio", "motion.head_scan_ratio",
  "motion.restless_ratio", "motion.startle_max_head_ratio"];

function barRow(feature: string, value: number | null): string {
  const label = FEATURES[feature];
  // log2 scale: 0.25x at the left, 1x at 40%, 8x at the right.
  const pos = (v: number) => Math.max(0, Math.min(100, ((Math.log2(Math.max(v, 0.25)) + 2) / 5) * 100));
  let fill = "";
  if (value != null) {
    const a = pos(1), b = pos(value);
    fill = `<span class="fill ${value >= 1 ? "up" : "down"}" style="left:${Math.min(a, b)}%;width:${Math.max(1, Math.abs(b - a))}%"></span>`;
  }
  return `<li title="${esc(label.long)}"><span>${esc(label.short)}</span><span class="bar"><span class="one"></span>${fill}</span><span class="val">${fmt(value, "×")}</span></li>`;
}

function recordCard(r: QsRecord): string {
  const b = r.behaviour, m = r.motion as Record<string, number | null>;
  const prompt = state.prompts[r.question_key]?.prompt;
  const cell = (f: string) => `<div title="${esc(FEATURES[f].long)}"><dt>${esc(FEATURES[f].short)}</dt><dd>${fmt(b[f.split(".")[1]], FEATURES[f].unit)}</dd></div>`;
  return `<article class="panel record">
    <header><h3>${esc(r.question_key.toUpperCase())}${prompt ? `<span class="prompt">${esc(prompt)}</span>` : ""}</h3>
      <span class="tag ${r.status === "open" ? "open" : ""}">${esc(OUTCOME_LABELS[r.outcome] ?? r.outcome)}</span></header>
    <dl class="behaviour">
      ${cell("behaviour.time_to_first_action_s")}${cell("behaviour.response_latency_s")}${cell("behaviour.hesitations")}
      ${cell("behaviour.answer_changes")}${cell("behaviour.incorrect_submissions")}${cell("behaviour.hints_used")}
    </dl>
    <ul class="bars">${BAR_FEATURES.map((f) => barRow(f, m[f.split(".")[1]] ?? null)).join("")}
      <li title="${esc(FEATURES["motion.freeze_s"].long)}"><span>${esc(FEATURES["motion.freeze_s"].short)}</span><span class="bar"><span class="fill down" style="left:0;width:${Math.min(100, ((m.freeze_s ?? 0) / Math.max(1, b.duration_s ?? 1)) * 100)}%"></span></span><span class="val">${fmt(m.freeze_s, "s")}</span></li>
    </ul>
    <p class="quality">Baseline ${esc(r.quality.baseline_status)} · head tracked ${fmt(r.quality.head_coverage)} · hands tracked ${fmt(r.quality.hand_coverage)} · ${esc(r.qs_version)}</p>
  </article>`;
}

function paintRecords() {
  const view = state.view;
  const grid = $("record-grid");
  const recs = view ? orderedRecords(view) : [];
  grid.innerHTML = recs.length ? recs.map(recordCard).join("")
    : `<p class="empty">No QS records yet. A record appears when the first question is shown and is updated as it is answered.</p>`;
  $("stat-records").textContent = String(recs.length);
}

// ---------------------------------------------------------------- summary

function observation(o: Observation): string {
  const chips = o.evidence.length
    ? o.evidence.map((e) => `<span class="chip">${esc(e.question_key.toUpperCase())} · ${esc(FEATURES[e.feature]?.short ?? e.feature)} <b>${esc(fmt(e.value, FEATURES[e.feature]?.unit ?? ""))}</b></span>`).join("")
    : o.features.map((f) => `<span class="chip">${esc(o.question_keys.join(", ").toUpperCase())} · ${esc(FEATURES[f]?.short ?? f)}</span>`).join("");
  return `<li><div class="s">${esc(o.statement)}</div><div class="chips">${chips}</div></li>`;
}

function paintSummary() {
  const view = state.view;
  const body = $("summary-body");
  if (!view) { body.innerHTML = ""; return; }
  const s: SummaryResult = view.summary;
  const closed = orderedRecords(view).filter((r) => r.status === "closed").length;
  const demo = state.source?.mode === "demo";
  const button = `<button type="button" id="make-summary" ${closed ? "" : "disabled"}>${demo ? "Show demo summary" : "Write summary with Gemini"}</button>`;
  const intro = `<p class="lede">Gemini turns the QS records into observations an employer can read. Every statement must cite the questions and measurements it rests on; the values shown beside it come from the records, not from the model. It gives no score and no recommendation.</p>`;
  if (s.status === "idle" || s.status === "loading") {
    body.innerHTML = `${intro}<div class="head">${button}<span class="meta">${s.status === "loading" ? "Working…" : closed ? `${closed} closed question${closed > 1 ? "s" : ""} ready.` : "Waiting for the first closed question."}</span></div>`;
  } else if (!s.summary) {
    body.innerHTML = `${intro}<div class="head">${button}</div><p class="empty">Summary unavailable: ${esc(s.reason ?? s.status)} The QS records above are unaffected.</p>`;
  } else {
    body.innerHTML = `${intro}<div class="head">
        <span class="badge">${s.status === "demo" ? "Demo summary · not generated by Gemini" : `Gemini · ${esc(s.model ?? "")}`}</span>${button}</div>
      ${s.status === "demo" ? `<p class="fine">${esc(s.reason ?? "")}</p>` : ""}
      <h4>Observations</h4><ul class="obs">${s.summary.observations.map(observation).join("")}</ul>
      ${s.summary.data_quality.length ? `<h4>Data quality</h4><ul class="obs">${s.summary.data_quality.map(observation).join("")}</ul>` : ""}
      <ul class="limits">${s.summary.limitations.map((l) => `<li>${esc(l)}</li>`).join("")}</ul>`;
  }
  $("make-summary")?.addEventListener("click", async () => {
    if (!state.view || !state.source) return;
    const v = state.view;
    v.summary = { status: "loading", summary: null };
    paintSummary();
    v.summary = await state.source.requestSummary(v.session.id);
    if (state.view === v) paintSummary();
  });
}

// ---------------------------------------------------------------- sign in (live mode only)

function showSignIn(source: import("./live").LiveSource) {
  const el = $("signin");
  el.hidden = false;
  el.innerHTML = `<p>Sign in with your employer account to see your candidates' sessions.</p>
    <form><input name="email" type="email" autocomplete="username" placeholder="Email" required aria-label="Email">
    <input name="password" type="password" autocomplete="current-password" placeholder="Password" required aria-label="Password">
    <button type="submit">Sign in</button></form><p class="fine" role="alert"></p>`;
  el.querySelector("form")!.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = new FormData(e.target as HTMLFormElement);
    const err = await source.signIn(String(f.get("email")), String(f.get("password")));
    if (err) { el.querySelector("[role=alert]")!.textContent = err; return; }
    el.hidden = true;
    await start(source);
  });
}

// ---------------------------------------------------------------- boot

async function start(source: QsSource) {
  state.source = source;
  state.prompts = await source.questionPrompts().catch(() => ({}));
  await refreshSessions();
  source.watchSessions(() => void refreshSessions());
  const first = state.sessions.find((s) => s.status === "live") ?? state.sessions[0];
  if (first) await select(first.id);
  $("session-list").addEventListener("click", (e) => {
    const b = (e.target as HTMLElement).closest<HTMLButtonElement>("button[data-session]");
    if (b) void select(b.dataset.session!);
  });
  setInterval(() => void refreshSessions(), source.mode === "demo" ? 5000 : 30000);
}

function frame() {
  if (state.dirty.charts) { state.dirty.charts = false; paintCharts(); }
  if (state.dirty.records) { state.dirty.records = false; paintRecords(); state.dirty.summary = true; }
  if (state.dirty.summary) { state.dirty.summary = false; paintSummary(); }
  requestAnimationFrame(frame);
}

async function boot() {
  starfield($("sky") as HTMLCanvasElement);
  tide($("tide") as HTMLCanvasElement, () => 0.45);
  paintStatic();
  const cfg = readConfig(import.meta.env as Record<string, string | undefined>, location.search);
  const mode = $("mode");
  pipeline($("pipe") as HTMLCanvasElement, ["HEADSET", "INGEST", "QS FEATURES", "POSTGRES", "REALTIME", "YOU"],
    () => (state.view?.session.status === "live" ? 1 : 0.25));
  addEventListener("resize", () => { state.dirty.charts = true; });
  requestAnimationFrame(frame);

  if (cfg.demo) {
    mode.className = "mode demo";
    mode.innerHTML = `Demo data<span class="long"> · synthetic session</span>`;
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
    $("session-list").innerHTML = `<li class="empty">${esc((e as Error).message)}</li>`;
    return;
  }
  const { LiveSource } = await import("./live");
  const live = await LiveSource.create(cfg);
  if (await live.signedIn()) await start(live); else showSignIn(live);
}

void boot();

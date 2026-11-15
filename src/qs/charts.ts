// Canvas drawing for the live view: small sparklines for the vitals and the session ribbon (one coloured
// mark per second of the session, by the strongest possible state, with questions and game events under it).
// Ratios use a log2 axis centred on 1x (the person's own calm calibration).
import { FAMILY, rank } from "./states";
import { questionBands, type SessionView } from "./store";
import type { LiveWindow } from "./types";

export const INK = "#070506";
export const COLORS = {
  alarm: "#D0112B",     // Pal.Blood
  arousal: "#CE682E",   // Pal.RustHot
  calm: "#10152E",      // Pal.Indigo
  line: "#C9D6E6",      // Pal.Dawn
  soft: "#4A423B",      // Pal.Ink
};

function fit(canvas: HTMLCanvasElement) {
  const dpr = Math.min(devicePixelRatio || 1, 2);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr)) canvas.width = Math.round(w * dpr);
  if (canvas.height !== Math.round(h * dpr)) canvas.height = Math.round(h * dpr);
  const ctx = canvas.getContext("2d")!;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  return { ctx, w, h };
}

const LOG_MIN = -2, LOG_MAX = 3; // 0.25x .. 8x

export function yOf(ratio: number, top: number, bottom: number): number {
  const l = Math.max(LOG_MIN, Math.min(LOG_MAX, Math.log2(Math.max(ratio, 1e-3))));
  return bottom - ((l - LOG_MIN) / (LOG_MAX - LOG_MIN)) * (bottom - top);
}

/** Smooth a series with a short centred moving average (in log space) so it reads as a signal, not noise. */
export function smooth(values: (number | null)[], k = 2): (number | null)[] {
  return values.map((v, i) => {
    if (v === null) return null;
    let s = 0, n = 0;
    for (let j = Math.max(0, i - k); j <= Math.min(values.length - 1, i + k); j++) {
      const x = values[j];
      if (x !== null && x > 0) { s += Math.log(x); n++; }
    }
    return n ? Math.exp(s / n) : v;
  });
}

/** A ratio sparkline over the last ``seconds``, with the calm line (1x) dotted. */
export function sparkline(canvas: HTMLCanvasElement, windows: LiveWindow[], pick: (w: LiveWindow) => number | null,
                          color: string, seconds = 60) {
  const { ctx, w, h } = fit(canvas);
  const last = windows.length ? windows[windows.length - 1].t_end : 0;
  const shown = windows.filter((x) => x.t_end > last - seconds);
  const top = 3, bottom = h - 3;
  ctx.strokeStyle = COLORS.line;
  ctx.setLineDash([2, 3]);
  ctx.beginPath(); ctx.moveTo(0, yOf(1, top, bottom)); ctx.lineTo(w, yOf(1, top, bottom)); ctx.stroke();
  ctx.setLineDash([]);
  const vals = smooth(shown.map(pick), 2);
  ctx.strokeStyle = color; ctx.lineWidth = 1.75; ctx.lineJoin = "round";
  ctx.beginPath();
  let pen = false;
  shown.forEach((win, i) => {
    const v = vals[i];
    if (v == null) { pen = false; return; }
    const x = w - ((last - win.t_end) / seconds) * w, y = yOf(v, top, bottom);
    if (pen) ctx.lineTo(x, y); else ctx.moveTo(x, y);
    pen = true;
  });
  ctx.stroke();
}

const STIMULI = new Set(["monster_appeared", "monster_stage", "monster_lunge", "jumpscare", "answer_wrong_fx", "death"]);

/**
 * The whole session on one strip: a mark per window coloured by its strongest state (full colour when likely,
 * pale when possible, empty during calibration), question spans under it, and game stimuli as ticks.
 */
export function ribbon(canvas: HTMLCanvasElement, view: SessionView, totalSeconds: number) {
  const { ctx, w, h } = fit(canvas);
  const end = Math.max(totalSeconds, view.windows.at(-1)?.t_end ?? 0, 30);
  const x = (t: number) => (t / end) * w;
  const bandTop = 0, bandH = Math.round(h * 0.5);
  for (const win of view.windows) {
    const s = [...(win.states ?? [])].sort(rank)[0];
    if (!s) continue;
    ctx.fillStyle = COLORS[FAMILY[s.key]];
    ctx.globalAlpha = s.key === "calm" ? 0.25 : s.likelihood === "likely" ? 1 : 0.4;
    ctx.fillRect(x(win.t_start + (win.t_end - win.t_start) / 2), bandTop, Math.max(1.5, x(0.5)), bandH);
  }
  ctx.globalAlpha = 1;
  // question spans
  const qy = bandH + 8, qh = 6;
  const now = view.windows.at(-1)?.t_end ?? 0;
  for (const b of questionBands(view)) {
    ctx.fillStyle = COLORS.soft;
    ctx.fillRect(x(b.t0), qy, Math.max(2, x((b.t1 ?? now) - b.t0)), qh);
  }
  // stimuli
  ctx.fillStyle = COLORS.alarm;
  for (const e of view.events) {
    if (!STIMULI.has(e.type)) continue;
    const ex = x(e.t);
    ctx.beginPath(); ctx.moveTo(ex - 4, h); ctx.lineTo(ex + 4, h); ctx.lineTo(ex, h - 7); ctx.closePath(); ctx.fill();
  }
  // now
  if (view.session.status === "live" && now > 0) {
    ctx.fillStyle = INK;
    ctx.fillRect(x(now) - 1, 0, 2, bandH);
  }
}

// Canvas charts for the live view. Ratios are drawn on a log2 axis centred on 1x (the person's own
// calibration level), so "twice as much" and "half as much" sit the same distance from the line.
import { EVENT_LABELS, MARKED_EVENTS, STIMULUS_EVENTS } from "./labels";
import { PAL } from "./palette";
import { questionBands, type SessionView } from "./store";
import type { LiveWindow } from "./types";

const FONT = "DotGothic16, ui-monospace, monospace";

function fit(canvas: HTMLCanvasElement) {
  const dpr = Math.min(devicePixelRatio || 1, 2);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr)) canvas.width = Math.round(w * dpr);
  if (canvas.height !== Math.round(h * dpr)) canvas.height = Math.round(h * dpr);
  const ctx = canvas.getContext("2d")!;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { ctx, w, h };
}

export interface Series { key: string; label: string; color: string; pick: (w: LiveWindow) => number | null }

export const SERIES: Series[] = [
  { key: "tremorR", label: "4–12 Hz R", color: PAL.blood, pick: (w) => w.rel?.right_tremor_rms ?? null },
  { key: "tremorL", label: "4–12 Hz L", color: PAL.rustHot, pick: (w) => w.rel?.left_tremor_rms ?? null },
  { key: "head", label: "Head rotation", color: PAL.bone, pick: (w) => w.rel?.head_ang_speed_mean ?? null },
];

const LOG_MIN = -2, LOG_MAX = 3; // 0.25x .. 8x

export function yOf(ratio: number, top: number, bottom: number): number {
  const l = Math.max(LOG_MIN, Math.min(LOG_MAX, Math.log2(Math.max(ratio, 1e-3))));
  return bottom - ((l - LOG_MIN) / (LOG_MAX - LOG_MIN)) * (bottom - top);
}

/** Smooth a series with a short centred moving average so the trace reads as a signal, not noise. */
export function smooth(values: (number | null)[], k = 2): (number | null)[] {
  return values.map((v, i) => {
    if (v === null) return null;
    let s = 0, n = 0;
    for (let j = Math.max(0, i - k); j <= Math.min(values.length - 1, i + k); j++) {
      const x = values[j];
      if (x !== null) { s += Math.log2(Math.max(x, 1e-3)); n++; }
    }
    return n ? 2 ** (s / n) : null;
  });
}

/** Scrolling live trace of the last `span` seconds. */
export function drawTrace(canvas: HTMLCanvasElement, view: SessionView, span = 60, hidden = new Set<string>()) {
  const { ctx, w, h } = fit(canvas);
  ctx.clearRect(0, 0, w, h);
  const padL = 62, padR = 10, top = 14, bottom = h - 26;
  const wins = view.windows;
  const tNow = wins.length ? wins[wins.length - 1].t_end : 0;
  const t0 = Math.max(0, tNow - span), t1 = Math.max(span, tNow);
  const x = (t: number) => padL + ((t - t0) / (t1 - t0)) * (w - padL - padR);

  // question bands
  questionBands(view).forEach((b, i) => {
    const a = x(Math.max(b.t0, t0)), z = x(Math.min(b.t1 ?? tNow, t1));
    if (z <= padL || a >= w - padR) return;
    ctx.fillStyle = i % 2 ? "rgba(42,30,25,0.55)" : "rgba(74,34,20,0.35)";
    ctx.fillRect(Math.max(a, padL), top, Math.min(z, w - padR) - Math.max(a, padL), bottom - top);
    ctx.fillStyle = PAL.boneDim;
    ctx.font = `13px ${FONT}`;
    ctx.textAlign = "left";
    ctx.fillText(b.key.toUpperCase(), Math.max(a, padL) + 4, top + 11);
  });

  // calibration: the calm stretch every ratio is measured against
  const cs = view.events.find((e) => e.type === "calibration_start"), ce = view.events.find((e) => e.type === "calibration_end");
  if (cs) {
    const a = Math.max(x(cs.t), padL), z = Math.min(x(ce ? ce.t : tNow), w - padR);
    if (z > a) {
      ctx.fillStyle = "rgba(201,214,230,0.05)";
      ctx.fillRect(a, top, z - a, bottom - top);
      ctx.fillStyle = PAL.dawn;
      ctx.font = `13px ${FONT}`;
      ctx.textAlign = "left";
      if (z - a > 90) ctx.fillText("CALIBRATION", a + 6, top + 11);
    }
  }

  // freeze shading
  ctx.fillStyle = "rgba(201,214,230,0.10)";
  for (const win of wins) if (win.freeze && win.t_end > t0) ctx.fillRect(x(win.t_start), top, x(win.t_end) - x(win.t_start), bottom - top);

  // axis
  ctx.font = `13px ${FONT}`;
  ctx.textAlign = "right";
  for (const r of [0.25, 0.5, 1, 2, 4, 8]) {
    const y = yOf(r, top, bottom);
    ctx.strokeStyle = r === 1 ? "rgba(232,220,192,0.55)" : "rgba(143,63,31,0.35)";
    ctx.setLineDash(r === 1 ? [] : [2, 4]);
    ctx.beginPath(); ctx.moveTo(padL, y); ctx.lineTo(w - padR, y); ctx.stroke();
    ctx.fillStyle = r === 1 ? PAL.bone : PAL.boneDim;
    ctx.fillText(r === 1 ? "×1 own" : `×${r}`, padL - 6, y + 3);
  }
  ctx.setLineDash([]);

  // event markers
  for (const e of view.events) {
    if (!MARKED_EVENTS.has(e.type) || e.t < t0) continue;
    const ex = x(e.t);
    const stim = STIMULUS_EVENTS.has(e.type);
    ctx.strokeStyle = stim ? "rgba(208,17,43,0.9)" : "rgba(255,192,102,0.5)";
    ctx.beginPath(); ctx.moveTo(ex, top); ctx.lineTo(ex, bottom); ctx.stroke();
    if (stim || e.type === "question_shown") {
      ctx.save();
      ctx.translate(ex + 3, bottom - 4);
      ctx.rotate(-Math.PI / 2);
      const label = EVENT_LABELS[e.type] ?? e.type;
      ctx.textAlign = "left";
      ctx.fillStyle = "rgba(7,5,6,0.85)";
      ctx.fillRect(-2, -12, ctx.measureText(label).width + 4, 15);
      ctx.fillStyle = stim ? "#ff5a6e" : PAL.amber;
      ctx.fillText(label, 0, 0);
      ctx.restore();
    }
  }

  // series
  const visible = wins.filter((win) => win.t_end >= t0);
  for (const s of SERIES) {
    if (hidden.has(s.key)) continue;
    const vals = smooth(visible.map(s.pick));
    ctx.lineWidth = 2;
    ctx.strokeStyle = s.color;
    ctx.shadowColor = s.color;
    ctx.shadowBlur = 6;
    ctx.beginPath();
    let pen = false;
    visible.forEach((win, i) => {
      const v = vals[i];
      if (v === null) { pen = false; return; }
      const px = x((win.t_start + win.t_end) / 2), py = yOf(v, top, bottom);
      if (pen) ctx.lineTo(px, py); else ctx.moveTo(px, py);
      pen = true;
    });
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // time axis
  ctx.fillStyle = PAL.boneDim;
  ctx.textAlign = "center";
  const step = span <= 60 ? 10 : 30;
  for (let t = Math.ceil(t0 / step) * step; t <= t1; t += step) ctx.fillText(`${Math.round(t)}s`, x(t), h - 8);

  if (!wins.length) {
    ctx.fillStyle = PAL.boneDim;
    ctx.textAlign = "center";
    ctx.font = `13px ${FONT}`;
    ctx.fillText("WAITING FOR MOTION WINDOWS…", w / 2, h / 2);
  }
}

/** The whole session on one strip: questions, stimuli, low-motion stretches, and where "now" is. */
export function drawTimeline(canvas: HTMLCanvasElement, view: SessionView, total: number) {
  const { ctx, w, h } = fit(canvas);
  ctx.clearRect(0, 0, w, h);
  const pad = 8, mid = h / 2;
  const tNow = view.windows.length ? view.windows[view.windows.length - 1].t_end : 0;
  const T = Math.max(total, tNow, 1);
  const x = (t: number) => pad + (t / T) * (w - 2 * pad);
  ctx.fillStyle = PAL.soot;
  ctx.fillRect(pad, mid - 9, w - 2 * pad, 18);
  questionBands(view).forEach((b, i) => {
    ctx.fillStyle = i % 2 ? PAL.rust : PAL.rustDeep;
    ctx.fillRect(x(b.t0), mid - 9, x(b.t1 ?? tNow) - x(b.t0), 18);
    ctx.fillStyle = PAL.bone;
    ctx.font = `13px ${FONT}`;
    ctx.textAlign = "left";
    if (x(b.t1 ?? tNow) - x(b.t0) > 22) ctx.fillText(b.key.toUpperCase(), x(b.t0) + 3, mid + 3);
  });
  ctx.fillStyle = "rgba(201,214,230,0.45)";
  for (const win of view.windows) if (win.freeze) ctx.fillRect(x(win.t_start), mid + 10, Math.max(1, x(win.t_end) - x(win.t_start)), 3);
  for (const e of view.events) {
    if (!STIMULUS_EVENTS.has(e.type)) continue;
    ctx.fillStyle = PAL.blood;
    const ex = x(e.t);
    ctx.beginPath(); ctx.moveTo(ex, mid - 12); ctx.lineTo(ex - 4, mid - 19); ctx.lineTo(ex + 4, mid - 19); ctx.fill();
  }
  ctx.fillStyle = PAL.amber;
  ctx.fillRect(x(tNow) - 1, mid - 14, 2, 28);
}

/** A live dial: the needle follows a ratio on the same log2 scale, the arc shows ×0.25 .. ×8. */
export function drawDial(canvas: HTMLCanvasElement, ratio: number | null, color: string) {
  const { ctx, w, h } = fit(canvas);
  ctx.clearRect(0, 0, w, h);
  const cx = w / 2, cy = h * 0.82, r = Math.min(w * 0.42, h * 0.72);
  const a0 = Math.PI * 1.1, a1 = Math.PI * 1.9;
  const ang = (v: number) => a0 + ((Math.max(LOG_MIN, Math.min(LOG_MAX, Math.log2(v))) - LOG_MIN) / (LOG_MAX - LOG_MIN)) * (a1 - a0);
  ctx.lineWidth = 6;
  ctx.strokeStyle = PAL.ash;
  ctx.beginPath(); ctx.arc(cx, cy, r, a0, a1); ctx.stroke();
  // ticks
  for (const t of [0.25, 0.5, 1, 2, 4, 8]) {
    const a = ang(t);
    ctx.strokeStyle = t === 1 ? PAL.bone : PAL.rust;
    ctx.lineWidth = t === 1 ? 2 : 1;
    ctx.beginPath(); ctx.moveTo(cx + Math.cos(a) * (r - 10), cy + Math.sin(a) * (r - 10)); ctx.lineTo(cx + Math.cos(a) * (r + 4), cy + Math.sin(a) * (r + 4)); ctx.stroke();
  }
  if (ratio === null) return;
  ctx.lineWidth = 6;
  ctx.strokeStyle = color;
  ctx.shadowColor = color;
  ctx.shadowBlur = 8;
  ctx.beginPath(); ctx.arc(cx, cy, r, ang(1), ang(ratio), ratio < 1); ctx.stroke();
  ctx.shadowBlur = 0;
  const a = ang(ratio);
  ctx.strokeStyle = PAL.ivory;
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(cx + Math.cos(a) * (r - 6), cy + Math.sin(a) * (r - 6)); ctx.stroke();
}

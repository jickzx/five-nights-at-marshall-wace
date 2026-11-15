// The game's look, drawn in the browser: a starfield, the rising blood tide from the title screen,
// procedural glyphs in the spirit of the console's "unknown tongue" (Unity UI/GlyphGen.cs), and a
// pipeline diagram with data flowing through it. Canvas 2D only; everything respects reduced motion.
import { PAL } from "./palette";

export const reducedMotion = () => matchMedia("(prefers-reduced-motion: reduce)").matches;

function fit(canvas: HTMLCanvasElement): { ctx: CanvasRenderingContext2D; w: number; h: number; dpr: number } {
  const dpr = Math.min(devicePixelRatio || 1, 2);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
  }
  const ctx = canvas.getContext("2d")!;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { ctx, w, h, dpr };
}

function rng(seed: number) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}

/** Fixed full-page backdrop: drifting stars with a faint bruise glow at the horizon, like the title sky. */
export function starfield(canvas: HTMLCanvasElement): () => void {
  const r = rng(7);
  const stars = Array.from({ length: 160 }, () => ({ x: r(), y: r(), z: 0.2 + r() * 0.8, tw: r() * 6.28 }));
  let raf = 0;
  const draw = (time: number) => {
    const { ctx, w, h } = fit(canvas);
    ctx.fillStyle = PAL.abyss;
    ctx.fillRect(0, 0, w, h);
    const g = ctx.createRadialGradient(w * 0.5, h * 1.1, 0, w * 0.5, h * 1.1, h * 0.9);
    g.addColorStop(0, "rgba(94,6,18,0.35)");
    g.addColorStop(1, "rgba(46,14,26,0)");
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, w, h);
    for (const s of stars) {
      const tw = 0.55 + 0.45 * Math.sin(time / 900 * s.z + s.tw);
      const y = (s.y + (reducedMotion() ? 0 : time / 1e6 * s.z)) % 1;
      ctx.fillStyle = `rgba(232,220,192,${(0.15 + 0.5 * s.z) * tw})`;
      const size = s.z > 0.85 ? 2 : 1;
      ctx.fillRect(Math.round(s.x * w), Math.round(y * h), size, size);
    }
    if (!reducedMotion()) raf = requestAnimationFrame(draw);
  };
  raf = requestAnimationFrame(draw);
  const onResize = () => { if (reducedMotion()) requestAnimationFrame(draw); };
  addEventListener("resize", onResize);
  return () => { cancelAnimationFrame(raf); removeEventListener("resize", onResize); };
}

/** The blood tide: layered sine swells with a hot rim, as on the game's title screen. */
export function tide(canvas: HTMLCanvasElement, level = () => 0.55): () => void {
  let raf = 0;
  const draw = (time: number) => {
    const { ctx, w, h } = fit(canvas);
    ctx.clearRect(0, 0, w, h);
    const t = reducedMotion() ? 0 : time / 1000;
    const base = h * (1 - level());
    for (let layer = 0; layer < 3; layer++) {
      ctx.beginPath();
      ctx.moveTo(0, h);
      for (let x = 0; x <= w; x += 4) {
        const y = base + layer * 7
          + Math.sin(x / (180 + layer * 40) + t * (0.6 + layer * 0.2)) * (5 - layer)
          + Math.sin(x / 61 + t * 1.3 + layer) * 1.5;
        ctx.lineTo(x, y);
      }
      ctx.lineTo(w, h);
      ctx.closePath();
      const g = ctx.createLinearGradient(0, base, 0, h);
      g.addColorStop(0, layer === 0 ? "rgba(208,17,43,0.95)" : "rgba(150,10,30,0.6)");
      g.addColorStop(1, "rgba(40,2,8,1)");
      ctx.fillStyle = g;
      ctx.fill();
      if (layer === 0) {
        ctx.strokeStyle = "rgba(255,120,110,0.55)";
        ctx.lineWidth = 1;
        ctx.stroke();
      }
    }
    if (!reducedMotion()) raf = requestAnimationFrame(draw);
  };
  raf = requestAnimationFrame(draw);
  return () => cancelAnimationFrame(raf);
}

/** A procedural glyph: a few strokes on a 5x5 lattice, deterministic per seed. Returns SVG markup. */
export function glyphSvg(seed: number, size = 28, color: string = PAL.rustHot): string {
  const r = rng(seed + 101);
  const pts = () => [Math.floor(r() * 5), Math.floor(r() * 5)];
  const strokes: string[] = [];
  const n = 2 + Math.floor(r() * 3);
  for (let i = 0; i < n; i++) {
    const [x0, y0] = pts();
    let [x1, y1] = pts();
    if (x0 === x1 && y0 === y1) x1 = (x1 + 2) % 5;
    if (r() < 0.5) y1 = y0; // favour horizontal/vertical strokes, like a script
    strokes.push(`M${x0 * 6 + 3} ${y0 * 6 + 3}L${x1 * 6 + 3} ${y1 * 6 + 3}`);
  }
  if (r() < 0.5) { const [x, y] = pts(); strokes.push(`M${x * 6 + 1} ${y * 6 + 3}h4`); }
  return `<svg viewBox="0 0 30 30" width="${size}" height="${size}" aria-hidden="true" shape-rendering="crispEdges">
    <path d="${strokes.join("")}" stroke="${color}" stroke-width="3" stroke-linecap="square" fill="none"/></svg>`;
}

/** The pipeline diagram: stages joined by a line with packets flowing along it. */
export function pipeline(canvas: HTMLCanvasElement, stages: string[], rate = () => 1): () => void {
  const packets: { p: number; v: number }[] = [];
  let raf = 0, last = 0, spawn = 0;
  const draw = (time: number) => {
    const dt = Math.min(0.05, (time - last) / 1000 || 0);
    last = time;
    const { ctx, w, h } = fit(canvas);
    ctx.clearRect(0, 0, w, h);
    const vertical = w < 560;
    const n = stages.length;
    const pos = (i: number) => vertical
      ? { x: w * 0.18, y: 24 + (i * (h - 48)) / (n - 1) }
      : { x: 40 + (i * (w - 80)) / (n - 1), y: h * 0.42 };
    const at = (p: number) => {
      const f = p * (n - 1), i = Math.min(n - 2, Math.floor(f)), k = f - i;
      const a = pos(i), b = pos(i + 1);
      return { x: a.x + (b.x - a.x) * k, y: a.y + (b.y - a.y) * k };
    };
    // rail
    ctx.strokeStyle = PAL.rust;
    ctx.lineWidth = 2;
    ctx.beginPath();
    for (let i = 0; i < n; i++) { const p = pos(i); i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y); }
    ctx.stroke();
    // packets
    if (!reducedMotion()) {
      spawn += dt * 6 * rate();
      while (spawn > 1) { packets.push({ p: 0, v: 0.12 + Math.random() * 0.05 }); spawn -= 1; }
    }
    for (let i = packets.length - 1; i >= 0; i--) {
      const k = packets[i];
      k.p += k.v * dt;
      if (k.p >= 1) { packets.splice(i, 1); continue; }
      const q = at(k.p);
      ctx.fillStyle = k.p > 0.62 ? PAL.amber : PAL.blood;
      ctx.fillRect(Math.round(q.x) - 2, Math.round(q.y) - 2, 4, 4);
      ctx.fillStyle = "rgba(255,192,102,0.15)";
      ctx.fillRect(Math.round(q.x) - 5, Math.round(q.y) - 5, 10, 10);
    }
    // nodes
    ctx.font = `14px DotGothic16, ui-monospace, monospace`;
    ctx.textBaseline = "middle";
    for (let i = 0; i < n; i++) {
      const p = pos(i);
      ctx.fillStyle = PAL.void;
      ctx.fillRect(p.x - 9, p.y - 9, 18, 18);
      ctx.strokeStyle = PAL.rustHot;
      ctx.strokeRect(p.x - 8.5, p.y - 8.5, 17, 17);
      ctx.fillStyle = PAL.bone;
      ctx.fillRect(p.x - 3, p.y - 3, 6, 6);
      ctx.fillStyle = PAL.boneDim;
      if (vertical) { ctx.textAlign = "left"; ctx.fillText(stages[i], p.x + 22, p.y); }
      else { ctx.textAlign = "center"; ctx.fillText(stages[i], p.x, p.y + 28); }
    }
    raf = requestAnimationFrame(draw);
  };
  raf = requestAnimationFrame(draw);
  return () => cancelAnimationFrame(raf);
}

// PERSON D owns src/ui. Judges' debug overlay (toggle with G).
import type { World } from "../lang";
import type { GameState } from "../game/types";

const el = document.getElementById("overlay")!;
let visible = true;
addEventListener("keydown", (e) => { if (e.key.toLowerCase() === "g") visible = !visible; });

export function renderOverlay(world: World, s: GameState) {
  el.style.display = visible ? "block" : "none";
  if (!visible) return;
  el.textContent = [
    `seed ${world.seed}   phase ${s.phase}   creature ${s.creatureDistance.toFixed(2)} (stage ${s.creatureStage})`,
    ...world.grammarSummary,
  ].join("\n");
}

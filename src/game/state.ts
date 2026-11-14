// PERSON C owns this file.
import type { GameState } from "./types";

export const CREEP_PER_SEC = 0.01; // slow, so thinking isn't punished
export const WRONG_LUNGE = 0.15;
export const RIGHT_RETREAT = 0.1;

export function initialState(): GameState {
  return { phase: "intro", creatureDistance: 1, creatureStage: 1, selected: [] };
}

export function tick(s: GameState, dt: number, demoMode: boolean): void {
  if (!s.phase.startsWith("door")) return;
  s.creatureDistance = Math.max(0, s.creatureDistance - CREEP_PER_SEC * dt * (demoMode ? 0.3 : 1));
  if (s.creatureDistance <= 0) s.phase = "dead";
}

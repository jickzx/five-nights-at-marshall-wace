import { describe, expect, it } from "vitest";
import { engine } from "./index";

describe("language engine", () => {
  it("every puzzle's solution passes check, across 100 seeds", () => {
    for (let seed = 1; seed <= 100; seed++) {
      const world = engine.generate(seed);
      for (const p of world.puzzles) {
        const byId = new Map(p.tiles.map((t) => [t.id, t]));
        const answer = p.solution.map((id) => byId.get(id)!);
        expect(answer.every(Boolean), `seed ${seed} door ${p.id}: solution uses a word not in tiles`).toBe(true);
        expect(engine.check(world, p.id, answer)).toBe(true);
        expect(p.tiles.length).toBeLessThanOrEqual(4);
      }
    }
  });

  it("same seed gives same world", () => {
    expect(JSON.stringify(engine.generate(42))).toBe(JSON.stringify(engine.generate(42)));
  });
});

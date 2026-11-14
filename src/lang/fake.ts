// Hard-coded fake engine so B, C, D can build now.
// Person A replaces this with the real generator in engine.ts.
import type { LanguageEngine, Word, World } from "./types";

const w = (id: string, meaning: string, form: string, glyphSeed: number, melody: string[], marker?: Word["marker"]): Word =>
  ({ id, meaning, form, glyphSeed, melody, marker });

const eye = w("eye", "eye", "mor", 11, ["C4", "Eb4"]);
const door = w("door", "door", "tesu", 22, ["G3", "Bb3", "C4"]);
const open = w("open", "open", "ila", 33, ["F4", "Eb4"]);
const creature = w("creature", "creature", "gath", 44, ["C3", "Gb3"]);
const rain = w("rain", "rain", "shun", 55, ["Bb4", "Ab4"]);
const stop = w("stop", "stop", "vek", 66, ["D4", "C4"]);
const see = w("see", "see", "oru", 77, ["Eb4", "F4"]);
const eyes = w("eye+PL", "eyes", "morka", 11, ["C4", "Eb4", "G4"], "suffix");
const doors = w("door+PL", "doors", "tesuka", 22, ["G3", "Bb3", "C4", "G4"], "suffix");

const world: World = {
  seed: 0,
  grammarSummary: [
    "FAKE ENGINE (seed 0)",
    "Word order: thing before action (OV)",
    "Plural: suffix -ka",
  ],
  puzzles: [
    {
      id: 1,
      examples: [
        { picture: "eye", phrase: [eye] },
        { picture: "door", phrase: [door] },
        { picture: "open eye", phrase: [eye, open] },
      ],
      tiles: [open, see, door, eye],
      targetMeaning: "open door",
      solution: ["door", "open"],
    },
    {
      id: 2,
      examples: [
        { picture: "eye", phrase: [eye] },
        { picture: "eyes", phrase: [eyes] },
        { picture: "open eye", phrase: [eye, open] },
      ],
      tiles: [door, open, doors, eyes],
      targetMeaning: "open doors",
      solution: ["door+PL", "open"],
    },
    {
      id: 3,
      examples: [
        { picture: "creature", phrase: [creature] },
        { picture: "stop rain", phrase: [rain, stop] },
      ],
      tiles: [stop, rain, creature, open],
      targetMeaning: "stop creature",
      solution: ["creature", "stop"],
    },
  ],
};

export const fakeEngine: LanguageEngine = {
  generate: (seed) => ({ ...world, seed }),
  check: (wld, puzzleId, answer) => {
    const p = wld.puzzles.find((x) => x.id === puzzleId);
    return !!p && answer.map((a) => a.id).join(" ") === p.solution.join(" ");
  },
};

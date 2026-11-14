// The ONE place the game imports the engine from.
// Person A flips this line from fakeEngine to realEngine when tests pass.
import { fakeEngine } from "./fake";
export const engine = fakeEngine;
export type * from "./types";

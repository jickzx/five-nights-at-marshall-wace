// Contract between game logic (Person C) and world rendering (Person B).
export type Phase = "intro" | "door1" | "door2" | "door3" | "win" | "dead";

export type GameState = {
  phase: Phase;
  creatureDistance: number; // 1 = far, 0 = caught
  creatureStage: 1 | 2 | 3;
  selected: string[];       // word ids the player has picked so far
};

// ============================================================
// THE CONTRACT. Everyone builds against this. Change it only
// after telling the whole team in the group chat.
// ============================================================

export type Word = {
  id: string;            // stable id, e.g. "door" or "door+PL"
  meaning: string;       // English gloss, e.g. "doors" (never shown to player)
  form: string;          // romanised form, e.g. "tesuka" (debug overlay only)
  glyphSeed: number;     // Person B draws the glyph from this
  marker?: "prefix" | "suffix" | "double"; // set if this is an inflected form
  melody: string[];      // Tone.js notes, e.g. ["C4", "Eb4", "G4"]
};

export type Phrase = Word[];

export type Mural = {
  picture: string;       // meaning key for the picture, e.g. "open eye"
  phrase: Phrase;        // the alien label shown under it
};

export type Puzzle = {
  id: 1 | 2 | 3;
  examples: Mural[];     // 2-3 murals per door, no more
  tiles: Word[];         // max 4, includes distractors, shuffled
  targetMeaning: string; // what the player must say, e.g. "open door"
  solution: string[];    // word ids in order. Game code must NOT read this; use check()
};

export type World = {
  seed: number;
  grammarSummary: string[]; // plain-English lines for the judges' overlay
  puzzles: [Puzzle, Puzzle, Puzzle];
};

export type LanguageEngine = {
  generate(seed: number): World;
  check(world: World, puzzleId: number, answer: Phrase): boolean;
};

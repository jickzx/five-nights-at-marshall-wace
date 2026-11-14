# TASKS — who does what

**Game:** you crash on a blood-rain planet. Three sealed doors, something hunting you. Open each door by composing a phrase in a procedurally generated alien language you decode from murals. Wrong answer → creature lunges. Target playthrough: **75 seconds**.

**Targets:** Marshall Wace (New Worlds) + overall (Composition). Nothing else.

**Run it:** `npm install` → `npm run dev` (open the printed URL; add `?demo` for demo mode) · `npm test` · `npm run build`

## Rules
- Each person owns one folder. Don't edit someone else's folder without asking.
- `src/lang/types.ts` is the contract. Change it only after telling everyone.
- Commit small, pull often, and push to `main` only when `npm run build` passes.
- Build everything VR-safe: **no HTML UI in the game** (tiles and murals are 3D meshes in `pickables`), and the player moves door to door automatically.
- **Feature freeze 3 hours before the deadline.**

---

## Person A: language engine (`src/lang/`)
No graphics. Pure TypeScript and tests.

1. Write a seeded PRNG (mulberry32) in `engine.ts`.
2. From the seed, pick the grammar: OV or VO order, and the plural as a prefix, suffix or doubling.
3. Generate ~12 words (door, eye, creature, rain, light, open, stop, see, red, big, many, not) with pronounceable forms from a seeded syllable set.
4. Give each word a `glyphSeed` and a 2–4 note `melody` from a seeded minor scale. The plural adds one note.
5. Build three puzzles in the same shape as `fake.ts` (door 1 vocab and order, door 2 plural, door 3 "stop creature"). Use 4 tiles max, including distractors.
6. Fill in `grammarSummary` in plain English.
7. When `npm test` passes, flip `src/lang/index.ts` to the real engine.
8. **Stretch (do it, it's the technical-depth story):** a brute-force solver that enumerates every interpretation consistent with the murals and rejects puzzles that have more than one valid answer. Expose a count of remaining interpretations for the overlay.

## Person B: world and visuals (`src/world/`)
1. Build the corridor: walls, 3 doors spaced along −Z, red fog (a starter is in `scene.ts`).
2. Add blood rain: one `THREE.Points` with ≤ 3,000 particles falling and resetting.
3. Write a glyph renderer: `glyphSeed` → strokes on a 3×3 grid drawn to a `CanvasTexture`. Show the marker as an extra stroke.
4. Make murals: a picture plane plus a row of glyph planes for each `Mural` in the current puzzle.
5. Make tiles: a glyph plane per tile, pushed into `pickables`. Highlight a tile when it's selected.
6. Animate the camera to the next door when a door opens.
7. Make the creature a dark silhouette with glowing eyes. Read `state.creatureDistance` for position and `state.creatureStage` for scale and shape.

## Person C: game logic, audio and wiring (`src/game/`, `src/main.ts`)
1. Write the state machine: intro → door1 → door2 → door3 → win/dead.
2. Handle selection: picking a tile appends to `state.selected`. When the length matches, call `engine.check`. Right → retreat 0.1 and open the door; wrong → lunge 0.15 and clear.
3. Set the creature stage from the door number.
4. Audio with Tone.js: play the word's melody when a mural or tile is picked, plus an ambient drone and a heartbeat whose rate rises as distance falls. Note that the audio must start on the first click (browser rule).
5. Add hotkeys: `N` new world, `R` reset, `1`/`2`/`3` jump to a door, `Space` skip the intro.
6. Demo mode (`?demo`): fixed seed 1337 and a slow creature, so it can't kill you on stage.

## Person D: VR gate, content, ship, pitch (`src/ui/`)
1. **Now:** connect the GitHub repo to Vercel (framework: Vite). Confirm the public HTTPS link works.
2. **Now, 60 minutes max — the VR gate.** Open the Vercel link in the Quest browser → Enter VR → point at the red cube and pull the trigger (it should spin). Then ask B to add rain and check that it's smooth. **All pass = keep VR as a bonus mode. Any fail = drop VR forever.** Post the result in the chat.
3. Make 12 mural pictures, one per meaning (Gemini or simple icons) → `public/murals/<meaning>.png`.
4. Build the intro (≤ 8s, skippable) and the ending (≤ 5s) as stills with text.
5. Polish the judges' overlay (toggle `G`): seed, grammar summary and the interpretations counter.
6. Playtest on strangers. If door 1 takes more than 60s, tell A to make it easier.
7. Write the Devpost entry, record a backup demo video, and script and rehearse the 3-minute pitch (timings are below).

---

## Milestones (tick as you go)
- [ ] M1: one door playable end to end with the fake engine
- [ ] M2: real engine swapped in, still one door
- [ ] M3: all three doors, creature stages, audio
- [ ] M4: new-world button and judges' overlay
- [ ] FREEZE: from here, only fixes, the backup video and rehearsals

**Cut order if behind:** ending cutscene → door 3 → creature stages 2–3 → melodies. **Never cut the seeded generator.**

## Pitch (3:00)
| Time | Beat |
|---|---|
| 0:00–0:20 | Hook over the intro: "Stranded on a planet where the only way out is to learn the language of the thing hunting you." |
| 0:20–1:00 | Door 1 live, reasoning out loud |
| 1:00–1:25 | Door 2: get it wrong on purpose (lunge), then solve |
| 1:25–1:45 | Door 3: "stop creature", ending |
| 1:45–2:15 | Press N: new language, overlay, "every puzzle proven to have exactly one solution" |
| 2:15–2:45 | How you built it, New Worlds + Composition (meaning composed from sung parts) |
| 2:45–3:00 | Buffer |

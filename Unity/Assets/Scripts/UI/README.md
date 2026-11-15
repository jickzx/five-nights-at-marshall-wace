# UI (`Fnmw.UI`)

`UIModule.Install()` registers `GameUI`, the real `IGameUI`. Nothing lives in the scene: the whole
interface is built from code.

## How it is drawn

1. A uGUI canvas is rendered by its own camera into a **640x360 texture** (wider or taller if the
   window is not 16:9). It sits 4 km under the level on the UI layer, so no other camera sees it.
2. An overlay quad shows that texture through `Resources/UI/CRT.shader`. Curvature, scanlines,
   phosphor glow, static, tearing, the blood wash and tide, the cracks, the heartbeat vignette and
   the white of the death screen all live in that one pass, driven by `CrtFx`.

`CrtFx` reads `GameState.Threat` every frame and listens for `GameEvents.BeastStageChanged`.
Screens pick a `GlassMode` (Title, Visor, Console, Decode, Death, Escape) and fire impulses
(`Kick`, `Bleed`, `Crack`).

## Files

| File | What it holds |
|---|---|
| `GameUI.cs` | `IGameUI`, screen switching, input lock between screens, `Tick(dt, input)` |
| `UiRig.cs` | camera, render texture, presenter, keyboard and mouse reading (`UiInput`) |
| `CrtFx.cs` | every shader parameter; threat, heartbeat, impulses, cracks |
| `GlyphGen.cs` | procedural glyphs from `Tile.GlyphSeed`, seven stroke styles by language family |
| `Pictograms.cs` | 13x13 pictures for meanings ("open eye"); unknown words fall back to text |
| `PuzzleScreen.cs` | the console |
| `DecodeScreen.cs` | reveal, quiz and payoff (one screen, so the decoded phrase stays in view) |
| `OtherScreens.cs` | title, HUD, death, escape |
| `Lore.cs` | reveal text helpers, the "now you know" facts, the run journal |
| `Rtl.cs` | Arabic joining and right-to-left ordering for the legacy text renderer |
| `Kit.cs` | palette, fonts, small uGUI factory |

## For the other threads

- **Lang.** The reveal reads `RevealInfo.RealText`, `Translation`, `AlienName`, `LanguageName`,
  `Feature` (falls back to `Explanation`) and `NowYouKnow` (falls back to a built-in fact for the
  language). The quiz reads the first line of `Prompt` (the phrase is already on screen), and shows
  `QuizQuestion.Explanation` after the answer. `RealText` may carry a reading in brackets in either
  order, `"扉を開けて (tobira o akete)"` or `"sakana (سكن)"`: the native script becomes the big
  phrase, the reading its own line. `LanguageWorld.Real.Code` (the console in play) picks the
  script's stroke style (`GlyphGen.FamilyFor`).
- **Quiz order.** The contract shows the reveal before the quiz, so the reveal blocks out the
  language's name (`DecodeScreen.RedactUntilQuiz`) and the quiz answer pays it off with the name,
  the full explanation and the fact. Set the flag to `false` to name the language straight away.
- **Quiz result.** The UI calls `ILanguageEngine.CheckQuiz` itself to show right or wrong, then
  calls `onChoose` once the player continues past the payoff. `CheckQuiz` may be called for
  several options of the same puzzle (to find the right one), so keep it free of side effects.
- **Game.** `ShowPrompt` text is shown as given, in capitals. `ShowHud(state)` every frame is what
  keeps threat effects alive, including while a puzzle is open.

## Controls

Number keys place a tile, arrows and Space move and place, Backspace takes back one tile,
Delete clears, Enter sends, Esc or Tab steps away from the console. Number keys or arrows and Enter answer the quiz. Enter, Space
or a click continues. The mouse works everywhere (tiles, SEND, quiz options).

## Fonts

`Resources/UI/Fonts/DotGothic16` (SIL OFL) draws Japanese, accented Latin and Cyrillic.
`Unifont` (SIL OFL) is the fallback for every other script in the Basic Multilingual Plane.
Each has a `.coverage.bytes` bitmap generated from its cmap, because Unity's `Font.HasCharacter`
also says yes for glyphs it borrows from the operating system. Right-to-left and complex-shaping
text goes through `Rtl.Visual`: Arabic letters are joined and right-to-left runs reversed. Scripts
that need complex shaping (Devanagari, Thai) get their glyphs but not shaping.

## Tests

`Assets/Tests/EditMode/UI` covers glyph determinism and distinctness, font coverage, the text
helpers, Arabic shaping, a scripted run through every screen with the fake engine, and a
five-console run with the real one (including a check that every string it emits can be drawn).

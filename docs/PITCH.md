# Pitch notes: how the puzzles, answers and beast actually work

Read this once before talking to judges. Everything here was checked against the code; file paths point at the evidence. Paths are relative to `Unity/Assets/Scripts/` unless they start with `Unity/` or `docs/`.

## 0. The correction to get right first

**The languages are not exaggerated or invented. They are the real languages, with real words and real grammar.** An earlier version used made-up "alien" words built on real grammar. Commit `d4affa3` ("Lang: real languages, no alien disguise") removed that. Now every tile, example and reveal shows the real word, romanised (Hepburn for Japanese, pinyin for Mandarin, a standard transliteration for Arabic). See the comment at the top of `Core/LanguageTypes.cs`: the fields are still *called* `Alien...`, but "they now hold real romanised text".

The only alien thing is how it looks. Every word gets a procedurally drawn glyph (`UI/GlyphGen.cs`) printed above its real spelling, and the console calls it an "unknown tongue". So the line to use is:

> "Each console speaks a real Earth language, dressed up in an alien script. You crack it from examples the way a field linguist would, then the game decodes the script back into the real language and teaches you what you just learned."

Do not say "exaggerated" or "made-up language". A judge who speaks German or Japanese will see real words, and the old pitch would sound like we don't know our own game. (Two code comments still say "exaggerates": `Audio/Synth/Motif.cs:2` and the title of commit `73a8803`, "seven exaggerated real languages". Both are out of date.)

## 1. The puzzle "answer sheet"

### What the player sees at a console (`UI/PuzzleScreen.cs`)

- **Header:** the language's own name ("NIHONGO", "DEUTSCH", "AL-ʿARABIYYA") and how much ship power is restored.
- **Examples (2 or 3 rows):** an English meaning on the left (with a pictogram if one exists), and the real phrase on the right, one glyph-plus-word per word. Example: *"Look at the door!" = tobira o mite*.
- **The target row:** a new English meaning with empty slots.
- **Tiles (2 to 6):** each tile is one word or one word-part (a suffix like `-lar`, a kanji reading like `den`), drawn as a glyph with the real spelling underneath and a number key in the corner.

Controls: number keys place tiles in order, Backspace undoes the last one, Enter sends. (Esc also clears, but it also walks you away from the console. Use Backspace.)

### What a correct answer is

The tiles in the right order spell the target in the real language. The rule you need is never stated. You work it out by comparing the examples. Example (German, demo seed):

| Shown | Real |
|---|---|
| fireworks (work done with fire) | Feuerwerk |
| lighter (stuff for fire) | Feuerzeug |
| **tool (stuff for work)** | ? |

Tiles: `Zeug`, `Werk`, `Feuer`. From the two examples, Feuer = fire, Werk = work and Zeug = stuff, and the thing named goes *last*. So "stuff for work" is **Werk + Zeug = Werkzeug**, which really is the German word for tool.

**Why it is guaranteed to be the only answer** (`Lang/Framework/PuzzleBuilder.cs`, the header comment explains it): every puzzle comes with "rival grammars", meaning the rules a player might wrongly assume (English word order, no gender agreement, suffixes in another order, head-first compounds). Before a puzzle is used, the engine:

1. keeps only the rivals that would also produce every example exactly as shown (those can't be ruled out from the examples);
2. brute-forces every ordering of every subset of the tiles (at most 1,956 sequences for 6 tiles) and checks that the only sequences a player could justify are the ones real speakers would accept;
3. checks that every word-part in the answer appears somewhere in the examples, so nothing has to be guessed.

If any check fails it throws the puzzle away and generates another (`Lang/LanguageEngine.cs`, `BuildAt`, up to 60 attempts). So every console has exactly one answer, or exactly the orders the real language allows. Japanese is the one case with two: the object can move to the front because the particles carry the roles, and the console accepts both.

### What a wrong answer does

- **On screen:** the glass shakes, cracks and bleeds, and a hint appears (`UI/PuzzleScreen.cs`, `Reject`). The hint names the *rule* you broke, never the answer (`Lang/Feedback.cs`), e.g. "Right pieces, wrong train. The suffixes always couple in the same order."
- **Diagnosis:** the engine works out which concept you missed (`LanguageEngine.Diagnose`): an exact rival grammar match, right pieces in the wrong order, or the concept carried by a wrong or missing tile.
- **Consequences:** the mistake counter goes up, the beast lunges and evolves faster (`Monster/Beast/BeastBrain.cs`, `OnAnswer`), and the learning model records the miss against that concept. After about a second your tiles clear by themselves and the same puzzle stays up for another try.

### Puzzles are generated from a seed

There is no fixed list of puzzles. `LanguageEngine.Generate(seed)` shuffles the seven languages, picks five for the run, and builds each puzzle from that language's word tables with a deterministic RNG (`Lang/Framework/Rng.cs`, SplitMix64). The same seed always gives the same world.

Three more facts decide what you actually get:

- **Only three consoles are played.** The engine builds 5 puzzles, but the shore has 3 consoles (`World/WorldLayout.cs`, `Consoles`), so puzzles 4 and 5 are never seen.
- **The puzzle depends on difficulty as well as the seed.** When you walk up to a console the game asks the beast's director for a difficulty and rebuilds that console's puzzle (`Game/GameDirector.cs`, `OpenPuzzle`). Higher difficulty means a harder puzzle type, more distractor tiles and fewer examples (`LanguageEngine.Effective`, `TierFor`, `DistractorsFor`).
- **There is no "default" seed.** Normal play uses a random seed every run (`GameDirector.cs:41`). Demo mode always uses **seed 4242** (`Game/DemoMode.cs`).

### Answer key: demo seed 4242 (Arabic, German, Japanese)

This is exactly the last playthrough. Demo mode caps difficulty at 0.4, so consoles 1 and 2 are always the versions below. Console 3 has two possible versions, so read the words on the tiles rather than trusting key numbers.

| Console | Target shown | Examples shown | Tiles (key: word) | Answer | Real phrase | What it teaches |
|---|---|---|---|---|---|---|
| 1 Arabic | Learner (one who studies) | He wrote = kataba; Writer = kātib; He studied = darasa | 1 dāris, 2 rākib | **1** (dāris) | dāris دارس | Meaning lives in the consonants d-r-s ("study"); the vowel pattern CāCiC means "one who does" (kātib, writer). |
| 2 German | tool (stuff for work) | fireworks = Feuerwerk; lighter = Feuerzeug | 1 Zeug, 2 Werk, 3 Feuer | **2, 1** (Werk Zeug) | Werkzeug | German glues nouns into one word, and the last part names the thing. |
| 3 Japanese, usual | telephone (electric talk) | train = densha; conversation = kaiwa | 1 sha, 2 wa, 3 kai, 4 den | **4, 2** (den wa) | 電話 denwa | Each kanji carries a meaning; words are built by joining them. |
| 3 Japanese, if the beast thinks you are struggling (director below 0.09) | Search for the door! | Look at the door! = tobira o mite; Search for the ship! = fune o sagashite | 1 sagashite, 2 mite, 3 tobira, 4 o | **3, 4, 1** (tobira o sagashite) | 扉を探して | The verb goes last, and を (o) marks the thing acted on. |

Quiz answers for seed 4242: Arabic is option **2**, German **3**, Japanese **2**.

### Answer key: every puzzle type in the engine

The seven languages have 22 puzzle types between them. The words are picked by the seed, so this table gives one real generated example of each type (from `docs/answer-key` with `--catalog`). The tier is how hard the type is, from 0 to 3.

| Language | Type (tier) | Example target → answer | Teaches |
|---|---|---|---|
| Japanese | verb-last (0) | Look at the door! → tobira o mite 扉を見て | Verb last; を (o) after the object |
| Japanese | kanji-compound (1) | florist → hana + ya 花屋 | Kanji carry meanings; describing part first |
| Japanese | particles (2) | The cat sees the ship. → neko ga fune o miru (also accepts fune o neko ga miru) | が (ga) marks the doer, を (o) the thing; roles survive reordering |
| Japanese | compound-sentence (3) | Open the exit! → de guchi o akete 出口を開けて | Build 出口 from 出 "go out" + 口 "mouth", then verb last |
| German | compound (0) | glove → Hand + Schuh = Handschuh | Head-last compounds ("hand shoe") |
| German | verb-final-clause (2) | …because the beast is dead → weil das Biest tot ist | Verb goes to the end after weil |
| German | long-compound (3) | glove compartment → Hand + Schuh + Fach | Compounds stack |
| French | adjective-after (0) | The open tunnel → le tunnel ouvert | Adjective after the noun |
| French | gender (1) | The black monster → le monstre noir | le/la and the adjective agree with the noun |
| French | plural (2) | The green walls → les murs verts | Written plural -s on noun and adjective |
| French | plural-mixed (3) | The green keys → les clés vertes | Gender and plural together |
| Spanish | verb-ending (0) | You read → le + -es = lees | The ending says who acts; no "you" needed |
| Spanish | punctuation (1) | Does she learn? → ¿ aprend -e ? = ¿Aprende? | ¿ opens a question |
| Spanish | gender (2) | The tall son → el hijo alto | -o/-a agreement, adjective after |
| Turkish | suffix-order (0) | At the doors → kapı -lar -da = kapılarda | Plural before case |
| Turkish | long-chain (1) | In my rooms → oda -lar -ım -da = odalarımda | Plural, then "my", then case |
| Turkish | vowel-harmony (2) | From the doors → kapı -lar -dan = kapılardan | Suffix vowels match the word (-lar not -ler) |
| Turkish | chain-harmony (3) | In my villages → köy -ler -im -de = köylerimde | Order and harmony together |
| Mandarin | measure-word (0) | Three wolves → sān zhī láng 三只狼 | A measure word sits between number and noun, chosen by kind (只 for animals) |
| Mandarin | measure-word-this (2) | This car → zhè liàng chē 这辆车 | "This" needs one too (辆 for vehicles) |
| Arabic | root-pattern (0) | Boat → markab مركب | Root r-k-b "ride" + pattern maCCaC "place/means of" |
| Arabic | root-pattern-lean (2) | He wrote → kataba كتب | Same idea with fewer examples |

**To print the key for any seed** (needs only the .NET SDK that ships inside Unity, so the Editor doesn't have to be open):

```bash
D=/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet
cd docs/answer-key
$D run -c Release -- 4242               # what a fresh run gets at each console
$D run -c Release -- 4242 --all-tiers   # every version each console can take, with the difficulty range for each
$D run -c Release -- --catalog          # one example of every puzzle type
```

It compiles the game's own `Lang/` source, so its output is exactly what the game generates. The seed isn't shown on screen in normal play. With the Editor open you can read it with `unity command script eval --code 'return Fnmw.Core.Services.State.Seed;'`.

## 2. The seven languages

The feature each one tests is real. Where the game simplifies, it says so in the last column.

| Language | Real feature the puzzles test | Example from the game | One sentence for a judge | Be careful about |
|---|---|---|---|---|
| **Arabic** (`Lang/Languages/Arabic.cs`) | Root-and-pattern morphology: a 3-consonant root carries the meaning, a vowel pattern carries the role | k-t-b: kataba "he wrote", kātib "writer", maktab "office"; d-r-s → dāris "learner" | "Arabic words are built like templates: the consonants k-t-b mean writing, and the vowels around them decide whether it's 'he wrote', 'writer' or 'office'." | Only 3 patterns and 7 roots, and only real words are used (looked up, never generated). The player chooses a whole word; root and pattern are not separate tiles. At console 1 it's often a pick-one-of-two. |
| **German** (`German.cs`) | Head-final noun compounds; verb-final word order after *weil* | Werkzeug "work stuff" = tool; Handschuh "hand shoe" = glove; *…weil der Mond dunkel ist* | "German builds new nouns by gluing old ones, and the last part tells you what it is: a glove is a 'hand shoe'." | Literal glosses like "work stuff" are etymology, not how speakers think of the word. Compounds with a linking -s- are deliberately avoided. |
| **Japanese** (`Japanese.cs`) | Verb-last (SOV) order; particles が/を mark roles; kanji compounds | 扉を探して tobira o sagashite "search for the door"; 電話 denwa "electric talk" = telephone | "Japanese tags each noun with a little particle saying what it's doing, so the verb can wait until the end, and you can even swap the nouns around." | In natural speech the subject is often marked with は (wa), not が (ga); the game only teaches ga. Kanji readings change from word to word (入 is iri in 入口, hai in 入る); the game says so. |
| **French** (`French.cs`) | Adjective after the noun; gender agreement; written plural -s on noun and adjective | le tunnel ouvert; les murs verts | "In French the adjective usually comes after the noun and has to agree with it, and those plural -s are written but silent." | "Most", not all: a few common adjectives (petit, grand) go first, and the reveal says this. The quiz's one-line explanation drops "most" (`French.QuizExplanation`). |
| **Spanish** (`Spanish.cs`) | Verb endings encode the subject (pro-drop); ¿…? and ¡…!; -o/-a gender | lees "you read"; ¿Aprende? "does she learn?" | "Spanish puts the 'who' inside the verb ending, so 'lees' already means 'you read', and questions open with an upside-down mark." | Only regular -er verbs in the present tense. The 1754 Royal Academy fact is correct. |
| **Turkish** (`Turkish.cs`) | Agglutination with a fixed suffix order; vowel harmony; consonant assimilation (-de/-te) | köylerimde "in my villages" = köy-ler-im-de | "Turkish snaps meanings on as suffixes in a fixed order, and the suffixes change their vowels to rhyme with the word." | Correct as implemented: 2-way -ler/-lar, 4-way -im/-ım/-üm/-um, -m after a vowel, t- after voiceless consonants. Only 4 suffixes are covered. |
| **Mandarin** (`Mandarin.cs`) | Measure words (classifiers) between a number or "this" and the noun; no plural on nouns for things | 三只狼 sān zhī láng "three wolves"; 这辆车 zhè liàng chē "this car" | "In Mandarin you can't say 'three wolves' directly. You say 'three animal-wolves', with a measure word chosen by the kind of thing." | Only 4 classifiers. 个 (gè) is the everyday fallback and the reveal mentions it. "No plural" is limited to things: 们 exists for people. Tones are shown but not tested. |
## 3. The reveal and the quiz (`UI/DecodeScreen.cs`, `UI/Lore.cs`)

After a correct answer, one screen does four things in order:

1. **Decode.** The alien glyphs you placed scramble and lock in, character by character, into the real script: 電話, دارس (Arabic is joined right to left by `UI/Rtl.cs`), or Werkzeug. Underneath come the romanisation, the English meaning, and what you typed. This is the "comparison": the alien form turning into the real one.
2. **The pattern.** One or two plain sentences stating the rule you just used, with the language's English name blacked out (`Lore.Redact`).
3. **The quiz:** "Which language is this?", with four options: the right language and three plausible neighbours (for Japanese: Korean, Mandarin Chinese, Vietnamese, Thai). It's built in `LanguageEngine.MakeQuiz`.
4. **Payoff.** The language name is stamped on, with a one-line explanation and a "Now you know" fact (for example, that Japanese station signs read 出口 for exit).

Why it's there: the puzzle teaches the rule by doing; the reveal attaches it to a real language and a real script; the quiz makes you make the link yourself before being told. A wrong quiz answer costs nothing: you just don't get the check mark. The quiz is not fed to the beast's learning model; only console answers are.

**End screen** (`UI/OtherScreens.cs`, `EscapeScreen`): "WHAT YOU CARRY OUT" lists each language decoded, the real phrases you built with their meanings, the fact, and a check mark if you named it right in the quiz. It also shows consoles solved, "misfires" (wrong answers) and your time.

## 4. The monster: what it really learns

**Accurate one-liner:**

> "The beast runs a small online Bayesian model of you, a logistic regression that updates after every answer. It estimates your skill on each grammar concept, with uncertainty, picks puzzle difficulty to aim for a 70% success rate, and a hand-written formula on top makes it bolder when you're weak or on a losing streak. It's not a neural network and it isn't pre-trained: it learns only from you, live, and remembers you between runs."

Details, so you can answer follow-ups (`Monster/Learning/PlayerModel.cs`, `AdaptiveDirector.cs`):

- **Model:** P(correct) = sigmoid(bias + ability + average concept skill − w·difficulty + w·hesitation + w·beast-proximity + w·streak). Ability and each concept skill are Gaussian beliefs (mean and variance), updated with an extended-Kalman/Laplace step. The shared weights update by SGD with an L2 pull back to hand-set priors. A probit correction makes uncertain beliefs predict closer to 50%.
- **Inputs it actually uses:** right or wrong; *which concept you missed* (from the language engine's diagnosis); the concepts each puzzle tests; how long you took compared with your own usual pace (a slow right answer counts as weaker evidence); how close the beast was when the puzzle opened; your streak.
- **Outputs:**
  - **Difficulty:** solved in closed form for a 70% predicted success (`DifficultyFor`). On a fresh machine the first value is about 0.32.
  - **Aggression:** 0.05 + (1 − mastery) − 0.4·momentum ± 0.06 per streak step. This part is a hand-tuned heuristic.
- **Memory:** saved to `beast_memory.json` in Unity's persistent data folder (`BrainStore.cs`). Press F3 to see what it has learned, and F4 while the overlay is open to wipe it.
- **Behaviour** (`Monster/Beast/BeastBrain.cs`) is a hand-written state machine, not learned: it lurks in the fog, stalks behind you, freezes while you look at it, creeps up while you work a console, lunges on a wrong answer and retreats on a right one. It evolves from stage 1 to 3 over time (faster when aggressive) and with each mistake. **It can only catch you at stage 3.**

**What to be honest about:**

- The director's difficulty only nudges each console by up to ±0.3 around a fixed ramp (`LanguageEngine.Effective`), so console 1 is always easy and later consoles get harder whatever you do.
- The overlay's "Probing *concept*" line (`FocusConcept`) is computed, but **the language engine never reads it**. It does not change which puzzle you get. Don't claim the beast "targets your weak grammar"; say it *tracks* it.
- Difficulty is read only when a console opens, so a wrong answer doesn't change the puzzle in front of you. It affects the next console and the beast.
- The priors and the aggression weights were chosen by hand. "Learning" means online updates of a small statistical model, nothing more.

## 5. 60-second pitch and 3-minute demo

### 60-second script

> "You crash on a planet drowning in blood. To power your ship you have to talk to three alien consoles, and something in the fog is learning how you think.
>
> Each console speaks a real Earth language (Arabic, German, Japanese, Turkish, Mandarin, French or Spanish) disguised in an alien script. You get two or three example phrases and work out the grammar yourself: German puts the thing last in a compound, Turkish chains suffixes in a fixed order. Every puzzle is generated from a seed and brute-force checked to have exactly one answer, so it's always solvable by reasoning, never guessing.
>
> Solve it and the alien script decodes into the real language. You're asked which language it was, then told a real fact about it. Get it wrong and the beast lunges.
>
> The beast runs an online Bayesian model of your skill on each grammar concept. It tunes the puzzles to keep you at about 70% and gets bolder when you struggle. The whole score is synthesised live and reacts to the danger. You escape having actually learned how three real languages work."

### 3-minute demo path (won't fail live)

Before going on: launch, press **F3** then **F4** to wipe the beast's memory, F3 to close it. Then press **F1** for demo mode: the seed is fixed at 4242, the beast keeps 9 m away, and getting caught is forgiven unless you press F12. "DEMO" appears in the top-right corner.

| Time | Do | Say |
|---|---|---|
| 0:00 | **Enter** to start. Walk a few steps with WASD and the mouse; look at the fog. | Setting, beast in the fog. |
| 0:15 | **F7** (warps you to console 1 and opens it). | Read the Arabic examples aloud: kataba, kātib, darasa. |
| 0:35 | Optional: press **2**, **Enter** for a deliberate wrong answer. The glass cracks and the beast lunges; the tiles clear by themselves. | "Wrong answers feed the beast." |
| 0:45 | **1**, **Enter**. | "d-r-s means study, CāCiC means 'one who does', so: learner." |
| 0:55 | Reveal: **Enter** to skip the decode animation, **Enter** again, then quiz **2** (Arabic), **Enter**. | Point at دارس appearing, the blacked-out name, the fact. |
| 1:30 | **F3** to show the overlay, then F3 to close. | "This is what it has learned about me: skill per concept, its prediction against what I actually did." |
| 1:45 | **F7**, then **2**, **1**, **Enter** (Werk Zeug). Reveal, **Enter**, quiz **3** (German), **Enter**. | Werkzeug = "work stuff". |
| 2:15 | **F7** for console 3. Read the tiles: either `den` then `wa`, or `tobira`, `o`, `sagashite`. Or press **F8** to skip straight to the reveal. | Japanese, kanji or particles. |
| 2:35 | **F10** (final chase), then **F11** (escape). | Lift-off, end screen: "what you carry out". |
| 2:55 | If there's time: **F5** to restart, then **F12** for the death ending. | |

**Traps:**

- **Never press F9.** It's bound twice: "skip to quiz" (`Game/DevKeys.cs`) *and* the audio audition mode (`Audio/AuditionMode.cs`). Pressing it takes the music over with a test sequence. If it happens, press **backquote (`)** to leave audition mode. Don't press F9 again: that also skips ahead and solves the current console.
- Don't press Esc in a puzzle; use Backspace.
- Demo console numbers only hold with demo mode on. In normal play the seed is random.
- Beast memory changes difficulty. That's why you wipe it first. Even so, console 3 can come up either way, so read the words.

Other keys: **F5** restart, **F6** back to walking, **F7** next puzzle, **F8** solve the current console and show its reveal, **F10** final chase, **F11** escape ending, **F12** death ending, **F** flashlight, **F3/F4** beast overlay and memory wipe. All of these are in `Game/DevKeys.cs` except F3/F4 (`Monster/Overlay/BrainOverlay.cs`) and F (`Game/PlayerController.cs`).

## 6. Likely judge questions, with honest answers

**Marshall Wace, "New Worlds"**

- *How does it fit?* You land on a new world and survive by understanding its language, the way a field linguist would: examples first, no dictionary. The twist is that the alien languages turn out to be real human ones, so you leave with real knowledge.
- *Is it educational or just a game?* Each puzzle isolates one real grammatical idea, and the reveal names it and ties it to the real script. Within three consoles you'll have met three real features. It's an introduction, not a course: vocabularies are small (a handful of nouns per language).

**QRT (technical depth)**

- *What's technically interesting?* (1) Puzzle generation with a uniqueness proof by brute force: rival grammars are filtered by consistency with the examples, then every tile sequence is enumerated, and generation is deterministic per seed (SplitMix64, FNV hashing; `Lang/Framework`). (2) An online Bayesian player model: Kalman-style updates on Gaussian skill beliefs, a probit-corrected predictive, difficulty solved in closed form for a target success rate, and lower-confidence-bound concept selection (`PlayerModel.cs`). (3) The engine is pure C# with no Unity dependency, so it runs and is tested outside the game. 87 EditMode tests, according to the merge commit `0efe439`.
- *Is it really ML?* It's online Bayesian logistic regression, a statistical model fitted live to one player, not a trained network. Some weights are learned by SGD; the priors and the aggression formula are hand-set. Say that.
- *How do you know it works?* The overlay shows predicted against actual for recent answers. There's no offline evaluation on real players.

**Composition / audio** (`Audio/README.md`)

- *What does the audio do?* Everything is synthesised in C# at runtime; there are no audio files. An 11-layer generative score with a 16th-note sequencer moves between moods (title, unsettling, tense, panic, death, escape) with hysteresis, quantised to the beat, driven by beast distance, stage and phase. A correct answer plays the phrase you built as a melody, with each word's motif generated from its glyph seed. The beast's voice and footsteps are spatialised in 3D. The death ending drops to a failing heartbeat, choir and bell; the escape resolves into D major.
- *Caveat:* only Japanese (In scale, koto), French (whole-tone, glass flute) and Spanish (Phrygian dominant, guitar) have their own musical mode. The other four languages use one generic "alien" mode (`Audio/Synth/Motif.cs`, `ForCode`). The UI doesn't raise the tile-picked event, so motifs play on a correct answer and the reveal, not on each tile press.

**The awkward ones**

- *How much was AI-generated?* Say it plainly: most of the code was written by AI coding agents (Claude) working in parallel threads, one per area (language, monster, world, UI, audio), with the team directing, reviewing and merging. Sixteen commits carry a Claude co-author line. The team chose the design, the languages and the direction. If you don't know who made a particular asset, say you'll check rather than guess.
- *What's fake or stubbed?* `Core/Fakes` holds stand-ins for every system, used only if a real one fails to load. The real language engine, monster, UI, world and audio are all registered in a normal run (`Bootstrap/GameBootstrap.cs`). The root-folder web version (`src/`, `TASKS.md`) is an older prototype with a different design. Don't quote it.
- *What doesn't work yet, or is weaker than it sounds?*
  - The quiz is easy to give away: the console header and the reveal show the language's own name (NIHONGO, DEUTSCH) and the real script before the question, and only the English name is blacked out.
  - The "focus concept" is not used to choose puzzles (see section 4).
  - Only 3 of the 5 generated puzzles are ever played, and with the demo seed you'll only ever see Arabic, German and Japanese.
  - The final run is only dangerous if the beast has reached stage 3; before that it can't catch you.
  - Vocabularies are tiny, there's no audio pronunciation of the words, and Mandarin tones aren't tested.

## Appendix: where things live

| Thing | File |
|---|---|
| Contracts (`LangPuzzle`, `RevealInfo`, `QuizQuestion`, `LanguageWorld`) | `Core/LanguageTypes.cs` |
| Concept names used for diagnosis | `Core/LangConcepts.cs` |
| Engine: generate, check, reveal, quiz, difficulty ramp | `Lang/LanguageEngine.cs` |
| Uniqueness check | `Lang/Framework/PuzzleBuilder.cs` |
| Wrong-answer hints | `Lang/Feedback.cs` |
| Phase machine, skip keys | `Game/GameDirector.cs`, `Game/DevKeys.cs`, `Game/DemoMode.cs` |
| Player model, director, memory | `Monster/Learning/*.cs` |
| Beast behaviour | `Monster/Beast/BeastBrain.cs` |
| Console, reveal/quiz, end screen | `UI/PuzzleScreen.cs`, `UI/DecodeScreen.cs`, `UI/OtherScreens.cs` |
| Answer key printer | `docs/answer-key/` |

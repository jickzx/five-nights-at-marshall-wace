# Sponsor features: implementation plan

Written 15 November 2026 against `main` at `d2acc00496325b7abfc011b909ea96be117f1768`. This is a plan only. Nothing here is implemented, and nothing may be pushed, merged, deployed or opened as a PR until the user has reviewed and approved the local result.

Three features, all optional at runtime and all layered on top of the existing game:

1. **Monster dialogue.** Gemini writes a rare, short line grounded in what the player actually did. ElevenLabs speaks it. It plays in 3D from the beast, and gets deeper and harsher at each evolution stage.
2. **Ship-computer hints.** The player presses a key at a console. Gemini writes a hint grounded in an approved rule and the three examples on screen. ElevenLabs speaks it in a clear voice and it is subtitled. It never gives the answer and never names the language before the quiz.
3. **Pronunciation.** After the language quiz is answered, the solved sentence is spoken in its real language, with a replay key. Only the native-script sentence is sent to speech, never the "native (romanisation)" pair.

What stays exactly as it is: answer checking (`LanguageEngine.Check`), puzzle generation, the beast's movement and learning (`AdaptiveDirector`, `BeastBrain`), the synthesised score and stingers, the tutorial, and a full offline game. `GameDirector` is not edited at all.

## 1. Facts this plan relies on

### From the code (read at d2acc00)

| Fact | Where | Consequence |
|---|---|---|
| Feature assemblies reference Core only; Bootstrap installs them in order | `Unity/README.md`, `Bootstrap/GameBootstrap.cs` | New code goes in a new Core-only assembly, `Fnmw.Voice`. Bootstrap needs two added lines (see 3.7). |
| Core interfaces already use default interface methods (`ShowTutorial`, `FocusConcept`) | `Core/Interfaces.cs` | Every Core addition can be a default member, so existing fakes and test doubles keep compiling. |
| `AnswerEvent` has `Correct`, `ResponseTime`, `MissedConcept`, `Difficulty`, `PuzzleId` | `Core/GameEvents.cs` | Enough for monster grounding. No Game or Monster change is needed. |
| `IDifficultyDirector` exposes `BeastAggression` and `FocusConcept` | `Core/Interfaces.cs` | The new assembly can read the learner's outputs without referencing `Fnmw.Monster`. |
| `LangPuzzle.Id` is `index + 1`, and `GetPuzzle` can rebuild a console at another difficulty under the same Id | `Lang/LanguageEngine.cs` (`BuildAt`) | A puzzle Id is not enough to detect a stale response. Use a content hash of the puzzle. |
| `RevealInfo.RealText` takes three shapes: `扉を…ください (tobira o … kudasai)`, `汉字。(Pinyin.)`, and Arabic `reading (script)` | `Lang/Languages/*.cs`, `UI/Lore.cs` (`SplitReal`) | Speech needs its own clean field. |
| `Draft.Feature` often contains the answer sentence or the language's name (German: `…, but {d.RealText}`; "Japanese puts the verb…") | `Lang/Languages/German.cs:213`, `Japanese.cs:146` | `Feature` cannot ground a pre-solve hint. A new per-concept table of neutral rule statements is needed. |
| The quiz answer is locked in the UI at `DecodeScreen.Choose`; `GameEvents.QuizAnswered` only fires when the payoff screen is dismissed | `UI/DecodeScreen.cs`, `Game/GameDirector.cs` (`Choose`) | "After the quiz" means the UI's `Payoff` step. The UI must be the one to ask for pronunciation. |
| The beast voice is one `AudioSource` driven by `PlayOneShot`; vocals fire on a timer | `Audio/SpatialAudio.cs` | Speech needs its own stoppable source on the beast, and the timer must yield while speech plays. |
| The score is rendered in `OnAudioFilterRead` on a 2D source; a `MasterLimiter` sits on the listener | `Audio/AudioDirector.cs`, `Audio/MasterLimiter.cs` | Ducking is done on the score source; the limiter already protects the sum. |
| `Audio/Synth/*.cs` is compiled outside Unity by `Tools~/RenderHarness` | `RenderHarness.csproj` | Voice DSP goes in `Synth/` as pure C#, so it can be rendered to WAV and tested without Unity. |
| Keys in use: digits, Enter, Space, Backspace, Delete, arrows, Esc, Tab, WASD, E, F, Q, T, Shift, F1, F3–F12, backquote | grep of `*Key` | `H` (hint), `P` (pronounce) and `F2` (debug line) are free. |
| The scene reloads on restart; Bootstrap clears the event bus and resets services each load | `GameDirector.Restart`, `GameBootstrap.Awake` | Per-run objects die with the scene. In-flight requests must be aborted in `OnDestroy`. |
| `Unity/UserSettings/` is git-ignored | `Unity/.gitignore` | A safe place for a local, untracked client config. |

### From provider documentation (checked 15 November 2026)

These came from the official pages. The programmer should re-check anything marked "confirm" with the first live call, because the pages did not state it outright.

**Gemini** (`ai.google.dev` now redirects to `aistudio.google.com/docs`):

- Endpoint: `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`. It is still documented. Most guide examples now use the newer Interactions API (`POST …/v1beta/interactions`, with `Api-Revision: 2026-05-20`); its status is not stated. Keep the Gemini call in one file so it can be swapped.
- Stable models: `gemini-3.5-flash-lite` ("fastest, most cost-effective … for high-throughput execution"), `gemini-3.6-flash`, `gemini-3.8-flash`, `gemini-3.1-flash-lite`. Use `gemini-3.5-flash-lite` by default. Do not use 2.5 models: `gemini-2.5-flash` is deprecated and is rerouted from 1 December 2026; `gemini-2.0-*` is shut down.
- Structured output on `generateContent`: `generationConfig.responseMimeType = "application/json"` with `generationConfig.responseJsonSchema`. `enum`, `required`, `maxItems` are honoured. `maxLength` and `minLength` are ignored by the server, so lengths must be checked in our code.
- Thinking: `generationConfig.thinkingConfig.thinkingLevel`, with `MINIMAL` available on `gemini-3.5-flash-lite` (not on `gemini-3.8-flash`). Thinking tokens count against `maxOutputTokens`.
- `systemInstruction` is a top-level request field (text only).
- Safety: the default threshold is Off for current models, so set `safetySettings` explicitly. A blocked prompt returns `promptFeedback.blockReason` and no candidates; a blocked answer has `candidates[0].finishReason` other than `STOP`.
- Keys: "Do not hardcode API keys directly in web or mobile apps… run a backend proxy server." Unrestricted standard keys are rejected; restrict the key to the Gemini API. Rate limits are per project and are shown only in AI Studio. Over-limit is `429 RESOURCE_EXHAUSTED`.
- Confirm: the `x-goog-api-key` header on `generateContent`. The reference page's examples pass `?key=`; the header appears on other pages. Use the header. If it is refused, stop and report rather than putting the key in a URL.

**ElevenLabs**:

- Endpoint: `POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}`, header `xi-api-key`, query `output_format`. Body: `text`, `model_id`, `language_code`, `voice_settings` (`stability`, `similarity_boost`, `style`, `speed`, `use_speaker_boost`), `seed`, `apply_text_normalization`.
- `output_format` includes `pcm_24000` (16-bit little-endian mono). 44.1 kHz PCM needs the Pro tier, so use 24 kHz.
- Models on this endpoint: `eleven_flash_v2_5` (32 languages including all seven of ours, about 100 ms median, half price per character) and `eleven_multilingual_v2` (29 languages, the default if `model_id` is omitted). `language_code` is **not** supported by Multilingual v2. `eleven_turbo_v2_5` is deprecated. `eleven_v4` and `eleven_v4_turbo` are documented only for the Text to Dialogue endpoints, so they are not a drop-in.
- Use `eleven_flash_v2_5` with `language_code` for everything. This matters most for pronunciation: a short kanji-only Japanese sentence can be read as Chinese without a language code.
- Errors: body `{"detail": {"type", "code", "message", …}}`. `429` with `rate_limit_exceeded` or `concurrent_limit_exceeded`; `system_busy`; `402 insufficient_credits`; `401 invalid_api_key`.
- Voices: Voice Library voices are "not available via the API to free tier users". Default (premade) voices "will expire on December 31, 2026". Voice Design creates an original voice from a text description. Professional clones are verified as the account holder's own voice.
- API keys can carry a credit quota and endpoint scope. `enable_logging=false` (zero retention) is enterprise only, so assume ElevenLabs keeps the text; we only ever send game text.
- Not found: per-plan concurrency numbers, and per-character prices. Budget by characters and keep concurrency at 2.

## 2. Voice identity

The user asked for a "Jason Huang" voice, possibly meaning Jensen Huang. That is still being clarified. This plan does not depend on the answer:

- The backend takes three voice IDs from its environment: `FNMW_VOICE_MONSTER`, `FNMW_VOICE_SHIP`, `FNMW_VOICE_NATIVE` (plus optional per-language overrides such as `FNMW_VOICE_NATIVE_JA`). Nothing in the game or the repository names a voice.
- Any voice the ElevenLabs account is licensed to use through the API works by pasting its ID. That covers a stock voice, a Voice Design voice, or a library voice that happens to carry that name.
- No voice is cloned from, scraped from, or tuned to imitate a real person. If the intent was Jensen Huang, the supported route is an original Voice Design voice with a similar *register* (warm, measured, confident keynote cadence), given a fictional ship-computer name. No code path uploads audio samples.
- A feature whose voice ID is unset is reported as off by the backend, and the game runs that feature in its offline mode.

The monster's character comes from our own DSP (section 5), not from the voice choice. The ship and the native speaker are left clean.

## 3. Architecture

### 3.1 Overview

```
Unity (no provider keys)                         backend/ (holds both provider keys)
────────────────────────                         ──────────────────────────────────
GameEvents ─▶ Fnmw.Voice ──HTTPS, session token──▶ /v1/session
  RunFacts, DialogueBrain                          /v1/monster-line ─▶ Gemini ─▶ validate ─▶ ElevenLabs
  HintFlow, PronounceFlow                          /v1/hint         ─▶ Gemini ─▶ validate ─▶ ElevenLabs
  SpeechArbiter, ClipCache                         /v1/pronounce    ─▶ validate ─▶ ElevenLabs
      │                                            /v1/speech/{id}  ─▶ PCM bytes
      ├─▶ Services.Audio.PlayVoice(clip)   (Fnmw.Audio: stage DSP, 3D source, ducking)
      ├─▶ Services.UI.ShowSubtitle / ShowHint / ShowPronunciation   (Fnmw.UI)
      └─▶ Services.Language.GetHintContext / IsHintSafe / GetReveal (Fnmw.Lang)
```

The UI calls `Services.Voice.RequestHint` and `Services.Voice.RequestPronunciation`. Monster dialogue needs no caller: `Fnmw.Voice` listens to the event bus.

### 3.2 Core changes (all additive)

These must be listed under `## Core changes` in the eventual PR description.

New file `Core/Voice.cs`:

```csharp
public enum VoiceChannel { Monster, Ship, Native }
public enum VoiceSource  { Live, Cache, Mock, Offline }

public sealed class VoiceClip {           // mono PCM handed to the audio thread's owner
    public float[] Samples; public int SampleRate;
    public VoiceChannel Channel; public int Stage;   // Stage: beast stage 1..3, Monster only
    public float Gain = 1f;
}
public struct SubtitleLine { public VoiceChannel Channel; public string Text; public float Seconds; public VoiceSource Source; }
public struct HintView     { public int PuzzleId; public bool Pending; public string Text; public int Level; public VoiceSource Source; }
public enum  PronunciationState { Hidden, Loading, Playing, Ready, Unavailable }

public sealed class HintContext {         // from the language engine; holds nothing answer-specific
    public string Concept;                // LangConcepts id
    public string[] OfflineHints;         // authored, index = level - 1
}

public interface IVoiceDirector {
    void RequestHint(int puzzleId);                          // UI: H at a console
    void RequestPronunciation(int puzzleId, bool replay);    // UI: payoff step, and P
    bool HintsAvailable { get; }                             // false hides the key cue
}
```

Changed files, default members only:

| File | Addition |
|---|---|
| `Core/Interfaces.cs`, `IAudioDirector` | `float PlayVoice(VoiceClip clip) => 0f;` (returns seconds it will play, 0 if it will not) and `void StopVoice(VoiceChannel channel, float fade = 0.15f) { }` |
| `Core/Interfaces.cs`, `IGameUI` | `void ShowSubtitle(SubtitleLine line) { }`, `void ShowHint(HintView hint) { }`, `void ShowPronunciation(PronunciationState state) { }` |
| `Core/Interfaces.cs`, `ILanguageEngine` | `HintContext GetHintContext(LanguageWorld w, int puzzleId, string missedConcept) => null;` and `bool IsHintSafe(LanguageWorld w, int puzzleId, string text) => false;` |
| `Core/LanguageTypes.cs`, `RevealInfo` | `public string SpeechText;` (native script only) and `public string SpeechLang;` (ISO 639-1) |
| `Core/Services.cs` | `public static IVoiceDirector Voice = new Fakes.FakeVoiceDirector();`, reset in `ResetToFakes` |
| `Core/Fakes/FakeVoiceDirector.cs` (new) | `RequestHint` shows the engine's offline hint through `Services.UI.ShowHint`; `RequestPronunciation` shows `Unavailable`; `HintsAvailable` is true when the engine returns a context |
| `Core/Fakes/FakeLanguageEngine.cs` | Implements `GetHintContext` (one fixed line) and `IsHintSafe` (rejects "Japanese" and any two tile words); fills `SpeechText`/`SpeechLang` |
| `Core/Fakes/FakeGameUI.cs` | Draws the hint line and one subtitle line in IMGUI; `H` calls `Services.Voice.RequestHint` |

`IsHintSafe` defaults to `false`: an engine that cannot vouch for generated text gets authored hints only.

No field is renamed or removed. `GameEvents` is unchanged.

### 3.3 Lang (`Assets/Scripts/Lang`)

- **`Speech.cs` (new).** `Speech.Clean(realText)` returns the sentence to speak. If the text has a bracketed part, return whichever side contains native script; otherwise return the text. Strip brackets and surrounding spaces, keep the sentence's own punctuation. `LanguageEngine.GetReveal` sets `SpeechText = Speech.Clean(d.RealText)` and `SpeechLang = b.Language.Code`.
- **`HintFacts.cs` (new).** One entry per `LangConcepts` id (15 entries): one or two approved, neutral rule statements. They must not name a language, a country or a script, and must not contain words from any puzzle. Example for `verb-final`: "In this tongue the action word waits until the end of its clause." The level-1 offline hint is the existing `Feedback.Wrong(concept)` line with its "glyphs flicker red" opening removed; the level-2 offline hint is the approved statement plus "Find it in the three examples."
- **`LanguageEngine.GetHintContext`.** Concept is `missedConcept` if it is a known concept, otherwise the puzzle's `Draft.Concept`.
- **`LanguageEngine.IsHintSafe`.** This is the authoritative leak check, because only the engine knows the answer. Reject the text if any of these hold:
  1. Longer than 160 characters, empty, or containing a character outside Latin letters, digits, spaces and `. , ; : ' " ! ? - ( )`. This also rejects every native script.
  2. Contains a language giveaway: the language's `Name`, its `Endonym`, any of its `QuizDistractors`, any other source language's name, or an entry from a fixed list of demonyms and country names. Matching ignores case and diacritics and uses word boundaries.
  3. Quotes more than one distinct puzzle word. A puzzle word is any tile form or example word, folded for case and diacritics, leading hyphen removed. Forms of one or two letters only count inside quotation marks, so "a" and "o" in ordinary English do not trip it.
  4. Contains an answer-giving phrase: "the answer is", "correct order", "the solution", "type", "press", or an ordinal ("first", "second", "last", …) within four words of a quoted puzzle word.

  Rule 3 is the structural guarantee: answers are four to eight tiles, and a passing hint can show at most one.
- **`backend/data/hint-facts.json`** is the same table for the server. A Lang EditMode test loads that file by relative path and fails if it differs from `HintFacts`.
- Optional, flagged as an open decision: Arabic words are stored unvowelled (`كتب`), so speech has to guess the vowels. Adding a vowelled form to the 21-entry table in `Arabic.cs`, used only for `SpeechText`, would fix this, but a fluent reader has to check it.

### 3.4 Audio (`Assets/Scripts/Audio`)

- **`Synth/VoiceFx.cs` (new, pure C#, no UnityEngine).** `float[] Monster(float[] mono, int sampleRate, int stage)`, `float[] Ship(...)`, `float[] Native(...)`. Chains are in section 5. Reuse `Svf`, `OnePole`, `Reverb`, `Dsp.SoftClip` from `Synth/Dsp.cs`.
- **`VoicePlayer.cs` (new).** Owns three sources: `BeastSpeech` (a child of the beast, `spatialBlend 1`, min 3 m, max 70 m, logarithmic, doppler 0, the same settings as `BeastVoice`), `ShipVoice` and `NativeVoice` (2D, children of the director). It builds an `AudioClip` from processed samples, plays it with `clip`/`Play` so it can be stopped, destroys the clip when it ends or is replaced, and fades on stop. Monster processing runs off the main thread (the same pattern as `SfxBank`) and playback starts on the next `Update`; a 5-second line must not cause a frame hitch.
- **`AudioDirector`.** Implement `PlayVoice` and `StopVoice` by delegating to `VoicePlayer`. `Die()` and `Escape()` stop all three channels with no fade. In `Update`, move `_scoreSource.volume` toward the duck target.
- **`SpatialAudio`.** While `BeastSpeech` is playing and for 1.5 s after, hold `_vocalTimer` so synthesised vocals do not talk over a line. If there is no beast transform, a Monster clip is not played (return 0).
- **Ducking.** Targets: 0.55 while Ship or Native plays, 0.75 while Monster plays, 1.0 otherwise; 120 ms down, 600 ms back. Confirm in the Editor with `AudioTap` that the source's volume scales the filter's output. If it does not, add one smoothed gain at the end of `ScoreEngine.Process` instead.
- **Volume.** Channel gains multiply a master voice volume (section 4.6).

### 3.5 UI (`Assets/Scripts/UI`)

- **`UiRig.cs`.** Add `UiInput.Hint` (H) and `UiInput.Speak` (P), read in `ReadInput`. Named by intent so a Quest controller mapping can set them later.
- **`SubtitleLayer.cs` (new).** One line of text over a dark box, with a speaker tag (`IT` or `SHIP`), upper-cased like the rest of the UI. When roaming it sits above SHIP POWER. On the console it uses the footer message line. It must never cover the tile row or the answer slots; extend `SentencePuzzles_FitTheConsole`-style checks to prove it.
- **`PuzzleScreen.cs`.** `Hint` input calls `Services.Voice.RequestHint(_p.Id)` when `Interactive`. New `ShowHint(HintView)` writes to the footer `_msg`: `SHIP: ANALYSING…` while pending, then the text typed out. It does not call `Reject`, does not shake, bleed or crack the glass. Footer priority: rejection text, then hint, then monster subtitle, then the key legend. After the second wrong answer on a puzzle, the legend gains `H ASK THE SHIP` (only if `Services.Voice.HintsAvailable`).
- **`DecodeScreen.cs`.** In `SetStep(Step.Payoff)`, if `_reveal` is not null, call `Services.Voice.RequestPronunciation(_puzzle.Id, false)` once. In the `Payoff` tick, `Speak` input calls it with `replay: true`. A small cue near the phrase shows the state: `LOADING`, `PLAYING`, `P HEAR IT AGAIN`, or `VOICE OFFLINE`. No call is made in `Morph`, `Read`, `Quiz` or `Verdict`.
- **`GameUI.cs`.** Implement the three new `IGameUI` members and route them. `HideAll` and `ShowEnd` clear the hint line and the subtitle.
- `WorkedExampleScreen` gets no hint key. The tutorial's worked example is not a real puzzle.

### 3.6 Voice (`Assets/Scripts/Voice`, new assembly `Fnmw.Voice`)

References: `Fnmw.Core` and `Unity.InputSystem` (for the F2 debug line) only.

| File | Role |
|---|---|
| `VoiceModule.cs` | `Install()`: `Services.Voice = VoiceDirector.Create()` |
| `VoiceDirector.cs` | The only MonoBehaviour. Implements `IVoiceDirector`, subscribes to `GameEvents`, ticks the pure classes, owns the run epoch, aborts everything in `OnDestroy` |
| `VoiceConfig.cs` | Loads and validates the backend URL, game token, volume and toggles |
| `RunFacts.cs` | Pure. Counts this run's misses per concept, the wrong streak, pace bucket, and buckets for distance and aggression |
| `DialogueBrain.cs` | Pure. Decides whether a monster line is wanted now (triggers, cooldown, budget) |
| `HintFlow.cs`, `PronounceFlow.cs` | Pure state machines (section 4) |
| `SpeechArbiter.cs` | Pure. Priority, interruption and drop rules between the three channels |
| `TextGuard.cs` | Pure. Client-side validation of monster lines (the hint check is the engine's) |
| `ClipCache.cs` | Static in-memory LRU of decoded clips, about 20 entries; survives scene reloads |
| `CircuitBreaker.cs` | Pure. Three consecutive failures open it for 60 s, then one probe |
| `Backend/IVoiceBackend.cs` | Callback-based interface: `Session`, `MonsterLine`, `Hint`, `Pronounce`, `Speech`, `CancelAll` |
| `Backend/HttpVoiceBackend.cs` | `UnityWebRequest` in coroutines; timeouts; no redirects |
| `Backend/OfflineBackend.cs` | Used when there is no config. Never touches the network |
| `Backend/Protocol.cs`, `Backend/Pcm.cs` | `[Serializable]` DTOs for `JsonUtility`; S16LE bytes to `float[]` |
| `VoiceDebug.cs` | F2: one line showing mode and the last result of each feature as `live`, `cache`, `mock` or `offline` |
| `AssemblyInfo.cs` | `InternalsVisibleTo("Fnmw.Tests.Voice")` |

The split follows `TutorialFlow` (pure) and `TutorialCoach` (MonoBehaviour feeder): every decision is in a pure class with an injected clock, so EditMode tests can drive it without play mode or a network.

**Client configuration.** Read in this order: command-line flags `-voiceUrl` and `-voiceToken`; environment variables `FNMW_VOICE_URL` and `FNMW_VOICE_TOKEN`; the file `Unity/UserSettings/fnmw-voice.json` in the Editor, or `fnmw-voice.json` under `Application.persistentDataPath` in a build. If none is present the module runs offline and makes no request. The URL must be `https`, or `http` only for `127.0.0.1` and `localhost`. The client only ever contacts that one origin. The token is a low-value shared game token, not a provider key. It is never committed and never logged.

### 3.7 Bootstrap

`Scripts/Bootstrap` is marked "don't touch without asking". Two lines are needed, and the user's authorisation for these features is taken to cover them, but they must be called out:

- `Fnmw.Bootstrap.asmdef`: add `"Fnmw.Voice"` to `references`.
- `GameBootstrap.Awake`: add `Voice.VoiceModule.Install();` as the last install, after `Game.GameModule.Install()`.

No scene or shared prefab is edited.

### 3.8 Backend (`backend/`, new top-level folder)

A small Node 20+ TypeScript service with no runtime dependencies (`node:http`, built-in `fetch`, `node:crypto`). It has its own `package.json`; the root `package.json` belongs to the old web prototype and is not touched. Tests use `vitest` with an injected fake `fetch`.

```
backend/
  package.json, tsconfig.json, .env.example, README.md
  data/hint-facts.json
  src/server.ts            node:http adapter, body limit, routing
  src/routes.ts            the five handlers
  src/auth.ts              game token check, signed session tokens
  src/limits.ts            per-session, per-IP and global counters; concurrency gates
  src/validate.ts          request schemas, text and script checks
  src/prompts.ts           system instructions, PROMPT_VERSION
  src/providers/gemini.ts, src/providers/elevenlabs.ts, src/providers/mock.ts
  src/cache.ts             LRU with byte cap and TTL
  src/log.ts               structured, redacting logger
  test/*.test.ts
```

Run it as **one long-lived process** (the demo laptop on `127.0.0.1`, or one small VM). The limits and caches are in memory. On a serverless platform each instance would have its own counters, so the limits would not hold; that needs a shared store and is out of scope.

**Environment** (documented in `.env.example`, values never committed): `GEMINI_API_KEY`, `ELEVENLABS_API_KEY`, `FNMW_GAME_TOKENS` (comma-separated), `FNMW_SESSION_SECRET`, `FNMW_GEMINI_MODEL` (default `gemini-3.5-flash-lite`), `FNMW_TTS_MODEL` (default `eleven_flash_v2_5`), the voice IDs from section 2, `FNMW_PROVIDER_MODE` (`live` or `mock`, default `mock`), `FNMW_BIND` (default `127.0.0.1:8787`), and the limit overrides.

At boot the service validates model and voice IDs against `^[A-Za-z0-9_.-]{1,64}$` and refuses to start in `live` mode without a session secret of at least 32 bytes. A missing provider key or voice ID turns that feature off; it does not stop the server.

**Endpoints.** Every body is JSON of at most 8 KB. Every response except `/healthz` requires `Authorization: Bearer …`.

| Route | Auth | Request | Response |
|---|---|---|---|
| `GET /healthz` | none | – | `{"ok":true}` and nothing else |
| `POST /v1/session` | game token | `{"proto":1,"build":"<≤32 chars>"}` | `{"session","expires_in_s":7200,"mode":"live"\|"mock","features":{"monster","hint","pronounce"}}` |
| `POST /v1/monster-line` | session | enums and small integers only (below) | `{"id","text","source","speech":{"id","sample_rate"}\|null}` |
| `POST /v1/hint` | session | the visible puzzle (below) | same shape |
| `POST /v1/pronounce` | session | `{"seq","lang","text"}` | `{"id","source","speech":{…}}` |
| `GET /v1/speech/{id}` | session | – | raw PCM, `X-Fnmw-Audio: pcm_s16le;rate=24000;channels=1` |

`/v1/monster-line` request: `seq`, `trigger` (`first_mistake`, `repeat_concept`, `streak`, `stage_up`, `final_chase`), `stage` (1–3), `concept` (a `LangConcepts` id or null), `concept_misses` (0–99), `wrong_streak` (0–99), `mistakes` (0–999), `solved` and `total` (0–12), `pace` (`fast`, `normal`, `slow`), `distance` (`far`, `mid`, `near`), `aggression` (`low`, `mid`, `high`). There is no free-text field, so a client cannot choose what the monster voice says.

`/v1/hint` request: `seq`, `puzzle_key` (16 hex characters), `level` (1 or 2), `concept`, `attempts` (0–99), `target_meaning` (≤ 100 characters), `examples` (exactly 3, each `meaning` ≤ 100 characters and 1–12 `words`), `tiles` (4–14). Words and tiles are ≤ 24 characters of Latin letters with diacritics, hyphen, apostrophe and `¿ ¡ ? ! . ,`. The request carries no answer, no language name or code, no native script, and no rule text; the server adds the approved rule from `hint-facts.json`.

`/v1/pronounce` request: `lang` is one of `ja fr es de tr zh ar`; `text` is ≤ 80 characters and must pass a per-language script check (kana, kanji and Japanese punctuation for `ja`; Han for `zh`; the Arabic block for `ar`; Latin with that language's letters for the rest). The voice is chosen by the server.

Speech generation starts as soon as the text has passed validation. The speech id is 128 random bits, bound to the session, and valid for 10 minutes. Splitting text from audio lets a hint's text appear before its audio is ready, and means audio failure still leaves a usable hint.

**Errors.** `{"error":{"code","retry_after_ms"?}}` with one of: `unauthorized` (401), `bad_request` (400), `too_large` (413), `not_found` (404), `rate_limited` (429), `invalid_generation` (502), `provider_unavailable` (503), `provider_timeout` (504), `disabled` (503), `budget_exhausted` (503). Provider error bodies are never forwarded.

**Provider calls.**

- Gemini: `systemInstruction` from `prompts.ts`; the request facts as a JSON user turn; `generationConfig` with `responseMimeType: "application/json"`, a `responseJsonSchema` of `{line: string}` (monster) or `{hint: string}` (hint), `maxOutputTokens: 200`, `thinkingConfig.thinkingLevel: "MINIMAL"`, temperature 0.9 for the monster and 0.4 for hints; explicit `safetySettings` at `BLOCK_MEDIUM_AND_ABOVE`. Accept only when there is no `promptFeedback.blockReason`, `finishReason` is `STOP`, and the body parses to exactly the expected key.
- ElevenLabs: `output_format=pcm_24000`, `model_id` from config, `language_code` (`en` for the monster and ship, the request's language for pronunciation), fixed `voice_settings` per channel, and `seed` derived from the text hash so repeats are as stable as the provider allows. Responses over 1.5 MB are rejected.
- Provider base URLs are constants. `fetch` is called with `redirect: "error"`. No request field can influence a host, path segment other than the validated voice or model ID, or header.

**Output validation on the server** (the client validates again):

- Monster line: 1–120 characters, at most 18 words, only letters, spaces and `. , ' ! ? -`; no language, country or demonym from the shared list; no provider or product names; no entry from a small profanity list; not equal to any of the session's last five lines.
- Hint: 1–160 characters, the same character set plus quotes; no language giveaway; at most one distinct token from the request's `tiles` and example `words`; none of the answer-giving phrases.
- A failed validation is `invalid_generation`. It is not retried: a second paid call is not worth it when an authored fallback exists.

**Prompts** state the persona and the hard rules: only use the supplied facts; never name or hint at which language this is; never give tile order; one sentence; no real people or brands. The hint prompt also says level 1 may only direct attention ("compare where the action word sits in rows one and three"), and level 2 may state the approved rule and point at one example word. `PROMPT_VERSION` is part of every cache key.

## 4. State flows

### 4.1 Session and modes

```
Install ─▶ no config ───────────────────────────────▶ OFFLINE (no requests, ever)
        └▶ config ─▶ POST /v1/session ─▶ ok ─────────▶ ONLINE (per-feature flags from the reply)
                                      └▶ fail ─▶ one retry after 2 s ─▶ fail ─▶ OFFLINE until next scene load
ONLINE ─▶ 401 on any call ─▶ one re-session ─▶ still 401 ─▶ OFFLINE
ONLINE ─▶ budget_exhausted or disabled ─▶ that feature OFFLINE for the rest of the session
ONLINE ─▶ 3 consecutive failures ─▶ breaker OPEN 60 s ─▶ one probe ─▶ CLOSED or OPEN again
```

The session token is kept in a static so a restart (scene reload) reuses it.

### 4.2 Staleness

Every request carries a ticket: `{epoch, kind, puzzleKey, seq}`.

- `epoch` is a static counter incremented in `VoiceDirector.Awake`. A scene reload therefore invalidates everything from the previous run.
- `puzzleKey` is an FNV hash of the target meaning, the tile forms in order and the example words, taken from the `LangPuzzle` in `PuzzleStarted`. It changes if the console is rebuilt at another difficulty.
- A result is used only if its epoch is current, its key matches the puzzle now in play (hint and pronunciation), its `seq` is the newest for its kind, and the phase still allows it. Anything else is discarded without playing; valid audio may still be stored in `ClipCache`.
- `OnDestroy` and a phase change to `Dead` or `Escaped` call `CancelAll`, which aborts every `UnityWebRequest`.

### 4.3 Monster dialogue

```
AnswerSubmitted / BeastStageChanged / PhaseChanged(→Chase)
   └▶ RunFacts.Observe
   └▶ DialogueBrain.Want(now)?  ── no ──▶ nothing
          yes
   └▶ POST /v1/monster-line (5 s)
          ├─ fail / stale ─▶ nothing is said; the synthesised vocals carry on as today
          └─ ok ─▶ TextGuard ─▶ GET /v1/speech (6 s)
                      └─ ok and still fresh ─▶ SpeechArbiter.Offer(Monster)
                              ├─ refused (another voice is speaking) ─▶ drop
                              └─ accepted ─▶ Audio.PlayVoice(stage now) ─▶ UI.ShowSubtitle for the returned duration
```

`DialogueBrain` rules:

| Trigger | Condition | Limit |
|---|---|---|
| `repeat_concept` | wrong answer, and this concept has now been missed twice or more this run | – |
| `streak` | wrong answer, wrong streak of two or more | – |
| `first_mistake` | first wrong answer of the run | once |
| `stage_up` | the beast reaches stage 2 or 3 | once per stage |
| `final_chase` | phase becomes `Chase` | once |

- At most one request in flight. At least 25 s between lines. At most 8 lines per run.
- Never during `Title`, `Reveal`, `Quiz`, `Escaped` or `Dead`, and never while `State.BeastGrace > 0` (the tutorial is holding the beast back).
- Earliest start: 1.2 s after a wrong answer and 2.5 s after a stage change or the final roar, so the existing stingers and lunge roar land first. A result older than 7 s is dropped: a late taunt is worse than none.
- The stage used for DSP is the stage when playback starts.
- The line is grounded in counts the game measured. It never claims the monster chose the puzzle, remembers earlier sessions, or is controlled by Gemini.

### 4.4 Hints

```
H at a console ─▶ Voice.RequestHint(puzzleId)
   ├─ no real puzzle in play, or cooldown (4 s), or a request already in flight ─▶ ignored
   ├─ this console already has 2 generated hints ─▶ replay the last hint (text and cached audio), no request
   └─ level = hints so far + 1 ─▶ UI.ShowHint(Pending)
        ├─ OFFLINE or breaker open ─▶ offline hint from GetHintContext
        └─ POST /v1/hint (3.5 s)
             ├─ timeout / error / 429 / stale ─▶ offline hint for this level
             └─ ok ─▶ Language.IsHintSafe(text)?
                        ├─ no ─▶ offline hint for this level
                        └─ yes ─▶ UI.ShowHint(text) at once
                                  └▶ GET /v1/speech (6 s) ─▶ if this hint is still the one on screen:
                                         SpeechArbiter.Offer(Ship) ─▶ Audio.PlayVoice
```

- The concept is the last missed concept on this puzzle, or the puzzle's main concept if there has been no wrong answer.
- Once an offline hint has been shown, a late live answer does not replace it.
- Offline hints are text only. There is no bundled speech.
- Leaving the console (Esc), solving it, or a new `PuzzleStarted` clears the hint line, fades the ship voice, and discards anything in flight.
- A hint does not change `Mistakes`, the difficulty model or the beast. It costs only time.

### 4.5 Pronunciation

```
PhaseChanged(→Reveal) ─▶ GetReveal ─▶ SpeechText empty? ─▶ state Unavailable
                              └▶ ClipCache hit? ─▶ Ready
                              └▶ POST /v1/pronounce + GET /v1/speech  (prefetch; nothing is played)
DecodeScreen reaches Payoff ─▶ Voice.RequestPronunciation(id, replay: false)
        ├─ Ready ─▶ SpeechArbiter.Offer(Native) ─▶ PlayVoice ─▶ UI state Playing ─▶ Ready
        ├─ still loading ─▶ UI state Loading ─▶ play on arrival if the payoff is still up (phase Quiz, same puzzle)
        └─ Unavailable / failed ─▶ UI state Unavailable ("VOICE OFFLINE")
P on the payoff ─▶ RequestPronunciation(id, replay: true) ─▶ restart from ClipCache; never a new request
```

- The only code path that plays the Native channel is `RequestPronunciation`, and the only caller is the payoff step. Prefetching sends the sentence to the backend during the reveal but plays nothing, so the quiz stays a real question.
- If the payoff is dismissed while the sentence is playing, it finishes (it is two to four seconds). If the audio arrives after dismissal, it is cached and not played.
- A failed pronunciation is not retried automatically. Pressing P after a failure makes one new attempt, at most twice per console.
- If the quiz is skipped (the engine returns none), there is no payoff and no pronunciation.

### 4.6 Scheduling, priority, volume

- Only one of the three voices is audible at a time. Priority: Native, then Ship, then Monster.
- A higher-priority voice interrupts a lower one with a 150 ms fade. A lower-priority voice offered while a higher one is playing is dropped, not queued. A voice replaces its own channel.
- Subtitles are shown only for audio that actually starts, for the duration `PlayVoice` returns. The exception is the hint, whose text is the feature: it shows with or without audio. Pronunciation has no subtitle; the sentence is already on screen.
- Settings, stored in `PlayerPrefs` and overridable by launch flags: `fnmw.voice.enabled` (`-voice off`), `fnmw.voice.volume` 0–1, default 0.9 (`-voiceVolume`), `fnmw.voice.subtitles` (`-subtitles off`). Channel gains before the master: Monster 1.0, Ship 0.85, Native 0.9. There is no settings screen today, and this plan does not add one.

### 4.7 Timeouts, cancellation, retries

| Call | Client timeout | Server deadline | Retry |
|---|---|---|---|
| `/v1/session` | 4 s | – | client: once after 2 s |
| `/v1/monster-line` | 5 s | Gemini 4 s | server: Gemini once on 429, 500 or 503 after 250–500 ms, if the deadline allows. Client: none |
| `/v1/hint` | 3.5 s | Gemini 3 s | as above |
| `/v1/pronounce` | 4 s | – | none |
| `/v1/speech/{id}` | 6 s | ElevenLabs 8 s | none. A paid generation is never retried automatically |

- The server aborts its upstream call when the client disconnects.
- ElevenLabs `429` is returned as `rate_limited` at once; the request is not queued. `401`, `402` and `403` from either provider turn that feature off for ten minutes and are logged by code only.
- All timeouts in the client use unscaled time.

### 4.8 Caching and invalidation

| Cache | Key | Lifetime | Invalidated by |
|---|---|---|---|
| Server, speech | hash of TTS model, voice ID, voice-settings version, language, text | 24 h, 64 MB cap | any key part changing |
| Server, hint text | `PROMPT_VERSION`, Gemini model, facts version, `puzzle_key`, concept, level | 24 h | any key part changing |
| Server, monster text | not cached (freshness is the point) | – | – |
| Client, clips | channel, language, text hash | process lifetime, about 20 clips | app restart |

Every response says `source: "live"` or `"cache"` (or `"mock"`); the client adds `"offline"`. The F2 line shows it, so a demo can say truthfully which it was. Nothing is written to disk.

## 5. Voice processing

All offline, on mono float samples, in `Synth/VoiceFx.cs`. The numbers are starting points to tune by ear.

| | Stage 1 | Stage 2 | Stage 3 |
|---|---|---|---|
| Pitch and speed (resampled together) | −2 semitones | −4 semitones | −7 semitones |
| Extra layers | none | one at −12 semitones, 35%, 12 ms late | −12 at 45% and −19 at 20%, 12 ms and 23 ms late |
| Drive (`SoftClip`) | none | ×2 | ×3.5 |
| Growl (amplitude modulation) | none | 20% at 38 Hz | 40% at 31 Hz |
| Low-pass | 5 kHz | 3.8 kHz | 3 kHz, with a +4 dB shelf under 180 Hz |
| Reverb send | 0.15 | 0.3 | 0.45, longer tail |

Every monster output is normalised to a −3 dBFS peak. Because pitch and speed move together, a stage-3 line is about 50% longer than the raw speech; the 18-word cap keeps it under about nine seconds.

- **Ship:** a 150 Hz high-pass, a gentle presence lift around 3 kHz, light compression, no reverb. It must stay easy to understand.
- **Native:** peak normalisation only. No pitch change, no filter, no reverb. This is a pronunciation reference.

Tests assert that the three monster stages differ from each other and from the ship and native outputs (spectral centroid falls with stage; duration grows with stage; native output equals the input up to gain).

## 6. Security and cost

| Concern | Control |
|---|---|
| Provider secrets | Only in the backend's environment. Not in Unity, the repository, logs, responses or error messages. `.env` is git-ignored; `.env.example` has names only. |
| Client authentication | A shared game token gets a signed, expiring session token. Tokens are compared in constant time. Session tokens are an HMAC over a session id, expiry and a salted IP hash. |
| Honest limit of that | The game token ships with the build and can be extracted. It is a speed bump. The real protection is that the endpoints are narrow and every budget is capped. |
| Narrow endpoints | No endpoint accepts a prompt. Monster input is enums and integers. Hint input is the visible puzzle with strict shapes. Pronounce input is one short sentence that must match its language's script. Voices and models are chosen by the server. |
| Residual abuse | A token holder can obtain short generic speech in seven languages (≤ 80 characters) and hint text for a made-up puzzle, within the budgets below. Accepted and documented. |
| Bounds | Body ≤ 8 KB; every string and array has a maximum; unknown JSON fields are rejected. |
| Rate limits (defaults) | Per session: 40 monster lines, 40 hints, 40 pronunciations, 8,000 speech characters. Per IP: 3 sessions and 120 requests per 10 minutes. Concurrency: 4 Gemini calls, 2 ElevenLabs calls. |
| Cost ceiling (defaults) | Global per day: 2,000 Gemini calls and 60,000 speech characters, then `budget_exhausted`. A normal run uses under 1,500 speech characters. Also set a credit quota on the ElevenLabs key and a spend cap on the Google project; those are the hard stops. |
| Outbound requests | Two constant hosts. No redirects. No client-supplied URL, host, voice or model. |
| Prompt injection in | Puzzle words come from the client and reach Gemini. The character and length limits remove instructions in practice; the output checks handle the rest. The worst outcome is a rejected or odd line returned to the same client. |
| Injected or malformed output | Strict JSON shape, `finishReason`, length, character set, deny-lists, tile-token limit; then the client repeats the monster check and the engine runs `IsHintSafe`. Text is only ever rendered as plain text or sent to speech. |
| Hint leakage | The answer, the language and the rule text never leave the game. The server caps quoted tokens at one. The engine, which knows the answer, has the final say. |
| Logging | Route, status, latency, cache hit, provider status code, character count, and a short salted hash of the session. Never bodies, generated text, tokens, keys, `Authorization` headers or raw IPs. Unity logs outcomes and error codes only. |
| Transport | HTTPS, or loopback HTTP. The server binds to `127.0.0.1` by default. No CORS headers are sent. |
| Data | No personal data is collected or sent. Player behaviour reaches the backend only as coarse buckets. |

## 7. Work order and file ownership

One programmer (Opus 5.5, medium), working locally on a branch, no pushes. Each phase ends with a clean compile and green EditMode tests (commands in section 10).

| Phase | Files | Done when |
|---|---|---|
| 0. Contracts | `Core/Voice.cs` (new), `Core/Interfaces.cs`, `Core/LanguageTypes.cs`, `Core/Services.cs`, `Core/Fakes/FakeVoiceDirector.cs` (new), `Core/Fakes/FakeLanguageEngine.cs`, `Core/Fakes/FakeGameUI.cs`, `Tests/EditMode/CoreContractTests.cs` (append) | Everything compiles unchanged; fakes answer the new members |
| 1. Lang | `Lang/Speech.cs`, `Lang/HintFacts.cs` (new), `Lang/LanguageEngine.cs`, `backend/data/hint-facts.json` (new), `Tests/EditMode/Lang/SpeechAndHintTests.cs` (new) | Section 8 Lang tests pass |
| 2. Audio | `Audio/Synth/VoiceFx.cs`, `Audio/VoicePlayer.cs` (new), `Audio/AudioDirector.cs`, `Audio/SpatialAudio.cs`, `Audio/Tools~/RenderHarness/Program.cs` (add a `voicefx in.wav outdir` mode), `Tests/EditMode/Audio/VoiceFxTests.cs` (new) | Section 8 Audio tests pass |
| 3. UI | `UI/UiRig.cs`, `UI/SubtitleLayer.cs` (new), `UI/PuzzleScreen.cs`, `UI/DecodeScreen.cs`, `UI/GameUI.cs`, `Tests/EditMode/UI/VoiceUiTests.cs` (new) | Section 8 UI tests pass; existing UI tests untouched and green |
| 4. Backend | everything under `backend/` | `npm test` passes in `backend/`; mock mode serves all routes |
| 5. Voice | everything under `Assets/Scripts/Voice/`, `Tests/EditMode/Voice/` (new asmdef `Fnmw.Tests.Voice` and tests), `Bootstrap/Fnmw.Bootstrap.asmdef`, `Bootstrap/GameBootstrap.cs` | Section 8 Voice tests pass; the game plays offline exactly as before |
| 6. End to end | `backend/README.md`, a short section in `Unity/README.md` | The mock-backend play-through in section 9 works; the report in section 11 is written |

Files that must not change: `Assets/Scenes/*`, any prefab, `Game/*`, `Monster/*`, `World/*`, `Lang/Framework/*`, `Lang/Languages/*` (unless the Arabic vowelling decision is taken), `ProjectSettings/*`, `Packages/*`, the root `package.json` and `src/`.

**Then an independent security check** (Opus 5.5, medium, a fresh context, read-only except for a findings file). It gets this plan and the diff, not the programmer's summary. Its checklist:

1. Search the whole diff and the working tree for anything that looks like a key or token, and for provider hostnames inside `Unity/`.
2. Read `auth.ts`, `limits.ts`, `validate.ts` and `routes.ts` line by line against section 6. Try to reach a provider without a session, with an expired session, with another session's speech id, with oversized and malformed bodies, and with extra fields.
3. Confirm no request field reaches a URL, header or log line.
4. Attack the hint path: craft model outputs that name the language indirectly, spell the answer one token per sentence, use look-alike characters, or use native script; confirm both the server and `IsHintSafe` reject them.
5. Confirm the Native channel cannot play before the payoff step by any call order.
6. Confirm budgets cannot be reset by reconnecting, and that a provider failure cannot cause a retry storm.
7. Confirm the game makes no network request with no config.
8. Report findings by severity. Small fixes go back to the same programmer.

**Could programming be done in parallel?** Not by feature. All three features meet in the same files: `Core/Interfaces.cs`, `AudioDirector.cs`, `GameUI.cs`, `VoiceDirector.cs`, and the backend's `routes.ts` and `validate.ts`. Three feature branches would conflict in every one of them. The only clean seam is `backend/` against `Unity/`, joined by the protocol in section 3.8 and by `hint-facts.json`. One programmer in the order above is the lower-risk choice; if time forces a split, it is backend versus Unity, with `hint-facts.json` owned by the Unity side.

One known outside conflict: the Quest branch (`feat/oculus-quest`, PR #17) also touches Core and Bootstrap. Expect a small merge in `GameBootstrap.cs`, `Fnmw.Bootstrap.asmdef` and possibly `Services.cs`. `UiInput.Hint` and `UiInput.Speak` will need controller bindings there.

## 8. Acceptance criteria and tests

All Unity tests are EditMode, use fakes, and make no network call. All backend tests use an injected fake `fetch`. **No test, script or manual step in this plan calls a paid generation endpoint.**

### Must hold for the whole change

- A1. With no voice config, the game makes zero network requests and plays as it does at d2acc00. Every test that exists today still passes unmodified.
- A2. No provider key, provider hostname or voice ID appears anywhere under `Unity/`.
- A3. `GameDirector`, `LanguageEngine.Check`, `AdaptiveDirector`, `BeastBrain`, `ScoreEngine` and the tutorial behave identically with the feature on, off, or failing. No voice call can block, delay or throw into the answer path.
- A4. The scene and prefabs are byte-identical.

### Core and fakes

| # | Case | Expect |
|---|---|---|
| C1 | `Services.ResetToFakes()` | `Services.Voice` is a `FakeVoiceDirector` |
| C2 | Fake voice, `RequestHint` on the fake engine's puzzle | `ShowHint` receives the offline text, source `Offline` |
| C3 | Fake voice, `RequestPronunciation` | `ShowPronunciation(Unavailable)`; nothing throws |
| C4 | A UI or audio double that implements none of the new members | Compiles and runs |

### Lang

| # | Case | Expect |
|---|---|---|
| L1 | Every language, 200 seeds, every console | `SpeechText` is non-empty, ≤ 80 characters, has no brackets, and `SpeechLang` is the language's code |
| L2 | Japanese, Mandarin, Arabic | `SpeechText` contains no Latin letter and does not contain the romanisation |
| L3 | French, Spanish, German, Turkish | `SpeechText` equals `RealText` |
| L4 | `HintFacts` | An entry for every id in `LangConcepts.All`; none contains a language name, endonym or quiz distractor |
| L5 | Every language, 200 seeds | Both offline hints pass `IsHintSafe` for their own puzzle |
| L6 | Hint containing the language name, in any case, with or without diacritics | Rejected |
| L7 | Hint containing a quiz distractor's name, or "Germany", or "kanji" | Rejected |
| L8 | Hint quoting two answer tiles, adjacent or far apart | Rejected |
| L9 | Hint quoting exactly one example word | Accepted |
| L10 | The full answer rendered as text | Rejected |
| L11 | Hint with native-script characters; hint of 161 characters; empty hint | Rejected |
| L12 | "The answer is…", "put 'kauft' second" | Rejected |
| L13 | Ordinary English using "a", "o", "no", "la" unquoted | Not counted as puzzle words |
| L14 | `hint-facts.json` against `HintFacts` | Identical |
| L15 | `GetHintContext` with an unknown concept | Falls back to the puzzle's main concept |

### Audio

| # | Case | Expect |
|---|---|---|
| U1 | `VoiceFx.Monster` at stages 1, 2, 3 on a fixed test signal | Output length grows with stage; spectral centroid falls with stage; peak ≤ −3 dBFS; no NaN |
| U2 | `VoiceFx.Native` | Equal to the input up to one gain factor |
| U3 | `VoiceFx.Ship` against `Monster` stage 1 | Different; ship duration equals input duration |
| U4 | Empty input, one-sample input, 30 s input | No exception; bounded time |
| U5 | Duck target logic | 0.55 for Ship or Native, 0.75 for Monster, 1.0 for none; the lowest wins when two overlap |
| U6 | `PlayVoice(Monster)` with no beast in the scene | Returns 0; nothing plays |
| U7 | `Die()` or `Escape()` during speech | All three channels stop |

### UI

| # | Case | Expect |
|---|---|---|
| I1 | `Hint` input on the console, interactive | Exactly one `RequestHint` with the puzzle's id |
| I2 | `Hint` input during the wrong-answer melt, on the worked example, on the decode screen | No call |
| I3 | `ShowHint` with text | The footer shows it; no shake, bleed or crack; picks are untouched |
| I4 | A rejection arrives while a hint is showing | The rejection text takes the footer |
| I5 | Full reveal and quiz, driven by scripted input | No `RequestPronunciation` during `Morph`, `Read`, `Quiz` or `Verdict`; exactly one, `replay: false`, on entering `Payoff` |
| I6 | `Speak` input on the payoff | One call with `replay: true`. `Speak` on any earlier step: no call |
| I7 | Quiz shown with no preceding reveal | No pronunciation call; no exception |
| I8 | Subtitle on every console layout in `SentencePuzzles_FitTheConsole` | Never overlaps the tile row or the answer slots |
| I9 | `HideAll`, `ShowEnd` | Hint and subtitle are cleared |
| I10 | Subtitles off | `ShowSubtitle` draws nothing; hints still show |

### Voice (pure classes with a fake backend and a fake clock)

| # | Case | Expect |
|---|---|---|
| V1 | Success path for each feature | The right `PlayVoice` channel, the right subtitle or hint, source `Live` |
| V2 | The same request served from cache | Source `Cache`; no second backend call |
| V3 | Network failure, timeout, `429`, `500`, `503`, `504` on each endpoint | Monster: silence. Hint: the offline hint, shown no later than the timeout. Pronunciation: `Unavailable`. No exception, no retry |
| V4 | Three failures in a row | Breaker opens; no request for 60 s; one probe after; success closes it |
| V5 | Malformed JSON; a missing field; an empty `text`; `speech` absent | Treated as failure for that feature |
| V6 | Monster text with a URL, a digit string, a language name, 200 characters, non-Latin characters | Rejected by `TextGuard`; nothing is played or shown |
| V7 | Hint text that fails `IsHintSafe` | The offline hint is shown; the generated text never reaches the UI |
| V8 | Speech bytes of odd length; zero length; over the size cap | Rejected; no clip |
| V9 | Two wrong answers 100 ms apart | One monster request |
| V10 | Triggers inside the 25 s cooldown; a ninth line in one run | No request |
| V11 | Wrong answer during `BeastGrace > 0` | No request |
| V12 | `H` pressed five times quickly | One request; later presses ignored until it resolves and the cooldown passes |
| V13 | A third hint on one console | Replays the second; no request |
| V14 | A hint response arrives after `PuzzleStarted` for a different puzzle, or for the same Id with different content | Discarded |
| V15 | A hint response arrives after the player left the console | Discarded; the footer is clear |
| V16 | A monster line arrives after 7 s; after the phase became `Reveal`; after `Dead` | Discarded |
| V17 | A response from epoch N arrives when the epoch is N+1 (restart) | Discarded |
| V18 | Phase becomes `Dead` or `Escaped` with requests in flight | `CancelAll` is called; all channels are stopped; nothing plays afterwards |
| V19 | Monster speaking, then a hint's audio arrives | Monster stops; ship plays |
| V20 | Ship speaking, then a monster line arrives | Monster line dropped; no subtitle |
| V21 | Pronunciation prefetched during `Reveal` | No `PlayVoice` until `RequestPronunciation` |
| V22 | `RequestPronunciation` while still loading, then audio arrives with the payoff still up | Plays once |
| V23 | Audio arrives after the payoff was dismissed | Cached, not played |
| V24 | Replay | Plays from `ClipCache`; zero backend calls |
| V25 | Pronunciation uses `SpeechText` and `SpeechLang` | The request text contains no brackets and no romanisation |
| V26 | No config | `OfflineBackend`; a recording transport sees zero calls |
| V27 | Session reply with `hint: false` | Hints go straight to offline; no `/v1/hint` call |
| V28 | `401` mid-session | One re-session; a second `401` switches to offline |
| V29 | URL `http://example.com`; a URL with credentials; a malformed URL | Config rejected; offline |
| V30 | `budget_exhausted` | That feature is offline for the rest of the session |
| V31 | Voice disabled by setting | No requests; `HintsAvailable` still true (offline hints) |

### Backend (`vitest`, fake `fetch`)

| # | Case | Expect |
|---|---|---|
| B1 | No `Authorization`; a wrong game token; a tampered, expired or other-secret session token | `401`; the fake provider sees no call |
| B2 | `GET /v1/speech/{id}` with another session's id, an unknown id, an expired id | `404` |
| B3 | Body of 8,193 bytes; invalid JSON; an unknown field; a wrong type; an out-of-range integer; an unknown enum | `413` or `400`; no provider call |
| B4 | Hint with 2 or 4 examples; a 25-character word; a word containing `{`, a newline or a URL | `400` |
| B5 | Pronounce with Latin text for `ja`; an unsupported language; 81 characters | `400` |
| B6 | Session limit plus one; IP limit plus one; daily budget plus one | `429` or `503 budget_exhausted`; no provider call |
| B7 | A new session from the same IP after the limit | Does not reset the IP or global counters |
| B8 | Three concurrent speech requests with a gate of 2 | The third gets `429` at once |
| B9 | Gemini `429` then `200` | One retry, then success |
| B10 | Gemini `429` twice; `500`; a timeout | `provider_unavailable` or `provider_timeout`; at most two calls |
| B11 | ElevenLabs `429`, `500`, timeout | Mapped error; exactly one call |
| B12 | ElevenLabs `401` or `402` | `disabled`; the feature stays off for ten minutes; the provider's message is not in the response |
| B13 | Gemini returns prose, an extra key, an empty string, `finishReason: "MAX_TOKENS"`, `promptFeedback.blockReason` | `invalid_generation`; no speech call |
| B14 | Gemini returns a 300-character line, a language name, a URL, non-Latin text, a repeat of the previous line | `invalid_generation`; no speech call |
| B15 | Gemini returns a hint quoting two tiles, or "ignore previous instructions and…" in 300 characters | `invalid_generation` |
| B16 | The same pronounce request twice | The second is `source: "cache"`; one ElevenLabs call |
| B17 | The same request after `PROMPT_VERSION`, the voice ID or the model changes | Cache miss |
| B18 | The captured Gemini request | Correct host and path; key only in the header; `responseMimeType`, schema, `thinkingLevel` and `safetySettings` present; no answer, language name or code in a hint request |
| B19 | The captured ElevenLabs request | Correct host; `pcm_24000`; `language_code` equal to the request's language; voice from config |
| B20 | Every log line produced by the suite | Contains none of the test secrets, tokens, request texts or generated texts |
| B21 | A missing provider key or voice ID | The server starts; `/v1/session` reports the feature off; the route returns `disabled` |
| B22 | `live` mode with a short session secret; a model ID containing `/` | The server refuses to start |
| B23 | Client disconnects mid-request | The upstream call is aborted |
| B24 | `mock` mode | All routes work; `source` is `"mock"`; `fetch` is never called |

## 9. What can be verified locally, and what cannot

### Without any keys

- Everything in section 8.
- A full play-through against the backend in `mock` mode on `127.0.0.1`. The mock Gemini returns canned lines chosen from the request's facts. The mock ElevenLabs returns a synthetic tone pattern of the right length. It is plainly not speech, and every subtitle and the F2 line are marked `MOCK`. This proves the pipeline: triggers, timing, 3D placement on the beast, stage DSP differences, ducking, subtitles, hint flow, replay, restart, death and escape.
- Failure behaviour, by stopping the mock backend mid-run, or starting it with a fault flag (`FNMW_MOCK_FAULT=timeout|429|500|garbage`).
- Voice DSP on real speech without a provider: `RenderHarness voicefx in.wav outdir` renders any WAV the user records themselves through the three monster stages, the ship chain and the native chain.
- Offline play with no config.

These are honest mocked integration tests. They do not show that Gemini writes good lines or that ElevenLabs pronounces anything correctly, and the report must not claim so.

### Needs keys and the user

- The first live call to each provider, confirming the request shapes in section 1 (in particular the `x-goog-api-key` header, `responseJsonSchema`, `thinkingLevel: MINIMAL`, and `language_code` with `eleven_flash_v2_5`).
- That the chosen voice IDs are usable through the API on the account's plan.
- Real latency from request to audible speech on the demo network, and whether the timeouts in 4.7 suit it.
- The quality and tone of generated lines and hints, and how often validation rejects them.
- Subjective audio: whether stage 1 to 3 sounds progressively deeper and scarier yet stays intelligible; whether the ship voice is clear over the score; levels against the stingers.
- Pronunciation correctness in all seven languages, judged by fluent listeners. Arabic vowels, Japanese kanji readings and Mandarin tones are the known risks.
- Real cost per run, read from the provider dashboards.

These steps spend money and are for the user to run or approve. The programmer and the security checker treat keys as unavailable.

## 10. Commands

From `Unity/`, as in `Unity/README.md`:

```bash
~/.unity/bin/unity recompile --project-path "$PWD"                          # zero rows = clean
~/.unity/bin/unity test "$PWD" --mode EditMode -o /tmp/test-results.xml     # Editor closed
~/.unity/bin/unity command test run --project-path "$PWD" --mode editor     # Editor open
```

A new asmdef is not visible to an out-of-editor compile check that reuses another checkout's build graph. Use the Unity commands above for the real result.

From `backend/`: `npm install`, `npm test`, `npm run dev` (mock mode on `127.0.0.1:8787`).

## 11. What the programmer reports back

- The test output: counts passed and failed, for Unity and for the backend, with any failure quoted.
- A list of every Core member added, and the two Bootstrap lines.
- Confirmation that no scene, prefab, `Game/` or `Monster/` file changed (`git status`, `git diff --stat`).
- What was verified with the mock backend, and a plain statement that nothing was verified against the live providers.
- Anything in this plan that turned out not to fit the code, and what was done instead.

No commit is required. Nothing is pushed.

## 12. Open decisions

1. **Which voices.** Waiting on what "Jason Huang" meant. Not blocking: IDs are configuration. A real person's voice will not be cloned.
2. **Where the backend runs for the demo.** The demo laptop on loopback is simplest and safest. A small VM is needed if the game runs on another machine, such as a headset.
3. **The two Bootstrap lines.** Assumed approved with the features; say so if not.
4. **Does a hint cost anything?** The plan says no penalty and no effect on the learner. The alternative is to count a hinted solve as weaker evidence, which means a Monster change.
5. **Arabic vowelling for speech.** Add vowelled forms (needs a fluent check), or accept the provider's guess.
6. **Keys.** `H` and `P` on keyboard. Quest bindings are for the Quest branch.
7. **Offline monster lines.** The plan is silence plus the existing vocals. Authored subtitle-only lines are possible but read oddly without a voice.
8. **Gemini model.** `gemini-3.5-flash-lite` by default; `gemini-3.6-flash` if the lines are flat. Configuration only.
9. **Sponsor rules.** Not supplied. Whether cached or mock output counts, and what must be shown live, is unknown.

## 13. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| Latency on venue Wi-Fi | Late or missing lines | Short timeouts, drop-if-stale, prefetch for pronunciation, offline hints |
| A hint that helps too much while passing validation | A puzzle solved by the ship | One quoted token at most; level rules in the prompt; two generated hints per console; review real outputs with keys |
| Validation rejects many good lines | The features look absent | Measure the rejection rate in the live check; tune prompts before loosening rules |
| Wrong pronunciation | A teaching claim that is false | Fluent review; label it a reference; the Arabic option above |
| ElevenLabs plan limits | Voices unusable through the API; default voices expire 31 December 2026 | Check voice access at setup; prefer a Voice Design voice |
| Gemini API drift | Requests rejected (the docs are moving to the Interactions API; 2.5 models reroute on 1 December 2026) | One provider file; model by configuration; contract tests on the request shape |
| The game token is extracted | Someone spends the budget | Narrow endpoints, per-IP and daily caps, provider-side spend caps, rotate the token |
| In-memory limits on a multi-instance host | Limits do not hold | Single process only; documented |
| Ducking through the source volume does not affect the filter output | No ducking | Check with `AudioTap`; fall back to a gain in `ScoreEngine.Process` |
| Merge with the Quest branch | Conflicts in Core and Bootstrap | Additive changes only; small, known files |
| `JsonUtility` leniency | Unknown fields ignored; a missing object becomes an empty one | Validate every field after parsing; treat an empty id as absent |
| The monster's speech masks a console the player is reading | Annoyance | Sparse budget, 25 s cooldown, never in the reveal or quiz, subtitles |

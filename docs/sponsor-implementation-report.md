# Sponsor features: implementation report

Local work on branch `feat/sponsor-voice-integrations`, based on `d2acc00`. Nothing is committed, pushed, merged or deployed, and no paid API was called. No credentials were used or looked for. Start from [sponsor-implementation-plan.md](sponsor-implementation-plan.md); this report covers what was built, what changed from that plan, how it was tested, and what remains unverified.

## Summary

All three features are implemented end to end, behind a new backend:

1. **Monster dialogue.** When the player's actual mistakes, a beast evolution or the final chase trigger it, Gemini writes one line. The trigger is rare and grounded in what happened. ElevenLabs speaks the line, and it plays in 3D from the beast. The game's own DSP makes it deeper and harsher at each beast stage. A subtitle shows only while the audio is actually playing.
2. **Ship hints.** H at a console shows a hint at once in the console footer. If the backend has speech, the hint is also spoken in a clean voice. The hint is grounded in an approved rule for the concept and in the three rows on screen. The server checks it, and so does the language engine, which knows the answer. If either check fails, the player gets an approved guided or authored hint instead. There are two hints per console; after that, H replays the last one.
3. **Pronunciation.** The decoded sentence, in native script only, is fetched quietly during the reveal. It plays only at the quiz payoff, and P replays it from a local cache without a new request.

With no configuration the game makes no network request. The monster stays silent, hints are authored text, and there is no pronunciation cue. Provider keys, provider hosts and voice ids exist only in `backend/`.

## Corrections to the plan, and what was done

| Correction from the parent review | What was built |
|---|---|
| 1. Quoting at most one tile does not prove there is no leak | The checks are conservative, on both sides. The engine (`Lang/HintSafety.cs`) also rejects an English meaning of an answer word next to an ordering word, any puzzle word next to an ordering word, two or more puzzle words, and three or more references in total. It rejects digits other than row numbers, URLs, native script and look-alike characters, language, country and script names, and instruction words. Gemini must also choose rows (`{rows, hint}`). When its text fails, the server builds a **guided hint** from approved text plus those rows, so the model contributes only the choice of rows. The remaining risk is stated plainly in `backend/README.md` and below. Character limits are not treated as protection against prompt injection. |
| 2. The hint cache key must not trust the client | There is no `puzzle_key` in the request; a body containing one gets `400`. The server key hashes the whole validated request (except `seq` and `attempts`), plus `PROMPT_VERSION`, the facts version, the mode and the Gemini model. The game's clip cache is keyed on backend identity (URL plus a hash of the token), the server's `config_version` (which covers mode, models, voices and prompt, facts and voice-settings versions) and the content. Entries remember whether they were mock audio, so a mock clip is never relabelled live. |
| 3. Async speech must not be cancelled when the text reply finishes | Speech jobs are independent of the request that started them. They are bounded: at most 2 TTS calls in flight (refused at once beyond that), 2 per session, 256 job records with a 10-minute TTL, and audio held only in a 64 MB LRU. Reservations are released whenever a job fails or is refused. Characters are refunded only when nothing can have been billed. A client that disconnects stops only its own wait. |
| 4. Budgets are not hard caps; session creation needs bounds | Session ids are server-chosen and 128-bit. Sessions are capped at 3 per IP per 10 minutes and 500 alive at once. All limits are in process memory, so a restart resets them and a second instance would count separately. The README says this and names the provider-side quota and spending limit as the only hard stops. A Google budget alert is described as an alert, not a cap. |
| 5. Minimum useful design, no fake stubs | Some of the plan's classes were merged: `HintFlow` and `PronounceFlow` live inside `VoiceCore`, and the offline backend is "no transport". Everything listed is real behaviour; nothing is stubbed. |
| 6. UI state must match actual playback | `IAudioDirector.PlayVoice` reports events instead of returning a duration. `VoicePlayer` reports `Started`, with the length after processing, only when the source actually starts. That happens a frame or more after DSP on a worker thread. Subtitles and the `Playing` cue wait for `Started`, and the cue shows `Preparing` until then. Playback stops or is cancelled on death and escape (stop everything, abort requests), on restart (`OnDestroy` aborts requests; a static epoch drops late results), on a console change or rebuilt content (a content hash), on walking away from a console, and on solving. |
| 7. Real sentence lengths | Measured across 120 seeds × 5 consoles × 4 difficulties: the longest `SpeechText` is de 39, fr 31, tr 28, es 26, ar 20, ja 10 and zh 8 characters. The bound was set to 80, and a test fails if the engine ever exceeds it. Real puzzle maxima for hint requests: words up to 16 characters, meanings up to 34, 5 words per row, 8 tiles. A real-data test found that German examples contain multi-word units ("Ich weiß, dass"). The word schema was widened to allow single inner spaces, on the client and the server, so German hints would no longer fall silently back to offline. Speech extraction handles Japanese `native (reading)`, Mandarin `漢字。(Pinyin.)`, Arabic `reading (script)` and plain Latin. Transliterations with ṭ, ḥ and ā count as Latin. |
| 8. Off by default; mock clearly labelled; H and P cues | The backend defaults to `live`, where any feature without a key or voice is off. Mock mode has to be chosen explicitly (`npm run dev`). Mock output is labelled `SHIP (MOCK)`, `IT (MOCK TONE, NOT SPEECH)`, `MOCK TONE PLAYING (NOT SPEECH)` and `VOICE MOCK … not speech` on F2. The console legend adds `H SHIP` only after a wrong answer, so a player who is never wrong sees the old legend. P is cued only on the payoff. Neither key is used anywhere else, and the tutorial layer is untouched. |

## Changed Core members (for the PR's `## Core changes`)

All changes are additive.

- New `Core/Voice.cs`: `VoiceChannel`, `VoiceSource`, `VoiceClip`, `VoicePlaybackEvent`, `VoicePlayback`, `SubtitleLine`, `HintView`, `PronunciationState`, `PronunciationView`, `HintContext` and `IVoiceDirector`.
- Parent review added the default `IVoiceDirector.NotifyQuizAnswerLocked(puzzleId)` member. The UI signals an accepted quiz choice, and the voice core refuses pronunciation until that signal matches the current puzzle.
- `ILanguageEngine`: `HintContext GetHintContext(world, puzzleId, missedConcept) => null` and `bool IsHintSafe(world, puzzleId, text) => false`.
- `IAudioDirector`: `int PlayVoice(VoiceClip, Action<VoicePlayback>)`, where the default reports `Dropped` and returns 0, and `void StopVoice(VoiceChannel, float fade = 0.15f)`.
- `IGameUI`: `ShowSubtitle(SubtitleLine)`, `ShowHint(HintView)` and `ShowPronunciation(PronunciationView)`, all defaulting to no-ops.
- `RevealInfo`: `SpeechText` and `SpeechLang`.
- `Services.Voice` (a `FakeVoiceDirector`), which `ResetToFakes` resets.
- Fakes: a new `FakeVoiceDirector`, which shows the engine's authored hints and reports pronunciation unavailable. `FakeLanguageEngine` implements both new members and fills the speech fields. `FakeGameUI` draws a hint line and a subtitle, and H asks for a hint.

**Bootstrap** (authorised for these features):

- `Fnmw.Bootstrap.asmdef` adds `"Fnmw.Voice"`.
- `GameBootstrap.Awake` adds `Voice.VoiceModule.Install();` after `Game.GameModule.Install()`.

The new assembly `Fnmw.Voice` references only `Fnmw.Core` and `Unity.InputSystem`. `InternalsVisibleTo` was added to Audio, UI and Voice, for their own test assemblies only.

## Files

- **Lang**: `Speech.cs`, `HintFacts.cs` and `HintSafety.cs` are new. `LanguageEngine.cs` gains `GetReveal` speech fields, `GetHintContext` and `IsHintSafe`. `backend/data/hint-facts.json` is generated from `HintFacts`, and a test keeps the two identical. `Lang/Languages` and `Lang/Framework` are untouched.
- **Audio**: `Synth/VoiceFx.cs` is new (pure C#: monster stages, ship, native, duck curve), and so is `VoicePlayer.cs`. `AudioDirector.cs` gains `PlayVoice`/`StopVoice`, stops all voices on death and escape, and ducks the score with a smoothed gain inside `ScoreFilter`, so it does not depend on how `AudioSource.volume` meets `OnAudioFilterRead`. `SpatialAudio.cs` holds the synthesised vocals while the beast is speaking, plus 1.5 s. `RenderHarness` gains `voicefx in.wav outdir`.
- **UI**: `SubtitleLayer.cs` is new. `UiRig` adds `Hint` (H) and `Speak` (P). `PuzzleScreen` gains the footer priority (rejection, then hint, then subtitle, then legend), two-line paging for long text, and H. `DecodeScreen` gains exactly one pronunciation request on entering the payoff, P to replay, and a state cue. `GameUI` gains routing, clears these views on `HideAll` and `ShowEnd`, and adds test read-outs.
- **Voice**: `VoiceModule`, `VoiceDirector` (the only MonoBehaviour: events, ticking on unscaled time, abort on destroy, the F2 line), `VoiceCore` (session, deadlines, staleness, the three flows), `Rules.cs` (`RunFacts`, `DialogueBrain`, `SpeechArbiter`, `TextGuard`, `CircuitBreaker`, `ClipCache`), `Protocol.cs`, `Transport.cs` (UnityWebRequest with redirects off, capped downloads and at most 6 in flight) and `VoiceConfig.cs`.
- **backend/**: Node 22, TypeScript run through type stripping, no runtime dependencies. `config`, `auth`, `limits`, `cache`, `validate`, `textcheck`, `prompts`, `hintfacts`, `service`, `server`, `main`, `providers/{common,gemini,elevenlabs,mock}`, tests, `README.md` and `.env.example`. `.gitignore` excludes `.env` and `node_modules`.
- **Docs**: a section added to `Unity/README.md`, and this report. The three planning docs are unchanged.

No scene, prefab, `Game/`, `Monster/`, `World/`, `ProjectSettings/`, `Packages/`, root `package.json` or `src/` file changed (checked with `git status`).

## Test results

### Backend

`npm run typecheck` is clean. `npm test` gives **34 passed, 0 failed**. Every provider call goes to an injected fake `fetch`, behind a real local HTTP server. The tests cover:

- B1–B24 from the plan: authentication, ownership, schemas, limits, the concurrency gate, retries, error mapping, malformed generations, the cache, request shapes, log redaction, disconnects and mock mode;
- the async jobs surviving the end of the text reply;
- gate release and character refunds after failures;
- bounded caches, counters and sessions;
- mock faults;
- hint attacks;
- guided hints always passing.

### Unity

The Editor is open, so the CLI was used for real compiles:

- `unity recompile` shows zero errors and zero warnings.
- The last complete `unity command test run --mode editor` gave **180 passed, 1 failed**. The failure was the German multi-word case above. It was fixed, and the affected suites were re-run in full afterwards:
  - `SpeechAndHintTests`: 12/12;
  - `Fnmw.Tests.Voice`: 32/32;
  - `VoiceUiTests`: 9/9;
  - Audio `VoiceFxTests` and `VoicePlayerTests`: 10/10;
  - `CoreContractTests`, including C1–C4.
- The final full run, after the fix, passed everything: see the next section.

Existing tests were not modified.

What the new Unity tests cover:

- **Lang**: L1–L15, real hint request bounds, and `hint-facts.json` parity.
- **Audio**: U1–U7. The stage centroids on the test voice are 1780 Hz raw, then 1289, 948 and 689 Hz for stages 1 to 3. Lengths are 1.50, 1.97, 2.38 and 3.21 s. The native output equals the input up to one gain. Started fires only after processing.
- **UI**: I1–I10.
- **Voice**: V1–V31 plus mock labelling and bounded caches.

### Final full EditMode run

After every change, with the Editor open: `unity recompile` showed zero rows, and `unity command test run --project-path Unity --mode editor --timeout 1500` gave **182 passed, 0 failed**. That covers all existing suites, unmodified, and all new ones. An earlier attempt with the default 600 s CLI timeout returned no results; the longer timeout fixed it.

### Play-mode smoke test against the mock backend

Run in the Editor's play mode, driven by `unity command script eval`. Each step lists what was observed.

1. Start: the backend runs in mock mode on `127.0.0.1:8787` with a throwaway token. The game reads a temporary `Unity/UserSettings/fnmw-voice.json` (deleted afterwards). `POST /v1/session` returned 200 over plain HTTP to loopback, so the Editor allows it despite "Allow downloads over HTTP" being off. A standalone build was not tested.
2. A restart in demo mode reused the static session, with no second `/v1/session`.
3. Two wrong answers produced a monster line, then a speech fetch. A `BeastSpeech` source was created as a child of the `Beast` object, and F2 showed `monster mock`. The console legend changed to `… · H SHIP`.
4. H produced a hint, then speech. The footer showed `SHIP (MOCK): COMPARE ROWS 1 AND 3 …` under the tiles (screenshot checked), with no shake or extra crack.
5. Solving fetched the pronunciation during the reveal. Nothing played and no cue showed. Answering the quiz through `GameUI.Tick` reached `Payoff`. The cue read `P  REPLAY MOCK TONE (NOT SPEECH)` once the first play had ended. On pressing P, the same frame showed the source not yet playing (still preparing). The next eval showed `MOCK TONE PLAYING (NOT SPEECH)`, a 2D source playing a 1.40 s clip, and no new backend request.
6. Death (`SkipTo(Dead)`): 0 console errors over the whole run.
7. With the config removed, a new play session showed `VOICE OFFLINE`. Two wrong answers and H gave an authored hint, with **0 requests** reaching the still-running backend, no beast speech source and 0 console errors.

Not observed in play mode: the stage-2/3 monster DSP heard on a real voice, ducking heard by ear, and ship-over-monster interruption with real timing. Those are covered by EditMode tests, not by listening.

## What is mock, and what is unverified

### Parent verification after implementation

The parent found that pronunciation still being prepared could start after the payoff was dismissed or another console opened. Pending native playback is now stopped in both transitions, including when processing has not emitted `Started`. Two regression cases cover these transitions. The pronunciation cache callback also captures its request's cache key rather than reading mutable state on completion.

An explicit quiz-choice signal now gates pronunciation in the voice core. Tests reject requests during the unanswered quiz and signals for another puzzle. The UI test checks the signal occurs when a choice is accepted. A tautological hints-available assertion was replaced with a real assertion.

After these changes, `unity recompile` returned zero diagnostics. The targeted real Editor run (`--filter Voice`) passed all 55 selected tests, including Audio, Voice, UI and two Core tests. Results were recorded in `/tmp/fnmw-parent-voice-tests.json`. The earlier 182-test full run predates these parent fixes; it is not claimed as a rerun of the final tree.

The mock backend proves the plumbing: triggers, timing, 3D placement, stage DSP, ducking, subtitles, the hint flow, replay, restart, death, escape, failure handling and labelling. **It does not show** that Gemini writes good or safe lines, or that ElevenLabs pronounces anything correctly. Its "speech" is a beeping tone.

Nothing was verified against the live providers:

- Gemini: `x-goog-api-key` on `generateContent` (documented for the Interactions API), and `responseJsonSchema` plus `thinkingLevel: MINIMAL` on `gemini-3.5-flash-lite`. These were checked against the current docs pages with `curl` on 15 November, but not run.
- ElevenLabs: `language_code` with `eleven_flash_v2_5`; `pcm_24000` is documented, but the raw body format is unverified. The account's voice access is also unverified.
- Generation quality, rejection rates, latency, cost, and pronunciation correctness. The last needs fluent listeners: Arabic is stored unvowelled, and Japanese kanji readings and Mandarin tones are known risks.

## Residual risks and known limitations

- **Hint leakage.** The checks block known patterns but cannot prove that no paraphrase reveals the answer. Approved level-2 rules narrow the answer on purpose. `HintFacts` was written for this game and needs a teacher's review. Gemini still sees puzzle words, so prompt injection is possible. Its output is constrained by the checks and the guided fallback, not prevented.
- **Pronunciation gate.** Parent review added an explicit quiz-answer-locked signal and reset it on puzzle changes, new reveals and exit from the quiz. Being in `GamePhase.Quiz` alone no longer permits playback. Only the payoff requests playback, and UI tests check that a choice is signaled before that request.
- **The game token can be extracted.** Limits are in-process and single-instance. See `backend/README.md`.
- **Standalone builds may refuse `http://127.0.0.1`** because of the project's HTTP setting. Use HTTPS, or change the setting deliberately.
- **Ducking** uses a gain inside `ScoreFilter`, verified by unit tests on the curve but not measured with `AudioTap`.
- **Existing UI issue, not fixed** (out of scope): `UI/Lore.NativeScript` treats Latin Extended Additional letters (ṭ, ḥ) as native script. For Arabic sentences whose transliteration contains them, `SplitReal` may show the transliteration as the large phrase. `Lang/Speech.cs` uses a corrected check, so the spoken text is right.
- **Voice identity** is still pending. The voice ids are configuration only. Nothing names, clones or imitates a person.

## Setup

See `backend/README.md` (Run it, and Live mode setup) and the voice section in `Unity/README.md`. In short: in `backend/`, run `npm install`, copy `.env.example` to `.env`, set `FNMW_GAME_TOKENS`, and run `npm run dev` for mock. In `Unity/UserSettings/fnmw-voice.json`, set `{ "url": "http://127.0.0.1:8787", "token": "…" }`. For live mode, add the keys, voice ids and session secret, plus provider-side quotas, and run the first calls yourself.

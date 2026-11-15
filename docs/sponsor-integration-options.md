# Google and ElevenLabs integration options

Reviewed 15 November 2026 against GitHub `main`, commit `d2acc00496325b7abfc011b909ea96be117f1768`, which matched the local checkout. These are proposals, not implemented features. Sponsor eligibility remains unverified because the hackathon rules were not supplied.

## Recommendation

Use Google's Gemini API to write short, state-aware monster dialogue and ElevenLabs text-to-speech to perform it. Add a ship-computer hint feature if time permits. Spoken pronunciation after the language quiz is a useful alternative if the team wants to emphasize education.

The Unity project is the active game. The root TypeScript project is an older prototype. Some details in `docs/PITCH.md` also predate the current code: `PuzzleBuilder` now requires three example sentences and four to eight answer tiles. Recommendations below use the current implementation.

## A monster that reacts to your mistakes

Example proposed moment: after a repeated word-order error, a voice behind the player says, "You keep putting the pieces in the wrong order. I can wait."

The existing game already supplies useful context. `GameDirector.Submit` checks an answer, updates the difficulty director, then raises `GameEvents.AnswerSubmitted`. That event contains correctness, response time, missed concept and puzzle difficulty. `AdaptiveDirector.Observe` updates the player model and records the change. Gemini can receive a small snapshot of these facts and write one short line in the monster's voice. ElevenLabs can turn that line into speech.

This makes the existing player model perceptible without opening its debug overlay. The model still learns locally; Gemini expresses its observations. Only mention repeated errors or previous runs if the supplied state supports them. Avoid claiming that Gemini controls the monster or that the game selects puzzles to target a weakness.

Integration points:

- `Unity/Assets/Scripts/Core/GameEvents.cs`: subscribe to answer and evolution events.
- `Unity/Assets/Scripts/Game/GameDirector.cs`: capture any needed submitted-tile context here; the answer event does not contain the selected tile IDs.
- `Unity/Assets/Scripts/Monster/Learning/AdaptiveDirector.cs`: read the existing learning state through a small snapshot contract.
- `Unity/Assets/Scripts/Audio/SpatialAudio.cs`: add playback for externally supplied clips using the monster's existing spatial source. `BeastSay` currently accepts a local synthesizer effect name, not arbitrary audio.
- `Unity/Assets/Scripts/Audio/AudioDirector.cs`: coordinate voice priority and music levels.

Use sparse speech. The monster already vocalizes periodically, increasingly often as it approaches. Generated dialogue needs a cooldown and arbitration with those sounds. Play the existing immediate lunge sound first; request dialogue asynchronously. Discard late responses after the player changes puzzle, restarts, escapes or dies. Cache speech, keep local fallback lines, and never wait for an API response before applying an answer or moving the monster.

ElevenLabs sound-effects generation also fits stage-specific chitters, breathing, growls and roars. Generate and audition these ahead of the demo, then play locally. Asset generation and live API use are different claims; check which the sponsor rules reward.

## A ship computer that helps you reason

Example proposed hint for a verb-final puzzle: "Compare where the action word appears in all three examples."

`LanguageEngine.Check` already diagnoses mistakes, and `Lang/Feedback.cs` already maps concepts to short hints. Gemini's useful addition would be connecting that diagnosis to the particular examples currently on screen, or explaining an approved rule more simply when the player remains stuck.

Start with a deliberate hint request or a repeated-failure trigger. Supply the displayed examples, missed concept and an authored explanation of the relevant rule. Request a short structured response. Validate its length and relevance, and fall back to the current hint when unavailable. Structured output checks the response's shape, not whether its grammar advice is true or whether it leaks a solution.

Use `UI/PuzzleScreen.cs` for the hint display, with a dedicated asynchronous update method. Calling `Reject` again would replay the rejection effects. The existing first-run tutorial in `Game/TutorialCoach.cs` should remain the introduction; contextual help supplements it.

Optional ElevenLabs speech gives the ship a calm radio voice. Give it priority over monster dialogue while explaining a rule. Keep hints and subtitles available without sound. Before the language-identification quiz, dialogue must not name the language or read the complete target answer.

## Hear the sentence you decoded

After the player answers the language quiz, pronounce the completed sentence and offer a replay button. This adds listening to a game currently centered on reading and arranging language tiles.

`LanguageEngine.GetReveal` already provides the phrase, translation, feature and language name. `UI/DecodeScreen.cs` has a `BuildPayoff` stage after the quiz that fits this interaction. Schedule the voice at that UI stage, not merely at `GamePhase.Reveal`, to preserve the language-identification question.

Add a clean speech-text field and language code. `RevealInfo.RealText` can contain native script followed by romanization in parentheses; sending it directly to speech synthesis could read the phrase twice. Have speakers check the actual generated examples, particularly Arabic vocalization and Mandarin tones. Gemini could adapt the post-quiz explanation using the authored feature and fact, but ElevenLabs pronunciation is already useful by itself.

## Keep the integration small

Use a small backend for Gemini and ElevenLabs requests, with credentials stored there, bounded output, caching and per-session request limits. Do not put provider keys in Unity assets, source control or a distributed game build. There is no existing provider integration in the inspected Unity scripts, so this requires network plumbing as well as UI/audio work.

Keep answer checking, puzzle generation, difficulty updates and movement local. `Lang/Framework/PuzzleBuilder.cs` validates answers against explicitly modeled grammar alternatives. Generating arbitrary puzzles with Gemini would require a separate validation project and would weaken that existing guarantee if simply substituted.

For a first demo, implement one contextual monster reaction with subtitles and reliable fallback audio. Demonstrate two different player outcomes producing different responses, and accurately identify cached versus live output. Add hints or pronunciation only after the first interaction works consistently.

## API sources

- [Gemini structured output](https://ai.google.dev/gemini-api/docs/structured-output): schema-constrained text responses.
- [Gemini API key guidance](https://ai.google.dev/gemini-api/docs/api-key): keep keys confidential and proxy production client requests through a backend.
- [ElevenLabs text-to-speech](https://elevenlabs.io/docs/overview/capabilities/text-to-speech): speech generation, streaming and model-dependent multilingual support.
- [ElevenLabs sound effects](https://elevenlabs.io/docs/overview/capabilities/sound-effects): sound generation from text descriptions.

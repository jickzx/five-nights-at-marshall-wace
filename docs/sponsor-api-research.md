# Sponsor API research

Checked official documentation on 15 November 2026. Research only; no API calls, audio generation, or game changes. Game context comes from [PITCH.md](PITCH.md), not a separate code audit. Recommendations below are design judgments, not vendor promises.

## Recommendation

Add ElevenLabs pronunciation at the language reveal first. The pitch explicitly identifies missing spoken pronunciation; hearing the phrase the player just solved closes that gap. For a meaningful Google integration, add a small Gemini feature that uses actual run context: a rule-specific hint or an escape/death debrief. If implementing both, Gemini can write a short debrief and ElevenLabs can speak it.

Keep puzzle generation, answer checking, and beast decisions in the existing deterministic/statistical systems. The pitch's strongest claims depend on those systems. No hackathon rules were supplied or verified, so neither sponsor-track eligibility nor whether generated assets qualify is established.

## Best ideas for this game

1. After the language-identification quiz, play the solved phrase in its real language, with a replay button. Capture the actual demo phrases from the current engine before preparing clips: the pitch's old answer sheet predates the sentence-puzzle changes on current main. Generate reviewed clips beforehand for a dependable demo; runtime generation can later cover other seeds. Prefer complete phrases in native script, including needed Arabic vowel marks, over isolated romanized tiles. Have a fluent speaker check the clips, especially Arabic vowels and Mandarin tones. This is a pronunciation reference, not proof of teaching accuracy.
2. Let the ship explain a mistake using the engine's diagnosed concept and an approved rule explanation. Gemini receives visible examples, the attempted tiles, concept ID, hint level, and permitted facts. It returns a short hint in JSON. Never send the hidden answer unless needed, and do not assume withholding it prevents inference. The safest version lets Gemini choose an approved hint ID; freer paraphrases need checks and a stock-hint fallback.
3. At escape or death, narrate a brief account of the languages encountered, mistakes, and observed beast behavior. Supply recorded events only. This gives Gemini and ElevenLabs a visible runtime role without putting an API request inside the chase. Avoid invented claims such as the beast selecting puzzles to target weak concepts, which the pitch says it does not do.
4. Generate a few alien-console and creature sound effects before the demo. Use them as accents over the existing reactive score. Runtime SFX generation has little gameplay value here and adds a dependency at the moment an effect must play.

These are proposed features, not statements about current implementation.

## ElevenLabs capabilities and limits

`POST /v1/text-to-speech/{voice_id}` produces audio from text. Its `/stream` variant returns audio progressively. Requests choose a model and voice. `language_code` can enforce supported languages, but the current reference explicitly excludes Multilingual v2. Up to three pronunciation dictionaries can be supplied. [Create speech](https://elevenlabs.io/docs/api-reference/text-to-speech/convert), [Stream speech](https://elevenlabs.io/docs/api-reference/text-to-speech/stream).

Flash v2.5 supports 32 languages, including all seven in the pitch, with a 40,000-character input limit. Multilingual v2 supports 29 languages and 10,000 characters. Current docs also list v4 and v4 Turbo with 90+ languages. Their integration differs: v4 uses Text to Dialogue; v4 Turbo uses its WebSocket. Do not assume either is a drop-in model change on the older TTS endpoint. Concurrency and queue priority depend on the account plan; budget calls accordingly. [Models and concurrency](https://elevenlabs.io/docs/overview/models).

Pronunciation dictionaries support aliases and phonemes. Current docs allow phoneme tags on `eleven_v4`, `eleven_v4_turbo`, `eleven_flash_v2`, and `eleven_v3`; non-English IPA/CMU requires v4 or v4 Turbo. Flash **v2.5** is absent from that phoneme list. Other models skip phoneme tags, so use aliases where appropriate and audition the result. Dictionaries are case-sensitive. These controls do not guarantee correct pronunciation of the game's transliterations. [Pronunciation dictionaries](https://elevenlabs.io/docs/eleven-api/guides/how-to/text-to-speech/pronunciation-dictionaries).

Use HTTP streaming when the complete text is ready. WebSockets accept incremental text, but chunk thresholds can delay short utterances; `auto_mode` avoids manual chunk scheduling. For Gemini JSON output, validate the complete response before speaking it instead of streaming unvalidated fragments into TTS. [Latency optimization](https://elevenlabs.io/docs/eleven-api/guides/how-to/best-practices/latency-optimization).

The current published median for Flash v2.5 and v4 Turbo is about 100 ms, measured server-side to first audio on WebSocket requests with warm voices. This excludes network time, Gemini generation, and playback buffering, and is not a per-request guarantee. Older promotional pages still quote 75 ms for Flash; use the dated methodology rather than promise that end-to-end delay. Measure request-to-audible-playback on the demo machine. [Latency methodology, updated 29 October 2026](https://elevenlabs.io/docs/eleven-api/concepts/latency-methodology).

Sound effects use `POST /v1/sound-generation`. The reference supports 0.5–30 seconds, `loop: true`, and `prompt_influence` from 0 to 1. It lists `eleven_text_to_sound_v2` and `eleven_text_to_sound_v3`, with v2 the default. Looping means a reusable clip, not a generation longer than the duration limit. No guaranteed real-time SFX latency was established. [Create sound effect](https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert).

## Gemini capabilities and limits

Structured output supports a subset of JSON Schema. Use a small object such as `concept_id`, `hint_id`, `hint_text`, and `narration_text`, with enums where possible. Valid JSON does not establish factual correctness or prevent answer leaks. Current docs also say string-length constraints such as `maxLength` are not enforced by constrained decoding; enforce limits in the application. The current Interactions API examples use `response_format` with `mime_type: application/json` and `schema`. Match the chosen endpoint's documentation rather than mix it with older SDK examples. [Structured outputs](https://ai.google.dev/gemini-api/docs/structured-output).

Choose a currently available Flash/Flash-Lite model with the required capabilities and confirm account access before implementation. Google's current catalog lists stable `gemini-3.5-flash-lite` and `gemini-3.6-flash`; older 2.5 examples are a poor new-project default because access and deprecation rules have changed. Rate limits depend on model and project tier, including requests and input tokens per minute and requests per day. They apply per project, not per key. [Model catalog](https://ai.google.dev/gemini-api/docs/models), [Rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).

Audio understanding can transcribe and translate uploaded speech and return structured results. WAV and MP3 are supported; inline requests have a 20 MB total limit, with the Files API for larger inputs. The docs point to Live API for live interactions and Cloud Speech-to-Text for dedicated streaming transcription. A possible extension is optional push-to-talk after a reveal, showing what the system heard. Transcription is not validated pronunciation or tone scoring, so do not make it decide whether the player survives. Defer this feature for the hackathon unless spoken input is central to the demo. [Audio understanding](https://ai.google.dev/gemini-api/docs/audio).

## What counts as integration, and a dependable demo

Audio generated during development and bundled with Unity is prerecorded generated content. It demonstrates an asset-production workflow, but there is no ElevenLabs request during play. A runtime integration sends current game text/context to the service and uses its response. A cache hit is cached playback; identify it honestly. The same distinction applies to Gemini-written text baked into the build.

For runtime work, keep provider keys in a backend and call it from Unity. Google explicitly warns that keys embedded in client builds can be extracted. [API key guidance](https://ai.google.dev/gemini-api/docs/api-key).

Use one optional request per relevant event, a short timeout, subtitles, stock hints, and reviewed audio fallbacks. Reject responses for a puzzle or run that has already ended. Cache audio by text, language, voice, model, and pronunciation-dictionary version. Record whether the demonstrated result was live, cached, or a fallback. Validate the full path on venue networking before depending on it.

Before claiming sponsor eligibility, read the actual event and track rules for accepted APIs, runtime requirements, generated-asset treatment, development dates, and submission evidence. This research establishes technical possibilities only.

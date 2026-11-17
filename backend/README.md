# Voice backend

A small Node service that sits between the Unity game and two providers, so provider keys never ship with the game:

- **OpenRouter** (`chat/completions`) writes short monster lines and ship hints from validated game facts.
- **ElevenLabs** (text to speech) speaks those lines, and the decoded sentence after the language quiz.

Everything is optional. With no backend configured, the game makes no network request and shows authored hints only. With this backend running and no provider keys, every feature reports itself off. `mock` mode serves canned text and a beeping placeholder tone so the whole pipeline can be exercised without keys. Mock output is labelled `mock` at every step, and the game labels it on screen. Mock output is not speech and does not demonstrate either provider.

Nothing here has been run against the live providers. See [What has not been verified](#what-has-not-been-verified).

## Run it

Needs Node 22.18 or later. Node runs the TypeScript sources directly through type stripping, so there is no build step.

```bash
cd backend
npm install          # dev tools only: typescript and @types/node. No runtime dependencies.
cp .env.example .env # then fill it in; .env is git-ignored
npm run dev          # mock mode on http://127.0.0.1:8787
npm start            # FNMW_PROVIDER_MODE from .env (default live)
npm run typecheck
npm test             # node:test; every provider call goes to an injected fake fetch
```

Set up the game token in two places with the same value:

1. `backend/.env`: `FNMW_GAME_TOKENS=<value>`. Generate one with `openssl rand -hex 24`.
2. `Unity/UserSettings/fnmw-voice.json` (git-ignored):

   ```json
   { "url": "http://127.0.0.1:8787", "token": "<the same value>" }
   ```

   You can also pass `-voiceUrl` and `-voiceToken` on the command line, or set `FNMW_VOICE_URL` and `FNMW_VOICE_TOKEN`. The game accepts `https://` anywhere, but plain `http://` only to `127.0.0.1` or `localhost`.

Unity's player setting "Allow downloads over HTTP" is off in this project. The Editor smoke test against `http://127.0.0.1` worked anyway: the Editor allows it. A standalone build may refuse plain HTTP, even to localhost. For a build, put the backend behind HTTPS, or change that setting yourself. The voice work does not change `ProjectSettings`.

## Live mode setup

1. OpenRouter: set `OPENROUTER_API_KEY` in the git-ignored `backend/.env` file. Missing or empty keys disable text generation. `FNMW_OPENROUTER_MODEL` defaults to `openrouter/free`; overrides must end in `:free`. Requests cap prompt and completion prices at zero.
2. ElevenLabs: create a key scoped to text to speech, and give it a credit quota. That quota is the hard stop on ElevenLabs spending.
3. Choose voices your account may use through the API: an original Voice Design voice, or a stock or library voice you are licensed for. Put their ids in `FNMW_VOICE_MONSTER`, `FNMW_VOICE_SHIP` and `FNMW_VOICE_NATIVE`. You can override the native voice per language with `FNMW_VOICE_NATIVE_JA` and so on. Do not clone or imitate a real person's voice. Nothing in the code uploads audio samples, and nothing in it names a voice.
4. Set `FNMW_SESSION_SECRET` to at least 32 bytes (`openssl rand -hex 32`). Live mode refuses to start without it.

A feature whose key or voice id is missing is reported off in the session reply. The monster needs only ElevenLabs and `FNMW_VOICE_MONSTER`: when OpenRouter is missing, out of credits, disabled by an auth error, or over its daily budget, the server writes the line itself (`src/monster-lines.ts`) from the trigger and the words of `meaning`, runs the same check on it, and ElevenLabs speaks it.

### Monster triggers

The game (chatty by default, `DialogueBrain` in `Unity/Assets/Scripts/Voice/Rules.cs`) asks for a line when a console opens (`puzzle_start`), after a solved sentence (`solved`), after every mistake (`first_mistake`, `mistake`, `streak`, `repeat_concept`), when the beast grows (`stage_up`), on the final chase, and after 30 s of quiet (`stalk`). Lines are at least 9 s apart and capped at 36 per run (80 per session on the server). The game then uses its offline behaviour for that feature: silence for the monster, authored text for hints, and no pronunciation.

## API

Every POST body is JSON of at most 8 KB, with exactly the documented keys. Unknown or missing keys get `400`. Every route except `/healthz` needs `Authorization: Bearer …`. Errors look like `{"error":{"code":"…","retry_after_ms":…}}`. Provider error bodies are never passed on.

| Route | Auth | Body | Reply |
|---|---|---|---|
| `GET /healthz` | none | – | `{"ok":true}` |
| `POST /v1/session` | game token | `{"proto":1,"build":"[A-Za-z0-9._-]{1,32}"}` | `{session, expires_in_s, mode, config_version, features:{monster,hint,hint_speech,pronounce}}` |
| `POST /v1/monster-line` | session | enums and small integers: `seq, trigger, stage, concept, concept_misses, wrong_streak, mistakes, solved, total, pace, distance, aggression`, plus optional `meaning` (the console sentence's English meaning, plain letters, at most 100 characters) | `{id, text, source, speech}` |
| `POST /v1/hint` | session | `seq, level (1-2), concept, attempts, target_meaning, examples[3]{meaning, words[1-12]}, tiles[4-12]` | `{id, text, kind: "generated"\|"guided", rows, source, speech}` |
| `POST /v1/pronounce` | session | `{seq, lang: ja\|fr\|es\|de\|tr\|zh\|ar, text}`, where text is at most 80 characters in that language's script | `{id, source, speech}` |
| `GET /v1/speech/{id}` | session that owns the id | – | raw PCM, `X-Fnmw-Audio: pcm_s16le;rate=24000;channels=1`, `X-Fnmw-Source` |

`speech` is `{id, sample_rate: 24000, version, source}` or `null`. `source` is `live`, `cache` or `mock`. `version` is an opaque hash of the mode, TTS model, voice, settings version, language and text. The game keys its own clip cache on it, so audio is never replayed under a different configuration.

### Async speech jobs

Text comes back first, and speech is generated in the background, so a hint's text appears at once. The job does not depend on the HTTP request that started it: once the text reply has gone, the job keeps running, and the same session fetches the audio with `GET /v1/speech/{id}` for up to ten minutes. That fetch waits up to 7 seconds for the job. A client that disconnects only stops its own wait, never the job, and finished audio goes into the cache. Jobs are bounded:

- at most 2 TTS calls in flight overall (a third is refused at once with `429`) and 2 per session;
- at most 256 job records, each expiring after 10 minutes;
- audio is held only in a 64 MB LRU cache with a 24-hour TTL (a job whose audio was evicted returns `404`);
- character reservations are refunded when the provider certainly billed nothing, such as an HTTP error or a refused connection. They are kept after a timeout, because the provider may still have charged.

OpenRouter calls, unlike TTS, are tied to their request: a client that disconnects aborts the upstream call.

## Hints: what is and is not guaranteed

The request carries only what the player can see: the target meaning, the three example rows, and the tiles. It does not carry the answer, the language name or code, native script, or rule text. The server adds the approved rule for the concept from `data/hint-facts.json`. An EditMode test in Unity fails if that file drifts from the game's `HintFacts`.

OpenRouter returns `{rows, hint}`. Checks happen in this order:

1. The server checks the hint text. It rejects language, country or script names, instruction words, digits other than row numbers, URLs and injection markers. It also rejects more than one puzzle word, and any puzzle word next to an ordering word ("first", "last", "before" and so on).
2. If the text fails, the server builds a guided hint instead, from approved text and the rows the model chose, for example: "Compare rows 1 and 3: look at where the action word sits." The model then contributes only the choice of rows.
3. The game's language engine runs the final check, because only the engine knows the answer (`IsHintSafe`). It applies the same checks, and also rejects any hint that mentions an English meaning of an answer word together with an ordering word. If the engine rejects a hint, the game shows its authored hint and never fetches that hint's audio.

Residual risk, stated plainly: these checks block the known ways a hint gives the answer away, but they cannot prove that no paraphrase does. Approved rule statements say things like "the action word can wait until the end of its clause". That narrows the answer by design: it is the rule the puzzle teaches. The per-concept statements in `hint-facts.json` were written for this game and still need a teacher's review. Validating the structure of the output does not make it true. Limiting characters does not prevent prompt injection: puzzle words do reach OpenRouter, which is why the output checks and the guided fallback exist.

## Limits and what they do not do

Defaults are in `src/config.ts`:

- per session: 40 monster lines, 40 hints, 40 pronunciations, 8,000 speech characters and 200 speech fetches;
- per IP, per 10 minutes: 3 sessions and 120 requests;
- sessions: server-chosen 128-bit ids, valid for two hours, signed with HMAC and bound to a salted hash of the client IP, with at most 500 alive at once;
- global, per UTC day: 2,000 OpenRouter calls and 60,000 speech characters;
- concurrency: 4 OpenRouter calls and 2 TTS calls, refused at once rather than queued;
- a provider that answers 401, 402 or 403 turns its features off for 10 minutes, so a bad key cannot cause a retry storm.

These limits exist in memory, in one process. A restart resets them. Several instances (serverless or autoscaled) would each keep their own counts, so the limits would not hold. Run a single long-lived process: the demo laptop on loopback, or one small VM. They are brakes, not spending caps. The only hard stops on money are the ElevenLabs key's credit quota and the free-only OpenRouter routing constraint.

The game token ships inside the game build and can be extracted. It is a speed bump. Someone who has it can get short generic speech in seven languages (at most 80 characters each) and hints for made-up puzzles, within the limits above.

Logs are allow-listed JSON fields: route, status, latency, provider status code, character and byte counts, error code, and a short salted hash of the session. They never contain bodies, generated text, tokens, keys, `Authorization` headers or raw IP addresses. A test checks this.

## What has not been verified

No key was available, and no test calls a provider. All of the following are unverified:

- OpenRouter: free-model availability, latency and generated text quality can vary. The client uses Bearer authentication, chat messages and JSON Schema output; local tests mock upstream responses.
- ElevenLabs: that `eleven_flash_v2_5` honours `language_code` for all seven languages, that `pcm_24000` comes back raw (no header) as expected, and that the chosen voice ids are usable through the API on the account's plan.
- The quality of generated lines and hints, and how often validation rejects them.
- Pronunciation correctness, which needs fluent listeners. Arabic words are stored without vowel marks, and Japanese kanji readings and Mandarin tones are known risks.
- Real latency and cost per run.

Run the first live calls yourself, watch the provider dashboards, and adjust the prompts in `src/prompts.ts` (bump `PROMPT_VERSION`) before loosening any check.

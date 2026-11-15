# Sponsor features: security review

Independent review of branch `feat/sponsor-voice-integrations`: the tracked diff and the new untracked files, compared with `d2acc00`. Scope: `backend/` and the Unity network, config and output boundaries (`Voice/`, `Lang/HintSafety.cs`, the UI text sinks). Game behaviour and Unity tests are covered by the parent review. Nothing was committed, pushed or deployed. No paid provider was called, and no credential was used or looked for. All reproductions use the backend modules with a local fake `fetch`.

The parent was editing `Voice/VoiceCore.cs` and `UI/DecodeScreen.cs` during this review (it added a quiz-answer lock). Line numbers below are from the files as read on 2026-11-15.

## Verdict

No secret-exposure, auth-bypass or injection defect was found. The key, logging, auth, schema and body-bound claims in the implementation report hold up against the code. Two problems block a deployment beyond loopback. One answer-leak gap in the hint checks should be fixed before live hints ship. The rest is optional hardening.

| # | Severity | Finding | Blocks |
|---|---|---|---|
| 1 | Medium-High | Behind the HTTPS proxy that a build needs, every player shares one IP bucket | Any non-loopback deployment |
| 2 | Medium | Hint checks ignore short or English-looking puzzle words, so they can be put in order | Live hints |
| 3 | Medium | One token holder fills the global speech-job table with free cache hits, and speech stops for everyone | Any shared deployment |
| 4 | Low-Medium | The session table (500) can be filled from many IPs, or from one IPv6 /64 | Hardening |
| 5 | Low | Gemini's invalid-key reply (HTTP 400) is not treated as an auth failure | Hardening |
| 6 | Low | Hint speech is paid for before the engine's safety check | Hardening |
| 7 | Info | Other branches bake a Gemini key into the client build | Merge risk |
| 8 | Info | The report's pronunciation residual is out of date: a lock now exists | Docs |

## Findings

### 1. Behind a TLS proxy, every client shares one IP (Medium-High; blocks remote deployment)

**Trace.**

- `backend/src/server.ts:62`: `const ip = req.socket.remoteAddress`. That IP keys `admit` (`service.ts:114-117`, 120 requests per 10 minutes) and the session limit (`service.ts:123-124`, 3 sessions per 10 minutes). It is also bound into every session token (`auth.ts:47-58`).
- The Node server speaks plain HTTP only (`createServer` from `node:http`).
- Unity accepts plain `http://` only to loopback (`Voice/VoiceConfig.cs:38-41`). The README says to put the backend behind HTTPS for builds, on "one small VM".

So any remote deployment means a reverse proxy (Caddy, nginx, a cloud load balancer). Behind it, `remoteAddress` is the proxy's address for every player.

**Effect.** The whole player base gets 3 new sessions and 120 requests per 10 minutes. A fourth player in that window gets `rate_limited` on `/v1/session`, and the game goes offline for the scene. One player who mashes H uses up everyone's request budget. This is an availability defect, not a cost leak. The daily budgets still cap spend.

**Preconditions.** Any deployment that is not the demo laptop on loopback.

**Fix.** Pick one:

- Add an opt-in `FNMW_TRUSTED_PROXIES` (explicit CIDRs). Only when the socket peer is in that list, take the right-most `X-Forwarded-For` hop that is not a trusted proxy. Never trust the header otherwise.
- Or terminate TLS in Node (`node:https` with cert paths from env).

Either way, bucket IPv6 by /64 (see #4). Re-check that IP binding of session tokens is still wanted for mobile clients.

### 2. Hint checks let short or English-looking puzzle words be ordered (Medium; fix before live hints)

**Trace.** The server check (`backend/src/textcheck.ts:120-124`) and the engine's final check (`Unity/Assets/Scripts/Lang/HintSafety.cs:143-159`) share the same exemption. An unquoted puzzle word of 1 or 2 letters, or one on the `English` list (`die`, `hat`, `ist`, `con`, `les`, `des`, `son`, …), is never counted as a reference. The "ordering" rule then fires only when `refs >= 1`.

The engine's extra gloss rule does not cover this gap. It needs a lowercase gloss part of 4 or more letters (`HintSafety.cs:154-155`), and Japanese particles have glosses like `SUBJ` (`Lang/Languages/Japanese.cs:102`). Real tiles hit the gap: `ga`, `o`, `wa` (Japanese particles), `el`, `la`, `es`, `un` (Spanish), `die`, `ist`, `hat` (German).

**Reproduction.** Script: `scratchpad/sec/repro.ts`. Server `checkHint` with the real puzzle-word shapes:

```
"Start with wa, then ga, then o comes last."                           { ok: true }
"Put wa after the first word, then o just before the verb at the end."  { ok: true }
"Row 2: o follows the thing eaten and wa follows the eater."            { ok: true }
es: "Begin with el, then es comes before the colour word at the end."   { ok: true }
de: "Start with die, then ist, and put the colour last."                { ok: true }
```

The C# code path is the same line for line, and none of these hints trips its gloss rule. So, by trace, `IsHintSafe` also returns true. Unity was not run, because the parent is using it.

**Effect.** For particle, article and agreement puzzles, the answer is mostly the order of these short tiles. A generated hint like the first one gives away most of the answer. Two things must happen at once: the model has to disobey the system prompt, and the check has to miss it. The inputs are generated by the game, so a third party cannot steer this. It depends on the model misbehaving, which the checks exist to catch.

**Fix.** In `HintSafety.Check`, which knows the answer: if any position word appears, reject when any answer-tile token, of any length and whether or not it is on the English list, appears as a standalone token. Do the same on the server for every puzzle word whenever a position word is present. More false rejections are fine, because a rejected hint falls back to approved text. Add the five strings above as regression tests on both sides.

If that is too blunt, a simpler and safer design is to allow position words only inside the stripped approved facts. Generated text could then point at rows but never order anything.

### 3. The global speech-job table can be exhausted with free cache hits (Medium)

**Trace.**

- `backend/src/service.ts:235` refuses when `#jobs.size >= maxSpeechJobs` (256). That table is shared by all sessions.
- A cache hit still creates a job record (`service.ts:243-247`) and costs no characters.
- `pronounce` takes any text in valid script (`validate.ts:133-141`), with 40 per session.
- Jobs live for 10 minutes (`speechJobTtlS`). There is no cap per session on job records.

**Reproduction.** Script: `scratchpad/sec/repro.ts`. Seven sessions from three IPs, within the 3-per-IP limit, each send `pronounce {lang:"es", text:"hola"}` repeatedly:

```
jobs created by attacker: 256 jobCount 256 daily chars used 4
victim pronounce -> rate_limited 5000
victim after 9 min -> rate_limited
```

Monster and hint speech go through `#trySpeech`, which turns this into `speech: null`, so every player loses all voice. The attacker can repeat it every 10 minutes for 4 billed characters.

**Preconditions.** The extracted game token (accepted) and 3 IPs, or one IP once #1 applies.

**Fix.**

- Cap live job records per session (for example 8). Evict the session's own oldest first, never other sessions'.
- Key job records by `(sid, contentKey)`, so repeat requests reuse one id.
- Optionally drop a job once its audio has been fetched.

### 4. The session table can be filled from many addresses (Low-Medium, hardening)

`service.ts:126-128` caps live sessions at 500, each valid for 2 hours. The limit of 3 per 10 minutes is per exact IP string, so 167 addresses, or a single IPv6 /64, fill the table. New players then get `rate_limited` for up to 2 hours. Money is not at risk, because the daily budgets hold.

**Fix:** bucket IPv6 by /64, and add a global session-creation rate. Optionally use a shorter TTL with renewal.

### 5. A bad Gemini key (HTTP 400) does not trip the provider switch (Low)

`backend/src/providers/common.ts:22-23` maps only 401, 402 and 403 to `auth`. Gemini usually reports an invalid key as `400 INVALID_ARGUMENT` (reason `API_KEY_INVALID`). That reply is mapped to `invalid`, then to `invalid_generation`, and the feature stays advertised.

**Reproduction** (`scratchpad/sec/repro2.ts`): 5 hints against a fake that always returns 400 gave 5 provider calls, 5 daily-budget units used, and `features().hint` still true. The key did not appear in the logs. The client breaker contains the impact, and failed calls are not billed.

**Fix:** if Gemini answers 400 several times in a row, trip `geminiSwitch`. Or classify by the error `reason` field without logging the body.

### 6. Hint speech is generated before the engine's check (Low)

`service.ts:387` starts TTS for every hint the server accepts. The engine can still reject that hint (`VoiceCore.RequestHint`). When it does, the audio is never fetched or played, which is correct, but the characters were spent. Expect this to happen whenever #2's stricter check rejects more.

**Fix (optional):** add a second step, such as `POST /v1/hint/{id}/speech`, that the client calls only after `IsHintSafe` passes.

### 7. Other branches put a Gemini key in the client (Info, merge risk)

`feat/gemini-scaffold` and `feat/gemini-all` contain `Core/Ai/AiConfig.cs`. On those branches the game calls `generativelanguage.googleapis.com` directly, with a key loaded through `Resources.Load<TextAsset>`, which ends up in the build. This branch has no provider host or key under `Unity/Assets` (checked with grep). Merging those branches would undo the "keys stay in `backend/`" property. Before merging, route them through this backend or drop them. The key file itself was not opened.

### 8. Report drift on pronunciation (Info)

`docs/sponsor-implementation-report.md` still says that "nothing is spoken before the answer" depends only on the UI. The current code gates it:

- `RequestPronunciation` returns unless `_quizAnswerLocked` is set (`VoiceCore.cs:560`).
- The late-arrival path also requires the lock (`VoiceCore.cs:536`).
- The lock is set only by `NotifyQuizAnswerLocked`, which `DecodeScreen.Choose` calls after the answer is checked (`DecodeScreen.cs:259`).
- The lock is cleared on leaving Quiz, on a new puzzle, at the next reveal and at the end of a run.

Update the report, and the Core changes list, which needs `IVoiceDirector.NotifyQuizAnswerLocked`.

## Checks performed that found no defect

- **Secrets.** Provider keys are read only in `backend/src/config.ts` and sent only as headers: `x-goog-api-key` (`gemini.ts:31`) and `xi-api-key` (`elevenlabs.ts:36`). They never appear in a URL. Only the validated model or voice id is interpolated into a path, through `encodeURIComponent`.
  - `ConfigError` messages name variables, not values. `internal_error` logs no message.
  - The logger drops any string field that is not a short label (`log.ts:23-37`).
  - `repro2.ts` confirmed no key in the logs.
  - Unity logs never include the token or the URL (three `Debug.Log` sites checked). `Unity/UserSettings/` and `backend/.env` are git-ignored (`git check-ignore`).
- **Auth.**
  - The game token is compared in constant time over SHA-256 digests (`auth.ts:11-17`).
  - Session tokens are HMAC-signed, carry a server-chosen 128-bit id and are bound to an IP tag. They are checked with an exact regex, `timingSafeEqual`, expiry in the token and expiry in the server map.
  - Speech ids are 128-bit random, and `fetchSpeech` checks `job.sid === s.sid` (ownership). A replayed token works only until it expires, from the same IP, within per-session quotas. That is acceptable for a bearer design.
- **Request bounds.**
  - Content-Length and streamed size are capped at 8 KB, with a strict content type and fatal UTF-8 decoding.
  - Schemas have exact keys and bound every value: enums, integers, regexes with character counts. A `puzzle_key` field is rejected.
  - `requestTimeout`, `headersTimeout` and `maxHeadersCount` are set.
- **Response bounds.** Provider bodies are read through `readLimited`: Gemini at 64 KB and TTS at 1.5 MB, with Content-Length checked first and a cancel on overflow. `finishReason` must be `STOP`, and block reasons count as invalid. Parsed output has an exact shape (`service.ts:333`, `374-378`). PCM must be non-empty, even-length and within its cap, checked on both server and client (`Protocol.Pcm`).
- **Redirects and SSRF.** Both provider calls use `redirect: 'error'` against constant origins (`common.ts:57`). Unity sets `redirectLimit = 0`, paths must start with `/v1/`, speech ids must match 32 hex characters, and the URL validator rejects userinfo, path, query and fragment, and allows http only to loopback (`VoiceConfig.cs:31-44`).
- **Budgets and refunds.**
  - Gemini reserves 2 units, or 1 when only 1 is left, and refunds unused calls in `finally`.
  - TTS reserves characters before the gate and refunds if the gate refuses. Characters are refunded only for `unbilled` errors (an HTTP error or no connection) and kept after a timeout mid-body.
  - `Gate.leave` and `activeTts--` run in `finally`.
  - In-flight deduplication per content key means concurrent identical requests bill once.
  - No awaits sit between the check and the reservation, so the single-threaded event loop rules out races there.
- **Async lifetime.** TTS jobs are deliberately detached from the request and bounded by a deadline (8 s, which covers the body read). `fetchSpeech` waits at most 7 s, and a disconnect ends only the wait. Every rejection on a job promise is caught, so there are no unhandled rejections. In Unity, per-feature tokens, a static epoch, `CancelFeature` and `Dispose` (on `OnDestroy`) drop late results. The client caps requests in flight at 6.
- **Text sinks.** `Kit.Label` sets `supportRichText = false` (`UI/Kit.cs:232`). The server and the client both restrict hint and monster characters to sets without `<` or `>`. The one `supportRichText = true` site (`DecodeScreen.cs:189`) predates this branch and does not receive generated text.
- **Mock and live separation.**
  - Mock mode must be chosen explicitly, and `FNMW_MOCK_FAULT` is refused in live mode.
  - `configVersion` covers mode, models, voices and prompt, facts and settings versions.
  - Speech and hint cache keys include the mode. Cached mock audio keeps `mock: true`, and the client forces Mock labelling when the session is mock.
- **Config.**
  - Integer limits reject NaN, non-integers and out-of-range values.
  - Ids and tokens are checked with regexes. Live mode refuses to start without a 32-byte secret.
  - Unity clamps volume, including NaN, and caps the config file at 4 KB.
- **Dependencies.** There are no runtime dependencies, only dev dependencies (`typescript`, `@types/node`). `npm audit --omit=dev` found 0 vulnerabilities.
- **Existing suites.** `npm test` gives 34/34 and `tsc` is clean. Both were run locally with a fake fetch. Neither result was relied on as evidence for the findings above.

## Accepted limitations (documented, not defects)

- **The game token is shared and can be extracted.** Whoever has it gets bounded generic TTS (7 languages, at most 80 characters) and hints for made-up puzzles, within the daily caps. Findings #3 and #4 are about how cheaply that same holder can deny service to others, which goes beyond the accepted cost exposure.
- **Limits live in memory, in one process.** A restart resets them and several instances would each count separately. The README says so, and names provider-side quotas as the only hard stops.
- **Live provider behaviour is unverified.** That covers Gemini header and schema support, ElevenLabs `language_code` and raw `pcm_24000`, voice access, quality, latency and cost.
- **No voice identity has been selected.** Voice ids are configuration only. Nothing names, clones or imitates a person.

## Residual risk

- **Hint leakage beyond #2.** No string check can prove that a paraphrase does not reveal the answer. Level-2 approved facts narrow the answer by design, and they still need a teacher's review. Puzzle words reach Gemini, so injection-shaped tile text is possible. The checks and the guided fallback limit what comes out; they do not stop what goes in. If #2's fix makes generated hints rare, the authored or guided path, where the model only picks rows, is an acceptable final design.
- **Monster lines and concept names.** The monster prompt receives the missed concept (`vowel-harmony`, `measure-word`, `consonant-root`, `particles`), which correlates with the language. `checkMonsterLine` blocks language names, but not a line like "your vowels never agree", heard before the language quiz. The impact is low, because approved hint facts already show the concept, but it is a pre-quiz hint channel. Sending `concept: null` to the monster prompt would close it at little cost to flavour.
- **Pronunciation before the payoff.** This is now gated by an explicit lock (#8), not by UI convention. The remaining dependency is that `NotifyQuizAnswerLocked` is called only after an answer is chosen, and today only `DecodeScreen.Choose` calls it.
- **The current code is a moving target.** The parent changed `VoiceCore.cs` and `DecodeScreen.cs` during this review. Re-check #8 against the final diff.

## Reproduction scripts

Kept outside the repo, in `/private/tmp/claude-501/-Volumes-DeveloperStorage-GitHub-five-nights-at-marshall-wace/de6ca8f4-6750-4976-a1dc-96c1bb76055d/scratchpad/sec/`:

- `repro.ts`: findings #2 (server `checkHint`) and #3 (job-table exhaustion).
- `repro2.ts`: finding #5 (Gemini 400 handling) and the key-in-logs check.

Run each with `node <file>` on Node 22.18 or later. They import `backend/src` directly and use only a local fake `fetch`.

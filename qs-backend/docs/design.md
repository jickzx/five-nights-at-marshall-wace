# QS backend: research and design decisions

Written 15 November 2026. Facts below were checked against the vendors' current documentation on that date; each has its source. Where a doc contradicted the original brief, the doc won and the difference is called out (**Brief vs docs**).

## 1. Vercel: Python / FastAPI runtime

**Found**
- FastAPI deploys with zero configuration: Vercel looks for a `FastAPI` instance named `app` in `app.py`, `index.py`, `server.py`, `main.py`, `wsgi.py` or `asgi.py` at the project root (or under `src/`, `app/`). The whole app becomes one Function. `vercel.json` is optional and keyed on the resolved entrypoint for per-function settings. Python 3.12 (default), 3.13, 3.14; dependencies from `pyproject.toml` (with or without `uv.lock`) or `requirements.txt`. ([FastAPI on Vercel](https://vercel.com/docs/frameworks/backend/fastapi), [Python runtime](https://vercel.com/docs/functions/runtimes/python))
- Limits with Fluid compute (the default): max duration 300 s on Hobby, up to 800 s on Pro; **request and response body 4.5 MB** (413 above); 2 GB memory default; Python bundle up to 500 MB. ([Limits](https://vercel.com/docs/functions/limitations))
- Streaming responses are supported for Python.
- **Brief vs docs:** the brief assumed Vercel Python functions cannot hold WebSockets. Vercel now documents WebSocket support for Functions, including Python ASGI apps. But each connection is pinned to one instance and closes at the function's max duration, fan-out across instances needs an external store such as Redis, and the function is billed for the whole time a connection is open. ([WebSockets](https://vercel.com/docs/functions/websockets); the page links both "public beta" and "generally available" changelog entries, so the exact status is unclear.)

**Decided**
- Entry point `qs-backend/main.py` (zero-config detection); `vercel.json` only sets `maxDuration: 60` and excludes tests, scripts, migrations and docs from the bundle. Deploy with the Vercel project's Root Directory set to `qs-backend`.
- Ingest requests are capped at **1 MB** by the API (well under 4.5 MB). One 1 s window at 90 Hz with head and both hands is about 60 KB of JSON (about 9 KB in the 72 Hz simulation without controller rotations). Duration limits do not matter for ingest, but latency does, because the headset sends every 0.5 s: a window-only batch makes about five sequential Supabase calls (token, session, consent, insert, one record-status read) plus the broadcast. All of them reuse one keep-alive HTTP client per warm function, so only the first request pays TLS setup; with the function and the database in the same region this should fit inside the hop, but it has **not been measured on real Vercel and Supabase**. The Unity sender queues up to four batches (about 2 s) while one is in flight, so a slow request delays windows rather than losing them. Put the Vercel function region next to the Supabase region.
- **Live updates do not go through Vercel WebSockets** even though they now exist: dashboards would need Redis for fan-out, and we would pay for open connections on the function. Supabase Realtime already does fan-out and authorization (next section).

## 2. Supabase Realtime: which mechanism for live updates

**Found**
- Supabase calls **Broadcast** "the recommended method for scalability and security"; **Postgres Changes** "does not scale as well". Postgres Changes processes changes on a single thread to keep order, authorizes every change against every subscriber, tops out around 3,000 messages/s with RLS on, and larger compute does not raise that. ([Subscribing to database changes](https://supabase.com/docs/guides/realtime/subscribing-to-database-changes), [Postgres Changes](https://supabase.com/docs/guides/realtime/postgres-changes))
- Broadcast authorization uses RLS policies on `realtime.messages`, checked when a client joins a **private** channel (`config: { private: true }`), with `realtime.topic()` giving the channel name. ([Realtime authorization](https://supabase.com/docs/guides/realtime/authorization))
- A server can broadcast over HTTP without holding a WebSocket: `POST /realtime/v1/api/broadcast` with a `messages` array (topic, event, payload, private). A trigger can also call `realtime.send()`. ([Broadcast](https://supabase.com/docs/guides/realtime/broadcast))
- Quotas (Free / Pro / Pro without spend cap): 200 / 500 / 10,000 concurrent connections; 100 / 500 / 2,500 messages per second; Broadcast payload 256 KB / 3,000 KB. ([Limits](https://supabase.com/docs/guides/realtime/limits))
- **Brief vs docs:** Supabase is deprecating the legacy `anon` / `service_role` JWT keys by the end of 2026 in favour of `sb_publishable_…` and `sb_secret_…` keys. Everything here uses the new names and still accepts the legacy ones. ([API keys](https://supabase.com/docs/guides/api/api-keys))

**Decided**
- **API writes rows, then broadcasts over HTTP to private channels.** After each ingest request the API makes one batched `POST /realtime/v1/api/broadcast` with the server secret key:
  - `qs:session:<session_id>` with events `window` (compact window features and ratios), `event` (game event), `record` (updated QS record), `baseline`;
  - `qs:employer:<employer_id>` with event `session` (created, live, ended).
- The `realtime.messages` policy in the migration lets a signed-in employer join only topics for their own sessions and employer. No one but the server can send.
- Why not Postgres Changes: per-subscriber RLS on a single thread, and it would push whole rows (including fields the dashboard does not need). Why not a `realtime.send()` trigger: it would add work inside every insert transaction, and the HTTP call lets the API shape a small payload and is easy to test. The cost is that a broadcast can be lost if that call fails; the dashboard recovers on reconnect with `GET /v1/sessions/{id}/windows?after_seq=…`.
- Budget: each session sends 2 windows/s plus events, so about 2–3 messages/s per viewer. On capped Pro (500 msg/s) that is roughly 50 sessions with 3–4 viewers each. Watch this before a large live demo.

**Conclusion on feasibility:** FastAPI on Vercel plus Supabase Realtime meets the live-dashboard requirement. No stop condition applies.

## 3. What the Quest exposes through Unity XR / OpenXR

**Found**
- Through OpenXR, the Oculus Touch profile gives grip (`devicePose`) and aim (`pointerPose`) poses, with `isTracked`, `trackingState`, `devicePosition`, `deviceRotation`. The profile page does not list velocity. The HMD exposes `centerEyePosition/Rotation`, which on OpenXR equal the device pose. ([Oculus Touch profile](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.18/manual/features/oculustouchcontrollerprofile.html), [OpenXR input](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.18/manual/input.html))
- `UnityEngine.XR.CommonUsages` has `devicePosition`, `deviceRotation`, `deviceVelocity`, `deviceAngularVelocity`, `isTracked`. OpenXR's `XrSpaceVelocity` is core, but the runtime fills it only "if observed or can be calculated" and sets a validity bit. Whether Meta's runtime fills controller velocity in practice is **not confirmed**. ([CommonUsages](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/xr/commonusages), [XrSpaceVelocity](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSpaceVelocity.html))
- Apps default to 72 Hz; Quest 2 also offers 80, 90, 120 Hz, and the OS can drop back to 72 under heat. Poses are sampled once per frame (plus an optional before-render refresh). ([Meta: display frequency](https://developers.meta.com/horizon/documentation/unity/unity-set-disp-freq/), [TrackedPoseDriver](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/api/UnityEngine.InputSystem.XR.TrackedPoseDriver.html))
- Positions are metres relative to the tracking origin.

**Decided**
- The sender records **one pose per rendered frame** for head and both controllers, with tracking flags, plus velocity when the runtime reports it. Expect 72 Hz unless the game raises the refresh rate; the backend measures the actual rate from timestamps and resamples, so it does not care.
- The backend **derives velocity from pose** and uses reported velocity only when there is no position. Reported velocity may be missing, filtered or predicted, and deriving it keeps every device comparable. Tremor (up to 12 Hz) is well below the 36 Hz Nyquist limit of 72 Hz sampling.
- Untracked samples are dropped; a device tracked for less than 60 % of a window gets `null` features rather than guesses.

## 4. Storage: raw, downsampled, or features only

| Option | Rows | Size (rough) | Notes |
|---|---|---|---|
| Raw samples | 72–90 per second | ~15 KB/s of JSONB, ~55 MB per session-hour | Exhausts Supabase Free (500 MB) in about 9 session-hours. |
| Downsampled to 30 Hz | 30/s | ~20 MB/hour | Loses the top of the tremor band (Nyquist 15 Hz). |
| **Window features (chosen)** | 2/s | ~1 KB/row, **~7 MB per session-hour** | About 70 session-hours on Free. |

**Decided: store window features only and discard raw samples after computing them.** Beyond cost, this is data minimisation: raw head and hand motion is known to identify people with high accuracy across sessions, so not keeping it reduces what could leak and what a deletion request has to chase. The trade-off is that features cannot be recomputed later with a different algorithm. If the team needs that for research, add an opt-in, separately consented raw store with a short retention window; it is deliberately not in this version.

## 5. Gemini: "the Gemini decisions model"

**Found**
- A Google product with that name now exists: the **Gemini Decisions API** (`gemini-decision-1`, announced 11 November 2026, `POST /v1beta/decisions`). It answers closed-set questions (yes/no, choice among 2–255 options, scores) with probabilities and confidence, and produces **no free text or custom JSON**; its own docs point to structured output for schema-based extraction. ([Decisions API](https://aistudio.google.com/docs/decisions-api), [announcement](https://blog.google/innovation-and-ai/technology/developers-tools/gemini-decisions-api/))
- Structured output: `generationConfig.responseMimeType: "application/json"` with a JSON Schema in `responseJsonSchema` (supports `enum`, `required`, `minItems`, `maxItems`, `additionalProperties`; ignores `minLength`/`maxLength`). Use structured output, not function calling, when the model's final answer is the report. ([Structured output](https://aistudio.google.com/docs/structured-output))
- Google now recommends the newer Interactions API, but `generateContent` (`POST /v1beta/models/{model}:generateContent`, header `x-goog-api-key`) remains supported. ([Get started](https://aistudio.google.com/docs/get-started?generate-content=true), [Text generation](https://aistudio.google.com/docs/text-generation))
- Current stable models include `gemini-3.8-flash`, `gemini-3.6-flash` and `gemini-3.5-flash-lite`. ([Models](https://aistudio.google.com/docs/models))
- Errors: retry 408/429/5xx with backoff; do not retry 400, **402** (prepaid credits depleted) or 403. ([Troubleshooting](https://aistudio.google.com/docs/troubleshooting))

**Decided**
- **Not the Decisions API.** Its output is exactly what QS must not produce: a probability or score attached to a closed question ("suitable: yes/no"). The team's intent (an employer-readable account) needs text tied to evidence.
- **Structured output on `generateContent`**, the same endpoint, header and default model (`gemini-3.5-flash-lite`, which the team's key listed as supporting `generateContent`) as the Unity Gemini scaffold (PR #19, `Core/Ai/GeminiClient.cs`), called over plain HTTP with `httpx` (no SDK dependency). `GEMINI_MODEL` overrides it, e.g. `gemini-3.8-flash` for a stronger model. One difference: the scaffold sends its schema as `responseSchema` (the older OpenAPI subset); this service uses `responseJsonSchema`, which the current docs describe and which supports `additionalProperties`.
- The schema has two arrays, `observations` and `data_quality`. Each item must carry a `statement`, the `question_keys` it rests on (enum: this session's keys plus `session`), the `features` it rests on (enum of QS field names) and a `direction` relative to baseline. There is **no field for a score, ranking or decision**.
- After the reply, `check_summary` drops any item that cites an unknown question or feature or uses verdict, mental-state or diagnostic language (hire, reject, suitable, nervous, anxious, confident, honest, disorder, score, …). The **evidence values shown next to each statement are looked up from the stored records by the server**, not copied from the model. Three fixed limitations are always appended by the server (QS is movement not mental state; 4–12 Hz motion has ordinary medical causes; this is not a hiring decision).
- **Credits are spent once per summary:** `POST …/summary` returns the stored summary when there is one; `?refresh=true` calls Gemini again only if the stored one is over two minutes old.
- **Failure is normal, not an error:** no key, 402, 429, other HTTP errors, timeouts and unparseable replies return HTTP 200 with `status: "unavailable"` and a reason. Nothing else in the service touches Gemini.
- Only the candidate's pseudonym, the question prompts and the QS records are sent. Never identities.

## 6. Privacy and access, built in

- **Consent before data.** `GET /v1/consent` serves the statement and its SHA-256; the headset posts the same version and hash after the candidate agrees. Ingest returns 403 for a session with no unwithdrawn consent. Withdrawing ends the session and revokes its token.
- **Pseudonymous candidates.** `qs_candidates` holds a pseudonym (`C-7F3A21`) and nothing else. Real names and e-mails go in `qs_candidate_identities`, readable only by employer **owners**, never joined into reads, broadcasts or Gemini prompts.
- **RLS on every table**, scoped by `employer_id`; composite foreign keys `(id, employer_id)` stop one employer attaching rows to another's records. Employer API calls forward the employer's own JWT to PostgREST, so Postgres enforces this, not just the API. Tested on a real local Postgres in `tests/test_sql_rls.py`.
- **Server-only secrets.** The secret key is used only for ingest and derived data; ingest tokens are stored as SHA-256 hashes in a table no policy exposes.
- **Delete.** `DELETE /v1/candidates/{id}` (owners only) runs `qs_delete_candidate()` in one transaction: identity, sessions, tokens, consents, windows, events, records, summaries, and the candidate's broadcast copies in `realtime.messages` (Supabase also expires those after 3 days; if the role may not delete them the receipt says `realtime_messages: -1` and the rest is still deleted). There is no DELETE policy on candidates, so this function is the only delete path.
- **Neutral language.** Field names and docs describe what is measured. The frontend has a test that fails if verdict or diagnostic words appear in its labels.

## 7. What is not done here

- No consent screen in the game yet: `QsTelemetry` exposes `ConsentStatement` and `Consent(bool)`, and the UI needs to show the statement and call it (`Unity/Docs/qs/README.md`).
- No rate limiting beyond request size and per-batch caps; add Vercel Firewall rules or a per-token limit before public use.
- No retention policy: add a scheduled deletion (for example `pg_cron`) once the team decides how long data is kept.
- Thresholds are reasoned defaults, not validated on real Quest recordings.

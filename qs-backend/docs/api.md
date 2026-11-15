# QS API

Base URL: the Vercel deployment of `qs-backend/` (locally `http://localhost:8000`). Interactive docs at `/docs` (FastAPI's OpenAPI UI).

Two kinds of caller:

| Caller | Credential | Header |
|---|---|---|
| Employer dashboard | Supabase Auth access token of a signed-in employer user | `Authorization: Bearer <jwt>` |
| Headset | Per-session ingest token from `POST /v1/sessions` (shown once) | `X-QS-Ingest-Token: qsi_…` |

Employer reads and writes are executed **as that employer** against Postgres, so row-level security decides what exists for them. Anything they cannot see returns 404.

## Public

### `GET /healthz`
`{"ok": true, "store": "supabase", "gemini": false}`

### `GET /v1/consent`
The consent statement the headset must show, its version and its SHA-256.
```json
{"consent_version": "qs-consent-2026-11", "statement": "QS motion data consent …", "statement_sha256": "9f…"}
```

## Headset

### `POST /v1/ingest/{session_id}/consent` → 201
```json
{"consent_version": "qs-consent-2026-11", "statement_sha256": "9f…", "accepted": true,
 "client": {"device": "Quest 3", "app": "0.1"}}
```
409 if the version or hash is not the current statement's.

### `POST /v1/ingest/{session_id}/consent/withdraw` → 200
Withdraws consent, ends the session and revokes the token.

### `POST /v1/ingest/{session_id}` → 202
One motion window and/or game events. Sent every 0.5 s by the Unity sender.

```json
{
  "seq": 42,
  "question_key": "p3",
  "t_start": 21.0, "t_end": 22.0,
  "rate_hz": 72,
  "samples": {
    "t": [21.0042, 21.0181, "…"],
    "head":  {"pos": [[0.01, 1.62, 0.0], "…"], "rot": [[0.0, 0.08, 0.0, 0.997], "…"], "tracked": [true, "…"]},
    "left":  {"pos": ["…"], "rot": ["…"], "vel": ["…"], "tracked": ["…"]},
    "right": {"pos": ["…"], "rot": ["…"], "tracked": ["…"]}
  },
  "events": [
    {"id": "1b4e…-uuid", "t": 21.4, "type": "answer_submitted", "question_key": "p3", "payload": {"correct": false}}
  ]
}
```

| Field | Rules |
|---|---|
| `seq` | Window number, ≥0, required with `samples`. A repeated `seq` is ignored (safe retries). |
| `question_key` | `^[A-Za-z0-9_.:-]{1,64}$`, the question on screen at the window's end, or null. |
| `samples.t` | 1–600 timestamps in seconds; every device array must have the same length. |
| `head/left/right` | Each optional. `pos` (m), `rot` (x, y, z, w), `vel` (m/s, optional), `angvel` (rad/s, optional, accepted but unused), `tracked`. |
| `events` | ≤200. `id` is a client UUID (repeats ignored). `type` is one of the list below. `payload` ≤2 KB JSON. |

Event types: `session_start`, `calibration_start`, `calibration_end`, `question_shown`, `tile_placed`, `tile_removed`, `answer_cleared`, `answer_submitted` (`payload.correct`; `payload.final: true` when the answer cannot be retried, which closes the question even if wrong), `answer_wrong_fx`, `hint_used`, `monster_appeared`, `monster_stage`, `monster_lunge`, `jumpscare`, `chase_start`, `chase_end`, `death`, `question_abandoned`, `session_end`, `tracking_lost`, `tracking_restored`.

Response: `{"windows_accepted": 1, "events_accepted": 1, "events_duplicate": 0, "records_updated": 1}`

Errors: 401 bad token, 403 no consent, 409 session ended, 413 body over 1 MB, 422 invalid batch.

## Employer

| Method and path | What it does |
|---|---|
| `GET /v1/me` | Your user id and employer memberships (with role). |
| `POST /v1/question-sets` | Register a question set: `{"name": "...", "questions": [{"key": "p1", "prompt": "...", "position": 0}]}`. Keys are what the game sends as `question_key`. `employer_id` is needed only if you belong to several employers. |
| `GET /v1/question-sets` | Your question sets with their questions. |
| `POST /v1/candidates` | `{"pseudonym": "C-…" (optional, generated otherwise), "identity": {"display_name", "email", "external_ref"} (optional, owners only)}`. Returns the pseudonymous candidate; the identity is stored separately and never returned. |
| `POST /v1/sessions` | `{"candidate_id", "question_set_id"}` → `{"session": {...}, "ingest_token": "qsi_…"}`. The token is shown once; configure it on the headset. |
| `GET /v1/sessions?status=live&limit=100` | Your sessions, newest first, with `candidate_pseudonym`. |
| `GET /v1/sessions/{id}` | One session with `baseline` and `consent` (granted, version, hash, times). |
| `GET /v1/sessions/{id}/qs` | The session's QS records (`[{question_key, question_id, status, outcome, record}]`). See `qs-schema.md`. |
| `GET /v1/sessions/{id}/windows?after_seq=-1&limit=2000&tail=false` | Window features in the live format, in seq order. `tail=true` returns the most recent `limit` (dashboard load); `after_seq` catches up after a Realtime reconnect. |
| `GET /v1/sessions/{id}/events` | The session's game events. |
| `GET /v1/candidates/{id}/summary` | Per-candidate roll-up of every session (observations, no score). |
| `POST /v1/sessions/{id}/summary?refresh=false` | Ask Gemini for the evidence-cited summary. Returns the stored one if it exists (`cached: true`); `refresh=true` calls Gemini again if the stored one is over 2 minutes old. Always 200: `status` is `ok`, `unavailable` (with `reason`: not configured, 402 credits, 429, timeout…) or `rejected` (nothing passed the checks). |
| `GET /v1/sessions/{id}/summary` | The last successful summary, or 404. |
| `DELETE /v1/candidates/{id}` | Owners only. Deletes the candidate and everything derived from them; returns counts. |

## Realtime contract (for dashboards)

Subscribe with `@supabase/supabase-js`, signed in as the employer, using the publishable key:

```ts
const ch = supabase.channel(`qs:session:${sessionId}`, { config: { private: true } });
ch.on("broadcast", { event: "window" }, ({ payload }) => …)   // {seq, t_start, t_end, question_key, features, rel, freeze}
  .on("broadcast", { event: "event" }, ({ payload }) => …)    // {t, type, question_key, payload}
  .on("broadcast", { event: "record" }, ({ payload }) => …)   // a full QS record
  .on("broadcast", { event: "baseline" }, ({ payload }) => …) // {status, windows}
  .subscribe();

supabase.channel(`qs:employer:${employerId}`, { config: { private: true } })
  .on("broadcast", { event: "session" }, ({ payload }) => …)  // {session_id, status}
  .subscribe();
```

`features` in a live window holds `head_ang_speed_mean`, `head_yaw_scan_rate`, `left/right_tremor_rms`, `left/right_hand_speed_mean`, `left/right_restless_rms` and coverage; `rel` the ratios to baseline. The reference dashboard is the QS Data Pipeline page in the root site (`src/qs/`).

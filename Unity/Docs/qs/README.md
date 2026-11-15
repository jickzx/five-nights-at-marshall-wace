# QS sender (motion telemetry)

`Assets/Scripts/Core/Telemetry/` samples the headset and both controllers every frame and posts them to the QS backend (`qs-backend/`) as 1 s windows every 0.5 s, together with the game events that give the motion meaning. **It is off by default:** with no settings file and no environment variables nothing is created and nothing is sent.

## Turn it on

1. Create a candidate and a session on the backend (`POST /v1/candidates`, then `POST /v1/sessions`; see `qs-backend/docs/api.md`). Keep the `ingest_token` it returns; it is shown once.
2. Copy `Unity/qs.example.json` to `Unity/Assets/Resources/Qs/qs.json` and fill in `enabled: true`, `endpointUrl`, `sessionId` and `ingestToken`. The `Assets/Resources/Qs` folder is gitignored: the token is a credential and this repository is public. Environment variables override the file: `FNMW_QS_ENABLED`, `FNMW_QS_URL`, `FNMW_QS_SESSION`, `FNMW_QS_TOKEN`.
3. Press Play. The console prints `[QS] ready, waiting for the candidate's consent`.

The endpoint must be `https://`; `http://` is accepted only for localhost and LAN addresses (local testing).

## Consent (needs a screen)

Nothing is recorded until the candidate agrees. On install the sender fetches the statement from `GET /v1/consent`. The game must then show `QsTelemetry.Instance.ConsentStatement` once its state is `AwaitingConsent`, and call `QsTelemetry.Instance.Consent(true)` or `Consent(false)` with the candidate's answer. **That screen is not built yet**: until it is, the sender waits and sends nothing. The server stores the hash of the exact text shown, and rejects motion for any session without consent.

After consent, the first `calibrationSeconds` (default 20) are the calm calibration period that becomes the person's own baseline. Keep play quiet during it (a lobby, the intro).

## What it sends

| Game event (`GameEvents`) | QS event |
|---|---|
| `PuzzleStarted(p)` | `question_shown`, key `p<Id>` |
| `TilePicked` | `tile_placed` |
| `TilesCleared` | `answer_cleared` |
| `AnswerSubmitted(e)` | `answer_submitted {correct}`, plus `answer_wrong_fx` when wrong (the glass cracks and bleeds) |
| phase `Puzzle` → anything but `Reveal`/`Dead` | `question_abandoned` (walked away from the console, or the chase started) |
| phase → `Quiz` | `question_shown`, key `p<Id>-quiz`; `QuizAnswered` → `answer_submitted {correct, final: true}` (the quiz is one-shot) |
| `BeastStageChanged` | `monster_stage {stage}` |
| phase → / from `Chase` | `chase_start` / `chase_end` |
| `PlayerCaught` | `death` |
| phase → `Escaped` or `Dead` | `session_end` (the sender stops; a new run needs a new session) |

Game code can log anything else with `QsTelemetry.Instance.Log(type, questionKey, payloadJson)` (the type must be one the backend accepts, e.g. `hint_used`, `monster_appeared`).

## Pose sources

- `CameraPoseSource` (default, every build): head pose from the main camera; controllers are reported untracked, so the backend computes head features only.
- `XrPoseSource`: headset and Touch controllers through `UnityEngine.XR.InputDevices` (position, rotation, tracking flag, and velocity when the runtime reports it). Compiled only with the `FNMW_QS_XR` scripting define, so this branch builds without any XR package. The Quest branch (`feat/oculus-quest`) should add `FNMW_QS_XR` to the Android player's scripting defines; `QsModule` then picks it whenever an XR head device is present.

## Network behaviour

One request at a time. Batches wait in a queue of up to four (about 2 s) while a request is in flight; past that the oldest window is dropped (its events are carried forward), so a bad connection does not build a backlog. Events are kept and retried (each has an id, so the server drops duplicates). A 401, 403 or 409 stops the sender (bad token, no consent, session ended).

## Checks

- Compile: `python3 Unity/Tools/cc.py`. Tests: `unity test "$PWD" --mode EditMode --filter QsTelemetryTests -o out.xml` from `Unity/`.
- `QsTelemetryTests` covers config parsing (off by default, https only, environment override), windowing, the exact JSON wire format, culture-invariant numbers, consent parsing and the camera source.

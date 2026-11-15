# Five Nights at Marshall Wace

A language-learning horror game built at a hackathon. You are stranded on a blood-sea planet, and each console you need to restart the ship shows a sentence in a real Earth language (Japanese, Mandarin, Arabic, German, French, Spanish, Turkish) written in an alien script. You work out the grammar from example rows, build the answer from tiles, and then the game reveals the real language and quizzes you on it, while a beast called The Principal grows and hunts you. It plays on desktop and on a Meta Quest 2.

The repo also holds two optional services the game can talk to (a voice server and a motion-telemetry service), an employer-facing web page for that telemetry, and the art pipeline.

## What's in the repo

| Path | What it is | Docs |
|---|---|---|
| `Unity/` | **The game.** Unity 6000.6.9f1 (URP), built code-first: `Assets/Scenes/Main.unity` holds one `GameBootstrap` object and everything else is spawned from code. Scripts in `Assets/Scripts/{Core,Game,Lang,Monster,UI,Audio,World,Voice,XR,Bootstrap,Editor}`; EditMode tests in `Assets/Tests/EditMode`. Includes the wormhole intro, tutorial, worked example, sentence puzzles, reveal and quiz, final chase, escape and death endings, run debrief, and the Quest 2 port (OpenXR, Touch controls). | [Unity/README.md](Unity/README.md), [Unity/Docs](Unity/Docs) |
| `backend/` | **Voice server** (Node, TypeScript, no runtime dependencies). Gemini writes monster lines and ship hints from game facts; ElevenLabs speaks them and the decoded sentence. When Gemini is unavailable or out of credit, the server writes monster lines itself. Has a keyless `mock` mode. | [backend/README.md](backend/README.md) |
| `qs-backend/` | **QS telemetry service** (FastAPI on Vercel, Postgres and Realtime on Supabase). Turns raw head and controller motion from the headset into one record per question, serves them to employers under row-level security, and pushes live updates. Migrations in `supabase/migrations/`. | [qs-backend/README.md](qs-backend/README.md), [qs-backend/docs](qs-backend/docs) |
| `index.html`, `src/`, `qs/`, `vite.config.ts` | **Vite site** with two pages. `/` is the early Three.js web prototype of the game (`src/game`, `src/lang`, `src/world`, `src/ui`), superseded by the Unity game. `/qs/` is the "QS Data Pipeline" employer page (`src/qs/`), which runs on synthetic demo data unless an API URL is configured. | this file |
| `assets/`, `tools/blender/` | Procedural Blender art kit: the 15 low-poly models the game loads from `Unity/Assets/Resources/Models/`, including The Principal. Rebuilding needs Blender 5.2.1 LTS, ffmpeg and Python with Pillow. | [assets/README.md](assets/README.md) |
| `docs/` | Pitch notes (`PITCH.md`: how the puzzles, answers and beast actually work), sponsor API research, plan, report and security review, and `answer-key/`, a small .NET program that prints the puzzles and answers for any seed, using the .NET SDK bundled with the Unity Editor. | [docs/PITCH.md](docs/PITCH.md), [docs/answer-key/README.md](docs/answer-key/README.md) |
| `TASKS.md` | The original hackathon task split for the web prototype. Historical. | |

## How the parts connect

```
Unity game ──HTTPS/loopback──▶ backend/ (voice) ──▶ Gemini (text), ElevenLabs (speech)
     │
     └──(off by default)──▶ qs-backend/ (Vercel) ──▶ Supabase Postgres + Realtime
                                    ▲                        │
                                    └──── /qs/ employer page ◀┘  (REST via the API, live updates via Realtime)
```

- **Game → voice backend.** Optional. With no voice settings (a `fnmw-voice.json`, `-voiceUrl`/`-voiceToken`, or `FNMW_VOICE_URL`/`FNMW_VOICE_TOKEN`), the game makes no network request and shows authored hints. Provider keys live only in `backend/.env`.
- **Game → QS backend.** Optional and off by default. `Assets/Scripts/Core/Telemetry` sends 1 s motion windows plus game events once it has a `qs.json` (or `FNMW_QS_*` variables) *and* the player's consent.
- **QS backend → Supabase → `/qs/`.** The API stores records in Supabase; the employer page reads them through the API and subscribes to Realtime for live updates.
- **Gemini in the Unity Editor.** Separately, `Services.Ai` can call Gemini directly in the Editor with a local key in `Unity/secrets.json` (used for the run debrief), overridable with `GEMINI_API_KEY`, `GEMINI_MODEL`, `FNMW_AI_PROXY_URL` and `FNMW_AI_DISABLED`. Builds never carry a key; see [Unity/Docs/ai/README.md](Unity/Docs/ai/README.md).

## Run it locally

### The game (desktop)

1. Install **Unity 6000.6.9f1** (from `Unity/ProjectSettings/ProjectVersion.txt`) and open the `Unity/` folder.
2. Open `Assets/Scenes/Main.unity` and press Play. Don't edit or save the scene: everything is spawned from code.

Controls: Enter (or Space) starts; T on the title replays the tutorial, and holding Q skips it. WASD and the mouse move and look, Shift runs, F toggles the lamp, E uses the green console, Esc or Tab leaves it. At a console, number keys or the arrows plus Space place tiles, Backspace undoes, Delete clears, Enter submits, and H asks the ship for a hint (authored text offline; generated hints and speech need the voice backend). On the quiz payoff, P replays the decoded sentence. The full desktop and Touch table is in [Unity/Docs/oculus/SETUP.md](Unity/Docs/oculus/SETUP.md#5-controls-touch).

Dev keys: F1 toggles demo mode (fixed seed 4242, forgiving beast; restarts the run; also the `-demo` launch flag), F5 restarts, F6–F12 skip to explore, puzzle, reveal, quiz, chase, escape and death. F9 also opens the audio audition mode. F2 shows the voice status line and F3 the beast-learning overlay.

[Unity/README.md](Unity/README.md) covers driving the Editor from the Unity CLI (open, recompile, play, screenshot, record), the module architecture and folder ownership. For a compile check without opening the project: `python3 Unity/Tools/cc.py` (it borrows compiler settings from a checkout Unity has already opened, so it needs an existing `Unity/Library`).

### The game on Meta Quest 2

Follow [Unity/Docs/oculus/SETUP.md](Unity/Docs/oculus/SETUP.md): install Android Build Support, enable developer mode, then in the Editor run **Fnmw > Quest > Configure project for Quest** and **Fnmw > Quest > Build APK**, which writes `Unity/Builds/Quest/FiveNightsAtMarshallWace-Quest.apk`. From a terminal on macOS, with the Editor closed (other platforms: adjust the Unity path):

```bash
cd Unity
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit \
  -projectPath "$PWD" -executeMethod Fnmw.XR.EditorTools.QuestSetup.BuildApk -logFile /tmp/quest-build.log
adb install -r Builds/Quest/FiveNightsAtMarshallWace-Quest.apk
adb shell am start -n com.fnmw.fivenights/com.unity3d.player.UnityPlayerGameActivity
```

Release signing reads `FNMW_KEYSTORE`, `FNMW_KEYSTORE_PASS`, `FNMW_KEY_ALIAS` (and optionally `FNMW_KEY_PASS`, `FNMW_VERSION_CODE`). The Touch control table is in the setup doc. The desktop build has no XR loader and is unaffected.

### Voice backend (`backend/`)

Needs Node 22.18+ (it runs the TypeScript sources directly; no build step).

```bash
cd backend
npm install
cp .env.example .env   # .env is git-ignored; set FNMW_GAME_TOKENS (e.g. openssl rand -hex 24) before starting
npm run dev            # mock mode, no provider keys needed, on http://127.0.0.1:8787
npm start              # uses FNMW_PROVIDER_MODE from .env
```

The server refuses to start without at least one `FNMW_GAME_TOKENS` entry, even in mock mode. The example `.env` sets `FNMW_PROVIDER_MODE=mock`; set it to `live` (or remove it: the code default is `live`) for real providers, which also needs `FNMW_SESSION_SECRET`.

Env vars (names only; see `backend/.env.example`): `FNMW_PROVIDER_MODE` (`mock` or `live`), `FNMW_BIND` (default `127.0.0.1:8787`), `FNMW_GAME_TOKENS`, `FNMW_SESSION_SECRET` (required in live mode), `GEMINI_API_KEY`, `ELEVENLABS_API_KEY`, `FNMW_GEMINI_MODEL`, `FNMW_TTS_MODEL`, `FNMW_VOICE_MONSTER`, `FNMW_VOICE_SHIP`, `FNMW_VOICE_NATIVE` (plus per-language `FNMW_VOICE_NATIVE_JA` etc.), optional limit overrides, `FNMW_TRUSTED_PROXIES`, and `FNMW_MOCK_FAULT` (mock mode only).

Point the game at it with a `fnmw-voice.json` holding `url` and `token` (one of the `FNMW_GAME_TOKENS` values): in the Editor at `Unity/UserSettings/fnmw-voice.json` (git-ignored), in a build under the player's persistent data folder. The `-voiceUrl`/`-voiceToken` flags and `FNMW_VOICE_URL`/`FNMW_VOICE_TOKEN` also work. The game accepts plain `http://` only to `127.0.0.1`, `localhost` or `[::1]`, and a standalone build may refuse plain HTTP even then (the project's "Allow downloads over HTTP" is off), so put the backend behind HTTPS for builds.

### QS backend (`qs-backend/`)

Needs Python 3.12+ and [uv](https://docs.astral.sh/uv/).

```bash
cd qs-backend
uv sync
# without Supabase: in-memory store, employer tokens are HS256 JWTs signed with SUPABASE_JWT_SECRET
QS_STORE=memory SUPABASE_JWT_SECRET=<a long random dev-only secret> uv run uvicorn main:app --reload
# API docs at http://localhost:8000/docs
```

With a real or local Supabase stack, apply `supabase/migrations/20261115000000_qs_init.sql`, copy `.env.example` to `.env`, and run `uv run uvicorn main:app --reload --env-file .env`. Env vars: `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY`, `SUPABASE_SECRET_KEY` (server only), `SUPABASE_JWT_SECRET` (legacy projects only), `GEMINI_API_KEY`, `GEMINI_MODEL`, `QS_GEMINI_DISABLED`, `QS_CORS_ORIGINS`, `QS_STORE` (`supabase` or `memory`). Provisioning and the Vercel deploy (Root Directory `qs-backend`) are in [qs-backend/README.md](qs-backend/README.md).

To send real motion from the game, copy `Unity/qs.example.json` to `Unity/Assets/Resources/Qs/qs.json` (git-ignored) or set `FNMW_QS_ENABLED`, `FNMW_QS_URL`, `FNMW_QS_SESSION`, `FNMW_QS_TOKEN`. See [Unity/Docs/qs/README.md](Unity/Docs/qs/README.md).

### Vite site (web prototype and `/qs/` page)

```bash
npm install
npm run dev        # prints the URL; the prototype is at /, the employer page at /qs/
npm run build      # tsc && vite build, both pages
npm run preview
```

The prototype takes `?demo` (fixed seed, slow creature) and `?seed=<n>`. The `/qs/` page runs in demo mode with synthetic data whenever `VITE_QS_API_URL` is unset (or `?demo` is in the URL). For live mode set `VITE_QS_API_URL`, `VITE_SUPABASE_URL` and `VITE_SUPABASE_PUBLISHABLE_KEY` in a root `.env` (see `.env.example`). These are bundled into the browser, so only public values belong there; the page refuses a Supabase secret or `service_role` key.

## Tests

| Part | Command |
|---|---|
| Unity (EditMode) | from `Unity/`, Editor closed: `~/.unity/bin/unity test "$PWD" --mode EditMode -o /tmp/test-results.xml`; Editor open: `~/.unity/bin/unity command test run --project-path "$PWD" --mode editor`. Or Window > General > Test Runner. |
| Unity compile only | `python3 Unity/Tools/cc.py` |
| Voice backend | `cd backend && npm test` (node:test; providers are faked) and `npm run typecheck` |
| QS backend | `cd qs-backend && uv run pytest` (the RLS tests start a throwaway Postgres through `pgserver`) |
| Vite site | `npm test` (Vitest: the prototype's language engine and the QS page) and `npm run build` |

The Unity CLI commands assume the `unity` CLI at `~/.unity/bin/unity`; see [Unity/README.md](Unity/README.md).

## Secrets

The repo is public. Nothing secret is committed, and the following stay git-ignored: `.env` files (root, `backend/`, `qs-backend/`), `Unity/secrets.json`, `Unity/UserSettings/` (voice settings), `Unity/Assets/Resources/Ai/` and `Unity/Assets/Resources/Qs/`. Each `*.example*` file lists names only. `AiKeyBuildGuard` fails a Unity build if a Gemini key would be bundled.

## Status and known gaps

- **Quest 2.** The port is merged (PRs #17, #18) and covered by EditMode tests. The setup doc lists what still needs a headset to judge: controller model proportions, label and wrist-card readability, the death scare's timing and distance, and the ground chevron's brightness. A VR tutorial step and more visible controller models are in progress and not yet in `main`.
- **QS telemetry is not live.** No Supabase project or Vercel deployment has been set up from this repo yet (provisioning is in progress), so the `/qs/` page's live mode has never run against them. The game has no consent screen yet, so the sender waits in `AwaitingConsent` and sends nothing, even with `qs.json`. The employer names on the `/qs/` page are invented placeholders (`src/qs/employers.ts`).
- **Gemini.** The last live check (15 November 2026) got HTTP 402: the project had no credit. So no Gemini-written line, hint or debrief has been seen yet. Monster lines fall back to server-written text, ship hints to authored text, and the debrief to stock text.
- **Live AI in builds.** A player build reaches Gemini only through a generateContent proxy, and none exists in this repo, so builds always show the stock run debrief.
- **Voice backend.** It runs as a single long-lived process (its rate limits are in memory) and has no deployment; a standalone build may refuse plain HTTP even to localhost, so a build needs the backend behind HTTPS. Its README lists the provider behaviour that has not been verified, and the hint rule texts still need a teacher's review.
- **Web prototype.** `/` is the early Three.js scaffold from the first hours of the hackathon, not the game.

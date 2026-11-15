# QS backend

Takes raw head and controller motion from the Quest, turns it into **QS records** (one per question: how the person moved and acted while answering), pushes live updates to employer dashboards, and serves the results per candidate per question. FastAPI on Vercel, Postgres and Realtime on Supabase.

- **The data type:** [`docs/qs-schema.md`](docs/qs-schema.md): every field, its unit and how it is computed.
- **The API and the Realtime contract:** [`docs/api.md`](docs/api.md).
- **Research and decisions:** [`docs/design.md`](docs/design.md): Vercel limits, Realtime Broadcast vs Postgres Changes, what the Quest exposes, storage, Gemini.
- **Unity sender:** [`Unity/Docs/qs/README.md`](../Unity/Docs/qs/README.md).
- **Employer page:** the QS Data Pipeline page of the root site, `/qs/` (`src/qs/`), runs in demo mode with no backend.

QS describes movement, not people: no field is a score or a verdict, every motion value is relative to the person's own calm calibration, and the Gemini summary cites evidence and leaves every judgement to the employer.

## Layout

```
main.py                      Vercel entrypoint (app = create_app())
qs/signal.py                 pure DSP: resampling, band power, angular speed, reversals
qs/features.py               window features, baseline, per-question QS records
qs/service.py                ingest pipeline and employer operations
qs/api.py                    HTTP routes and auth
qs/db.py                     PostgREST client (RLS as the employer) and an in-memory store with the same rules
qs/gemini.py                 schema-constrained summary with citation and language checks
qs/realtime.py               Broadcast over HTTP
qs/sim.py                    a simulated person and scripted session (tests, demo data)
supabase/migrations/         schema, RLS, Realtime policy, delete function
scripts/make_demo_fixture.py regenerates the employer page's demo data through this pipeline
tests/                       DSP on synthetic signals, the API end to end, RLS on a real Postgres
```

## Run locally

Needs Python 3.12+ and [uv](https://docs.astral.sh/uv/).

```sh
cd qs-backend
uv sync                       # creates .venv with runtime + dev dependencies
uv run pytest                 # 70 tests; the RLS tests start a throwaway Postgres via pgserver
```

**Without Supabase** (in-memory store, data lost on restart; employer tokens must be HS256 JWTs signed with `SUPABASE_JWT_SECRET`):

```sh
QS_STORE=memory SUPABASE_JWT_SECRET=dev-only-secret-at-least-32-characters uv run uvicorn main:app --reload
open http://localhost:8000/docs
```

**With a local Supabase stack** (Docker + Supabase CLI):

```sh
supabase init                 # once, then: cp supabase/migrations/* into the generated supabase/migrations/
supabase start                # prints the local URL and keys
supabase db reset             # applies the migration
cp .env.example .env          # fill SUPABASE_URL / SUPABASE_PUBLISHABLE_KEY / SUPABASE_SECRET_KEY from `supabase status`
uv run uvicorn main:app --reload --env-file .env
```

Regenerate the employer page's demo data after changing features: `PYTHONPATH=. uv run python scripts/make_demo_fixture.py ../src/qs/demo/fixture.json`.

## Provision and deploy

**Status (2026-11-15):** steps 1 and 2 are done on the team's Supabase project (eu-west-1): migration `20261115000000_qs_init` is applied, all 13 `qs_*` tables have RLS on, and Realtime is private-only. Steps 3 to 7 are still to do.

1. **Supabase project.** Create a project. In the SQL editor (or `supabase link && supabase db push`), run `supabase/migrations/20261115000000_qs_init.sql`. If `db push` hangs connecting to `*.pooler.supabase.com` (Postgres port blocked on your network), apply it through the Management API instead (`POST /v1/projects/<ref>/database/migrations`) and keep the version recorded as `20261115000000` so a later `db push` treats it as applied.
   The security advisor then reports two expected items: `qs_session_tokens` has RLS and no policies (server-only by design), and `qs_delete_candidate` is a `SECURITY DEFINER` function callable by `authenticated` (it checks the caller is an owner of the candidate's employer before deleting).
2. **Realtime.** Project Settings → Realtime: turn off "Allow public access" so only private channels work.
3. **Keys.** Project Settings → API Keys: copy the **publishable** key and create a **secret** key. The secret key goes only into Vercel's environment; never into Unity, the frontend or git.
4. **Employer accounts.** Authentication → Users: create a user per employer staff member. Then link them (SQL editor):
   ```sql
   insert into qs_employers (name) values ('Your company') returning id;
   insert into qs_employer_members (employer_id, user_id, role) values ('<employer id>', '<auth user id>', 'owner');
   ```
   `owner` can see real identities and delete data; `viewer` sees pseudonymous data only.
5. **Vercel project for the API.** Import the repo, set **Root Directory = `qs-backend`** (framework: FastAPI, detected), pick the function region closest to the Supabase region, and add environment variables from `.env.example`: `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY`, `SUPABASE_SECRET_KEY` (type Secret), optionally `GEMINI_API_KEY` (Secret) and `GEMINI_MODEL` (default `gemini-3.5-flash-lite`), and `QS_CORS_ORIGINS` = the site's URL. Deploy, then check `GET /healthz`.
6. **Site.** In the Vercel project for the root site, set `VITE_QS_API_URL`, `VITE_SUPABASE_URL`, `VITE_SUPABASE_PUBLISHABLE_KEY` (public values only) and redeploy. Without them `/qs/` stays in demo mode.
7. **Headset.** For each assessment, create a candidate and a session (`POST /v1/candidates`, `POST /v1/sessions`), then put the session id and ingest token into `Unity/Assets/Resources/Qs/qs.json` (gitignored) or the `FNMW_QS_*` environment variables (see the Unity doc).

## Security notes

- The repo is public: no keys or tokens are committed anywhere. `.env*` is gitignored; `.env.example` holds names only.
- Employer requests are verified (signature, expiry, `aud=authenticated`, `role=authenticated`; asymmetric keys through the project's JWKS, legacy HS256 only if `SUPABASE_JWT_SECRET` is set) and then run as that user in Postgres, so RLS applies even if the API had a bug.
- The secret key is used only for ingest and derived rows (windows, events, records, summaries) and for broadcasting.
- Ingest tokens are random 256-bit values stored as SHA-256; one token writes to one session only.
- Database error details are logged, not returned.

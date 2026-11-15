"""Drive the deployed QS stack as a real employer: question set, device key, simulated headset sessions.

Everything goes through the public API exactly as the headset and the employer page do (no database shortcuts),
so what shows up on the live page is what real sends produce. Simulated sessions are a fallback for when no
headset is at hand; they use qs/sim.py (motion + breathing) and the same ingest endpoint.

    uv run python scripts/feed_live.py live              # one session at headset pace (2 batches/s), ~2.5 min
    uv run python scripts/feed_live.py seed 3            # three finished sessions, as fast as the API allows
    uv run python scripts/feed_live.py sessions          # list this employer's sessions
    uv run python scripts/feed_live.py cleanup           # delete half-finished sessions (never started, or stalled)
    uv run python scripts/feed_live.py question-set      # make sure the game's question set exists; print its id
    uv run python scripts/feed_live.py enroll ../Unity/Assets/Resources/Qs/qs.json   # device key -> headset config

Sign-in (an employer *owner* account in Supabase Auth): ``QS_EMAIL`` and ``QS_PASSWORD``, or a JSON file
``{"email": ..., "password": ...}`` named by ``QS_LOGIN_FILE`` (default ``qs-backend/.qs-login.json``, gitignored).
Endpoints default to the team's deployment and can be overridden with ``QS_API_URL``, ``QS_SUPABASE_URL`` and
``QS_SUPABASE_PUBLISHABLE_KEY`` (the publishable key is public by design; it is also in the site's bundle).
Passwords, tokens and device keys are never printed.
"""
from __future__ import annotations

import json
import os
import pathlib
import sys
import time

import httpx

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from qs.sim import simulate_batches  # noqa: E402

API = os.environ.get("QS_API_URL", "https://qs-backend-iota.vercel.app").rstrip("/")
SUPA = os.environ.get("QS_SUPABASE_URL", "https://mnsvtsglxabzndxeoheh.supabase.co").rstrip("/")
PUB = os.environ.get("QS_SUPABASE_PUBLISHABLE_KEY", "sb_publishable_vmeH0eFu6qlyqL1JMxLN1Q_vCcS4haL")
LOGIN_FILE = pathlib.Path(os.environ.get("QS_LOGIN_FILE", HERE.parent / ".qs-login.json"))

# The game's consoles: five puzzles per run (LanguageEngine.DefaultPuzzleCount), ids 1..5, each followed by a
# one-shot quiz. The sender keys them p<id> and p<id>-quiz (Unity/Assets/Scripts/Core/Telemetry/QsWire.cs). The
# language is generated per run, so the prompts describe the console, not a fixed phrase.
GAME_SET = "Five Nights · shore consoles"
GAME_QUESTIONS = [q for i in range(1, 6) for q in (
    {"key": f"p{i}", "prompt": f"Console {i}: build the phrase in the run's alien language from the example glyphs"},
    {"key": f"p{i}-quiz", "prompt": f"Console {i} quiz: which real language was that console's tongue?"},
)]


def die(msg: str) -> None:
    raise SystemExit(f"feed_live: {msg}")


def login() -> tuple[str, str]:
    email, pw = os.environ.get("QS_EMAIL"), os.environ.get("QS_PASSWORD")
    if not (email and pw) and LOGIN_FILE.exists():
        d = json.loads(LOGIN_FILE.read_text())
        email, pw = d.get("email"), d.get("password")
    if not (email and pw):
        die(f"no credentials: set QS_EMAIL and QS_PASSWORD, or create {LOGIN_FILE} (gitignored)")
    return email, pw


class Client:
    def __init__(self):
        self.http = httpx.Client(timeout=30.0)
        email, pw = login()
        r = self.http.post(f"{SUPA}/auth/v1/token?grant_type=password", json={"email": email, "password": pw},
                           headers={"apikey": PUB})
        if r.status_code != 200:
            die(f"sign-in failed (HTTP {r.status_code})")
        self.auth = {"Authorization": f"Bearer {r.json()['access_token']}"}

    def call(self, method: str, path: str, body=None, headers=None, ok=(200, 201, 202)):
        r = self.http.request(method, API + path, json=body, headers={**self.auth, **(headers or {})})
        if r.status_code not in ok:
            die(f"{method} {path.split('?')[0]} -> HTTP {r.status_code}: {r.text[:300]}")
        return r.json() if r.content else None

    def ingest(self, sid: str, token: str, batch: dict, tries: int = 4):
        for i in range(tries):
            try:
                r = self.http.post(f"{API}/v1/ingest/{sid}", json=batch, headers={"X-QS-Ingest-Token": token})
            except httpx.HTTPError:
                time.sleep(0.5 * (i + 1))
                continue
            if r.status_code == 202:
                return r.json()
            if r.status_code < 500:
                die(f"ingest refused (HTTP {r.status_code}): {r.text[:200]}")
            time.sleep(0.5 * (i + 1))
        die("ingest failed after retries")


def game_question_set(c: Client) -> dict:
    for s in c.call("GET", "/v1/question-sets"):
        if s["name"] == GAME_SET:
            return s
    return c.call("POST", "/v1/question-sets", {"name": GAME_SET, "questions": [
        {**q, "position": i} for i, q in enumerate(GAME_QUESTIONS)]})


def run_session(c: Client, qset: dict, seed: int, intensity: float, pace: bool) -> str:
    cand = c.call("POST", "/v1/candidates", {})
    made = c.call("POST", "/v1/sessions", {"candidate_id": cand["id"], "question_set_id": qset["id"]})
    sid, token = made["session"]["id"], made["ingest_token"]
    consent = c.http.get(f"{API}/v1/consent").json()
    r = c.http.post(f"{API}/v1/ingest/{sid}/consent", headers={"X-QS-Ingest-Token": token}, json={
        "consent_version": consent["consent_version"], "statement_sha256": consent["statement_sha256"],
        "accepted": True, "client": {"device": "simulated (scripts/feed_live.py)", "app": "feed_live"}})
    if r.status_code != 201:
        die(f"consent refused (HTTP {r.status_code})")
    batches = simulate_batches(seed=seed, intensity=intensity)
    print(f"session {sid} · candidate {cand['pseudonym']} · {len(batches)} batches{' at headset pace' if pace else ''}", flush=True)
    t0 = time.monotonic()
    for i, b in enumerate(batches):
        if pace:
            wait = t0 + (b["t_end"] - batches[0]["t_end"]) - time.monotonic()
            if wait > 0:
                time.sleep(wait)
        c.ingest(sid, token, b)
        if i % 40 == 0:
            print(f"  t={b['t_end']:6.1f}s  batch {i}/{len(batches)}", flush=True)
    print(f"session {sid} ended", flush=True)
    return sid


def sessions(c: Client) -> list[dict]:
    return c.call("GET", "/v1/sessions?limit=200")


def cleanup(c: Client, stale_minutes: float = 10.0) -> None:
    """Delete sessions that never finished: still 'created' or 'live' with no activity for ``stale_minutes``.
    Deletion goes through DELETE /v1/candidates/{id} (owner only), which removes everything derived."""
    from datetime import datetime, timezone
    now = datetime.now(timezone.utc)
    removed = 0
    for s in sessions(c):
        if s["status"] == "ended":
            continue
        started = s.get("started_at") or s.get("created_at")
        age = (now - datetime.fromisoformat(started.replace("Z", "+00:00"))).total_seconds() / 60 if started else 1e9
        if s["status"] == "live":
            windows = c.call("GET", f"/v1/sessions/{s['id']}/windows?tail=true&limit=1")
            if windows and age < stale_minutes + windows[-1]["t_end"] / 60:
                continue     # still streaming
        elif age < stale_minutes:
            continue
        receipt = c.call("DELETE", f"/v1/candidates/{s['candidate_id']}")
        removed += 1
        print(f"deleted {s['status']} session {s['id']} ({s.get('candidate_pseudonym')}): {receipt['deleted']}")
    print(f"{removed} half-finished session(s) deleted")


def enroll(c: Client, out: pathlib.Path, name: str) -> None:
    """Create a device key for the game's question set and write it into the headset's qs.json. The key is
    written to that file only (keep it gitignored) and never printed."""
    qset = game_question_set(c)
    made = c.call("POST", "/v1/devices", {"name": name, "question_set_id": qset["id"]})
    cfg = json.loads(out.read_text()) if out.exists() else {}
    cfg.update({"endpointUrl": API, "deviceKey": made["device_key"], "microphone": cfg.get("microphone", True)})
    cfg.pop("enabled", None)
    for k in ("sessionId", "ingestToken"):
        cfg.pop(k, None)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(cfg, indent=2) + "\n")
    os.chmod(out, 0o600)
    print(f"device {made['device']['id']} ('{name}') enrolled for '{GAME_SET}'; key written to {out}")


def main(argv: list[str]) -> None:
    if not argv:
        print(__doc__)
        return
    cmd, rest = argv[0], argv[1:]
    c = Client()
    if cmd == "live":
        run_session(c, game_question_set(c), int(rest[0]) if rest else int(time.time()) % 1000, 1.0, pace=True)
    elif cmd == "seed":
        q = game_question_set(c)
        for seed, intensity in [(11, 0.6), (23, 1.4), (31, 0.85), (47, 1.1), (59, 0.7)][: int(rest[0]) if rest else 3]:
            run_session(c, q, seed, intensity, pace=False)
    elif cmd == "sessions":
        for s in sessions(c):
            print(f"{s['id']}  {s['status']:7}  {s.get('candidate_pseudonym') or '-':9}  started {s.get('started_at') or '-'}"
                  f"{'  (headset)' if s.get('device_id') else ''}")
    elif cmd == "cleanup":
        cleanup(c, float(rest[0]) if rest else 10.0)
    elif cmd == "question-set":
        print(game_question_set(c)["id"])
    elif cmd == "enroll":
        if not rest:
            die("usage: enroll <path to qs.json> [device name]")
        enroll(c, pathlib.Path(rest[0]).resolve(), rest[1] if len(rest) > 1 else "Quest headset")
    else:
        die(f"unknown command {cmd!r}")


if __name__ == "__main__":
    main(sys.argv[1:])

"""Generate the dashboard's demo data by running simulated sessions through the real QS pipeline.

    .venv/bin/python scripts/make_demo_fixture.py ../src/qs/demo/fixture.json

The motion is synthetic (qs/sim.py), but every window feature, baseline, QS record and Realtime message in
the output was produced by the same code that serves real sessions. The one hand-written part is the
summary: Gemini is not called here, so the demo shows a summary written for the demo, run through the same
citation and language checks (qs.gemini.check_summary) and labelled as such in the UI.
"""
import json
import sys
import uuid

from qs import gemini
from qs.api import backend_from_settings, create_app
from qs.config import Settings
from qs.db import MemoryStore
from qs.sim import simulate_batches

from fastapi.testclient import TestClient
import jwt, time

SECRET = "demo-fixture-only-secret-not-a-real-key-000000000000"


def main(out_path: str) -> None:
    store = MemoryStore()
    backend = backend_from_settings(Settings(store="memory", supabase_jwt_secret=SECRET), store)
    client = TestClient(create_app(backend))
    server = store.db(None)
    emp = server.insert("qs_employers", [{"name": "Demo employer"}])[0]
    user = str(uuid.uuid4())
    server.insert("qs_employer_members", [{"employer_id": emp["id"], "user_id": user, "role": "owner"}])
    tok = jwt.encode({"sub": user, "role": "authenticated", "aud": "authenticated", "exp": int(time.time()) + 3600},
                     SECRET, algorithm="HS256")
    h = {"Authorization": f"Bearer {tok}"}
    prompts = ["Compound noun (German)", "Plural suffix order (Turkish)", "Object particle (Japanese)",
               "Verb agreement (Arabic)", "Tone pair (Mandarin)"]
    qset = client.post("/v1/question-sets", headers=h, json={"name": "Linguistics screen A", "questions": [
        {"key": f"p{i + 1}", "prompt": p} for i, p in enumerate(prompts)]}).json()
    consent = client.get("/v1/consent").json()

    sessions = []
    # The featured live session, then three finished ones with other simulated people.
    for i, (pseudo, seed, intensity) in enumerate([("C-7F3A21", 7, 1.0), ("C-19B4E0", 11, 0.6),
                                                   ("C-A2C9D5", 23, 1.4), ("C-5E0B77", 31, 0.85)]):
        cand = client.post("/v1/candidates", headers=h, json={"pseudonym": pseudo}).json()
        made = client.post("/v1/sessions", headers=h, json={"candidate_id": cand["id"], "question_set_id": qset["id"]}).json()
        sid, itok = made["session"]["id"], made["ingest_token"]
        client.post(f"/v1/ingest/{sid}/consent", headers={"X-QS-Ingest-Token": itok}, json={
            "consent_version": consent["consent_version"], "statement_sha256": consent["statement_sha256"], "accepted": True})
        start = len(backend.broadcaster.sent)
        stream = []
        for b in simulate_batches(seed=seed, intensity=intensity):
            r = client.post(f"/v1/ingest/{sid}", headers={"X-QS-Ingest-Token": itok}, json=b)
            assert r.status_code == 202, r.text
            for m in backend.broadcaster.sent[start:]:
                if m["topic"].startswith("qs:session:"):
                    stream.append({"at": b["t_end"], "event": m["event"], "payload": m["payload"]})
            start = len(backend.broadcaster.sent)
        records = [r["record"] for r in client.get(f"/v1/sessions/{sid}/qs", headers=h).json()]
        session = client.get(f"/v1/sessions/{sid}", headers=h).json()
        summary = client.get(f"/v1/candidates/{cand['id']}/summary", headers=h).json()
        entry = {"session": {k: session.get(k) for k in ("id", "status", "candidate_id", "candidate_pseudonym",
                                                         "question_set_id", "created_at", "started_at", "ended_at")},
                 "consent": session["consent"], "baseline": session.get("baseline"),
                 "records": records, "candidate_summary": summary["sessions"][0]["summary"]}
        if i == 0:
            entry["stream"] = stream
            entry["summary"] = demo_summary(records)
        sessions.append(entry)

    fixture = {
        "generated_by": "qs-backend/scripts/make_demo_fixture.py",
        "note": "Synthetic motion run through the real QS pipeline. Not a real person.",
        "question_set": {"id": qset["id"], "name": qset["name"],
                         "questions": [{"key": q["key"], "prompt": q["prompt"]} for q in qset["questions"]]},
        "sessions": sessions,
    }
    with open(out_path, "w") as f:
        json.dump(fixture, f, separators=(",", ":"))
    print(f"wrote {out_path}: {sum(len(s.get('stream', [])) for s in sessions)} stream messages")


def demo_summary(records):
    by = {r["question_key"]: r for r in records}

    def fmt(x):
        return "n/a" if x is None else f"{x:.1f}"

    raw = {"observations": [
        {"statement": f"On p1 the first input came {fmt(by['p1']['behaviour']['time_to_first_action_s'])} s after the "
                      f"question appeared; right-hand motion in the 4-12 Hz band was "
                      f"{fmt(by['p1']['motion']['right_tremor_ratio'])}x the calibration level.",
         "question_keys": ["p1"], "features": ["behaviour.time_to_first_action_s", "motion.right_tremor_ratio"],
         "direction": "above_baseline"},
        {"statement": f"On p2 there were {by['p2']['behaviour']['hesitations']} pauses of 3 s or more between inputs and "
                      f"{by['p2']['behaviour']['answer_changes']} answer changes before the correct submission.",
         "question_keys": ["p2"], "features": ["behaviour.hesitations", "behaviour.answer_changes",
                                               "behaviour.incorrect_submissions"], "direction": "not_applicable"},
        {"statement": "When the monster appeared during p3, head rotation peaked well above the level just before it, "
                      "followed by a period of very little head and hand movement.",
         "question_keys": ["p3"], "features": ["motion.startle_max_head_ratio", "motion.freeze_s"],
         "direction": "above_baseline"},
        {"statement": f"Head scanning on p4 was {fmt(by['p4']['motion']['head_scan_ratio'])}x the calibration level and "
                      f"4-12 Hz hand motion was {fmt(by['p4']['motion']['right_tremor_ratio'])}x (right hand).",
         "question_keys": ["p4"], "features": ["motion.head_scan_ratio", "motion.right_tremor_ratio", "behaviour.hints_used"],
         "direction": "above_baseline"},
        {"statement": "The session ended during p5 when the game's monster caught the player.",
         "question_keys": ["p5"], "features": ["outcome"], "direction": "not_applicable"},
    ], "data_quality": [
        {"statement": "Calibration produced a usable baseline and tracking coverage was high on every question.",
         "question_keys": ["session"], "features": ["quality.baseline_status", "quality.head_coverage"],
         "direction": "not_applicable"},
    ]}
    summary, dropped = gemini.check_summary(raw, records)
    assert not dropped, dropped
    return {"status": "demo", "model": None,
            "reason": "Written for the demo and passed through the same evidence and language checks. Not generated by Gemini.",
            "summary": summary}


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "fixture.json")

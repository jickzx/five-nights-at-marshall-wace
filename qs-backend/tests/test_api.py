"""The API end to end on the in-memory store, with a full simulated session."""
import json
import uuid

import httpx
import pytest

from qs import gemini
from qs.sim import simulate_batches
from tests.conftest import make_token

BATCHES = simulate_batches()


def run_session(world, tag="a"):
    cand, session, token = world.new_session(tag)
    assert world.consent(session["id"], token).status_code == 201
    for b in BATCHES:
        r = world.post(session["id"], token, b)
        assert r.status_code == 202, r.text
    return cand, session, token


@pytest.fixture
def played(world):
    cand, session, token = run_session(world)
    records = {r["question_key"]: r["record"] for r in
               world.client.get(f"/v1/sessions/{session['id']}/qs", headers=world.h()).json()}
    return world, cand, session, token, records


# ---- consent and ingest auth

def test_ingest_refused_without_consent(world):
    _, session, token = world.new_session()
    r = world.post(session["id"], token, BATCHES[0])
    assert r.status_code == 403 and "consent" in r.json()["detail"]


def test_consent_must_match_current_statement(world):
    _, session, token = world.new_session()
    r = world.client.post(f"/v1/ingest/{session['id']}/consent", headers={"X-QS-Ingest-Token": token},
                          json={"consent_version": "old", "statement_sha256": "0" * 64, "accepted": True})
    assert r.status_code == 409
    r = world.client.post(f"/v1/ingest/{session['id']}/consent", headers={"X-QS-Ingest-Token": token},
                          json={"consent_version": "x", "statement_sha256": "0" * 64, "accepted": False})
    assert r.status_code == 422


def test_wrong_or_missing_token_and_cross_session_token(world):
    _, s1, t1 = world.new_session()
    _, s2, t2 = world.new_session()
    world.consent(s1["id"], t1)
    assert world.post(s1["id"], "qsi_wrong", BATCHES[0]).status_code == 401
    assert world.client.post(f"/v1/ingest/{s1['id']}", json=BATCHES[0]).status_code == 401
    assert world.post(s1["id"], t2, BATCHES[0]).status_code == 401          # token for another session
    assert world.post(str(uuid.uuid4()), t1, BATCHES[0]).status_code == 401
    assert world.post("not-a-uuid", t1, BATCHES[0]).status_code == 404


def test_withdrawn_consent_stops_ingest(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    assert world.post(s["id"], t, BATCHES[0]).status_code == 202
    assert world.client.post(f"/v1/ingest/{s['id']}/consent/withdraw", headers={"X-QS-Ingest-Token": t}).status_code == 200
    assert world.post(s["id"], t, BATCHES[1]).status_code == 401              # token revoked with the withdrawal


def test_oversized_and_malformed_batches(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    big = dict(BATCHES[10])
    big["samples"] = dict(big["samples"])
    big["samples"]["t"] = big["samples"]["t"] * 9
    assert world.post(s["id"], t, big).status_code == 422                       # >600 samples / misaligned
    r = world.client.post(f"/v1/ingest/{s['id']}", headers={"X-QS-Ingest-Token": t, "Content-Type": "application/json"},
                          content=b"{" + b" " * 1_100_000 + b"}")
    assert r.status_code == 413
    assert world.post(s["id"], t, {"seq": 1}).status_code == 422                 # empty batch
    bad = {"events": [{"id": str(uuid.uuid4()), "t": 1, "type": "drop table"}]}
    assert world.post(s["id"], t, bad).status_code == 422


def test_retries_are_idempotent(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    b = next(x for x in BATCHES if x["events"])
    first = world.post(s["id"], t, b).json()
    again = world.post(s["id"], t, b).json()
    assert first["windows_accepted"] == 1 and again["windows_accepted"] == 0
    assert again["events_accepted"] == 0 and again["events_duplicate"] == len(b["events"])


# ---- the QS pipeline on a simulated session

def test_session_produces_a_record_per_question(played):
    world, _, session, _, records = played
    assert sorted(records) == ["p1", "p2", "p3", "p4", "p5"]
    assert all(r["status"] == "closed" for r in records.values())
    s = world.client.get(f"/v1/sessions/{session['id']}", headers=world.h()).json()
    assert s["status"] == "ended" and s["consent"]["granted"]
    assert s["baseline"]["status"] == "ok" and s["baseline"]["windows"] >= 40


def test_behaviour_features(played):
    *_, records = played
    p1, p2 = records["p1"]["behaviour"], records["p2"]["behaviour"]
    assert records["p1"]["outcome"] == "correct" and records["p5"]["outcome"] == "died"
    assert p1["time_to_first_action_s"] == pytest.approx(4.2, abs=0.01)
    assert p1["response_latency_s"] == pytest.approx(9.0, abs=0.01)
    assert p2["time_to_first_action_s"] == pytest.approx(9.5, abs=0.01)
    assert p2["incorrect_submissions"] == 1 and p2["answer_changes"] == 2
    assert p2["hesitations"] >= 2 and p1["hesitations"] == 0
    assert records["p4"]["behaviour"]["hints_used"] == 1


def test_motion_features_follow_the_script(played):
    *_, records = played
    m = {k: r["motion"] for k, r in records.items()}
    # Tremor amplitude in the script: p1 0.18 mm, p4 0.45 mm (calibration 0.15 mm). Sensor noise and reaches
    # add a floor of band energy, so measured ratios are compressed relative to the amplitudes.
    assert m["p4"]["right_tremor_ratio"] > 2.0
    assert m["p4"]["right_tremor_ratio"] > 1.5 * m["p1"]["right_tremor_ratio"]
    assert 0.7 < m["p1"]["right_tremor_ratio"] < 1.8
    # Looking around: p4 scans far more than p1.
    assert m["p4"]["head_scan_ratio"] > 2 * m["p1"]["head_scan_ratio"]
    # The monster appears at 84 s in p3: a startle, then the scripted 5 s freeze.
    startle = next(s for s in m["p3"]["startles"] if s["event_type"] == "monster_appeared")
    assert startle["head_peak_ratio"] > 2.0
    assert 0.0 < startle["latency_s"] < 1.0
    assert m["p3"]["freeze_s"] >= 2.0 and m["p1"]["freeze_s"] == 0
    assert m["p2"]["fidget_index"] > m["p1"]["fidget_index"]


def test_live_windows_and_events_endpoints(played):
    world, _, session, *_ = played
    w = world.client.get(f"/v1/sessions/{session['id']}/windows?after_seq=100&limit=5", headers=world.h()).json()
    assert [x["seq"] for x in w] == [101, 102, 103, 104, 105]
    assert "right_tremor_rms" in w[0]["features"] and w[0]["rel"]["right_tremor_rms"] is not None
    ev = world.client.get(f"/v1/sessions/{session['id']}/events", headers=world.h()).json()
    assert ev[0]["type"] == "session_start" and ev[-1]["type"] == "session_end"


def test_ended_session_refuses_more_data(played):
    world, _, session, token, _ = played
    assert world.post(session["id"], token, BATCHES[0]).status_code == 409


def test_candidate_summary_has_no_score(played):
    world, cand, *_ = played
    s = world.client.get(f"/v1/candidates/{cand['id']}/summary", headers=world.h()).json()
    assert s["candidate"]["pseudonym"].startswith("C-")
    text = json.dumps(s).lower()
    for word in ("score", "suitab", "hire", "reject", "nervous"):
        assert word not in text


def test_realtime_messages(played):
    world, _, session, *_ = played
    sent = world.backend.broadcaster.sent
    topics = {m["topic"] for m in sent}
    assert topics == {f"qs:session:{session['id']}", f"qs:employer:{world.users['a']['employer']}"}
    kinds = {m["event"] for m in sent}
    assert {"window", "event", "record", "session", "baseline"} <= kinds
    assert not any("display_name" in json.dumps(m) or "email" in json.dumps(m) for m in sent)


# ---- employer isolation, identity, deletion

def test_other_employer_sees_nothing(played):
    world, cand, session, *_ = played
    hb = world.h("b")
    assert world.client.get("/v1/sessions", headers=hb).json() == []
    for path in (f"/v1/sessions/{session['id']}", f"/v1/sessions/{session['id']}/qs",
                 f"/v1/sessions/{session['id']}/windows", f"/v1/candidates/{cand['id']}/summary"):
        assert world.client.get(path, headers=hb).status_code == 404, path
    assert world.client.delete(f"/v1/candidates/{cand['id']}", headers=hb).status_code == 404
    assert world.client.post("/v1/sessions", headers=hb, json={"candidate_id": cand["id"]}).status_code == 404


def test_employer_endpoints_need_a_valid_jwt(world):
    assert world.client.get("/v1/sessions").status_code == 401
    expired = make_token(world.users["a"]["owner"], exp=1)
    assert world.client.get("/v1/sessions", headers={"Authorization": f"Bearer {expired}"}).status_code == 401
    wrong_aud = make_token(world.users["a"]["owner"], aud="anon")
    assert world.client.get("/v1/sessions", headers={"Authorization": f"Bearer {wrong_aud}"}).status_code == 401
    anon_role = make_token(world.users["a"]["owner"], role="anon")
    assert world.client.get("/v1/sessions", headers={"Authorization": f"Bearer {anon_role}"}).status_code == 401
    import jwt as pyjwt
    forged = pyjwt.encode({"sub": world.users["a"]["owner"], "role": "authenticated", "aud": "authenticated",
                           "exp": 9999999999}, "some-other-secret-that-is-long-enough-123", algorithm="HS256")
    assert world.client.get("/v1/sessions", headers={"Authorization": f"Bearer {forged}"}).status_code == 401
    assert world.client.get("/v1/sessions", headers={"Authorization": "Bearer e30.e30."}).status_code == 401


def test_identity_is_separate_and_owner_only(world):
    cand, session, _ = world.new_session(identity={"display_name": "Ada Example", "email": "ada@example.com"})
    rows = world.store.tables["qs_candidate_identities"]
    assert len(rows) == 1 and rows[0]["candidate_id"] == cand["id"]
    for path in ("/v1/sessions", f"/v1/sessions/{session['id']}", f"/v1/candidates/{cand['id']}/summary"):
        assert "Ada" not in world.client.get(path, headers=world.h()).text
    r = world.client.post("/v1/candidates", headers=world.h(role="viewer"), json={"identity": {"display_name": "X"}})
    assert r.status_code == 403
    assert len(world.store.tables["qs_candidates"]) == 1                     # the half-made candidate was removed


def test_delete_candidate_removes_everything(played):
    world, cand, session, *_ = played
    assert world.client.delete(f"/v1/candidates/{cand['id']}", headers=world.h(role="viewer")).status_code == 404
    r = world.client.delete(f"/v1/candidates/{cand['id']}", headers=world.h())
    assert r.status_code == 200 and r.json()["deleted"]["sessions"] == 1
    for table in ("qs_sessions", "qs_motion_windows", "qs_events", "qs_records", "qs_consents", "qs_session_tokens"):
        assert world.store.tables[table] == [], table
    assert world.client.get(f"/v1/sessions/{session['id']}", headers=world.h()).status_code == 404


# ---- Gemini summary

def _fake_gemini(status=200, body=None):
    def handler(request: httpx.Request):
        assert request.headers["x-goog-api-key"] == "k"
        sent = json.loads(request.content)
        assert sent["generationConfig"]["responseMimeType"] == "application/json"
        assert "responseJsonSchema" in sent["generationConfig"]
        assert "display_name" not in request.content.decode()
        return httpx.Response(status, json=body or {})
    return httpx.Client(transport=httpx.MockTransport(handler))


def _reply(obj):
    return {"candidates": [{"content": {"parts": [{"text": json.dumps(obj)}]}}]}


def test_summary_unavailable_without_key_and_rest_still_works(played):
    world, _, session, *_ = played
    r = world.client.post(f"/v1/sessions/{session['id']}/summary", headers=world.h())
    assert r.status_code == 200 and r.json()["status"] == "unavailable"
    assert world.client.get(f"/v1/sessions/{session['id']}/qs", headers=world.h()).status_code == 200


@pytest.mark.parametrize("status,needle", [(402, "credits"), (429, "429"), (500, "500")])
def test_summary_fails_gracefully_on_http_errors(played, status, needle):
    *_, records = played
    out = gemini.summarize("k", "gemini-x", {"q": 1}, list(records.values()), client=_fake_gemini(status))
    assert out["status"] == "unavailable" and needle in out["reason"]


def test_summary_validates_citations_and_language(played):
    *_, records = played
    raw = {
        "observations": [
            {"statement": "Hand motion in the 4-12 Hz band on p4 was about 2.5x the calibration level.",
             "question_keys": ["p4"], "features": ["motion.right_tremor_ratio"], "direction": "above_baseline"},
            {"statement": "The candidate seemed nervous and is not suitable.", "question_keys": ["p4"],
             "features": ["motion.right_tremor_ratio"], "direction": "above_baseline"},
            {"statement": "Something about q99.", "question_keys": ["q99"], "features": ["motion.freeze_s"],
             "direction": "near_baseline"},
            {"statement": "Made-up feature.", "question_keys": ["p1"], "features": ["motion.honesty"],
             "direction": "near_baseline"},
        ],
        "data_quality": [],
    }
    out = gemini.summarize("k", "gemini-x", {"q": 1}, list(records.values()), client=_fake_gemini(200, _reply(raw)))
    assert out["status"] == "ok"
    obs = out["summary"]["observations"]
    assert len(obs) == 1 and len(out["dropped"]) == 3
    assert obs[0]["evidence"][0]["value"] == records["p4"]["motion"]["right_tremor_ratio"]
    assert any("medical" in line for line in out["summary"]["limitations"])


def test_summary_rejected_when_nothing_survives(played):
    *_, records = played
    raw = {"observations": [{"statement": "Hire this person.", "question_keys": ["p1"],
                             "features": ["outcome"], "direction": "not_applicable"}], "data_quality": []}
    out = gemini.summarize("k", "m", {}, list(records.values()), client=_fake_gemini(200, _reply(raw)))
    assert out["status"] == "rejected" and out["summary"] is None


def test_summary_schema_has_no_decision_field():
    schema = json.dumps(gemini.response_schema(["p1"])).lower()
    for word in ("decision", "score", "rating", "hire", "recommend"):
        assert word not in schema


def test_summary_is_cached_when_ok(played, monkeypatch):
    world, _, session, *_ = played
    ok = {"status": "ok", "model": "m", "summary": {"observations": [], "data_quality": [], "limitations": []}}
    world.backend.summarizer = lambda facts, records: ok
    assert world.client.post(f"/v1/sessions/{session['id']}/summary", headers=world.h()).json()["status"] == "ok"
    assert world.client.get(f"/v1/sessions/{session['id']}/summary", headers=world.h()).json()["status"] == "ok"
    assert world.client.get(f"/v1/sessions/{session['id']}/summary", headers=world.h("b")).status_code == 404

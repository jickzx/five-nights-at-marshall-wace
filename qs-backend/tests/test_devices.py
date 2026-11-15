"""Enrolled headsets: device keys can start sessions for their own employer and do nothing else.
Also: audio consent gating and the automatic end-of-session summary."""
import hashlib
import json

import pytest

from qs import service
from qs.sim import simulate_batches


def question_set(world, tag="a"):
    r = world.client.post("/v1/question-sets", headers=world.h(tag), json={
        "name": "Game puzzles", "questions": [{"key": f"p{i}", "prompt": f"Puzzle {i}"} for i in range(1, 6)]})
    assert r.status_code == 201
    return r.json()


def enroll(world, tag="a"):
    qs = question_set(world, tag)
    r = world.client.post("/v1/devices", headers=world.h(tag), json={"name": "Quest 2 #1", "question_set_id": qs["id"]})
    assert r.status_code == 201, r.text
    return qs, r.json()


def start(world, key):
    return world.client.post("/v1/device-sessions", headers={"X-QS-Device-Key": key}, json={"client": {"device": "Quest"}})


def test_owner_enrolls_a_device_and_the_key_is_stored_hashed(world):
    qs, made = enroll(world)
    key = made["device_key"]
    assert key.startswith("qsd_") and "key_hash" not in made["device"]
    row = world.store.tables["qs_devices"][0]
    assert row["key_hash"] == hashlib.sha256(key.encode()).hexdigest()
    assert key not in json.dumps(world.store.tables)
    listed = world.client.get("/v1/devices", headers=world.h()).json()
    assert len(listed) == 1 and "key_hash" not in listed[0] and listed[0]["question_set_id"] == qs["id"]


def test_only_owners_enroll_and_only_with_their_own_question_set(world):
    qs = question_set(world, "a")
    r = world.client.post("/v1/devices", headers=world.h("a", "viewer"), json={"name": "x", "question_set_id": qs["id"]})
    assert r.status_code == 403
    r = world.client.post("/v1/devices", headers=world.h("b"), json={"name": "x", "question_set_id": qs["id"]})
    assert r.status_code == 404
    assert world.client.get("/v1/devices", headers=world.h("a", "viewer")).json() == []


def test_device_key_starts_a_session_the_headset_can_use(world):
    qs, made = enroll(world)
    r = start(world, made["device_key"])
    assert r.status_code == 201, r.text
    body = r.json()
    assert set(body) == {"session_id", "ingest_token", "candidate_pseudonym"}
    assert body["candidate_pseudonym"].startswith("C-")
    s = world.client.get(f"/v1/sessions/{body['session_id']}", headers=world.h()).json()
    assert s["question_set_id"] == qs["id"] and s["device_id"] == made["device"]["id"]
    assert world.consent(body["session_id"], body["ingest_token"]).status_code == 201
    assert world.post(body["session_id"], body["ingest_token"], simulate_batches()[0]).status_code == 202
    # the session is employer A's and invisible to B
    assert world.client.get(f"/v1/sessions/{body['session_id']}", headers=world.h("b")).status_code == 404


def test_device_key_can_do_nothing_else(world):
    _, made = enroll(world)
    key = made["device_key"]
    body = start(world, key).json()
    dev_auth = {"Authorization": f"Bearer {key}"}
    for path in ("/v1/sessions", "/v1/me", "/v1/devices", "/v1/question-sets", f"/v1/sessions/{body['session_id']}/qs"):
        assert world.client.get(path, headers=dev_auth).status_code == 401, path
        assert world.client.get(path, headers={"X-QS-Device-Key": key}).status_code == 401, path
    assert world.client.post("/v1/candidates", headers=dev_auth, json={}).status_code == 401
    # it is not an ingest token either
    assert world.post(body["session_id"], key, simulate_batches()[0]).status_code == 401
    assert world.client.post(f"/v1/ingest/{body['session_id']}/consent", headers={"X-QS-Ingest-Token": key},
                             json={"consent_version": "x", "statement_sha256": "0" * 64, "accepted": True}).status_code == 401


def test_bad_or_revoked_keys_are_refused(world):
    _, made = enroll(world)
    assert start(world, "qsd_not-a-real-key").status_code == 401
    assert start(world, "").status_code == 401
    dev = made["device"]["id"]
    assert world.client.delete(f"/v1/devices/{dev}", headers=world.h("a", "viewer")).status_code in (403, 404)
    assert world.client.delete(f"/v1/devices/{dev}", headers=world.h("b")).status_code == 404
    assert start(world, made["device_key"]).status_code == 201
    assert world.client.delete(f"/v1/devices/{dev}", headers=world.h()).status_code == 200
    assert start(world, made["device_key"]).status_code == 401


def test_employers_cannot_forge_device_sessions(world):
    cand = world.client.post("/v1/candidates", headers=world.h(), json={}).json()
    r = world.client.post("/v1/sessions", headers=world.h(), json={"candidate_id": cand["id"], "device_id": cand["id"]})
    assert r.status_code == 422


def test_device_session_rate_limit(world, monkeypatch):
    monkeypatch.setattr(service, "DEVICE_SESSIONS_PER_10_MIN", 2)
    _, made = enroll(world)
    codes = [start(world, made["device_key"]).status_code for _ in range(4)]
    assert codes[:3] == [201, 201, 201] and codes[3] == 429


# ---- audio consent

def _audio_batch():
    return next(b for b in simulate_batches() if b.get("audio") and b["audio"]["breath_rate_bpm"])


def test_audio_is_stored_only_under_a_consent_that_names_the_microphone(world):
    _, s1, t1 = world.new_session()
    world.consent(s1["id"], t1)
    r = world.post(s1["id"], t1, _audio_batch()).json()
    assert r["audio_accepted"] is True
    w = world.store.tables["qs_motion_windows"][-1]
    assert w["features"]["audio_breath_rate_bpm"] is not None

    # a session consented under the previous (motion-only) statement keeps working but never stores audio
    _, s2, t2 = world.new_session()
    world.store.db(None).insert("qs_consents", [{"session_id": s2["id"], "employer_id": world.users["a"]["employer"],
                                                 "consent_version": "qs-consent-2026-11", "statement_sha256": "a" * 64}])
    r = world.post(s2["id"], t2, _audio_batch()).json()
    assert r["windows_accepted"] == 1 and r["audio_accepted"] is False
    w = world.store.tables["qs_motion_windows"][-1]
    assert not any(k.startswith("audio_") for k in w["features"])


def test_consent_statement_names_the_microphone_and_raw_audio():
    text, digest = service.consent_statement("qs-consent-2026-11-mic")
    assert "microphone" in text and "Raw audio never leaves the headset" in text
    assert "qs-consent-2026-11-mic" in service.AUDIO_CONSENT_VERSIONS
    assert service.consent_statement("qs-consent-2026-11")[1] != digest


# ---- summary at session end

def test_summary_is_written_when_the_session_ends(world):
    calls = []

    def summarizer(facts, records):
        calls.append(facts)
        return {"status": "unavailable", "reason": "Gemini refused the request (HTTP 402).", "summary": None, "model": None}
    world.backend.summarizer = summarizer
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    for b in simulate_batches():
        world.post(s["id"], t, b)
    assert len(calls) == 1
    assert any(q["states"] for q in calls[0]["questions"])          # the model sees the states and their evidence
    assert "display_name" not in json.dumps(calls[0])
    row = world.client.get(f"/v1/sessions/{s['id']}/summary", headers=world.h()).json()
    assert row["status"] == "unavailable" and "402" in row["error"]
    assert any(m["event"] == "summary" for m in world.backend.broadcaster.sent)


def test_failed_refresh_never_replaces_a_good_summary(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    world.backend.summarizer = lambda f, r: {"status": "ok", "model": "m", "summary": {"observations": [], "data_quality": [], "limitations": []}}
    for b in simulate_batches():
        world.post(s["id"], t, b)
    world.backend.summarizer = lambda f, r: {"status": "unavailable", "reason": "402", "summary": None}
    row = world.store.tables["qs_sessions"][0]
    service._store_summary(world.store.db(None), row, world.backend.summarizer({}, []), keep_ok=True)
    assert world.store.tables["qs_summaries"][0]["status"] == "ok"

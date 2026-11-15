"""Regression tests for the issues raised in the pre-PR review."""
import json
import uuid

import httpx
import pytest

from qs import features as F
from qs.db import PAGE, MemoryStore, PostgrestDb, Query
from qs.sim import simulate_batches


def ev(t, typ, key=None, payload=None):
    return {"t": t, "type": typ, "question_key": key, "payload": payload}


# ---- questions close in the real game flow

def test_final_wrong_answer_closes_a_one_shot_question():
    spans = F.question_spans([ev(35, "question_shown", "p1-quiz"),
                              ev(40, "answer_submitted", "p1-quiz", {"correct": False, "final": True}),
                              ev(200, "question_shown", "p2")])
    assert spans["p1-quiz"] == (35, 40)


def test_wrong_non_final_answer_keeps_puzzle_open():
    spans = F.question_spans([ev(1, "question_shown", "p1"), ev(5, "answer_submitted", "p1", {"correct": False})])
    assert spans["p1"] == (1, None)


def test_abandon_closes_question():
    spans = F.question_spans([ev(1, "question_shown", "p1"), ev(9, "question_abandoned", "p1"), ev(60, "death")])
    assert spans["p1"] == (1, 9)


def test_abandon_for_another_key_does_not_close_current():
    spans = F.question_spans([ev(1, "question_shown", "p2"), ev(3, "question_abandoned", "p1")])
    assert spans["p2"] == (1, None)


# ---- baseline-only rules

def test_no_baseline_means_no_freeze_judgement():
    still = {"head_ang_speed_mean": 0.1, "left_hand_speed_mean": 0.001, "right_hand_speed_mean": 0.001}
    assert F.is_freeze(still, None) is None
    assert F.is_freeze(still, {"status": "missing", "features": {}}) is None


def test_startle_pre_level_ignores_windows_after_the_event():
    base = {"status": "ok", "features": {"head_ang_speed_mean": {"median": 5.0}}}
    calm = [{"t_start": t - 1, "t_end": t, "head_ang_speed_mean": 5.0} for t in (82.0, 83.0, 84.0)]
    flinch = {"t_start": 83.5, "t_end": 84.25, "head_ang_speed_mean": 80.0, "head_ang_speed_peak": 100.0,
              "head_ang_speed_peak_t": 84.2}
    out = F._startles(calm + [flinch], [ev(84.0, "monster_appeared")], base)
    assert out[0]["head_peak_ratio"] == pytest.approx(20.0)   # 100 / 5, not diluted by the flinch window


# ---- input validation

def test_nan_and_infinity_are_rejected(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    body = '{"events":[{"id":"%s","t":NaN,"type":"death"}]}' % uuid.uuid4()
    r = world.client.post(f"/v1/ingest/{s['id']}", headers={"X-QS-Ingest-Token": t, "Content-Type": "application/json"}, content=body)
    assert r.status_code == 422
    b = simulate_batches()[5]
    b["samples"]["head"]["pos"][0] = [1e400, 0, 0]       # json.dumps writes Infinity
    r = world.client.post(f"/v1/ingest/{s['id']}", headers={"X-QS-Ingest-Token": t, "Content-Type": "application/json"},
                          content=json.dumps(b))
    assert r.status_code == 422


# ---- PostgREST's row cap

def test_postgrest_select_pages_past_the_row_cap():
    rows = [{"i": i} for i in range(2500)]
    seen = []

    def handler(request: httpx.Request):
        off, lim = int(request.url.params["offset"]), int(request.url.params["limit"])
        seen.append((off, lim))
        return httpx.Response(200, json=rows[off: off + min(lim, PAGE)])

    db = PostgrestDb("https://x.supabase.co", "sb_secret_test", None, client=httpx.Client(transport=httpx.MockTransport(handler)))
    assert len(db.select("t")) == 2500
    assert seen == [(0, 1000), (1000, 1000), (2000, 1000)]
    seen.clear()
    assert len(db.select("t", Query(limit=1200))) == 1200
    assert seen == [(0, 1000), (1000, 200)]


def test_memory_store_models_the_cap():
    store = MemoryStore()
    server = store.db(None)
    emp = server.insert("qs_employers", [{"name": "e"}])[0]
    store.tables["qs_events"] = [{"session_id": "s", "employer_id": emp["id"], "t": i} for i in range(2100)]
    store.requests = 0
    assert len(server.select("qs_events", Query(eq={"session_id": "s"}))) == 2100
    assert store.requests == 3


# ---- ingest cost and dashboard reads

def test_window_only_batches_do_not_fetch_all_events(world, monkeypatch):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    batches = simulate_batches()
    for b in batches[:120]:
        world.post(s["id"], t, b)
    calls = []
    real = type(world.store.db(None)).select

    def spy(self, table, q=None, columns="*"):
        calls.append(table)
        return real(self, table, q, columns)

    monkeypatch.setattr(type(world.store.db(None)), "select", spy)
    quiet = next(b for b in batches[120:] if not b["events"] and b["question_key"] and b["seq"] % 4 != 0)
    assert world.post(s["id"], t, quiet).status_code == 202
    assert "qs_events" not in calls


def test_open_question_record_refreshes_from_motion(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    batches = simulate_batches()
    # p2 is open from 40 s to 72 s: after 60 s of play its record must have motion windows.
    for b in batches:
        if b["t_end"] > 60:
            break
        world.post(s["id"], t, b)
    rec = next(r for r in world.store.tables["qs_records"] if r["question_key"] == "p2")
    assert rec["status"] == "open" and rec["record"]["motion"]["windows"] > 20


def test_windows_tail_returns_latest(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    for b in simulate_batches()[:50]:
        world.post(s["id"], t, b)
    w = world.client.get(f"/v1/sessions/{s['id']}/windows?tail=true&limit=5", headers=world.h()).json()
    assert [x["seq"] for x in w] == [45, 46, 47, 48, 49]


def test_summary_is_reused_instead_of_calling_gemini_again(world):
    _, s, t = world.new_session()
    world.consent(s["id"], t)
    for b in simulate_batches():
        world.post(s["id"], t, b)
    calls = []

    def summarizer(facts, records):
        calls.append(1)
        return {"status": "ok", "model": "m", "summary": {"observations": [], "data_quality": [], "limitations": []}}

    world.backend.summarizer = summarizer
    for _ in range(3):
        assert world.client.post(f"/v1/sessions/{s['id']}/summary", headers=world.h()).json()["status"] == "ok"
    assert world.client.post(f"/v1/sessions/{s['id']}/summary?refresh=true", headers=world.h()).json()["cached"] is True
    assert len(calls) == 1   # the refresh is inside the cooldown


def test_neutral_quality_language_survives_the_filter():
    from qs.gemini import BANNED
    for ok in ("Hand tracking failed for 12% of p3.", "Time passed before the first input was 9.5 s.",
               "The candidate is shown as C-7F3A21."):
        assert not BANNED.search(ok), ok
    for bad in ("They failed the assessment.", "Not suitable.", "Seemed anxious."):
        assert BANNED.search(bad), bad

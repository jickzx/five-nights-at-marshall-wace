"""Business logic, independent of HTTP. ``user`` is a Db acting as the employer (RLS applies);
``server`` is a Db acting as the service (RLS bypassed) and is only used where the employer could not
or must not write directly: ingest tokens, motion, events, QS records, summaries."""
from __future__ import annotations

import hashlib
import secrets
from typing import Any, Sequence

import numpy as np

from . import features as F
from .auth import hash_token, new_ingest_token, token_matches
from .db import Db, DbError, Principal, Query, now_iso
from .models import CandidateIn, ConsentIn, IngestBatch, QuestionSetIn, SessionIn
from .realtime import Broadcaster, employer_topic, session_topic

CONSENT_STATEMENT = """QS motion data consent (version {version})

While you play this assessment, the headset records how your head and hands move and what you do in the game
(questions shown, answers, hints). It turns that into measurements per question, such as how much your hands
moved in the 4-12 Hz band compared with your own calm calibration at the start.

- The employer that invited you sees these measurements under a pseudonymous id. Your name is kept separately
  and only the employer's account owners can see it.
- Raw motion samples are processed and discarded; only per-second measurements are stored.
- The measurements describe movement. They are not a diagnosis and are not, on their own, a hiring decision.
  Movement can change for many ordinary reasons, including medical ones.
- You can stop at any time by taking off the headset or choosing "withdraw", and you can ask the employer to
  delete your data. Deletion removes every measurement and event recorded for you.

Choose "I agree" to start, or "No thanks" to play without QS recording."""


class ServiceError(Exception):
    def __init__(self, status: int, message: str):
        super().__init__(message)
        self.status = status


def consent_statement(version: str) -> tuple[str, str]:
    text = CONSENT_STATEMENT.format(version=version)
    return text, hashlib.sha256(text.encode("utf-8")).hexdigest()


# ---------------------------------------------------------------- employers -----------------------------

def employer_memberships(user: Db, principal: Principal) -> list[dict]:
    return user.select("qs_employer_members", Query(eq={"user_id": principal.user_id}))


def resolve_employer(user: Db, principal: Principal, requested: Any | None) -> str:
    ids = [str(m["employer_id"]) for m in employer_memberships(user, principal)]
    if requested is not None:
        if str(requested) not in ids:
            raise ServiceError(403, "not a member of that employer")
        return str(requested)
    if len(ids) == 1:
        return ids[0]
    if not ids:
        raise ServiceError(403, "this account is not linked to an employer")
    raise ServiceError(400, "this account belongs to several employers: pass employer_id")


def create_question_set(user: Db, principal: Principal, body: QuestionSetIn) -> dict:
    employer_id = resolve_employer(user, principal, body.employer_id)
    keys = [q.key for q in body.questions]
    if len(keys) != len(set(keys)):
        raise ServiceError(422, "question keys must be unique within a set")
    qset = user.insert("qs_question_sets", [{"employer_id": employer_id, "name": body.name}])[0]
    questions = user.insert("qs_questions", [
        {"question_set_id": qset["id"], "employer_id": employer_id, "key": q.key, "prompt": q.prompt,
         "position": q.position if q.position else i}
        for i, q in enumerate(body.questions)])
    return {**qset, "questions": sorted(questions, key=lambda q: q["position"])}


def list_question_sets(user: Db) -> list[dict]:
    sets = user.select("qs_question_sets", Query(order="created_at", desc=True))
    if not sets:
        return []
    qs = user.select("qs_questions", Query(in_={"question_set_id": [s["id"] for s in sets]}, order="position"))
    for s in sets:
        s["questions"] = [q for q in qs if q["question_set_id"] == s["id"]]
    return sets


def create_candidate(user: Db, principal: Principal, body: CandidateIn) -> dict:
    employer_id = resolve_employer(user, principal, body.employer_id)
    wants_identity = bool(body.identity and any(body.identity.model_dump().values()))
    if wants_identity:
        # Checked before anything is written, so a refused request leaves no half-made candidate.
        roles = {str(m["employer_id"]): m["role"] for m in employer_memberships(user, principal)}
        if roles.get(employer_id) != "owner":
            raise ServiceError(403, "only employer owners may store a candidate's real identity")
    pseudonym = body.pseudonym or "C-" + secrets.token_hex(3).upper()
    cand = user.insert("qs_candidates", [{"employer_id": employer_id, "pseudonym": pseudonym}])[0]
    if wants_identity:
        user.insert("qs_candidate_identities", [{"candidate_id": cand["id"], "employer_id": employer_id,
                                                 **body.identity.model_dump()}])
    return {"id": cand["id"], "employer_id": employer_id, "pseudonym": pseudonym, "created_at": cand.get("created_at")}


def create_session(user: Db, server: Db, principal: Principal, body: SessionIn) -> dict:
    cands = user.select("qs_candidates", Query(eq={"id": str(body.candidate_id)}))
    if not cands:
        raise ServiceError(404, "candidate not found")
    employer_id = str(cands[0]["employer_id"])
    row = {"employer_id": employer_id, "candidate_id": str(body.candidate_id)}
    if body.question_set_id:
        if not user.select("qs_question_sets", Query(eq={"id": str(body.question_set_id)})):
            raise ServiceError(404, "question set not found")
        row["question_set_id"] = str(body.question_set_id)
    session = user.insert("qs_sessions", [row])[0]
    token = new_ingest_token()
    server.insert("qs_session_tokens", [{"session_id": session["id"], "token_hash": hash_token(token)}])
    return {"session": session, "ingest_token": token,
            "note": "The ingest token is shown once. Configure it on the headset for this session only."}


def list_sessions(user: Db, status: str | None = None, limit: int = 100) -> list[dict]:
    q = Query(order="created_at", desc=True, limit=min(max(limit, 1), 500))
    if status:
        q.eq["status"] = status
    sessions = user.select("qs_sessions", q)
    if not sessions:
        return []
    cands = {c["id"]: c for c in user.select("qs_candidates", Query(in_={"id": list({s["candidate_id"] for s in sessions})}))}
    for s in sessions:
        s["candidate_pseudonym"] = (cands.get(s["candidate_id"]) or {}).get("pseudonym")
        s.pop("baseline", None)
    return sessions


def get_session(user: Db, session_id: str) -> dict:
    rows = user.select("qs_sessions", Query(eq={"id": session_id}))
    if not rows:
        raise ServiceError(404, "session not found")
    s = rows[0]
    cand = user.select("qs_candidates", Query(eq={"id": s["candidate_id"]}))
    s["candidate_pseudonym"] = cand[0]["pseudonym"] if cand else None
    consents = user.select("qs_consents", Query(eq={"session_id": session_id}, order="granted_at"))
    s["consent"] = {"granted": any(c.get("withdrawn_at") is None for c in consents),
                    "records": [{k: c.get(k) for k in ("consent_version", "statement_sha256", "granted_at", "withdrawn_at")}
                                for c in consents]}
    return s


def get_records(user: Db, session_id: str) -> list[dict]:
    get_session(user, session_id)  # 404 if not visible
    rows = user.select("qs_records", Query(eq={"session_id": session_id}))
    return sorted(rows, key=lambda r: (r["record"].get("t_shown") is None, r["record"].get("t_shown")))


def get_windows(user: Db, session_id: str, after_seq: int = -1, limit: int = 2000, tail: bool = False) -> list[dict]:
    """Windows after ``after_seq`` in seq order. ``tail`` returns the most recent ``limit`` instead of the first."""
    get_session(user, session_id)
    rows = user.select("qs_motion_windows", Query(eq={"session_id": session_id}, gt={"seq": after_seq},
                                                  order="seq", desc=tail, limit=min(max(limit, 1), 5000)))
    return rows[::-1] if tail else rows


def get_events(user: Db, session_id: str) -> list[dict]:
    get_session(user, session_id)
    return user.select("qs_events", Query(eq={"session_id": session_id}, order="t", limit=5000))


def candidate_summary(user: Db, candidate_id: str) -> dict:
    cands = user.select("qs_candidates", Query(eq={"id": candidate_id}))
    if not cands:
        raise ServiceError(404, "candidate not found")
    sessions = user.select("qs_sessions", Query(eq={"candidate_id": candidate_id}, order="created_at"))
    out = []
    for s in sessions:
        records = [r["record"] for r in user.select("qs_records", Query(eq={"session_id": s["id"]}))]
        out.append({"session_id": s["id"], "status": s["status"], "created_at": s.get("created_at"),
                    "baseline_status": (s.get("baseline") or {}).get("status", "missing"),
                    "summary": F.session_summary(records),
                    "questions": [{"question_key": r["question_key"], "outcome": r["outcome"],
                                   "status": r["status"]} for r in records]})
    return {"candidate": {"id": cands[0]["id"], "pseudonym": cands[0]["pseudonym"]}, "sessions": out}


def delete_candidate(user: Db, candidate_id: str) -> dict:
    try:
        return user.rpc("qs_delete_candidate", {"p_candidate_id": candidate_id})
    except DbError as ex:
        if ex.status in (404, 403):
            raise ServiceError(404, "candidate not found") from None
        raise


# ---------------------------------------------------------------- ingest --------------------------------

def authenticate_ingest(server: Db, session_id: str, token: str) -> dict:
    """The session a headset token belongs to, or a 401. Same error for unknown session and wrong token."""
    if not token:
        raise ServiceError(401, "missing ingest token")
    tokens = server.select("qs_session_tokens", Query(eq={"session_id": session_id}))
    if not tokens or tokens[0].get("revoked_at") or not token_matches(token, tokens[0]["token_hash"]):
        raise ServiceError(401, "invalid ingest token")
    sessions = server.select("qs_sessions", Query(eq={"id": session_id}))
    if not sessions:
        raise ServiceError(401, "invalid ingest token")
    return sessions[0]


def record_consent(server: Db, session: dict, body: ConsentIn, version: str) -> dict:
    if session.get("status") == "ended":
        raise ServiceError(409, "session has ended")
    _, digest = consent_statement(version)
    if body.consent_version != version or body.statement_sha256 != digest:
        raise ServiceError(409, "consent statement does not match the current version; fetch /v1/consent again")
    row = server.insert("qs_consents", [{"session_id": session["id"], "employer_id": session["employer_id"],
                                         "consent_version": version, "statement_sha256": digest,
                                         "client": body.client}])[0]
    return {"consent_id": row["id"], "granted_at": row.get("granted_at") or row.get("created_at")}


def withdraw_consent(server: Db, session: dict, broadcaster: Broadcaster) -> dict:
    rows = server.update("qs_consents", {"withdrawn_at": now_iso()},
                         Query(eq={"session_id": session["id"]}, is_null={"withdrawn_at": True}))
    server.update("qs_sessions", {"status": "ended", "ended_at": now_iso()}, Query(eq={"id": session["id"]}))
    server.update("qs_session_tokens", {"revoked_at": now_iso()}, Query(eq={"session_id": session["id"]}))
    broadcaster.send([{"topic": employer_topic(session["employer_id"]), "event": "session",
                       "payload": {"session_id": session["id"], "status": "ended", "consent": "withdrawn"}}])
    return {"withdrawn": len(rows)}


def _has_consent(server: Db, session_id: str) -> bool:
    return bool(server.select("qs_consents", Query(eq={"session_id": session_id}, is_null={"withdrawn_at": True}, limit=1)))


def _track(dev, t: np.ndarray) -> F.DeviceTrack | None:
    if dev is None or (dev.pos is None and dev.rot is None and dev.vel is None):
        return None
    arr = lambda a: None if a is None else np.asarray(a, dtype=float)  # noqa: E731
    return F.DeviceTrack(t=t, pos=arr(dev.pos), rot=arr(dev.rot), vel=arr(dev.vel),
                         tracked=None if dev.tracked is None else np.asarray(dev.tracked, dtype=bool))


RECORD_REFRESH_WINDOWS = 4   # an open question's record is refreshed from motion every 4 windows (2 s)

LIVE_FIELDS = ("head_ang_speed_mean", "head_yaw_scan_rate", "left_tremor_rms", "right_tremor_rms",
               "left_hand_speed_mean", "right_hand_speed_mean", "left_restless_rms", "right_restless_rms",
               "head_coverage", "left_coverage", "right_coverage")


def live_window(row: dict) -> dict:
    """The compact form of a window sent over Realtime and used by the dashboard's live traces."""
    f = row["features"]
    return {"seq": row["seq"], "t_start": row["t_start"], "t_end": row["t_end"], "question_key": row.get("question_key"),
            "features": {k: f.get(k) for k in LIVE_FIELDS}, "rel": row.get("rel"), "freeze": row.get("freeze")}


def ingest(server: Db, session: dict, batch: IngestBatch, broadcaster: Broadcaster) -> dict:
    sid, emp = session["id"], session["employer_id"]
    if session.get("status") == "ended":
        raise ServiceError(409, "session has ended")
    if not _has_consent(server, sid):
        raise ServiceError(403, "no consent recorded for this session; ingest refused")

    messages: list[dict] = []
    if session.get("status") == "created":
        server.update("qs_sessions", {"status": "live", "started_at": now_iso()}, Query(eq={"id": sid}))
        session = {**session, "status": "live"}
        messages.append({"topic": employer_topic(emp), "event": "session", "payload": {"session_id": sid, "status": "live"}})

    # ---- motion window
    window_rows: list[dict] = []
    if batch.samples is not None:
        s = batch.samples
        t = np.asarray(s.t, dtype=float)
        t0 = batch.t_start if batch.t_start is not None else float(np.nanmin(t))
        t1 = batch.t_end if batch.t_end is not None else float(np.nanmax(t))
        if t1 < t0:
            raise ServiceError(422, "t_end is before t_start")
        feats = F.window_features(t0, t1, _track(s.head, t), _track(s.left, t), _track(s.right, t),
                                  nominal_fs=batch.rate_hz or 0.0)
        baseline = session.get("baseline")
        row = {"session_id": sid, "employer_id": emp, "seq": batch.seq, "t_start": t0, "t_end": t1,
               "question_key": batch.question_key, "features": feats,
               "rel": F.relative_to_baseline(feats, baseline) if baseline else None}
        window_rows = server.insert("qs_motion_windows", [row], on_conflict="session_id,seq", ignore_duplicates=True)
        for w in window_rows:
            w["freeze"] = F.is_freeze(w["features"], baseline) if baseline else None
            messages.append({"topic": session_topic(sid), "event": "window", "payload": live_window(w)})

    # ---- events
    new_events: list[dict] = []
    if batch.events:
        rows = [{"session_id": sid, "employer_id": emp, "client_event_id": str(e.id), "t": e.t, "type": e.type,
                 "question_key": e.question_key, "payload": e.payload} for e in batch.events]
        new_events = server.insert("qs_events", rows, on_conflict="session_id,client_event_id", ignore_duplicates=True)
        for e in new_events:
            messages.append({"topic": session_topic(sid), "event": "event",
                             "payload": {k: e.get(k) for k in ("t", "type", "question_key", "payload")}})

    all_events: list[dict] | None = None

    def events_all() -> list[dict]:
        nonlocal all_events
        if all_events is None:
            all_events = server.select("qs_events", Query(eq={"session_id": sid}, order="t", limit=20000))
        return all_events

    # ---- baseline from calibration
    if any(e["type"] == "calibration_end" for e in new_events):
        evs = events_all()
        end_t = max(e["t"] for e in evs if e["type"] == "calibration_end")
        starts = [e["t"] for e in evs if e["type"] == "calibration_start" and e["t"] < end_t]
        q = Query(eq={"session_id": sid}, lte={"t_end": end_t}, order="t_start", limit=2000)
        if starts:
            q.gte["t_start"] = max(starts)
        calib = [w["features"] for w in server.select("qs_motion_windows", q)]
        baseline = F.compute_baseline(calib)
        baseline["t_start"] = max(starts) if starts else None
        baseline["t_end"] = end_t
        server.update("qs_sessions", {"baseline": baseline}, Query(eq={"id": sid}))
        session = {**session, "baseline": baseline}
        messages.append({"topic": session_topic(sid), "event": "baseline",
                         "payload": {"status": baseline["status"], "windows": baseline["windows"]}})

    # ---- QS records for questions touched by this batch
    records_updated = 0
    touched: set[str] = set()
    if new_events:
        for key, (ts, te) in F.question_spans(events_all()).items():
            if any(ts <= e["t"] <= (te if te is not None else float("inf")) for e in new_events):
                touched.add(key)
    if window_rows and batch.question_key and batch.question_key not in touched:
        # A window alone refreshes its question's record when that question has already closed (a late
        # window) or, while it is open, every RECORD_REFRESH_WINDOWS windows. Checking the record's status
        # is one small read, so window-only batches do not fetch the session's events.
        rec = server.select("qs_records", Query(eq={"session_id": sid, "question_key": batch.question_key}), columns="status")
        if rec and (rec[0]["status"] == "closed" or batch.seq % RECORD_REFRESH_WINDOWS == 0):
            touched.add(batch.question_key)
    if touched:
        spans = F.question_spans(events_all())
        records_updated = _refresh_records(server, session, sorted(touched), events_all(), spans, messages,
                                           now_t=max([e["t"] for e in events_all()] + [w["t_end"] for w in window_rows]))

    if any(e["type"] == "session_end" for e in new_events):
        server.update("qs_sessions", {"status": "ended", "ended_at": now_iso()}, Query(eq={"id": sid}))
        messages.append({"topic": employer_topic(emp), "event": "session", "payload": {"session_id": sid, "status": "ended"}})

    broadcaster.send(messages)
    return {"windows_accepted": len(window_rows), "events_accepted": len(new_events),
            "events_duplicate": len(batch.events) - len(new_events), "records_updated": records_updated}


def _question_ids(server: Db, session: dict) -> dict[str, str]:
    if not session.get("question_set_id"):
        return {}
    return {q["key"]: q["id"] for q in server.select("qs_questions", Query(eq={"question_set_id": session["question_set_id"]}))}


def _refresh_records(server: Db, session: dict, keys: Sequence[str], events: list[dict],
                     spans: dict, messages: list[dict], now_t: float | None) -> int:
    sid, emp = session["id"], session["employer_id"]
    qids = _question_ids(server, session)
    rows = []
    for key in keys:
        span = spans.get(key)
        if not span:
            continue
        ts, te = span
        q = Query(eq={"session_id": sid}, gte={"t_end": ts - F.STARTLE_PRE_S}, order="t_start", limit=5000)
        if te is not None:
            q.lte["t_start"] = te + F.STARTLE_WINDOW_S
        windows = [{**w["features"], "question_key": w.get("question_key")} for w in server.select("qs_motion_windows", q)]
        rec = F.question_record(key, windows, events, session.get("baseline"), span=span, now_t=now_t)
        rows.append({"session_id": sid, "employer_id": emp, "question_key": key, "question_id": qids.get(key),
                     "status": rec["status"], "outcome": rec["outcome"], "record": rec, "updated_at": now_iso()})
    if rows:
        server.insert("qs_records", rows, on_conflict="session_id,question_key", upsert=True)
        for r in rows:
            messages.append({"topic": session_topic(sid), "event": "record", "payload": r["record"]})
    return len(rows)


# ---------------------------------------------------------------- summary -------------------------------

def get_summary(user: Db, session_id: str) -> dict | None:
    get_session(user, session_id)
    rows = user.select("qs_summaries", Query(eq={"session_id": session_id}))
    return rows[0] if rows else None


SUMMARY_COOLDOWN_S = 120


def make_summary(user: Db, server: Db, session_id: str, summarizer, refresh: bool = False) -> dict:
    """Authorise through the employer's own view, then call the summarizer and cache the result.

    A stored summary is returned instead of calling Gemini again, unless ``refresh`` is asked for and the
    stored one is older than SUMMARY_COOLDOWN_S (each call spends Gemini credits)."""
    session = get_session(user, session_id)
    cached = user.select("qs_summaries", Query(eq={"session_id": session_id}))
    if cached and cached[0].get("status") == "ok":
        from datetime import datetime, timezone
        try:
            age = (datetime.now(timezone.utc) - datetime.fromisoformat(str(cached[0]["created_at"]).replace("Z", "+00:00"))).total_seconds()
        except ValueError:
            age = float("inf")
        if not refresh or age < SUMMARY_COOLDOWN_S:
            return {"status": "ok", "reason": None, "summary": cached[0]["summary"], "model": cached[0].get("model"),
                    "cached": True, "dropped": []}
    records = [r["record"] for r in get_records(user, session_id)]
    if not records:
        raise ServiceError(409, "no QS records yet for this session")
    questions = {}
    if session.get("question_set_id"):
        questions = {q["key"]: q.get("prompt") for q in user.select("qs_questions", Query(eq={"question_set_id": session["question_set_id"]}))}
    from .gemini import build_facts
    facts = build_facts(session.get("candidate_pseudonym") or "candidate", records, session.get("baseline"), questions)
    result = summarizer(facts, records)
    if result["status"] == "ok":
        server.insert("qs_summaries", [{"session_id": session_id, "employer_id": session["employer_id"],
                                        "model": result.get("model"), "status": "ok", "summary": result["summary"],
                                        "error": None, "created_at": now_iso()}],
                      on_conflict="session_id", upsert=True)
    return result

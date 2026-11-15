"""Business logic, independent of HTTP. ``user`` is a Db acting as the employer (RLS applies);
``server`` is a Db acting as the service (RLS bypassed) and is only used where the employer could not
or must not write directly: ingest tokens, motion, events, QS records, summaries."""
from __future__ import annotations

import hashlib
import secrets
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Sequence

import numpy as np

from . import features as F
from .auth import hash_token, new_ingest_token, token_matches
from .db import Db, DbError, Principal, Query, now_iso
from . import states as S
from .models import CandidateIn, ConsentIn, DeviceIn, DeviceSessionIn, IngestBatch, QuestionSetIn, SessionIn
from .realtime import Broadcaster, employer_topic, session_topic

CONSENT_STATEMENT = """QS assessment data (version {version})

While you play, this headset measures how your head and hands move, how you breathe (from the microphone)
and what you do in the game. Your employer sees these measurements, and possible states read from them such
as "signs of stress", under a pseudonymous id, not your name.

- Raw audio never leaves the headset: it is turned into breathing numbers on the device and discarded.
- Raw motion is turned into per-second measurements on our server and discarded.
- Possible states are probabilities, not diagnoses, and not a hiring decision. Movement and breathing
  change for many ordinary reasons, including medical ones.
- Stop at any time by taking the headset off. Ask the employer to delete your data whenever you like.

Agree to start, or decline to play without any recording."""

# Consent versions whose statement names the microphone. A session consented under any other (older) version
# never has audio features stored, even if a headset sends them.
AUDIO_CONSENT_VERSIONS = frozenset({"qs-consent-2026-11-mic"})


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

_pool = ThreadPoolExecutor(max_workers=4)


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


def _active_consent(server: Db, session_id: str) -> dict | None:
    rows = server.select("qs_consents", Query(eq={"session_id": session_id}, is_null={"withdrawn_at": True},
                                              order="granted_at", desc=True, limit=1))
    return rows[0] if rows else None


def _track(dev, t: np.ndarray) -> F.DeviceTrack | None:
    if dev is None or (dev.pos is None and dev.rot is None and dev.vel is None):
        return None
    arr = lambda a: None if a is None else np.asarray(a, dtype=float)  # noqa: E731
    return F.DeviceTrack(t=t, pos=arr(dev.pos), rot=arr(dev.rot), vel=arr(dev.vel),
                         tracked=None if dev.tracked is None else np.asarray(dev.tracked, dtype=bool))


RECORD_REFRESH_WINDOWS = 4   # an open question's record is refreshed from motion every 4 windows (2 s)

LIVE_FIELDS = ("head_ang_speed_mean", "head_ang_speed_peak", "head_yaw_scan_rate", "left_tremor_rms", "right_tremor_rms",
               "left_hand_speed_mean", "right_hand_speed_mean", "left_restless_rms", "right_restless_rms",
               "head_coverage", "left_coverage", "right_coverage",
               "audio_breath_rate_bpm", "audio_breath_amp", "audio_deep_breaths", "audio_since_last_breath_s",
               "audio_voice_fraction")

# How far back a window looks for the game stimulus it may be reacting to (startle, freezing after a scare).
STATE_LOOKBACK_S = S.FREEZE_AFTER_STARTLE_S + 2.0


def live_window(row: dict) -> dict:
    """The compact form of a window sent over Realtime and used by the dashboard's live traces."""
    f = row["features"]
    return {"seq": row["seq"], "t_start": row["t_start"], "t_end": row["t_end"], "question_key": row.get("question_key"),
            "features": {k: f.get(k) for k in LIVE_FIELDS}, "rel": row.get("rel"), "freeze": row.get("freeze"),
            "states": row.get("states") or []}


def ingest(server: Db, session: dict, batch: IngestBatch, broadcaster: Broadcaster) -> dict:
    sid, emp = session["id"], session["employer_id"]
    if session.get("status") == "ended":
        raise ServiceError(409, "session has ended")
    consent = _active_consent(server, sid)
    if not consent:
        raise ServiceError(403, "no consent recorded for this session; ingest refused")
    # Audio only under a consent statement that named the microphone; older consents never cover it.
    audio_ok = batch.audio is not None and consent.get("consent_version") in AUDIO_CONSENT_VERSIONS

    messages: list[dict] = []
    if session.get("status") == "created":
        server.update("qs_sessions", {"status": "live", "started_at": now_iso()}, Query(eq={"id": sid}))
        session = {**session, "status": "live"}
        messages.append({"topic": employer_topic(emp), "event": "session", "payload": {"session_id": sid, "status": "live"}})

    # ---- motion window
    window_rows: list[dict] = []
    record_status = None
    if batch.samples is not None:
        s = batch.samples
        t = np.asarray(s.t, dtype=float)
        t0 = batch.t_start if batch.t_start is not None else float(np.nanmin(t))
        t1 = batch.t_end if batch.t_end is not None else float(np.nanmax(t))
        if t1 < t0:
            raise ServiceError(422, "t_end is before t_start")
        feats = F.window_features(t0, t1, _track(s.head, t), _track(s.left, t), _track(s.right, t),
                                  nominal_fs=batch.rate_hz or 0.0,
                                  audio=batch.audio.model_dump() if audio_ok else None)
        baseline = session.get("baseline")
        rel = F.relative_to_baseline(feats, baseline) if baseline else None
        freeze = F.is_freeze(feats, baseline) if baseline else None
        window_states: list[dict] = []
        if batch.question_key and not batch.events:
            # fetched alongside the stimuli below (each round trip is ~140 ms); used for the record refresh later
            record_status = _pool.submit(server.select, "qs_records",
                                         Query(eq={"session_id": sid, "question_key": batch.question_key}), "status")
        if baseline:
            # the stimuli this window may be reacting to: the batch's own events plus the last few seconds'
            recent = server.select("qs_events", Query(eq={"session_id": sid}, in_={"type": sorted(F.STARTLE_EVENTS)},
                                                      gte={"t": t0 - STATE_LOOKBACK_S}, lte={"t": t1}, limit=50))
            recent += [{"t": e.t, "type": e.type} for e in batch.events if e.type in F.STARTLE_EVENTS]
            window_states = S.compact(S.window_states({**feats, "question_key": batch.question_key}, rel, baseline,
                                                      recent, freeze))
        row = {"session_id": sid, "employer_id": emp, "seq": batch.seq, "t_start": t0, "t_end": t1,
               "question_key": batch.question_key, "features": feats, "rel": rel, "states": window_states}
        window_rows = server.insert("qs_motion_windows", [row], on_conflict="session_id,seq", ignore_duplicates=True)
        for w in window_rows:
            w["freeze"] = freeze
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
            all_events = server.select("qs_events", Query(eq={"session_id": sid}, order="t", tiebreak="id", limit=20000))
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
        rec = record_status.result() if record_status is not None else \
            server.select("qs_records", Query(eq={"session_id": sid, "question_key": batch.question_key}), columns="status")
        if rec and (rec[0]["status"] == "closed" or batch.seq % RECORD_REFRESH_WINDOWS == 0):
            touched.add(batch.question_key)
    if touched:
        spans = F.question_spans(events_all())
        records_updated = _refresh_records(server, session, sorted(touched), events_all(), spans, messages,
                                           now_t=max([e["t"] for e in events_all()] + [w["t_end"] for w in window_rows]))

    ended = any(e["type"] == "session_end" for e in new_events)
    if ended:
        server.update("qs_sessions", {"status": "ended", "ended_at": now_iso()}, Query(eq={"id": sid}))
        messages.append({"topic": employer_topic(emp), "event": "session", "payload": {"session_id": sid, "status": "ended"}})

    broadcaster.send(messages)
    return {"windows_accepted": len(window_rows), "events_accepted": len(new_events),
            "events_duplicate": len(batch.events) - len(new_events), "records_updated": records_updated,
            "audio_accepted": bool(audio_ok and window_rows), "session_ended": ended}


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


def _summary_inputs(db: Db, session: dict) -> tuple[dict, list[dict]]:
    rows = db.select("qs_records", Query(eq={"session_id": session["id"]}))
    records = [r["record"] for r in sorted(rows, key=lambda r: (r["record"].get("t_shown") is None, r["record"].get("t_shown")))]
    questions = {}
    if session.get("question_set_id"):
        questions = {q["key"]: q.get("prompt") for q in db.select("qs_questions", Query(eq={"question_set_id": session["question_set_id"]}))}
    from .gemini import build_facts
    return build_facts(session.get("candidate_pseudonym") or "candidate", records, session.get("baseline"), questions), records


def _store_summary(server: Db, session: dict, result: dict, keep_ok: bool) -> None:
    """Store the outcome. A failed attempt is stored too (so the page can say why), but never over a good one."""
    if result["status"] != "ok" and keep_ok:
        old = server.select("qs_summaries", Query(eq={"session_id": session["id"]}), columns="status")
        if old and old[0]["status"] == "ok":
            return
    server.insert("qs_summaries", [{"session_id": session["id"], "employer_id": session["employer_id"],
                                    "model": result.get("model"), "status": result["status"],
                                    "summary": result.get("summary"), "error": result.get("reason"),
                                    "created_at": now_iso()}], on_conflict="session_id", upsert=True)


def auto_summary(server: Db, session_id: str, summarizer, broadcaster: Broadcaster) -> dict | None:
    """Write the summary when a session ends (run after the ingest response). Never raises: a summary is
    optional, and a failure is stored with its reason so the employer page can show it."""
    try:
        rows = server.select("qs_sessions", Query(eq={"id": session_id}))
        if not rows:
            return None
        session = rows[0]
        if _active_consent(server, session_id) is None:
            return None   # consent withdrawn: nothing more is generated from this session's data
        cand = server.select("qs_candidates", Query(eq={"id": session["candidate_id"]}), columns="pseudonym")
        session["candidate_pseudonym"] = cand[0]["pseudonym"] if cand else None
        facts, records = _summary_inputs(server, session)
        if not records:
            return None
        result = summarizer(facts, records)
        _store_summary(server, session, result, keep_ok=True)
        broadcaster.send([{"topic": session_topic(session_id), "event": "summary",
                           "payload": {k: result.get(k) for k in ("status", "reason", "summary", "model")}}])
        return result
    except Exception:   # noqa: BLE001 - background work must not crash the worker
        import logging
        logging.getLogger("qs.service").exception("auto summary failed for %s", session_id)
        return None


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
    if _active_consent(server, session_id) is None:
        raise ServiceError(409, "consent was withdrawn for this session; no summary can be generated")
    facts, records = _summary_inputs(user, session)
    if not records:
        raise ServiceError(409, "no QS records yet for this session")
    result = summarizer(facts, records)
    _store_summary(server, session, result, keep_ok=True)
    return result


# ---------------------------------------------------------------- devices -------------------------------

DEVICE_COLUMNS = "id,employer_id,question_set_id,name,created_by,created_at,last_used_at,revoked_at"
DEVICE_SESSIONS_PER_10_MIN = 30   # a headset starts one session per run; more than this is misuse


def new_device_key() -> str:
    return "qsd_" + secrets.token_urlsafe(32)


def _owner_of(user: Db, principal: Principal, employer_id: str) -> None:
    roles = {str(m["employer_id"]): m["role"] for m in employer_memberships(user, principal)}
    if roles.get(employer_id) != "owner":
        raise ServiceError(403, "only employer owners may manage devices")


def create_device(user: Db, server: Db, principal: Principal, body: DeviceIn) -> dict:
    employer_id = resolve_employer(user, principal, body.employer_id)
    _owner_of(user, principal, employer_id)
    qset = user.select("qs_question_sets", Query(eq={"id": str(body.question_set_id)}))
    if not qset or str(qset[0]["employer_id"]) != employer_id:
        raise ServiceError(404, "question set not found")
    key = new_device_key()
    row = server.insert("qs_devices", [{"employer_id": employer_id, "question_set_id": str(body.question_set_id),
                                        "name": body.name, "key_hash": hash_token(key), "created_by": principal.user_id}])[0]
    row.pop("key_hash", None)
    return {"device": row, "device_key": key,
            "note": "The device key is shown once. Put it in the headset build's qs.json; it can only start sessions."}


def list_devices(user: Db) -> list[dict]:
    return user.select("qs_devices", Query(order="created_at", desc=True), columns=DEVICE_COLUMNS)


def revoke_device(user: Db, server: Db, principal: Principal, device_id: str) -> dict:
    rows = user.select("qs_devices", Query(eq={"id": device_id}), columns=DEVICE_COLUMNS)
    if not rows:
        raise ServiceError(404, "device not found")
    _owner_of(user, principal, str(rows[0]["employer_id"]))
    server.update("qs_devices", {"revoked_at": now_iso()}, Query(eq={"id": device_id}))
    return {"revoked": device_id}


def device_session(server: Db, device_key: str, body: DeviceSessionIn | None = None) -> dict:
    """The one thing a device key can do: start a new pseudonymous candidate and session for its employer and
    question set. Returns the session id and its ingest token. Reads nothing else and returns nothing else."""
    if not device_key or not device_key.startswith("qsd_"):
        raise ServiceError(401, "missing or invalid device key")
    rows = server.select("qs_devices", Query(eq={"key_hash": hash_token(device_key)}, limit=1))
    if not rows or rows[0].get("revoked_at") or not token_matches(device_key, rows[0]["key_hash"]):
        raise ServiceError(401, "missing or invalid device key")
    dev = rows[0]
    from datetime import datetime, timedelta, timezone
    since = (datetime.now(timezone.utc) - timedelta(minutes=10)).isoformat()
    recent = server.select("qs_sessions", Query(eq={"device_id": dev["id"]}, gte={"created_at": since}, limit=DEVICE_SESSIONS_PER_10_MIN + 1),
                           columns="id")
    if len(recent) > DEVICE_SESSIONS_PER_10_MIN:
        raise ServiceError(429, "too many sessions from this device; try again in a few minutes")
    emp = str(dev["employer_id"])
    cand = None
    for _ in range(5):   # pseudonym collisions are rare (16M space); retry a few times
        try:
            cand = server.insert("qs_candidates", [{"employer_id": emp, "pseudonym": "C-" + secrets.token_hex(3).upper()}])[0]
            break
        except DbError as ex:
            if ex.status != 409:
                raise
    if cand is None:
        raise ServiceError(503, "could not allocate a pseudonym")
    session = server.insert("qs_sessions", [{"employer_id": emp, "candidate_id": cand["id"],
                                             "question_set_id": dev.get("question_set_id"), "device_id": dev["id"]}])[0]
    token = new_ingest_token()
    server.insert("qs_session_tokens", [{"session_id": session["id"], "token_hash": hash_token(token)}])
    server.update("qs_devices", {"last_used_at": now_iso()}, Query(eq={"id": dev["id"]}))
    return {"session_id": session["id"], "ingest_token": token, "candidate_pseudonym": cand["pseudonym"]}

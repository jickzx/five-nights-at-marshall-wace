"""HTTP API. Thin: parse, authenticate, call ``service``, map errors. See README.md for the endpoint list."""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any, Callable, Optional

from fastapi import Depends, FastAPI, Header, HTTPException, Query as Q, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from . import gemini, service
from .auth import AuthError, verify_employer_token
from .config import Settings
from .db import Db, DbError, MemoryStore, PostgrestDb, Principal
from .models import CandidateIn, ConsentIn, IngestBatch, QuestionSetIn, SessionIn
from .realtime import Broadcaster, NullBroadcaster, SupabaseBroadcaster

log = logging.getLogger("qs.api")

MAX_INGEST_BYTES = 1_000_000   # Vercel's hard cap is 4.5 MB; a 1 s window at 90 Hz is ~60 KB of JSON


@dataclass
class Backend:
    """Everything a request needs, built once from settings (or injected by tests)."""
    settings: Settings
    user_db: Callable[[Principal], Db]
    server_db: Callable[[], Db]
    broadcaster: Broadcaster
    summarizer: Callable[[dict, list], dict]
    verify: Callable[[str], Principal]


def backend_from_settings(settings: Settings, memory: MemoryStore | None = None) -> Backend:
    def summarizer(facts, records):
        if not settings.gemini_usable:
            return gemini.unavailable("Gemini is not configured on this server.")
        return gemini.summarize(settings.gemini_api_key, settings.gemini_model, facts, records)

    if settings.store == "memory":
        store = memory or MemoryStore()

        def verify(token: str) -> Principal:
            return verify_employer_token(token, settings)
        return Backend(settings, lambda p: store.db(p), lambda: store.db(None), NullBroadcaster(), summarizer, verify)

    configured = settings.supabase_url and settings.supabase_publishable_key and settings.supabase_secret_key

    def need():
        if not configured:
            raise HTTPException(503, "QS backend is not configured (SUPABASE_URL / keys missing)")

    def user_db(p: Principal) -> Db:
        need()
        return PostgrestDb(settings.supabase_url, settings.supabase_publishable_key, p.token)

    def server_db() -> Db:
        need()
        return PostgrestDb(settings.supabase_url, settings.supabase_secret_key, None)

    broadcaster = SupabaseBroadcaster(settings.supabase_url, settings.supabase_secret_key) if configured else NullBroadcaster()
    return Backend(settings, user_db, server_db, broadcaster, summarizer,
                   lambda token: verify_employer_token(token, settings))


def create_app(backend: Backend | None = None) -> FastAPI:
    settings = backend.settings if backend else Settings.from_env()
    b = backend or backend_from_settings(settings)
    app = FastAPI(title="QS telemetry API", version="0.1.0",
                  description="Per-question motion records (QS) from VR assessment sessions.")
    app.state.backend = b
    if settings.cors_origins:
        app.add_middleware(CORSMiddleware, allow_origins=list(settings.cors_origins), allow_credentials=False,
                           allow_methods=["GET", "POST", "DELETE"], allow_headers=["Authorization", "Content-Type"])

    @app.exception_handler(service.ServiceError)
    async def _service_error(_: Request, ex: service.ServiceError):
        return JSONResponse({"detail": str(ex)}, status_code=ex.status)

    @app.exception_handler(DbError)
    async def _db_error(_: Request, ex: DbError):
        # Database messages can name tables and constraints; keep them out of responses.
        log.warning("db error %s %s", ex.status, ex.code)
        msg = {403: "not permitted", 404: "not found", 409: "conflicts with existing data",
               422: "invalid value"}.get(ex.status, "database error")
        return JSONResponse({"detail": msg}, status_code=ex.status if ex.status < 600 else 502)

    # ---- dependencies
    def employer(authorization: Optional[str] = Header(default=None)) -> Principal:
        token = (authorization or "").removeprefix("Bearer ").strip() if authorization else ""
        try:
            return b.verify(token)
        except AuthError as ex:
            raise HTTPException(401, str(ex), headers={"WWW-Authenticate": "Bearer"}) from None

    def user(p: Principal = Depends(employer)) -> Db:
        return b.user_db(p)

    def ingest_session(session_id: str, x_qs_ingest_token: Optional[str] = Header(default=None)) -> dict:
        return service.authenticate_ingest(b.server_db(), _uuid(session_id), x_qs_ingest_token or "")

    # ---- public
    @app.get("/healthz")
    def healthz():
        return {"ok": True, "store": settings.store, "gemini": settings.gemini_usable}

    @app.get("/v1/consent")
    def consent_text():
        text, digest = service.consent_statement(settings.consent_version)
        return {"consent_version": settings.consent_version, "statement": text, "statement_sha256": digest}

    # ---- headset (ingest token)
    @app.post("/v1/ingest/{session_id}/consent", status_code=201)
    def give_consent(body: ConsentIn, session: dict = Depends(ingest_session)):
        return service.record_consent(b.server_db(), session, body, settings.consent_version)

    @app.post("/v1/ingest/{session_id}/consent/withdraw")
    def withdraw(session: dict = Depends(ingest_session)):
        return service.withdraw_consent(b.server_db(), session, b.broadcaster)

    @app.post("/v1/ingest/{session_id}", status_code=202)
    async def ingest(request: Request, session_id: str, x_qs_ingest_token: Optional[str] = Header(default=None)):
        length = request.headers.get("content-length")
        if length and length.isdigit() and int(length) > MAX_INGEST_BYTES:
            raise HTTPException(413, "batch too large")
        raw = await request.body()
        if len(raw) > MAX_INGEST_BYTES:
            raise HTTPException(413, "batch too large")
        session = service.authenticate_ingest(b.server_db(), _uuid(session_id), x_qs_ingest_token or "")
        try:
            batch = IngestBatch.model_validate_json(raw)
        except ValueError as ex:
            raise HTTPException(422, _short(ex)) from None
        return service.ingest(b.server_db(), session, batch, b.broadcaster)

    # ---- employer (Supabase JWT, RLS-scoped)
    @app.get("/v1/me")
    def me(p: Principal = Depends(employer), db: Db = Depends(user)):
        return {"user_id": p.user_id, "employers": service.employer_memberships(db, p)}

    @app.post("/v1/question-sets", status_code=201)
    def create_question_set(body: QuestionSetIn, p: Principal = Depends(employer), db: Db = Depends(user)):
        return service.create_question_set(db, p, body)

    @app.get("/v1/question-sets")
    def list_question_sets(db: Db = Depends(user)):
        return service.list_question_sets(db)

    @app.post("/v1/candidates", status_code=201)
    def create_candidate(body: CandidateIn, p: Principal = Depends(employer), db: Db = Depends(user)):
        return service.create_candidate(db, p, body)

    @app.get("/v1/candidates/{candidate_id}/summary")
    def candidate_summary(candidate_id: str, db: Db = Depends(user)):
        return service.candidate_summary(db, _uuid(candidate_id))

    @app.delete("/v1/candidates/{candidate_id}")
    def delete_candidate(candidate_id: str, db: Db = Depends(user)):
        return {"deleted": service.delete_candidate(db, _uuid(candidate_id))}

    @app.post("/v1/sessions", status_code=201)
    def create_session(body: SessionIn, p: Principal = Depends(employer), db: Db = Depends(user)):
        return service.create_session(db, b.server_db(), p, body)

    @app.get("/v1/sessions")
    def list_sessions(status: Optional[str] = Q(default=None, pattern="^(created|live|ended)$"),
                      limit: int = Q(default=100, ge=1, le=500), db: Db = Depends(user)):
        return service.list_sessions(db, status, limit)

    @app.get("/v1/sessions/{session_id}")
    def get_session(session_id: str, db: Db = Depends(user)):
        return service.get_session(db, _uuid(session_id))

    @app.get("/v1/sessions/{session_id}/qs")
    def get_records(session_id: str, db: Db = Depends(user)):
        return service.get_records(db, _uuid(session_id))

    @app.get("/v1/sessions/{session_id}/windows")
    def get_windows(session_id: str, after_seq: int = Q(default=-1, ge=-1), limit: int = Q(default=2000, ge=1, le=5000),
                    tail: bool = False, db: Db = Depends(user)):
        return [service.live_window(w) for w in service.get_windows(db, _uuid(session_id), after_seq, limit, tail)]

    @app.get("/v1/sessions/{session_id}/events")
    def get_events(session_id: str, db: Db = Depends(user)):
        return service.get_events(db, _uuid(session_id))

    @app.get("/v1/sessions/{session_id}/summary")
    def get_summary(session_id: str, db: Db = Depends(user)):
        row = service.get_summary(db, _uuid(session_id))
        if row is None:
            raise HTTPException(404, "no summary yet; POST to create one")
        return row

    @app.post("/v1/sessions/{session_id}/summary")
    def make_summary(session_id: str, refresh: bool = False, db: Db = Depends(user)):
        return service.make_summary(db, b.server_db(), _uuid(session_id), b.summarizer, refresh)

    return app


def _uuid(value: str) -> str:
    import uuid
    try:
        return str(uuid.UUID(value))
    except ValueError:
        raise HTTPException(404, "not found") from None


def _short(ex: Exception) -> Any:
    errors = getattr(ex, "errors", None)
    if callable(errors):
        return [{"loc": e.get("loc"), "msg": e.get("msg")} for e in errors()[:10]]
    return str(ex)[:500]

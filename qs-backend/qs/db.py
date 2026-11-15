"""Database access: a tiny table API with two implementations.

* ``PostgrestDb`` talks to Supabase's PostgREST. Created per request either *as the employer* (publishable
  key + the employer's own JWT, so Postgres RLS decides what they can see and do) or *as the server*
  (secret key, bypasses RLS; used only for ingest and derived data).
* ``MemoryDb`` keeps tables in a dict and enforces the same rules as the SQL policies, so the API can be
  tested and run locally without a database. ``tests/test_sql_rls.py`` checks the real policies.
"""
from __future__ import annotations

import copy
import itertools
import threading
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Iterable, Protocol

import httpx


class DbError(Exception):
    def __init__(self, status: int, message: str, code: str | None = None):
        super().__init__(message)
        self.status = status
        self.code = code


@dataclass
class Query:
    eq: dict[str, Any] = field(default_factory=dict)
    in_: dict[str, Iterable[Any]] = field(default_factory=dict)
    gte: dict[str, Any] = field(default_factory=dict)
    lte: dict[str, Any] = field(default_factory=dict)
    gt: dict[str, Any] = field(default_factory=dict)
    is_null: dict[str, bool] = field(default_factory=dict)
    order: str | None = None
    desc: bool = False
    tiebreak: str | None = None   # secondary order column so offset paging is stable on a non-unique order
    limit: int | None = None


class Db(Protocol):
    def select(self, table: str, q: Query | None = None, columns: str = "*") -> list[dict]: ...
    def insert(self, table: str, rows: list[dict], *, on_conflict: str | None = None,
               ignore_duplicates: bool = False, upsert: bool = False) -> list[dict]: ...
    def update(self, table: str, values: dict, q: Query) -> list[dict]: ...
    def rpc(self, fn: str, args: dict) -> Any: ...


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


# ---------------------------------------------------------------- PostgREST -----------------------------

_SHARED: httpx.Client | None = None


def shared_client() -> httpx.Client:
    """One keep-alive client per process, so warm functions reuse TLS connections to Supabase."""
    global _SHARED
    if _SHARED is None:
        _SHARED = httpx.Client(timeout=10.0, limits=httpx.Limits(max_keepalive_connections=10))
    return _SHARED


# PostgREST returns at most this many rows per request by default (Supabase "Max rows").
PAGE = 1000


def _fmt(v: Any) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    return str(v)


class PostgrestDb:
    def __init__(self, base_url: str, api_key: str, bearer: str | None, client: httpx.Client | None = None):
        self._base = base_url.rstrip("/") + "/rest/v1"
        headers = {"apikey": api_key, "Content-Type": "application/json"}
        # New-style keys (sb_publishable_/sb_secret_) are not JWTs and go only in `apikey`. A legacy
        # service_role JWT also goes in Authorization. An employer call carries the employer's JWT there.
        if bearer:
            headers["Authorization"] = f"Bearer {bearer}"
        elif not api_key.startswith("sb_"):
            headers["Authorization"] = f"Bearer {api_key}"
        self._headers = headers
        self._client = client or shared_client()

    @staticmethod
    def _params(q: Query | None) -> list[tuple[str, str]]:
        p: list[tuple[str, str]] = []
        if not q:
            return p
        p += [(k, f"eq.{_fmt(v)}") for k, v in q.eq.items()]
        p += [(k, "in.(" + ",".join(_fmt(x) for x in v) + ")") for k, v in q.in_.items()]
        p += [(k, f"gte.{_fmt(v)}") for k, v in q.gte.items()]
        p += [(k, f"lte.{_fmt(v)}") for k, v in q.lte.items()]
        p += [(k, f"gt.{_fmt(v)}") for k, v in q.gt.items()]
        p += [(k, "is.null" if v else "not.is.null") for k, v in q.is_null.items()]
        if q.order:
            d = "desc" if q.desc else "asc"
            p.append(("order", f"{q.order}.{d}" + (f",{q.tiebreak}.{d}" if q.tiebreak else "")))
        return p

    def _send(self, method: str, path: str, *, params=None, json=None, prefer: str | None = None) -> Any:
        headers = dict(self._headers)
        if prefer:
            headers["Prefer"] = prefer
        try:
            r = self._client.request(method, self._base + path, params=params, json=json, headers=headers)
        except httpx.HTTPError as ex:
            raise DbError(503, f"database unreachable: {type(ex).__name__}") from None
        except ValueError:
            raise DbError(422, "value cannot be stored as JSON") from None
        if r.status_code >= 400:
            try:
                body = r.json()
            except ValueError:
                body = {}
            code = body.get("code")
            status = {"42501": 403, "23505": 409, "23503": 409, "23514": 422, "P0002": 404, "PGRST116": 404,
                      "22P02": 422}.get(code or "", r.status_code if r.status_code < 500 else 502)
            if r.status_code in (401, 403):
                status = 403
            raise DbError(status, body.get("message") or f"database error {r.status_code}", code)
        if not r.content:
            return None
        return r.json()

    def select(self, table, q=None, columns="*"):
        """Pages through results, since one PostgREST response holds at most PAGE rows."""
        want = q.limit if q and q.limit is not None else None
        rows: list[dict] = []
        while True:
            take = PAGE if want is None else min(PAGE, want - len(rows))
            params = [("select", columns)] + self._params(q) + [("limit", str(take)), ("offset", str(len(rows)))]
            page = self._send("GET", f"/{table}", params=params) or []
            rows.extend(page)
            if len(page) < take or (want is not None and len(rows) >= want):
                return rows

    def insert(self, table, rows, *, on_conflict=None, ignore_duplicates=False, upsert=False):
        if not rows:
            return []
        prefer = ["return=representation"]
        if ignore_duplicates:
            prefer.append("resolution=ignore-duplicates")
        elif upsert:
            prefer.append("resolution=merge-duplicates")
        params = [("on_conflict", on_conflict)] if on_conflict else None
        return self._send("POST", f"/{table}", params=params, json=rows, prefer=",".join(prefer)) or []

    def update(self, table, values, q):
        return self._send("PATCH", f"/{table}", params=self._params(q), json=values, prefer="return=representation") or []

    def rpc(self, fn, args):
        return self._send("POST", f"/rpc/{fn}", json=args)


# ---------------------------------------------------------------- memory --------------------------------

# Unique keys (for duplicate detection / upsert) and default columns, mirroring the migration.
UNIQUE = {
    "qs_employers": [("id",)],
    "qs_employer_members": [("employer_id", "user_id")],
    "qs_question_sets": [("id",)],
    "qs_questions": [("id",), ("question_set_id", "key")],
    "qs_candidates": [("id",), ("employer_id", "pseudonym")],
    "qs_candidate_identities": [("candidate_id",)],
    "qs_sessions": [("id",)],
    "qs_session_tokens": [("session_id",)],
    "qs_consents": [("id",)],
    "qs_motion_windows": [("id",), ("session_id", "seq")],
    "qs_events": [("id",), ("session_id", "client_event_id")],
    "qs_records": [("session_id", "question_key")],
    "qs_summaries": [("session_id",)],
    "qs_devices": [("id",), ("key_hash",)],
}
UUID_ID = {"qs_employers", "qs_question_sets", "qs_questions", "qs_candidates", "qs_sessions", "qs_consents", "qs_devices"}
SERIAL_ID = {"qs_motion_windows", "qs_events"}
# What an employer (authenticated role) may do, per table, as in the SQL policies.
MEMBER_READ = {"qs_question_sets", "qs_questions", "qs_candidates", "qs_sessions", "qs_consents",
               "qs_motion_windows", "qs_events", "qs_records", "qs_summaries"}
MEMBER_INSERT = {"qs_question_sets", "qs_questions", "qs_candidates", "qs_sessions"}
OWNER_RW = {"qs_candidate_identities"}
OWNER_READ = {"qs_devices"}            # owners read devices, but never the key_hash column; only the server writes
HIDDEN_COLUMNS = {"qs_devices": {"key_hash"}}
SERVER_ONLY = {"qs_session_tokens"}


@dataclass
class Principal:
    user_id: str
    token: str = ""


class MemoryStore:
    """Holds the tables; hand out ``MemoryDb`` views as the server or as an employer."""

    def __init__(self):
        self.tables: dict[str, list[dict]] = {t: [] for t in UNIQUE}
        self.lock = threading.RLock()
        self._serial = itertools.count(1)
        self.requests = 0   # select round trips, for tests

    def db(self, principal: Principal | None = None) -> "MemoryDb":
        return MemoryDb(self, principal)


def _match(row: dict, q: Query | None) -> bool:
    if not q:
        return True
    for k, v in q.eq.items():
        if str(row.get(k)) != str(v) if not isinstance(v, bool) else row.get(k) is not v:
            return False
    for k, vs in q.in_.items():
        if str(row.get(k)) not in {str(x) for x in vs}:
            return False
    for k, v in q.gte.items():
        if row.get(k) is None or row[k] < v:
            return False
    for k, v in q.lte.items():
        if row.get(k) is None or row[k] > v:
            return False
    for k, v in q.gt.items():
        if row.get(k) is None or row[k] <= v:
            return False
    for k, want_null in q.is_null.items():
        if (row.get(k) is None) != want_null:
            return False
    return True


class MemoryDb:
    def __init__(self, store: MemoryStore, principal: Principal | None):
        self.s = store
        self.p = principal

    # -- policy emulation
    def _member_ids(self, owner_only=False) -> set[str]:
        if self.p is None:
            return set()
        return {str(m["employer_id"]) for m in self.s.tables["qs_employer_members"]
                if str(m["user_id"]) == self.p.user_id and (not owner_only or m["role"] == "owner")}

    def _visible(self, table: str, row: dict) -> bool:
        if self.p is None:
            return True
        if table in SERVER_ONLY:
            raise DbError(403, "permission denied", "42501")
        if table == "qs_employers":
            return str(row["id"]) in self._member_ids()
        if table == "qs_employer_members":
            return str(row["user_id"]) == self.p.user_id
        if table in OWNER_RW or table in OWNER_READ:
            return str(row["employer_id"]) in self._member_ids(owner_only=True)
        if table in MEMBER_READ:
            return str(row["employer_id"]) in self._member_ids()
        return False

    def _check_insert(self, table: str, row: dict):
        if self.p is None:
            return
        allowed = (table in MEMBER_INSERT and str(row.get("employer_id")) in self._member_ids()) or \
                  (table in OWNER_RW and str(row.get("employer_id")) in self._member_ids(owner_only=True))
        if table == "qs_sessions" and (row.get("status", "created") != "created" or row.get("baseline") is not None
                                       or row.get("device_id") is not None):
            allowed = False
        if not allowed:
            raise DbError(403, "new row violates row-level security policy", "42501")

    def _check_fks(self, table: str, row: dict):
        """The composite foreign keys that matter for cross-employer safety."""
        def parent(t: str, key: str, value: Any) -> dict | None:
            return next((r for r in self.s.tables[t] if str(r[key]) == str(value)), None)
        refs = []
        if table in ("qs_sessions",):
            refs.append(("qs_candidates", "id", row.get("candidate_id")))
            if row.get("device_id"):
                refs.append(("qs_devices", "id", row.get("device_id")))
        if table == "qs_devices" and row.get("question_set_id"):
            refs.append(("qs_question_sets", "id", row.get("question_set_id")))
            if row.get("question_set_id"):
                refs.append(("qs_question_sets", "id", row.get("question_set_id")))
        if table == "qs_candidate_identities":
            refs.append(("qs_candidates", "id", row.get("candidate_id")))
        if table == "qs_questions":
            refs.append(("qs_question_sets", "id", row.get("question_set_id")))
        if table in ("qs_consents", "qs_motion_windows", "qs_events", "qs_records", "qs_summaries"):
            refs.append(("qs_sessions", "id", row.get("session_id")))
        for t, key, value in refs:
            par = parent(t, key, value)
            if par is None or str(par["employer_id"]) != str(row.get("employer_id")):
                raise DbError(409, f"foreign key violation on {table}", "23503")

    # -- table API
    def select(self, table, q=None, columns="*"):
        """Same paging contract as PostgrestDb: at most PAGE rows per underlying request."""
        with self.s.lock:
            rows = [copy.deepcopy(r) for r in self.s.tables[table] if self._visible(table, r) and _match(r, q)]
        if self.p is not None and table in HIDDEN_COLUMNS:
            hidden = HIDDEN_COLUMNS[table]
            if columns == "*" or hidden & set(columns.split(",")):
                raise DbError(403, f"permission denied for table {table}", "42501")   # column grant, as in SQL
        if q and q.order:
            cols = [q.order] + ([q.tiebreak] if q.tiebreak else [])
            rows.sort(key=lambda r: tuple((r.get(c) is None, r.get(c)) for c in cols), reverse=q.desc)
        if columns != "*":
            cols = [c.strip() for c in columns.split(",")]
            rows = [{c: r.get(c) for c in cols} for r in rows]
        want = q.limit if q and q.limit is not None else None
        out: list[dict] = []
        while True:
            take = PAGE if want is None else min(PAGE, want - len(out))
            page = rows[len(out): len(out) + take]
            self.s.requests += 1
            out.extend(page)
            if len(page) < take or (want is not None and len(out) >= want):
                return out

    def insert(self, table, rows, *, on_conflict=None, ignore_duplicates=False, upsert=False):
        out = []
        with self.s.lock:
            for row in rows:
                row = copy.deepcopy(row)
                if table in UUID_ID:
                    row.setdefault("id", str(uuid.uuid4()))
                if table in SERIAL_ID:
                    row["id"] = next(self.s._serial)
                row.setdefault("created_at", now_iso())
                if table == "qs_sessions":
                    row.setdefault("status", "created")
                    row.setdefault("baseline", None)
                self._check_insert(table, row)
                self._check_fks(table, row)
                keys = [tuple(on_conflict.split(","))] if on_conflict else UNIQUE[table]
                existing = None
                for key in keys:
                    existing = next((r for r in self.s.tables[table]
                                     if all(str(r.get(k)) == str(row.get(k)) for k in key)), None)
                    if existing is not None:
                        break
                if existing is not None:
                    if ignore_duplicates:
                        continue
                    if upsert:
                        existing.update(row)
                        out.append(copy.deepcopy(existing))
                        continue
                    raise DbError(409, "duplicate key value violates unique constraint", "23505")
                self.s.tables[table].append(row)
                out.append(copy.deepcopy(row))
        return out

    def update(self, table, values, q):
        if self.p is not None:
            return []   # no UPDATE policies for employers: silently matches nothing, as in Postgres
        out = []
        with self.s.lock:
            for r in self.s.tables[table]:
                if _match(r, q):
                    r.update(copy.deepcopy(values))
                    out.append(copy.deepcopy(r))
        return out

    def rpc(self, fn, args):
        if fn != "qs_delete_candidate":
            raise DbError(404, f"no function {fn}")
        cid = str(args["p_candidate_id"])
        with self.s.lock:
            cand = next((c for c in self.s.tables["qs_candidates"] if str(c["id"]) == cid), None)
            if cand is None or str(cand["employer_id"]) not in self._member_ids(owner_only=True):
                raise DbError(404, "candidate not found", "P0002")
            sessions = {str(s["id"]) for s in self.s.tables["qs_sessions"] if str(s["candidate_id"]) == cid}
            counts = {"motion_windows": sum(1 for w in self.s.tables["qs_motion_windows"] if str(w["session_id"]) in sessions),
                      "events": sum(1 for e in self.s.tables["qs_events"] if str(e["session_id"]) in sessions)}
            for t in ("qs_session_tokens", "qs_consents", "qs_motion_windows", "qs_events", "qs_records", "qs_summaries"):
                self.s.tables[t] = [r for r in self.s.tables[t] if str(r["session_id"]) not in sessions]
            self.s.tables["qs_sessions"] = [s for s in self.s.tables["qs_sessions"] if str(s["id"]) not in sessions]
            self.s.tables["qs_candidate_identities"] = [i for i in self.s.tables["qs_candidate_identities"]
                                                        if str(i["candidate_id"]) != cid]
            self.s.tables["qs_candidates"] = [c for c in self.s.tables["qs_candidates"] if str(c["id"]) != cid]
        return {"candidate_id": cid, "sessions": len(sessions), **counts, "realtime_messages": 0}


# ---------------------------------------------------------------- public view ---------------------------

PUBLIC_TABLES = frozenset({"qs_question_sets", "qs_questions", "qs_candidates", "qs_sessions", "qs_consents",
                           "qs_motion_windows", "qs_events", "qs_records", "qs_summaries"})


class PublicDb:
    """Read-only view of ONE employer's pseudonymous data, for the no-sign-in demo page (QS_PUBLIC_EMPLOYER_ID).

    Every read is filtered to that employer; identities, tokens, devices and memberships are not reachable, and
    every write raises. Built on the server connection, so the employer filter here is the only guard: keep the
    table list to pseudonymous data."""

    def __init__(self, server: Db, employer_id: str):
        self._db = server
        self._emp = employer_id

    def select(self, table, q=None, columns="*"):
        if table not in PUBLIC_TABLES:
            raise DbError(403, "not available in the public view")
        q = copy.deepcopy(q) if q else Query()
        q.eq["employer_id"] = self._emp
        return self._db.select(table, q, columns)

    def insert(self, *a, **k):
        raise DbError(403, "the public view is read-only")

    def update(self, *a, **k):
        raise DbError(403, "the public view is read-only")

    def rpc(self, *a, **k):
        raise DbError(403, "the public view is read-only")

"""Apply the migration to a real local Postgres and check row-level security as two employers.

Uses the ``pgserver`` package (a pip-installed Postgres) so no cloud project or Docker is needed. The
Supabase-provided pieces the migration relies on (roles, ``auth.uid()``, ``realtime.messages``,
``realtime.topic()``) are stubbed below the way Supabase defines them. Skipped when pgserver is absent.
"""
from __future__ import annotations

import json
import pathlib
import uuid

import pytest

pgserver = pytest.importorskip("pgserver")
psycopg = pytest.importorskip("psycopg")

MIGRATIONS = sorted((pathlib.Path(__file__).parent.parent / "supabase" / "migrations").glob("*.sql"))

SUPABASE_STUBS = """
create role anon nologin; create role authenticated nologin; create role service_role nologin bypassrls;
grant usage on schema public to anon, authenticated, service_role;
alter default privileges in schema public grant all on tables to anon, authenticated, service_role;
alter default privileges in schema public grant all on functions to anon, authenticated, service_role;
alter default privileges in schema public grant all on sequences to anon, authenticated, service_role;
create schema auth;
create function auth.uid() returns uuid language sql stable as
  $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
grant usage on schema auth to anon, authenticated, service_role;
create schema realtime;
create table realtime.messages (id bigserial primary key, topic text not null, extension text not null,
  payload jsonb, event text, private boolean default true, inserted_at timestamptz default now());
alter table realtime.messages enable row level security;
create function realtime.topic() returns text language sql stable as
  $$ select nullif(current_setting('realtime.topic', true), '') $$;
grant usage on schema realtime to anon, authenticated, service_role;
grant select, insert on realtime.messages to authenticated;
"""

A_USER, A_VIEWER, B_USER = (str(uuid.uuid4()) for _ in range(3))


@pytest.fixture(scope="module")
def db(tmp_path_factory):
    srv = pgserver.get_server(tmp_path_factory.mktemp("pg"), cleanup_mode="stop")
    uri = srv.get_uri()
    with psycopg.connect(uri, autocommit=True) as c:
        c.execute(SUPABASE_STUBS)
        for m in MIGRATIONS:
            c.execute(m.read_text())
        ids = seed(c)
    yield uri, ids
    srv.cleanup()


def seed(c) -> dict:
    """Two employers, each with a candidate, identity, session, consent, window, event, record."""
    ids = {}
    for tag, owner in (("a", A_USER), ("b", B_USER)):
        emp = c.execute("insert into qs_employers (name) values (%s) returning id", (f"Employer {tag}",)).fetchone()[0]
        c.execute("insert into qs_employer_members values (%s, %s, 'owner')", (emp, owner))
        cand = c.execute("insert into qs_candidates (employer_id, pseudonym) values (%s, %s) returning id",
                         (emp, f"C-{tag.upper()}001")).fetchone()[0]
        c.execute("insert into qs_candidate_identities (candidate_id, employer_id, display_name) values (%s,%s,%s)",
                  (cand, emp, f"Real Person {tag}"))
        sess = c.execute("insert into qs_sessions (employer_id, candidate_id) values (%s,%s) returning id",
                         (emp, cand)).fetchone()[0]
        c.execute("insert into qs_session_tokens values (%s, %s)", (sess, "0" * 64))
        c.execute("insert into qs_consents (session_id, employer_id, consent_version, statement_sha256) values (%s,%s,'v1',%s)",
                  (sess, emp, "a" * 64))
        c.execute("insert into qs_motion_windows (session_id, employer_id, seq, t_start, t_end, features) values (%s,%s,0,0,1,'{}')",
                  (sess, emp))
        c.execute("insert into qs_events (session_id, employer_id, client_event_id, t, type) values (%s,%s,%s,0,'session_start')",
                  (sess, emp, str(uuid.uuid4())))
        c.execute("insert into qs_records (session_id, employer_id, question_key, status, outcome, record) values (%s,%s,'q1','closed','correct','{}')",
                  (sess, emp))
        c.execute("insert into realtime.messages (topic, extension, payload) values (%s, 'broadcast', '{}')",
                  (f"qs:session:{sess}",))
        ids[tag] = {"employer": str(emp), "candidate": str(cand), "session": str(sess)}
    c.execute("insert into qs_employer_members values (%s, %s, 'viewer')", (ids["a"]["employer"], A_VIEWER))
    return ids


def as_user(uri, user):
    conn = psycopg.connect(uri)
    conn.execute("set role authenticated")
    conn.execute("select set_config('request.jwt.claim.sub', %s, false)", (user or "",))
    return conn


TABLES = ["qs_employers", "qs_candidates", "qs_sessions", "qs_consents", "qs_motion_windows",
          "qs_events", "qs_records"]


@pytest.mark.parametrize("table", TABLES)
def test_employer_sees_only_own_rows(db, table):
    uri, ids = db
    with as_user(uri, A_USER) as c:
        col = "id" if table == "qs_employers" else "employer_id"
        rows = [str(r[0]) for r in c.execute(f"select {col} from {table}").fetchall()]
    assert rows and set(rows) == {ids["a"]["employer"]}


def test_every_qs_table_has_rls_enabled(db):
    uri, _ = db
    with psycopg.connect(uri) as c:
        rows = c.execute("select relname, relrowsecurity from pg_class where relname like 'qs\\_%' and relkind = 'r'").fetchall()
    assert len(rows) == 14
    assert all(rls for _, rls in rows), rows


def test_tokens_are_invisible_to_employers(db):
    uri, _ = db
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("select * from qs_session_tokens").fetchall()


def test_anon_sees_nothing(db):
    uri, _ = db
    with psycopg.connect(uri) as c:
        c.execute("set role anon")
        with pytest.raises(psycopg.errors.InsufficientPrivilege):
            c.execute("select * from qs_sessions").fetchall()


def test_identity_is_owner_only(db):
    uri, _ = db
    with as_user(uri, A_USER) as c:
        assert len(c.execute("select * from qs_candidate_identities").fetchall()) == 1
    with as_user(uri, A_VIEWER) as c:
        assert c.execute("select * from qs_candidate_identities").fetchall() == []
        assert len(c.execute("select * from qs_candidates").fetchall()) == 1


def test_employer_cannot_write_motion_or_attach_foreign_candidate(db):
    uri, ids = db
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("insert into qs_motion_windows (session_id, employer_id, seq, t_start, t_end, features) values (%s,%s,9,0,1,'{}')",
                  (ids["a"]["session"], ids["a"]["employer"]))
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.Error):
        # Employer A tries to open a session for employer B's candidate under A's employer id: composite FK fails.
        c.execute("insert into qs_sessions (employer_id, candidate_id) values (%s, %s)",
                  (ids["a"]["employer"], ids["b"]["candidate"]))
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("insert into qs_sessions (employer_id, candidate_id) values (%s, %s)",
                  (ids["b"]["employer"], ids["b"]["candidate"]))


def test_employer_cannot_update_session_baseline(db):
    uri, ids = db
    with as_user(uri, A_USER) as c:
        cur = c.execute("update qs_sessions set baseline = '{}' where id = %s", (ids["a"]["session"],))
        assert cur.rowcount == 0


def test_realtime_topic_policy(db):
    uri, ids = db

    def can_join(user, topic):
        with as_user(uri, user) as c:
            c.execute("select set_config('realtime.topic', %s, false)", (topic,))
            return len(c.execute("select 1 from realtime.messages where topic = %s", (topic,)).fetchall()) > 0

    assert can_join(A_USER, f"qs:session:{ids['a']['session']}")
    assert can_join(A_VIEWER, f"qs:session:{ids['a']['session']}")
    assert not can_join(B_USER, f"qs:session:{ids['a']['session']}")
    assert not can_join(A_USER, f"qs:session:{ids['b']['session']}")
    with as_user(uri, A_USER) as c:
        assert c.execute("select qs_private.can_read_topic(%s)", (f"qs:employer:{ids['a']['employer']}",)).fetchone()[0]
        assert not c.execute("select qs_private.can_read_topic(%s)", (f"qs:employer:{ids['b']['employer']}",)).fetchone()[0]
        assert not c.execute("select qs_private.can_read_topic('qs:session:not-a-uuid')").fetchone()[0]


def test_delete_candidate_is_owner_only_and_removes_everything(db):
    uri, ids = db
    with as_user(uri, A_VIEWER) as c, pytest.raises(psycopg.Error):
        c.execute("select qs_delete_candidate(%s)", (ids["a"]["candidate"],))
    with as_user(uri, B_USER) as c, pytest.raises(psycopg.Error):
        c.execute("select qs_delete_candidate(%s)", (ids["a"]["candidate"],))
    with as_user(uri, A_USER) as c:
        receipt = c.execute("select qs_delete_candidate(%s)", (ids["a"]["candidate"],)).fetchone()[0]
        c.commit()
    assert receipt["sessions"] == 1 and receipt["motion_windows"] == 1 and receipt["realtime_messages"] == 1
    with psycopg.connect(uri) as c:
        for table in ["qs_candidates", "qs_candidate_identities", "qs_sessions", "qs_session_tokens", "qs_consents",
                      "qs_motion_windows", "qs_events", "qs_records"]:
            col = "candidate_id" if table == "qs_candidate_identities" else ("session_id" if table not in ("qs_candidates", "qs_sessions") else "id")
            ref = ids["a"]["candidate"] if table in ("qs_candidates", "qs_candidate_identities") else ids["a"]["session"]
            assert c.execute(f"select count(*) from {table} where {col} = %s", (ref,)).fetchone()[0] == 0, table
        assert c.execute("select count(*) from realtime.messages where topic = %s",
                         (f"qs:session:{ids['a']['session']}",)).fetchone()[0] == 0
        # Employer B untouched.
        assert c.execute("select count(*) from qs_sessions where id = %s", (ids["b"]["session"],)).fetchone()[0] == 1


# ---- enrolled devices (20261115120000_qs_devices_states.sql)

@pytest.fixture(scope="module")
def devices(db):
    uri, ids = db
    out = {}
    with psycopg.connect(uri, autocommit=True) as c:
        for tag in ("a", "b"):
            qs = c.execute("insert into qs_question_sets (employer_id, name) values (%s, 'set') returning id",
                           (ids[tag]["employer"],)).fetchone()[0]
            dev = c.execute("insert into qs_devices (employer_id, question_set_id, name, key_hash) values (%s,%s,'Quest',%s) returning id",
                            (ids[tag]["employer"], qs, (tag * 64)[:64].replace(tag, "c" if tag == "a" else "d"))).fetchone()[0]
            out[tag] = {"device": str(dev), "question_set": str(qs)}
    return out


def test_devices_owner_reads_everything_but_the_key_hash(db, devices):
    uri, ids = db
    with as_user(uri, A_USER) as c:
        rows = c.execute("select id, name, question_set_id, revoked_at from qs_devices").fetchall()
        assert [str(r[0]) for r in rows] == [devices["a"]["device"]]
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("select key_hash from qs_devices").fetchall()
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("select * from qs_devices").fetchall()


def test_devices_hidden_from_viewers_other_employers_and_anon(db, devices):
    uri, _ = db
    for user in (A_VIEWER, B_USER):
        with as_user(uri, user) as c:
            ids = [str(r[0]) for r in c.execute("select id from qs_devices").fetchall()]
            assert devices["a"]["device"] not in ids
    with psycopg.connect(uri) as c:
        c.execute("set role anon")
        with pytest.raises(psycopg.errors.InsufficientPrivilege):
            c.execute("select id from qs_devices").fetchall()


def test_no_client_role_can_write_devices(db, devices):
    uri, ids = db
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("insert into qs_devices (employer_id, question_set_id, name, key_hash) values (%s,%s,'x',%s)",
                  (ids["a"]["employer"], devices["a"]["question_set"], "e" * 64))
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("update qs_devices set revoked_at = null")
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("delete from qs_devices")


def test_employer_cannot_claim_a_session_came_from_a_device(db, devices):
    uri, ids = db
    with as_user(uri, A_USER) as c, pytest.raises(psycopg.errors.InsufficientPrivilege):
        c.execute("insert into qs_sessions (employer_id, candidate_id, device_id) values (%s,%s,%s)",
                  (ids["a"]["employer"], ids["a"]["candidate"], devices["a"]["device"]))


def test_device_of_one_employer_cannot_back_another_employers_session(db, devices):
    uri, ids = db
    with psycopg.connect(uri, autocommit=True) as c, pytest.raises(psycopg.errors.ForeignKeyViolation):
        c.execute("insert into qs_sessions (employer_id, candidate_id, device_id) values (%s,%s,%s)",
                  (ids["b"]["employer"], ids["b"]["candidate"], devices["a"]["device"]))

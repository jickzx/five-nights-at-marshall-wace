-- QS telemetry: schema, row-level security and Realtime authorization.
--
-- Who can do what
--   * Employers are Supabase Auth users linked to an employer through qs_employer_members.
--     Every table has RLS on, and every policy is scoped by employer_id, so an employer only ever sees
--     its own candidates and sessions. 'owner' members may also see real identities and delete data.
--   * The headset never talks to the database. It posts to the API with a per-session ingest token,
--     and the API writes motion, events and QS records with the server-side secret key (which bypasses
--     RLS by design and never leaves the server).
--   * anon (the publishable key with no signed-in user) gets nothing.
--
-- Every child row carries employer_id, and composite foreign keys (id, employer_id) make it impossible
-- to attach a row to a parent that belongs to a different employer.


create schema if not exists qs_private;
revoke all on schema qs_private from public;

-- ---------------------------------------------------------------- employers -------------------------

create table public.qs_employers (
  id          uuid primary key default gen_random_uuid(),
  name        text not null check (length(name) between 1 and 200),
  created_at  timestamptz not null default now()
);

create table public.qs_employer_members (
  employer_id uuid not null references public.qs_employers (id) on delete cascade,
  user_id     uuid not null,                      -- auth.users.id
  role        text not null default 'viewer' check (role in ('owner', 'viewer')),
  created_at  timestamptz not null default now(),
  primary key (employer_id, user_id)
);
create index qs_employer_members_user_idx on public.qs_employer_members (user_id);

-- Helpers run as definer so policies can read memberships without recursing through RLS.
create or replace function qs_private.employer_ids()
returns setof uuid language sql stable security definer set search_path = '' as $$
  select m.employer_id from public.qs_employer_members m where m.user_id = (select auth.uid())
$$;

create or replace function qs_private.owner_employer_ids()
returns setof uuid language sql stable security definer set search_path = '' as $$
  select m.employer_id from public.qs_employer_members m
  where m.user_id = (select auth.uid()) and m.role = 'owner'
$$;

-- ---------------------------------------------------------------- question sets ---------------------

create table public.qs_question_sets (
  id          uuid primary key default gen_random_uuid(),
  employer_id uuid not null references public.qs_employers (id) on delete cascade,
  name        text not null check (length(name) between 1 and 200),
  created_at  timestamptz not null default now(),
  unique (id, employer_id)
);
create index qs_question_sets_employer_idx on public.qs_question_sets (employer_id);

create table public.qs_questions (
  id              uuid primary key default gen_random_uuid(),
  question_set_id uuid not null,
  employer_id     uuid not null,
  key             text not null check (key ~ '^[A-Za-z0-9_.:-]{1,64}$'),   -- what the game sends as question_key
  prompt          text check (length(prompt) <= 2000),
  position        int not null default 0,
  unique (question_set_id, key),
  foreign key (question_set_id, employer_id) references public.qs_question_sets (id, employer_id) on delete cascade
);
create index qs_questions_employer_idx on public.qs_questions (employer_id);

-- ---------------------------------------------------------------- candidates ------------------------

-- Pseudonymous: nothing in this table identifies a person.
create table public.qs_candidates (
  id          uuid primary key default gen_random_uuid(),
  employer_id uuid not null references public.qs_employers (id) on delete cascade,
  pseudonym   text not null check (pseudonym ~ '^[A-Za-z0-9-]{3,32}$'),
  created_at  timestamptz not null default now(),
  unique (employer_id, pseudonym),
  unique (id, employer_id)
);

-- The only link to a real person. Separate table, owner-only, never joined into QS reads or broadcasts.
create table public.qs_candidate_identities (
  candidate_id uuid primary key,
  employer_id  uuid not null,
  display_name text check (length(display_name) <= 200),
  email        text check (length(email) <= 320),
  external_ref text check (length(external_ref) <= 200),           -- e.g. the employer's ATS id
  created_at   timestamptz not null default now(),
  foreign key (candidate_id, employer_id) references public.qs_candidates (id, employer_id) on delete cascade
);

-- ---------------------------------------------------------------- sessions --------------------------

create table public.qs_sessions (
  id              uuid primary key default gen_random_uuid(),
  employer_id     uuid not null,
  candidate_id    uuid not null,
  question_set_id uuid,
  status          text not null default 'created' check (status in ('created', 'live', 'ended')),
  baseline        jsonb,                         -- per-feature median/MAD from calibration (see docs/qs-schema.md)
  created_at      timestamptz not null default now(),
  started_at      timestamptz,
  ended_at        timestamptz,
  unique (id, employer_id),
  foreign key (candidate_id, employer_id) references public.qs_candidates (id, employer_id) on delete cascade,
  foreign key (question_set_id, employer_id) references public.qs_question_sets (id, employer_id)
);
create index qs_sessions_employer_idx on public.qs_sessions (employer_id, created_at desc);
create index qs_sessions_candidate_idx on public.qs_sessions (candidate_id);

-- Ingest tokens. RLS on with no policies: only the server's secret key can read or write this table.
create table public.qs_session_tokens (
  session_id  uuid primary key references public.qs_sessions (id) on delete cascade,
  token_hash  text not null,                     -- sha256 hex of the token; the token itself is never stored
  created_at  timestamptz not null default now(),
  revoked_at  timestamptz
);

-- Consent is recorded by the candidate in the headset before any motion is accepted.
create table public.qs_consents (
  id                uuid primary key default gen_random_uuid(),
  session_id        uuid not null,
  employer_id       uuid not null,
  consent_version   text not null check (length(consent_version) <= 64),
  statement_sha256  text not null check (statement_sha256 ~ '^[0-9a-f]{64}$'),
  granted_at        timestamptz not null default now(),
  withdrawn_at      timestamptz,
  client            jsonb,
  foreign key (session_id, employer_id) references public.qs_sessions (id, employer_id) on delete cascade
);
create index qs_consents_session_idx on public.qs_consents (session_id);

-- ---------------------------------------------------------------- QS data ---------------------------

-- One row per motion window (default: 1 s windows every 0.5 s). Raw samples are never stored.
create table public.qs_motion_windows (
  id           bigint generated always as identity primary key,
  session_id   uuid not null,
  employer_id  uuid not null,
  seq          int not null check (seq >= 0),
  t_start      double precision not null,
  t_end        double precision not null check (t_end >= t_start),
  question_key text,
  features     jsonb not null,
  rel          jsonb,
  created_at   timestamptz not null default now(),
  unique (session_id, seq),
  foreign key (session_id, employer_id) references public.qs_sessions (id, employer_id) on delete cascade
);
create index qs_motion_windows_session_t_idx on public.qs_motion_windows (session_id, t_start);
create index qs_motion_windows_employer_idx on public.qs_motion_windows (employer_id);

create table public.qs_events (
  id              bigint generated always as identity primary key,
  session_id      uuid not null,
  employer_id     uuid not null,
  client_event_id uuid not null,
  t               double precision not null,
  type            text not null check (type in (
                    'session_start', 'calibration_start', 'calibration_end', 'question_shown', 'tile_placed',
                    'tile_removed', 'answer_cleared', 'answer_submitted', 'answer_wrong_fx', 'hint_used',
                    'monster_appeared', 'monster_stage', 'monster_lunge', 'jumpscare', 'chase_start', 'chase_end',
                    'death', 'question_abandoned', 'session_end', 'tracking_lost', 'tracking_restored')),
  question_key    text,
  payload         jsonb,
  created_at      timestamptz not null default now(),
  unique (session_id, client_event_id),
  foreign key (session_id, employer_id) references public.qs_sessions (id, employer_id) on delete cascade
);
create index qs_events_session_t_idx on public.qs_events (session_id, t);
create index qs_events_employer_idx on public.qs_events (employer_id);

create table public.qs_records (
  session_id   uuid not null,
  employer_id  uuid not null,
  question_key text not null,
  question_id  uuid references public.qs_questions (id) on delete set null,
  status       text not null check (status in ('open', 'closed')),
  outcome      text not null,
  record       jsonb not null,
  updated_at   timestamptz not null default now(),
  primary key (session_id, question_key),
  foreign key (session_id, employer_id) references public.qs_sessions (id, employer_id) on delete cascade
);
create index qs_records_employer_idx on public.qs_records (employer_id);

create table public.qs_summaries (
  session_id  uuid primary key,
  employer_id uuid not null,
  model       text,
  status      text not null check (status in ('ok', 'unavailable', 'rejected')),
  summary     jsonb,
  error       text,
  created_at  timestamptz not null default now(),
  foreign key (session_id, employer_id) references public.qs_sessions (id, employer_id) on delete cascade
);
create index qs_summaries_employer_idx on public.qs_summaries (employer_id);

-- ---------------------------------------------------------------- RLS -------------------------------

alter table public.qs_employers            enable row level security;
alter table public.qs_employer_members     enable row level security;
alter table public.qs_question_sets        enable row level security;
alter table public.qs_questions            enable row level security;
alter table public.qs_candidates           enable row level security;
alter table public.qs_candidate_identities enable row level security;
alter table public.qs_sessions             enable row level security;
alter table public.qs_session_tokens       enable row level security;
alter table public.qs_consents             enable row level security;
alter table public.qs_motion_windows       enable row level security;
alter table public.qs_events               enable row level security;
alter table public.qs_records              enable row level security;
alter table public.qs_summaries            enable row level security;

-- Defence in depth: the publishable key without a signed-in user can touch none of these tables.
revoke all on public.qs_employers, public.qs_employer_members, public.qs_question_sets, public.qs_questions,
  public.qs_candidates, public.qs_candidate_identities, public.qs_sessions, public.qs_session_tokens,
  public.qs_consents, public.qs_motion_windows, public.qs_events, public.qs_records, public.qs_summaries
  from anon;
-- Tokens are server-only, whoever asks.
revoke all on public.qs_session_tokens from authenticated;

create policy qs_employers_select on public.qs_employers for select to authenticated
  using (id in (select qs_private.employer_ids()));

create policy qs_members_select_own on public.qs_employer_members for select to authenticated
  using (user_id = (select auth.uid()));

-- Question sets and questions: members read and write their own employer's.
create policy qs_question_sets_select on public.qs_question_sets for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_question_sets_insert on public.qs_question_sets for insert to authenticated
  with check (employer_id in (select qs_private.employer_ids()));
create policy qs_question_sets_delete on public.qs_question_sets for delete to authenticated
  using (employer_id in (select qs_private.owner_employer_ids()));

create policy qs_questions_select on public.qs_questions for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_questions_insert on public.qs_questions for insert to authenticated
  with check (employer_id in (select qs_private.employer_ids()));

-- Candidates: members create and read. There is deliberately no DELETE policy: the only way to delete a
-- candidate is qs_delete_candidate() below, which also clears their broadcast copies in realtime.messages.
create policy qs_candidates_select on public.qs_candidates for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_candidates_insert on public.qs_candidates for insert to authenticated
  with check (employer_id in (select qs_private.employer_ids()));

-- Real identities: owners only.
create policy qs_identities_select on public.qs_candidate_identities for select to authenticated
  using (employer_id in (select qs_private.owner_employer_ids()));
create policy qs_identities_insert on public.qs_candidate_identities for insert to authenticated
  with check (employer_id in (select qs_private.owner_employer_ids()));

-- Sessions: members create and read. Status, baseline and timestamps are written by the server.
create policy qs_sessions_select on public.qs_sessions for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_sessions_insert on public.qs_sessions for insert to authenticated
  with check (employer_id in (select qs_private.employer_ids()) and status = 'created' and baseline is null);

-- Motion, events, consents, records, summaries: read-only for members; only the server writes them.
create policy qs_consents_select on public.qs_consents for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_windows_select on public.qs_motion_windows for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_events_select on public.qs_events for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_records_select on public.qs_records for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));
create policy qs_summaries_select on public.qs_summaries for select to authenticated
  using (employer_id in (select qs_private.employer_ids()));

-- ---------------------------------------------------------------- delete ----------------------------

-- Remove a candidate and everything derived from them in one transaction: identity, sessions, tokens,
-- consents, motion windows, events, QS records, summaries (all by cascade), plus any copies of their
-- live updates still held in realtime.messages. Owners only. Returns counts for the deletion receipt.
create or replace function public.qs_delete_candidate(p_candidate_id uuid)
returns jsonb language plpgsql volatile security definer set search_path = '' as $$
declare
  v_employer uuid;
  v_sessions uuid[];
  v_windows bigint;
  v_events bigint;
  v_realtime bigint := 0;
begin
  select c.employer_id into v_employer from public.qs_candidates c where c.id = p_candidate_id;
  if v_employer is null or v_employer not in (select qs_private.owner_employer_ids()) then
    raise exception 'candidate not found' using errcode = 'P0002';
  end if;
  select coalesce(array_agg(s.id), '{}') into v_sessions from public.qs_sessions s where s.candidate_id = p_candidate_id;
  select count(*) into v_windows from public.qs_motion_windows w where w.session_id = any (v_sessions);
  select count(*) into v_events from public.qs_events e where e.session_id = any (v_sessions);
  if to_regclass('realtime.messages') is not null then
    -- Broadcast copies expire after 3 days anyway; if this role may not delete them, still delete the
    -- candidate and report -1 rather than failing the whole request.
    begin
      execute 'delete from realtime.messages where topic = any ($1)'
        using (select coalesce(array_agg('qs:session:' || s), '{}') from unnest(v_sessions) s);
      get diagnostics v_realtime = row_count;
    exception when insufficient_privilege then
      v_realtime := -1;
    end;
  end if;
  delete from public.qs_candidates c where c.id = p_candidate_id;
  return jsonb_build_object('candidate_id', p_candidate_id, 'sessions', coalesce(array_length(v_sessions, 1), 0),
                            'motion_windows', v_windows, 'events', v_events, 'realtime_messages', v_realtime);
end $$;
revoke all on function public.qs_delete_candidate(uuid) from public, anon;
grant execute on function public.qs_delete_candidate(uuid) to authenticated;

-- ---------------------------------------------------------------- Realtime --------------------------

-- Live updates go out as Broadcast messages on private channels:
--   qs:session:<session_id>   window features, events and QS record updates for one session
--   qs:employer:<employer_id> session created / started / ended, for the session list
-- The API sends them with the server key; dashboards subscribe with the signed-in employer's JWT and
-- {config: {private: true}}. This policy decides who may join; nobody but the server may send.
create or replace function qs_private.can_read_topic(p_topic text)
returns boolean language sql stable security definer set search_path = '' as $$
  select case
    when p_topic like 'qs:session:%' then exists (
      select 1 from public.qs_sessions s
      where s.id::text = substr(p_topic, length('qs:session:') + 1)
        and s.employer_id in (select qs_private.employer_ids()))
    when p_topic like 'qs:employer:%' then
      substr(p_topic, length('qs:employer:') + 1) in (select e::text from qs_private.employer_ids() e)
    else false
  end
$$;

grant usage on schema qs_private to authenticated;
grant execute on function qs_private.can_read_topic(text) to authenticated;
grant execute on function qs_private.employer_ids() to authenticated;
grant execute on function qs_private.owner_employer_ids() to authenticated;

do $$
begin
  if to_regclass('realtime.messages') is not null then
    execute $p$
      create policy qs_broadcast_receive on realtime.messages for select to authenticated
      using (realtime.messages.extension = 'broadcast' and qs_private.can_read_topic((select realtime.topic())))
    $p$;
  end if;
end $$;

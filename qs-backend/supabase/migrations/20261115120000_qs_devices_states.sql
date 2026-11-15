-- QS v2: enrolled headsets (device keys), possible states per window, breathing consent.
--
-- Devices
--   An employer owner enrolls a headset for one question set and gets a long-lived device key (shown once,
--   stored only as SHA-256 here). The key can do exactly one thing, through the API: start a new pseudonymous
--   candidate + session for that employer and question set (POST /v1/device-sessions). The API does that with
--   the server key; no database role ever acts as a device, so there is no policy that a device could use.
--   Owners can list and revoke their devices but can never read key_hash (column grant below).
--
-- States
--   qs_motion_windows.states holds the possible states read from each window (qs/states.py). Same RLS as the
--   rest of the row: employer members read, only the server writes.
--
-- Breathing
--   Audio features live inside qs_motion_windows.features (jsonb), so no column is needed. They are stored only
--   for sessions whose consent version names the microphone; that check is in the API (service.ingest).

create table public.qs_devices (
  id              uuid primary key default gen_random_uuid(),
  employer_id     uuid not null references public.qs_employers (id) on delete cascade,
  question_set_id uuid not null,
  name            text not null check (length(name) between 1 and 100),
  key_hash        text not null unique check (key_hash ~ '^[0-9a-f]{64}$'),   -- sha256 hex; the key is never stored
  created_by      uuid,                                                        -- auth.users.id of the owner
  created_at      timestamptz not null default now(),
  last_used_at    timestamptz,
  revoked_at      timestamptz,
  unique (id, employer_id),
  foreign key (question_set_id, employer_id) references public.qs_question_sets (id, employer_id) on delete cascade
);
create index qs_devices_employer_idx on public.qs_devices (employer_id);

alter table public.qs_devices enable row level security;
revoke all on public.qs_devices from anon, authenticated;
-- Owners read their devices, every column except key_hash. No insert/update/delete for any client role.
grant select (id, employer_id, question_set_id, name, created_by, created_at, last_used_at, revoked_at)
  on public.qs_devices to authenticated;
create policy qs_devices_select_owner on public.qs_devices for select to authenticated
  using (employer_id in (select qs_private.owner_employer_ids()));

-- Which device started a session (null for sessions an employer created by hand). Deleting a device keeps
-- its sessions and clears only this column.
alter table public.qs_sessions add column device_id uuid;
alter table public.qs_sessions add constraint qs_sessions_device_fk
  foreign key (device_id, employer_id) references public.qs_devices (id, employer_id) on delete set null (device_id);
create index qs_sessions_device_idx on public.qs_sessions (device_id, created_at);

-- Employers still create sessions by hand, but may not claim one came from a device.
drop policy qs_sessions_insert on public.qs_sessions;
create policy qs_sessions_insert on public.qs_sessions for insert to authenticated
  with check (employer_id in (select qs_private.employer_ids()) and status = 'created' and baseline is null
              and device_id is null);

alter table public.qs_motion_windows add column states jsonb;

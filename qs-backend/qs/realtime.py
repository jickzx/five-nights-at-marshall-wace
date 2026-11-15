"""Live updates via Supabase Realtime Broadcast, sent over HTTP (no WebSocket held by the function).

Channels (private: subscribers need a signed-in employer JWT, checked by the realtime.messages policy):
    qs:session:<session_id>    events "window", "event", "record"
    qs:employer:<employer_id>  event  "session"
Payloads carry pseudonymous ids and measurements only. A failed broadcast is logged and dropped; the
dashboard recovers by reading /v1/sessions/{id}/windows?after_seq=... on reconnect.
"""
from __future__ import annotations

import logging
from typing import Any, Protocol

import httpx

log = logging.getLogger("qs.realtime")


def session_topic(session_id: str) -> str:
    return f"qs:session:{session_id}"


def employer_topic(employer_id: str) -> str:
    return f"qs:employer:{employer_id}"


class Broadcaster(Protocol):
    def send(self, messages: list[dict[str, Any]]) -> None: ...


class NullBroadcaster:
    def __init__(self):
        self.sent: list[dict[str, Any]] = []

    def send(self, messages):
        self.sent.extend(messages)


class SupabaseBroadcaster:
    def __init__(self, supabase_url: str, secret_key: str, client: httpx.Client | None = None):
        self._url = supabase_url.rstrip("/") + "/realtime/v1/api/broadcast"
        self._headers = {"apikey": secret_key, "Content-Type": "application/json"}
        if not secret_key.startswith("sb_"):
            self._headers["Authorization"] = f"Bearer {secret_key}"
        from .db import shared_client
        self._client = client or shared_client()

    def send(self, messages):
        if not messages:
            return
        body = {"messages": [{"topic": m["topic"], "event": m["event"], "payload": m["payload"], "private": True}
                             for m in messages]}
        try:
            r = self._client.post(self._url, json=body, headers=self._headers, timeout=3.0)
            if r.status_code >= 300:
                log.warning("realtime broadcast refused: HTTP %s", r.status_code)
        except httpx.HTTPError as ex:
            log.warning("realtime broadcast failed: %s", type(ex).__name__)

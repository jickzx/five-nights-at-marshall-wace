import time
import uuid

import jwt
import pytest
from fastapi.testclient import TestClient

from qs import service
from qs.api import Backend, backend_from_settings, create_app
from qs.config import Settings
from qs.db import MemoryStore

SECRET = "test-only-jwt-secret-not-used-anywhere-else-0123456789"


def make_token(user_id: str, **overrides) -> str:
    claims = {"sub": user_id, "role": "authenticated", "aud": "authenticated", "exp": int(time.time()) + 3600}
    claims.update(overrides)
    return jwt.encode(claims, SECRET, algorithm="HS256")


class World:
    def __init__(self):
        self.store = MemoryStore()
        self.settings = Settings(store="memory", supabase_jwt_secret=SECRET)
        self.backend: Backend = backend_from_settings(self.settings, self.store)
        self.client = TestClient(create_app(self.backend))
        self.users = {}
        server = self.store.db(None)
        for tag in ("a", "b"):
            emp = server.insert("qs_employers", [{"name": f"Employer {tag}"}])[0]
            owner, viewer = str(uuid.uuid4()), str(uuid.uuid4())
            server.insert("qs_employer_members", [{"employer_id": emp["id"], "user_id": owner, "role": "owner"},
                                                  {"employer_id": emp["id"], "user_id": viewer, "role": "viewer"}])
            self.users[tag] = {"employer": emp["id"], "owner": owner, "viewer": viewer}

    def h(self, tag="a", role="owner"):
        return {"Authorization": f"Bearer {make_token(self.users[tag][role])}"}

    def new_session(self, tag="a", identity=None, question_set=True):
        body = {"identity": identity} if identity else {}
        cand = self.client.post("/v1/candidates", json=body, headers=self.h(tag)).json()
        payload = {"candidate_id": cand["id"]}
        if question_set:
            qs = self.client.post("/v1/question-sets", headers=self.h(tag), json={
                "name": "Demo set", "questions": [{"key": f"p{i}", "prompt": f"Puzzle {i}"} for i in range(1, 6)]}).json()
            payload["question_set_id"] = qs["id"]
        r = self.client.post("/v1/sessions", json=payload, headers=self.h(tag))
        assert r.status_code == 201, r.text
        return cand, r.json()["session"], r.json()["ingest_token"]

    def consent(self, session_id, token):
        c = self.client.get("/v1/consent").json()
        return self.client.post(f"/v1/ingest/{session_id}/consent", headers={"X-QS-Ingest-Token": token}, json={
            "consent_version": c["consent_version"], "statement_sha256": c["statement_sha256"], "accepted": True})

    def post(self, session_id, token, batch):
        return self.client.post(f"/v1/ingest/{session_id}", headers={"X-QS-Ingest-Token": token}, json=batch)


@pytest.fixture
def world():
    return World()

"""The no-sign-in demo view: one employer's pseudonymous data, read-only, nothing else."""
import dataclasses
import json

from fastapi.testclient import TestClient

from qs.api import backend_from_settings, create_app
from qs.sim import simulate_batches


def public_client(world, employer):
    settings = dataclasses.replace(world.settings, public_employer_id=employer)
    return TestClient(create_app(backend_from_settings(settings, world.store)))


def test_public_view_is_off_unless_configured(world):
    assert world.client.get("/v1/public/sessions").status_code == 404


def test_public_view_shows_one_employer_read_only(world):
    _, sa, ta = world.new_session("a", identity={"display_name": "Real Person", "email": "x@example.com"})
    _, sb, _ = world.new_session("b")
    world.consent(sa["id"], ta)
    for b in simulate_batches()[:80]:
        world.post(sa["id"], ta, b)
    c = public_client(world, world.users["a"]["employer"])
    sessions = c.get("/v1/public/sessions").json()
    assert [s["id"] for s in sessions] == [sa["id"]]
    assert c.get(f"/v1/public/sessions/{sa['id']}/windows?tail=true&limit=5").status_code == 200
    assert c.get(f"/v1/public/sessions/{sa['id']}/qs").status_code == 200
    assert c.get(f"/v1/public/sessions/{sb['id']}").status_code == 404          # other employer
    text = json.dumps([c.get(f"/v1/public/sessions/{sa['id']}").json(), sessions])
    assert "Real Person" not in text and "example.com" not in text
    assert c.post(f"/v1/public/sessions/{sa['id']}/summary").status_code == 405
    assert c.get("/v1/sessions").status_code == 401                            # employer API still needs sign-in

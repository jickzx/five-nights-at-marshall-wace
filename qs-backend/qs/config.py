"""Settings from environment variables only. Nothing secret has a default, and nothing here is ever logged."""
from __future__ import annotations

import os
from dataclasses import dataclass, field


def _env(name: str, default: str = "") -> str:
    return os.environ.get(name, default).strip()


@dataclass(frozen=True)
class Settings:
    supabase_url: str = ""
    # Publishable key (sb_publishable_... or the legacy anon JWT). Sent with the employer's own JWT so
    # Postgres applies RLS to every employer read.
    supabase_publishable_key: str = ""
    # Secret key (sb_secret_... or the legacy service_role JWT). Server only; bypasses RLS. Used for ingest.
    supabase_secret_key: str = field(default="", repr=False)
    # Legacy HS256 JWT secret, only for projects still on symmetric JWT signing. Newer projects use
    # asymmetric signing keys, which are verified through the project's JWKS endpoint instead.
    supabase_jwt_secret: str = field(default="", repr=False)
    gemini_api_key: str = field(default="", repr=False)
    gemini_model: str = "gemini-3.5-flash-lite"
    gemini_disabled: bool = False
    # "memory" runs the whole API on an in-process store (local demo, tests). "supabase" needs the keys above.
    store: str = "supabase"
    cors_origins: tuple[str, ...] = ()
    consent_version: str = "qs-consent-2026-11"

    @staticmethod
    def from_env() -> "Settings":
        origins = tuple(o.strip() for o in _env("QS_CORS_ORIGINS").split(",") if o.strip())
        store = _env("QS_STORE", "supabase")
        return Settings(
            supabase_url=_env("SUPABASE_URL").rstrip("/"),
            supabase_publishable_key=_env("SUPABASE_PUBLISHABLE_KEY") or _env("SUPABASE_ANON_KEY"),
            supabase_secret_key=_env("SUPABASE_SECRET_KEY") or _env("SUPABASE_SERVICE_ROLE_KEY"),
            supabase_jwt_secret=_env("SUPABASE_JWT_SECRET"),
            gemini_api_key=_env("GEMINI_API_KEY"),
            gemini_model=_env("GEMINI_MODEL", "gemini-3.5-flash-lite"),
            gemini_disabled=_env("QS_GEMINI_DISABLED") in ("1", "true", "yes"),
            store=store,
            cors_origins=origins,
        )

    @property
    def gemini_usable(self) -> bool:
        return bool(self.gemini_api_key) and not self.gemini_disabled

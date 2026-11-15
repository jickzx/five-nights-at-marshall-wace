"""Two kinds of caller.

* Employers: a Supabase Auth access token (JWT) in ``Authorization: Bearer``. Verified here (signature,
  expiry, audience) and then forwarded to PostgREST, where RLS decides what they may touch.
* Headsets: a per-session ingest token, generated when the employer creates the session and shown once.
  Only its SHA-256 is stored. It can write motion and events for that one session and nothing else.
"""
from __future__ import annotations

import hashlib
import hmac
import secrets
from functools import lru_cache

import jwt

from .config import Settings
from .db import Principal


class AuthError(Exception):
    pass


def new_ingest_token() -> str:
    return "qsi_" + secrets.token_urlsafe(32)


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def token_matches(token: str, stored_hash: str) -> bool:
    return hmac.compare_digest(hash_token(token), stored_hash)


@lru_cache(maxsize=4)
def _jwks_client(url: str) -> jwt.PyJWKClient:
    return jwt.PyJWKClient(url, cache_keys=True, lifespan=600)


def verify_employer_token(token: str, settings: Settings) -> Principal:
    """Return the employer principal for a valid Supabase access token, or raise AuthError."""
    if not token:
        raise AuthError("missing bearer token")
    try:
        header = jwt.get_unverified_header(token)
    except jwt.PyJWTError:
        raise AuthError("malformed token") from None
    alg = header.get("alg")
    try:
        if alg == "HS256":
            if not settings.supabase_jwt_secret:
                raise AuthError("symmetric tokens are not accepted (no SUPABASE_JWT_SECRET)")
            key = settings.supabase_jwt_secret
        elif alg in ("RS256", "ES256", "EdDSA"):
            if not settings.supabase_url:
                raise AuthError("no SUPABASE_URL to fetch signing keys from")
            key = _jwks_client(f"{settings.supabase_url}/auth/v1/.well-known/jwks.json").get_signing_key_from_jwt(token).key
        else:
            raise AuthError("unsupported token algorithm")
        claims = jwt.decode(token, key, algorithms=[alg], audience="authenticated",
                            options={"require": ["exp", "sub"]})
    except AuthError:
        raise
    except jwt.PyJWTError as ex:
        raise AuthError(f"invalid token: {type(ex).__name__}") from None
    if claims.get("role") != "authenticated":
        raise AuthError("token is not for a signed-in user")
    return Principal(user_id=str(claims["sub"]), token=token)

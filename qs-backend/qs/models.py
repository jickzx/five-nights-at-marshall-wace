"""Request bodies. Every limit here exists so one request cannot exhaust a function or the database."""
from __future__ import annotations

import json
from typing import Any, Literal, Optional
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

KEY_PATTERN = r"^[A-Za-z0-9_.:-]{1,64}$"
MAX_SAMPLES = 600          # 6.6 s at 90 Hz; the sender uses 1 s windows
MAX_EVENTS = 200
MAX_PAYLOAD_BYTES = 2048

EventType = Literal[
    "session_start", "calibration_start", "calibration_end", "question_shown", "tile_placed", "tile_removed",
    "answer_cleared", "answer_submitted", "answer_wrong_fx", "hint_used", "monster_appeared", "monster_stage",
    "monster_lunge", "jumpscare", "chase_start", "chase_end", "death", "question_abandoned", "session_end",
    "tracking_lost", "tracking_restored",
]

Vec3 = tuple[float, float, float]
Quat = tuple[float, float, float, float]


class Strict(BaseModel):
    # NaN / Infinity are valid to Python's JSON parser but not to Postgres JSON or the PostgREST client.
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)


class DeviceSamples(Strict):
    """One device's samples, columnar, aligned with ``Samples.t``. Units: metres, m/s, rad/s."""
    pos: Optional[list[Vec3]] = None
    rot: Optional[list[Quat]] = None          # Unity order x, y, z, w
    vel: Optional[list[Vec3]] = None          # runtime-reported, optional
    angvel: Optional[list[Vec3]] = None       # runtime-reported, optional (accepted, not used yet)
    tracked: Optional[list[bool]] = None


class Samples(Strict):
    t: list[float] = Field(min_length=1, max_length=MAX_SAMPLES)   # seconds, headset monotonic clock
    head: Optional[DeviceSamples] = None
    left: Optional[DeviceSamples] = None
    right: Optional[DeviceSamples] = None

    @model_validator(mode="after")
    def _aligned(self):
        n = len(self.t)
        for name in ("head", "left", "right"):
            dev = getattr(self, name)
            if dev is None:
                continue
            for col in ("pos", "rot", "vel", "angvel", "tracked"):
                arr = getattr(dev, col)
                if arr is not None and len(arr) != n:
                    raise ValueError(f"{name}.{col} has {len(arr)} samples, t has {n}")
        return self


class EventIn(Strict):
    id: UUID                                   # client-generated, makes retries idempotent
    t: float
    type: EventType
    question_key: Optional[str] = Field(default=None, pattern=KEY_PATTERN)
    payload: Optional[dict[str, Any]] = None

    @field_validator("payload")
    @classmethod
    def _small(cls, v):
        if v is not None and len(json.dumps(v)) > MAX_PAYLOAD_BYTES:
            raise ValueError("payload too large")
        return v


class AudioFeatures(Strict):
    """Breathing features the headset computed from its microphone over the last ~15 s (qs/audio.py).
    Numbers only: there is deliberately no field that could carry audio samples."""
    breath_rate_bpm: Optional[float] = Field(default=None, ge=0, le=120)
    breath_regularity: Optional[float] = Field(default=None, ge=-1, le=1)
    breath_amp: Optional[float] = Field(default=None, ge=0, le=10)
    deep_breaths: Optional[int] = Field(default=None, ge=0, le=20)
    since_last_breath_s: Optional[float] = Field(default=None, ge=0, le=600)
    voice_fraction: Optional[float] = Field(default=None, ge=0, le=1)


class IngestBatch(Strict):
    """What the headset posts a few times a second: one motion window and/or some game events."""
    seq: Optional[int] = Field(default=None, ge=0, le=10_000_000)   # window number; required with samples
    question_key: Optional[str] = Field(default=None, pattern=KEY_PATTERN)
    t_start: Optional[float] = None
    t_end: Optional[float] = None
    rate_hz: Optional[float] = Field(default=None, gt=0, le=1000)
    samples: Optional[Samples] = None
    audio: Optional[AudioFeatures] = None      # only used when the session's consent covers the microphone
    events: list[EventIn] = Field(default_factory=list, max_length=MAX_EVENTS)

    @model_validator(mode="after")
    def _window(self):
        if self.samples is not None and self.seq is None:
            raise ValueError("seq is required when samples are sent")
        if self.samples is None and not self.events:
            raise ValueError("empty batch")
        return self


class ConsentIn(Strict):
    consent_version: str = Field(max_length=64)
    statement_sha256: str = Field(pattern=r"^[0-9a-f]{64}$")
    accepted: Literal[True]
    client: Optional[dict[str, Any]] = None

    @field_validator("client")
    @classmethod
    def _small(cls, v):
        if v is not None and len(json.dumps(v)) > MAX_PAYLOAD_BYTES:
            raise ValueError("client info too large")
        return v


class QuestionIn(Strict):
    key: str = Field(pattern=KEY_PATTERN)
    prompt: Optional[str] = Field(default=None, max_length=2000)
    position: int = 0


class QuestionSetIn(Strict):
    employer_id: Optional[UUID] = None
    name: str = Field(min_length=1, max_length=200)
    questions: list[QuestionIn] = Field(min_length=1, max_length=200)


class IdentityIn(Strict):
    display_name: Optional[str] = Field(default=None, max_length=200)
    email: Optional[str] = Field(default=None, max_length=320)
    external_ref: Optional[str] = Field(default=None, max_length=200)


class CandidateIn(Strict):
    employer_id: Optional[UUID] = None
    pseudonym: Optional[str] = Field(default=None, pattern=r"^[A-Za-z0-9-]{3,32}$")
    identity: Optional[IdentityIn] = None     # stored separately, owner-only


class SessionIn(Strict):
    candidate_id: UUID
    question_set_id: Optional[UUID] = None


class DeviceIn(Strict):
    """An enrolled headset. Its key can do one thing: start a new pseudonymous session for this employer
    and question set (POST /v1/device-sessions)."""
    employer_id: Optional[UUID] = None
    name: str = Field(min_length=1, max_length=100)
    question_set_id: UUID


class DeviceSessionIn(Strict):
    client: Optional[dict[str, Any]] = None

    @field_validator("client")
    @classmethod
    def _small(cls, v):
        if v is not None and len(json.dumps(v)) > MAX_PAYLOAD_BYTES:
            raise ValueError("client info too large")
        return v

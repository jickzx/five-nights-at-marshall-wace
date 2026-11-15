"""Employer-readable account of a session's QS records, written by Gemini under a strict JSON schema.

Design rules (see docs/design.md, "Gemini summary"):
* Structured output (``responseJsonSchema``) on ``generateContent``, the same endpoint and key header the
  Unity scaffold uses. The schema forces every observation to cite the question keys and QS feature
  names it rests on, and the feature names are an enum, so the model cannot cite something that does
  not exist.
* Observations only. The schema has no field for a decision, score or rating, the prompt forbids
  verdicts, and ``check_summary`` drops any observation that uses verdict or diagnostic language anyway.
* The evidence shown next to each statement is attached by the server from the stored records, not
  copied from the model, so a number the employer sees is a number we measured.
* Gemini is optional. No key, a 402 (prepaid credits exhausted), a 429, a timeout or a bad reply all
  return ``status: "unavailable"`` with a reason, and nothing else in the service depends on it.
"""
from __future__ import annotations

import json
import re
from typing import Any, Mapping, Sequence

import httpx

ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"

# QS fields the model may cite: "<group>.<field>" of a record (docs/qs-schema.md).
CITABLE_FEATURES = [
    "behaviour.time_to_first_action_s", "behaviour.response_latency_s", "behaviour.duration_s",
    "behaviour.submissions", "behaviour.incorrect_submissions", "behaviour.answer_changes",
    "behaviour.hesitations", "behaviour.hesitation_total_s", "behaviour.hints_used",
    "motion.left_tremor_ratio", "motion.right_tremor_ratio", "motion.left_tremor_ratio_p90",
    "motion.right_tremor_ratio_p90", "motion.head_ang_speed_ratio", "motion.head_scan_ratio",
    "motion.head_yaw_reversals_per_min", "motion.restless_ratio", "motion.fidget_index", "motion.freeze_s",
    "motion.longest_freeze_s", "motion.freeze_fraction", "motion.startle_max_head_ratio",
    "quality.baseline_status", "quality.head_coverage", "quality.hand_coverage", "outcome",
]

# Verdicts, inferred states and diagnoses. An observation containing any of these is dropped.
BANNED = re.compile(
    r"\b(hire|hired|hiring|reject\w*|suitab\w*|unsuitab\w*|recommend\w*|fit for|good fit|poor fit|"
    r"nervous\w*|anxi\w*|stress\w*|panic\w*|fear\w*|afraid|scared|calm(ly)? under|composure|confiden\w*|"
    r"lying|liar|decepti\w*|dishonest\w*|honest\w*|competen\w*|incompeten\w*|intelligen\w*|personality|"
    r"disorder|disab\w*|parkinson\w*|diagnos\w*|medical condition|neurolog\w*|score[ds]?|rating|ranked|"
    r"pass(ed|es)? the (assessment|test|screen)|fail(ed|s)? the (assessment|test|screen)|strong candidate|weak candidate)\b",
    re.IGNORECASE,
)

FIXED_LIMITATIONS = [
    "QS measures movement, not mental state. Every value is relative to this person's own calm calibration "
    "period and can change for ordinary reasons: fatigue, caffeine, cold, posture, an unfamiliar headset.",
    "Hand motion in the 4-12 Hz band has common medical and physical causes and must not be read as a sign "
    "of anything about the person.",
    "This summary does not make, and must not be used as, a hiring decision. The employer decides.",
]

SYSTEM = """You describe measurements of how a person moved and acted while answering questions in a VR language-puzzle assessment.
Rules:
- Report observations only, each tied to the question keys and feature names it rests on (use the exact names given).
- Describe what was measured, in neutral words: "hand motion in the 4-12 Hz band was 2.1x their own calibration level on q3".
- Never infer feelings, mental states, personality, honesty, ability or health. Never use words such as nervous, anxious, stressed, confident, calm under pressure, competent, suitable, hire, reject, score, rating, pass, fail.
- Never recommend a decision or rank the person. The employer makes every judgement.
- Mention data-quality problems (missing baseline, low tracking coverage) where they limit what can be said.
- Ratios are relative to the person's own baseline: 1.0 means the same as during calm calibration.
Keep each statement under 240 characters."""


def response_schema(question_keys: Sequence[str]) -> dict[str, Any]:
    keys = sorted(set(question_keys)) or ["session"]
    citation = {
        "type": "object",
        "properties": {
            "statement": {"type": "string", "description": "One neutral observation, under 240 characters."},
            "question_keys": {"type": "array", "items": {"type": "string", "enum": keys + ["session"]}, "minItems": 1},
            "features": {"type": "array", "items": {"type": "string", "enum": CITABLE_FEATURES}, "minItems": 1},
            "direction": {"type": "string", "enum": ["above_baseline", "below_baseline", "near_baseline", "not_applicable"]},
        },
        "required": ["statement", "question_keys", "features", "direction"],
        "additionalProperties": False,
    }
    return {
        "type": "object",
        "properties": {
            "observations": {"type": "array", "items": citation, "minItems": 1, "maxItems": 12},
            "data_quality": {"type": "array", "items": citation, "maxItems": 6},
        },
        "required": ["observations", "data_quality"],
        "additionalProperties": False,
    }


def build_facts(pseudonym: str, records: Sequence[Mapping[str, Any]], baseline: Mapping[str, Any] | None,
                questions: Mapping[str, str] | None = None) -> dict[str, Any]:
    """Only what the model needs: pseudonymous id, baseline status, and each record. No identity, ever."""
    out = []
    for r in records:
        motion = {k: v for k, v in (r.get("motion") or {}).items() if k != "startles"}
        out.append({
            "question_key": r["question_key"],
            "question_prompt": (questions or {}).get(r["question_key"]),
            "outcome": r.get("outcome"),
            "behaviour": r.get("behaviour"),
            "motion": motion,
            "startles": (r.get("motion") or {}).get("startles"),
            "quality": r.get("quality"),
        })
    return {"candidate": pseudonym, "baseline_status": (baseline or {}).get("status", "missing"), "questions": out}


def _value(record: Mapping[str, Any], feature: str) -> Any:
    if feature == "outcome":
        return record.get("outcome")
    group, _, name = feature.partition(".")
    return (record.get(group) or {}).get(name)


def check_summary(raw: Mapping[str, Any], records: Sequence[Mapping[str, Any]]) -> tuple[dict[str, Any], list[str]]:
    """Keep only observations that cite real questions and features and use no verdict language.

    Returns (summary, dropped_reasons). Evidence values are attached from ``records``."""
    by_key = {r["question_key"]: r for r in records}
    dropped: list[str] = []

    def clean(items: Any) -> list[dict[str, Any]]:
        kept = []
        for item in items if isinstance(items, list) else []:
            if not isinstance(item, dict):
                dropped.append("not an object")
                continue
            text = str(item.get("statement", "")).strip()
            keys = [k for k in item.get("question_keys") or [] if isinstance(k, str)]
            feats = [f for f in item.get("features") or [] if isinstance(f, str)]
            if not text or len(text) > 400:
                dropped.append("empty or overlong statement")
                continue
            if BANNED.search(text):
                dropped.append(f"verdict or diagnostic language: {BANNED.search(text).group(0)!r}")
                continue
            if not keys or any(k != "session" and k not in by_key for k in keys):
                dropped.append("cites an unknown question")
                continue
            if not feats or any(f not in CITABLE_FEATURES for f in feats):
                dropped.append("cites an unknown feature")
                continue
            evidence = [{"question_key": k, "feature": f, "value": _value(by_key[k], f)}
                        for k in keys if k != "session" for f in feats]
            kept.append({"statement": text, "question_keys": keys, "features": feats,
                         "direction": item.get("direction", "not_applicable"), "evidence": evidence})
        return kept

    summary = {
        "observations": clean(raw.get("observations")),
        "data_quality": clean(raw.get("data_quality")),
        "limitations": FIXED_LIMITATIONS,
    }
    return summary, dropped


def unavailable(reason: str) -> dict[str, Any]:
    return {"status": "unavailable", "reason": reason, "summary": None}


def summarize(api_key: str, model: str, facts: Mapping[str, Any], records: Sequence[Mapping[str, Any]],
              client: httpx.Client | None = None, timeout: float = 25.0) -> dict[str, Any]:
    """Call Gemini and return {"status": "ok"|"unavailable"|"rejected", "reason", "summary", "dropped"}."""
    if not api_key:
        return unavailable("Gemini is not configured on this server.")
    body = {
        "systemInstruction": {"parts": [{"text": SYSTEM}]},
        "contents": [{"role": "user", "parts": [{"text": json.dumps(facts, separators=(",", ":"))}]}],
        "generationConfig": {
            "temperature": 0.2,
            "responseMimeType": "application/json",
            "responseJsonSchema": response_schema([r["question_key"] for r in records]),
        },
    }
    model_id = model[len("models/"):] if model.startswith("models/") else model
    try:
        if client is not None:
            r = client.post(ENDPOINT.format(model=model_id), json=body,
                            headers={"x-goog-api-key": api_key, "Content-Type": "application/json"})
        else:
            with httpx.Client(timeout=timeout) as http:
                r = http.post(ENDPOINT.format(model=model_id), json=body,
                              headers={"x-goog-api-key": api_key, "Content-Type": "application/json"})
    except httpx.TimeoutException:
        return unavailable("Gemini did not answer in time.")
    except httpx.HTTPError as ex:
        return unavailable(f"Gemini could not be reached ({type(ex).__name__}).")
    if r.status_code == 402:
        return unavailable("Gemini refused the request: the project's prepaid credits are used up (HTTP 402).")
    if r.status_code == 429:
        return unavailable("Gemini rate limit or quota reached (HTTP 429). Try again later.")
    if r.status_code >= 400:
        return unavailable(f"Gemini returned HTTP {r.status_code}.")
    try:
        data = r.json()
        parts = data["candidates"][0]["content"]["parts"]
        text = "".join(p.get("text", "") for p in parts if not p.get("thought"))
        raw = json.loads(text)
    except (ValueError, KeyError, IndexError, TypeError):
        return unavailable("Gemini's reply was not valid JSON for the schema.")
    summary, dropped = check_summary(raw, records)
    if not summary["observations"]:
        return {"status": "rejected", "reason": "No observation passed the evidence and language checks.",
                "summary": None, "dropped": dropped}
    return {"status": "ok", "reason": None, "summary": summary, "dropped": dropped, "model": model_id}

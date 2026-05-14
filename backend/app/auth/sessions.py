from __future__ import annotations

import base64
import hashlib
import hmac
import json
import time
from typing import Any

from fastapi import Response

from app.config import get_settings


COOKIE_NAME = "bidparser_session"


def create_session_token(user_id: int) -> str:
    payload = {"user_id": user_id, "issued_at": int(time.time())}
    payload_bytes = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    encoded_payload = base64.urlsafe_b64encode(payload_bytes).decode("ascii")
    signature = _sign(encoded_payload)
    return f"{encoded_payload}.{signature}"


def read_session_token(token: str | None) -> dict[str, Any] | None:
    if not token or "." not in token:
        return None
    encoded_payload, signature = token.rsplit(".", 1)
    if not hmac.compare_digest(_sign(encoded_payload), signature):
        return None
    try:
        payload = json.loads(base64.urlsafe_b64decode(encoded_payload.encode("ascii")))
    except (ValueError, json.JSONDecodeError):
        return None
    issued_at = int(payload.get("issued_at", 0))
    lifetime_seconds = get_settings().session_lifetime_hours * 60 * 60
    if issued_at + lifetime_seconds < int(time.time()):
        return None
    return payload


def set_session_cookie(response: Response, token: str, *, secure: bool) -> None:
    response.set_cookie(
        COOKIE_NAME,
        token,
        httponly=True,
        samesite="lax",
        secure=secure,
        max_age=get_settings().session_lifetime_hours * 60 * 60,
        path="/",
    )


def clear_session_cookie(response: Response) -> None:
    response.delete_cookie(COOKIE_NAME, path="/")


def _sign(value: str) -> str:
    secret = get_settings().session_secret.encode("utf-8")
    digest = hmac.new(secret, value.encode("ascii"), hashlib.sha256).digest()
    return base64.urlsafe_b64encode(digest).decode("ascii")

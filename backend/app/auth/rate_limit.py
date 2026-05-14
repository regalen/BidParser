from __future__ import annotations

import time
from collections import defaultdict, deque
from typing import Deque

from fastapi import HTTPException, Request, status

from app.config import get_settings


WINDOW_SECONDS = 60
_buckets: dict[str, Deque[float]] = defaultdict(deque)


def clear_rate_limits() -> None:
    _buckets.clear()


def client_ip(request: Request) -> str:
    forwarded = request.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",", 1)[0].strip()
    return request.client.host if request.client else "unknown"


def check_bucket(key: str) -> None:
    limit = get_settings().rate_limit_auth_per_min
    now = time.monotonic()
    bucket = _buckets[key]
    while bucket and now - bucket[0] >= WINDOW_SECONDS:
        bucket.popleft()
    if len(bucket) >= limit:
        retry_after = max(1, int(WINDOW_SECONDS - (now - bucket[0])))
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            detail="Too many attempts. Please try again later.",
            headers={"Retry-After": str(retry_after)},
        )
    bucket.append(now)

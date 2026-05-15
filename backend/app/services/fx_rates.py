from __future__ import annotations

import re
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation

import httpx
from sqlalchemy import update
from sqlalchemy.orm import Session

from app.config import get_settings
from app.models import User


_PRICE_PATTERNS = [
    re.compile(r'"price"\s*:\s*"?([0-9]+(?:\.[0-9]+)?)"?', re.IGNORECASE),
    re.compile(r'"last_price"\s*:\s*"?([0-9]+(?:\.[0-9]+)?)"?', re.IGNORECASE),
    re.compile(r'"lastPrice"\s*:\s*"?([0-9]+(?:\.[0-9]+)?)"?', re.IGNORECASE),
    re.compile(r"AUDUSD:CUR.*?([0-9]+\.[0-9]+)", re.IGNORECASE | re.DOTALL),
]


def fetch_bloomberg_aud_usd_rate() -> Decimal:
    settings = get_settings()
    response = httpx.get(
        settings.fx_rate_source_url,
        timeout=settings.fx_rate_timeout_seconds,
        follow_redirects=True,
        headers={
            "User-Agent": "BidParser/1.0",
            "Accept": "text/html,application/json",
        },
    )
    response.raise_for_status()
    return parse_aud_usd_rate(response.text)


def parse_aud_usd_rate(payload: str) -> Decimal:
    for pattern in _PRICE_PATTERNS:
        match = pattern.search(payload)
        if match:
            try:
                rate = Decimal(match.group(1)).quantize(Decimal("0.0001"))
            except InvalidOperation:
                continue
            if Decimal("0") < rate < Decimal("10"):
                return rate
    raise ValueError("Could not locate an AUD:USD price in the Bloomberg response.")


def update_user_default_fx_rates(db: Session, rate: Decimal) -> int:
    result = db.execute(
        update(User)
        .where(User.fx_rate_pegged.is_(True))
        .values(
            fx_rate=rate.quantize(Decimal("0.0001")),
            fx_rate_updated_at=datetime.now(timezone.utc),
        )
    )
    db.commit()
    return result.rowcount or 0

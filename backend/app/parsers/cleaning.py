from __future__ import annotations

import re
from datetime import date, datetime
from decimal import Decimal
from typing import Any


_WHITESPACE_RE = re.compile(r"\s+")


def clean_text(value: Any) -> str:
    if value is None:
        return ""
    return _WHITESPACE_RE.sub(" ", str(value).strip())


def join_spaced(parts: list[str]) -> str:
    text = clean_text(" ".join(part for part in parts if clean_text(part)))
    return re.sub(r"(?<=\w)-\s+(?=\w)", "-", text)


def join_unspaced(parts: list[str]) -> str:
    return clean_text("".join(clean_text(part) for part in parts if clean_text(part)))


def parse_decimal(value: Any, *, default_zero: bool = False) -> Decimal:
    text = clean_text(value)
    if not text:
        if default_zero:
            return Decimal("0")
        raise ValueError("Cannot parse an empty decimal value")
    text = text.replace("USD", "").replace("$", "").replace(",", "").strip()
    if not text:
        if default_zero:
            return Decimal("0")
        raise ValueError("Cannot parse an empty decimal value")
    return Decimal(text)


def parse_int(value: Any) -> int:
    text = clean_text(value)
    if not text:
        raise ValueError("Cannot parse an empty integer value")
    return int(Decimal(text.replace(",", "")))


def parse_optional_int(value: Any) -> int | None:
    text = clean_text(value)
    if not text:
        return None
    return parse_int(text)


def parse_mmddyyyy(value: str) -> date:
    return datetime.strptime(clean_text(value), "%m/%d/%Y").date()


def decimal_to_json(value: Decimal | None) -> str | None:
    return None if value is None else format(value, "f")


def date_to_json(value: date | None) -> str | None:
    return None if value is None else value.isoformat()

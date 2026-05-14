from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import ClassVar, Literal


@dataclass(slots=True)
class LineItem:
    vpn: str
    cost: Decimal
    qty: int
    description: str | None = None
    term: int | None = None
    msrp: Decimal | None = None
    serial_number: str | None = None
    start_date: date | None = None
    end_date: date | None = None
    raw: dict[str, str] = field(default_factory=dict)


@dataclass(slots=True)
class QuoteMetadata:
    quote_number: str | None
    supplier: str
    currency: str
    quoted_total: Decimal | None
    source_filename: str
    parser_slug: str


@dataclass(slots=True)
class ValidationResult:
    computed_total: Decimal
    quoted_total: Decimal | None
    matches: bool
    difference: Decimal | None
    warnings: list[str] = field(default_factory=list)


@dataclass(slots=True)
class ParseResult:
    metadata: QuoteMetadata
    line_items: list[LineItem]
    validation: ValidationResult


class ParseError(ValueError):
    def __init__(self, message: str, *, stage: str = "parse", hint: str | None = None) -> None:
        super().__init__(message)
        self.stage = stage
        self.hint = hint or message


class BaseParser(ABC):
    slug: ClassVar[str]
    display_name: ClassVar[str]
    vendor: ClassVar[Literal["Nutanix"]]
    accepted_mime: ClassVar[str]
    crm_template: ClassVar[str] = "Foreign Uplift"

    @classmethod
    def detect(cls, path: Path) -> float:
        return 0.0

    @classmethod
    @abstractmethod
    def parse(cls, path: Path) -> ParseResult:
        raise NotImplementedError


def validate_result(
    *,
    source_filename: str,
    parser_slug: str,
    quoted_total: Decimal | None,
    line_items: list[LineItem],
    quote_number: str | None = None,
    supplier: str = "Nutanix",
    currency: str = "USD",
) -> ParseResult:
    computed_total = sum((item.cost * item.qty for item in line_items), Decimal("0"))
    computed_total = computed_total.quantize(Decimal("0.01"))
    difference = None if quoted_total is None else (computed_total - quoted_total).quantize(Decimal("0.01"))
    matches = quoted_total is not None and abs(difference or Decimal("0")) <= Decimal("0.01")
    warnings: list[str] = []
    if quoted_total is None:
        warnings.append("Quoted total was not found.")
    elif not matches:
        warnings.append(f"Computed total {computed_total} does not match quoted total {quoted_total}.")

    return ParseResult(
        metadata=QuoteMetadata(
            quote_number=quote_number,
            supplier=supplier,
            currency=currency,
            quoted_total=quoted_total,
            source_filename=source_filename,
            parser_slug=parser_slug,
        ),
        line_items=line_items,
        validation=ValidationResult(
            computed_total=computed_total,
            quoted_total=quoted_total,
            matches=matches,
            difference=difference,
            warnings=warnings,
        ),
    )

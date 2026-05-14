from __future__ import annotations

from pathlib import Path

from app.parsers.base import BaseParser, LineItem, validate_result
from app.parsers.cleaning import clean_text, join_spaced, parse_decimal, parse_int, parse_optional_int
from app.parsers.pdf_utils import collect_words, find_product_code_header, find_sequence, raw_dict, rows_between


class NutanixHardwareOnlyPdfParser(BaseParser):
    slug = "nutanix_hardware_only_pdf"
    display_name = "Hardware Only (PDF)"
    vendor = "Nutanix"
    accepted_mime = "application/pdf"

    @classmethod
    def parse(cls, path: Path):
        words = collect_words(path)
        banner_index = find_sequence(words, ["Quote", "D", "For", "distributor", "to", "quote", "to", "the", "reseller", "only"])
        columns = find_product_code_header(words, start_index=banner_index)
        rows, quoted_total = rows_between(
            words,
            columns,
            start_page=int(columns["__page"]),
            start_top=columns["__top"],
        )

        items: list[LineItem] = []
        current: dict[str, object] | None = None
        for row in rows:
            cells = row.cells
            product_code = cells.get("Product Code", "")
            is_wrapped_code = product_code and current and not any(
                cells.get(key) for key in ["Term (Months)", "List Unit Price", "Net Unit Price", "Quantity", "Total Net Price"]
            )
            if product_code and not is_wrapped_code:
                if current:
                    items.append(_build_item(current))
                current = {"code_parts": [product_code], "description_parts": [cells.get("Product", "")], "cells": cells}
            elif current and any(cells.values()):
                if cells.get("Product Code"):
                    current["code_parts"].append(cells["Product Code"])  # type: ignore[index, union-attr]
                if cells.get("Product"):
                    current["description_parts"].append(cells["Product"])  # type: ignore[index, union-attr]
                base_cells = current["cells"]  # type: ignore[index]
                assert isinstance(base_cells, dict)
                for key, value in cells.items():
                    if value and not base_cells.get(key):
                        base_cells[key] = value
        if current:
            items.append(_build_item(current))

        return validate_result(
            source_filename=path.name,
            parser_slug=cls.slug,
            quote_number=path.stem,
            quoted_total=quoted_total,
            line_items=items,
        )


def _build_item(data: dict[str, object]) -> LineItem:
    cells = data["cells"]  # type: ignore[assignment]
    assert isinstance(cells, dict)
    return LineItem(
        vpn=_join_product_code(data["code_parts"]),  # type: ignore[arg-type]
        description=join_spaced(data["description_parts"]),  # type: ignore[arg-type]
        term=parse_optional_int(cells.get("Term (Months)", "")),
        msrp=parse_decimal(cells.get("List Unit Price", ""), default_zero=True),
        cost=parse_decimal(cells.get("Net Unit Price", ""), default_zero=True),
        qty=parse_int(cells["Quantity"]),
        raw=raw_dict(cells),
    )


def _join_product_code(parts: list[str]) -> str:
    cleaned = [clean_text(part) for part in parts if clean_text(part)]
    if not cleaned:
        return ""
    result = cleaned[0]
    for part in cleaned[1:]:
        if result.endswith("-") or part in {"CM", "AB1A-CM", "6517P-CM"}:
            result += part
        else:
            result += f" {part}"
    return result

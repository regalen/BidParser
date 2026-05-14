from __future__ import annotations

from pathlib import Path

from app.parsers.base import BaseParser, LineItem, validate_result
from app.parsers.cleaning import join_unspaced, parse_decimal, parse_int, parse_mmddyyyy
from app.parsers.pdf_utils import collect_words, find_renewal_header, raw_dict, rows_between


class NutanixRenewalPdfParser(BaseParser):
    slug = "nutanix_renewal_pdf"
    display_name = "Renewal (PDF)"
    vendor = "Nutanix"
    accepted_mime = "application/pdf"

    @classmethod
    def parse(cls, path: Path):
        words = collect_words(path)
        columns = find_renewal_header(words)
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
            number = cells.get("No", "")
            product_code = cells.get("Product Code", "")
            if number.isdigit() and product_code:
                if current:
                    items.append(_build_item(current))
                current = {"serial_parts": [cells.get("Serial Number", "")], "cells": cells}
            elif current and any(cells.values()):
                if cells.get("Serial Number"):
                    current["serial_parts"].append(cells["Serial Number"])  # type: ignore[index, union-attr]
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
        vpn=cells["Product Code"],
        serial_number=join_unspaced(data["serial_parts"]),  # type: ignore[arg-type]
        start_date=parse_mmddyyyy(cells["Start Date"]),
        end_date=parse_mmddyyyy(cells["End Date"]),
        msrp=parse_decimal(cells["Term Adjusted List Unit Price"]),
        cost=parse_decimal(cells["Net Unit Price"]),
        qty=parse_int(cells["Qty"]),
        raw=raw_dict(cells),
    )

from __future__ import annotations

import re
from pathlib import Path

from app.parsers.base import BaseParser, LineItem, validate_result
from app.parsers.cleaning import join_spaced, parse_decimal, parse_int
from app.parsers.pdf_utils import collect_words, find_product_code_header, raw_dict, rows_between


class NutanixSoftwareOnlyPdfParser(BaseParser):
    slug = "nutanix_software_only_pdf"
    display_name = "Software Only (PDF)"
    vendor = "Nutanix"
    accepted_mime = "application/pdf"

    @classmethod
    def parse(cls, path: Path):
        words = collect_words(path)
        columns = find_product_code_header(words)
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
            product = cells.get("Product", "")
            if product_code == "Term-Months" or product == "Term in months":
                continue
            if re.fullmatch(r"[A-Z0-9-]+", product_code or ""):
                if current:
                    items.append(_build_item(current))
                current = {"code_parts": [product_code], "description_parts": [product], "cells": cells}
            elif current and not product_code and product:
                current["description_parts"].append(product)  # type: ignore[index, union-attr]
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
        vpn=join_spaced(data["code_parts"]),  # type: ignore[arg-type]
        description=join_spaced(data["description_parts"]),  # type: ignore[arg-type]
        term=parse_int(cells["Term (Months)"]),
        msrp=parse_decimal(cells["List Unit Price"]),
        cost=parse_decimal(cells["Net Unit Price"]),
        qty=parse_int(cells["Quantity"]),
        raw=raw_dict(cells),
    )

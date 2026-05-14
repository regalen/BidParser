from __future__ import annotations

from pathlib import Path

from app.parsers.base import BaseParser, LineItem, validate_result
from app.parsers.cleaning import parse_decimal, parse_int, parse_optional_int
from app.parsers.xlsx_utils import active_sheet, cell_text, cell_value, find_cell, parse_total_text, require_labels
from app.parsers.xlsx_utils import header_map as build_header_map


class NutanixHardwareOnlyXlsxParser(BaseParser):
    slug = "nutanix_hardware_only_xlsx"
    display_name = "Hardware Only (XLSX)"
    vendor = "Nutanix"
    accepted_mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"

    @classmethod
    def parse(cls, path: Path):
        ws = active_sheet(path)
        banner_row, _ = find_cell(ws, "Quote D For distributor to quote to the reseller only")
        header_row, _ = find_cell(ws, "Product Code", min_row=banner_row + 1)
        labels = build_header_map(ws, header_row)
        required = ["Product Code", "Product Description", "Term (Months)", "List Price", "Sale Price", "Quantity"]
        require_labels(labels, required)

        line_items: list[LineItem] = []
        quoted_total = None
        for row in range(header_row + 1, ws.max_row + 1):
            total_cell = next((cell_text(ws, row, col) for col in range(1, ws.max_column + 1) if cell_text(ws, row, col).startswith("TOTAL ")), "")
            if total_cell:
                quoted_total = parse_total_text(total_cell)
                break
            vpn = cell_text(ws, row, labels["Product Code"])
            if not vpn:
                continue
            raw = {label: cell_text(ws, row, column) for label, column in labels.items() if cell_text(ws, row, column)}
            line_items.append(
                LineItem(
                    vpn=vpn,
                    description=cell_text(ws, row, labels["Product Description"]),
                    term=parse_optional_int(cell_value(ws, row, labels["Term (Months)"])),
                    msrp=parse_decimal(cell_value(ws, row, labels["List Price"]), default_zero=True),
                    cost=parse_decimal(cell_value(ws, row, labels["Sale Price"]), default_zero=True),
                    qty=parse_int(cell_value(ws, row, labels["Quantity"])),
                    raw=raw,
                )
            )

        return validate_result(
            source_filename=path.name,
            parser_slug=cls.slug,
            quote_number=path.stem,
            quoted_total=quoted_total,
            line_items=line_items,
        )

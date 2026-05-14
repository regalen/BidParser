from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal
from pathlib import Path
from typing import Any

import pdfplumber

from app.parsers.base import ParseError
from app.parsers.cleaning import clean_text, parse_decimal


@dataclass(slots=True)
class PdfWord:
    text: str
    x0: float
    x1: float
    top: float
    bottom: float
    page_index: int
    page_width: float


@dataclass(slots=True)
class PdfRow:
    page_index: int
    top: float
    cells: dict[str, str]


def collect_words(path: Path) -> list[PdfWord]:
    words: list[PdfWord] = []
    with pdfplumber.open(path) as pdf:
        for page_index, page in enumerate(pdf.pages):
            for word in page.extract_words(x_tolerance=1, y_tolerance=3, keep_blank_chars=False):
                words.append(
                    PdfWord(
                        text=word["text"],
                        x0=float(word["x0"]),
                        x1=float(word["x1"]),
                        top=float(word["top"]),
                        bottom=float(word["bottom"]),
                        page_index=page_index,
                        page_width=float(page.width),
                    )
                )
    return words


def word_stream_text(words: list[PdfWord]) -> str:
    return " ".join(word.text for word in words)


def find_sequence(words: list[PdfWord], sequence: list[str], *, start: int = 0) -> int:
    for index in range(start, len(words) - len(sequence) + 1):
        if [word.text for word in words[index : index + len(sequence)]] == sequence:
            return index
    raise ParseError(
        f"Could not find PDF word sequence: {' '.join(sequence)}",
        stage="detect",
        hint=f"Could not find {' '.join(sequence)!r}.",
    )


def find_product_code_header(words: list[PdfWord], *, start_index: int = 0) -> dict[str, float]:
    for i in range(start_index, len(words) - 2):
        first = words[i]
        second = words[i + 1]
        if first.text == "Product" and second.text == "Code" and first.page_index == second.page_index and abs(first.top - second.top) <= 4:
            page = first.page_index
            top_min = first.top - 16
            top_max = first.top + 16
            header_words = [w for w in words if w.page_index == page and top_min <= w.top <= top_max]
            product_words = [w for w in header_words if w.text == "Product"]
            if len(product_words) < 2:
                continue
            discount_words = [w for w in header_words if w.text == "Discount"]
            net_words = [w for w in header_words if w.text == "Net" and w.x0 > (discount_words[0].x0 if discount_words else 0)]
            total_words = [w for w in header_words if w.text == "Total" and w.x0 > 450]
            return {
                "Product Code": product_words[0].x0,
                "Product": product_words[1].x0,
                "Term (Months)": min((w.x0 for w in header_words if w.text in {"Term", "(Months)"} and w.x0 > product_words[1].x0), default=220.0),
                "List Unit Price": min((w.x0 for w in header_words if w.text in {"List", "Unit", "Price"} and 260 <= w.x0 <= 340), default=280.0),
                "Total Discount": discount_words[0].x0 if discount_words else 348.0,
                "Net Unit Price": (net_words[0].x0 - 22) if net_words else 396.0,
                "Quantity": next((w.x0 for w in header_words if w.text == "Quantity"), 460.0),
                "Total Net Price": total_words[0].x0 if total_words else 514.0,
                "__page": float(page),
                "__top": first.top,
                "__end_x": first.page_width,
            }
    raise ParseError("Could not find Product Code header", stage="detect", hint="Could not find the Product Code table header.")


def find_renewal_header(words: list[PdfWord]) -> dict[str, float]:
    for word in words:
        if word.text != "No":
            continue
        page = word.page_index
        top_min = word.top - 20
        top_max = word.top + 22
        header_words = [w for w in words if w.page_index == page and top_min <= w.top <= top_max]
        if not any(w.text == "Serial" for w in header_words):
            continue
        discount_words = [w for w in header_words if w.text == "Discount"]
        net_words = [w for w in header_words if w.text == "Net" and w.x0 > 430]
        total_words = [w for w in header_words if w.text == "Total" and w.x0 > 500]
        return {
            "No": word.x0,
            "Product Code": next(w.x0 for w in header_words if w.text == "Product"),
            "Serial Number": next(w.x0 for w in header_words if w.text == "Serial"),
            "Start Date": next(w.x0 for w in header_words if w.text == "Start"),
            "End Date": next(w.x0 for w in header_words if w.text == "End"),
            "Term Adjusted List Unit Price": min((w.x0 for w in header_words if w.text in {"Adjusted", "List"}), default=348.0) - 8,
            "Total Discount": discount_words[0].x0 if discount_words else 395.0,
            "Net Unit Price": net_words[0].x0 if net_words else 448.0,
            "Qty": next(w.x0 for w in header_words if w.text == "Qty"),
            "Total Net Price": total_words[0].x0 if total_words else 522.0,
            "__page": float(page),
            "__top": word.top,
            "__end_x": word.page_width,
        }
    raise ParseError("Could not find Renewal header", stage="detect", hint="Could not find the Renewal table header.")


def rows_between(
    words: list[PdfWord],
    columns: dict[str, float],
    *,
    start_page: int,
    start_top: float,
    stop_text: str = "TOTAL:",
    y_tolerance: float = 3.5,
) -> tuple[list[PdfRow], Decimal | None]:
    ordered = [w for w in words if (w.page_index > start_page or (w.page_index == start_page and w.top > start_top + 6))]
    stop_index = next((i for i, word in enumerate(ordered) if word.text == stop_text), len(ordered))
    body_words = ordered[:stop_index]
    total_words = ordered[stop_index : stop_index + 8]
    total = total_from_words(total_words)

    rows: list[list[PdfWord]] = []
    for word in body_words:
        if rows and word.page_index == rows[-1][0].page_index and abs(word.top - rows[-1][0].top) <= y_tolerance:
            rows[-1].append(word)
        else:
            rows.append([word])

    visible_columns = [(name, x0) for name, x0 in columns.items() if not name.startswith("__")]
    visible_columns.sort(key=lambda item: item[1])
    ranges: list[tuple[str, float, float]] = []
    for index, (name, x0) in enumerate(visible_columns):
        x1 = visible_columns[index + 1][1] if index + 1 < len(visible_columns) else columns.get("__end_x", 9999.0)
        ranges.append((name, x0, x1))

    parsed_rows: list[PdfRow] = []
    for row_words in rows:
        cells: dict[str, list[str]] = {name: [] for name, _, _ in ranges}
        for word in sorted(row_words, key=lambda w: w.x0):
            for name, x0, x1 in ranges:
                if x0 - 1 <= word.x0 < x1 - 1:
                    cells[name].append(word.text)
                    break
        parsed_cells = {name: clean_text(" ".join(parts)) for name, parts in cells.items()}
        if any(parsed_cells.values()):
            parsed_rows.append(PdfRow(page_index=row_words[0].page_index, top=row_words[0].top, cells=parsed_cells))
    return parsed_rows, total


def total_from_words(words: list[PdfWord]) -> Decimal | None:
    texts = [word.text for word in words]
    for index, text in enumerate(texts):
        if text == "USD" and index + 1 < len(texts):
            try:
                return parse_decimal(texts[index + 1])
            except Exception:
                continue
    joined = clean_text(" ".join(texts)).replace("TOTAL:", "").replace("TOTAL", "")
    try:
        return parse_decimal(joined)
    except Exception:
        return None


def raw_dict(cells: dict[str, Any]) -> dict[str, str]:
    return {key: clean_text(value) for key, value in cells.items() if clean_text(value)}

from __future__ import annotations

from decimal import Decimal
from pathlib import Path

import pytest

from app.parsers.registry import get_parser


ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "samples" / "inputs"


CASES = [
    (
        "nutanix_software_only_pdf",
        "XQ-4076249.pdf",
        Decimal("1625358.51"),
        [
            ("SW-NCM-STR-PR", 60, Decimal("383"), Decimal("101.11"), 2096),
            ("SW-NCI-PRO-PR", 60, Decimal("2275"), Decimal("600.60"), 864),
            ("SW-NCI-PRO-PR", 60, Decimal("2275"), Decimal("600.60"), 1232),
            ("SW-NCI-E-PRO-PR", 60, Decimal("3455"), Decimal("912.12"), 145),
            ("SW-NCM-E-STR-PR", 60, Decimal("583"), Decimal("153.91"), 145),
        ],
    ),
    (
        "nutanix_software_only_xlsx",
        "XQ-4076249.xlsx",
        Decimal("1625358.51"),
        [
            ("SW-NCM-STR-PR", 60, Decimal("383.00"), Decimal("101.11"), 2096),
            ("SW-NCI-PRO-PR", 60, Decimal("2275.00"), Decimal("600.60"), 864),
            ("SW-NCI-PRO-PR", 60, Decimal("2275.00"), Decimal("600.60"), 1232),
            ("SW-NCI-E-PRO-PR", 60, Decimal("3455.00"), Decimal("912.12"), 145),
            ("SW-NCM-E-STR-PR", 60, Decimal("583.00"), Decimal("153.91"), 145),
        ],
    ),
    (
        "nutanix_hardware_only_pdf",
        "XQ-4108785.pdf",
        Decimal("22491.87"),
        [
            ("NX-1175S-G10-6517P-CM", None, Decimal("25021.99"), Decimal("20017.57"), 1),
            ("C-MEM-32GB-6400-CM", None, Decimal("0"), Decimal("0"), 4),
            ("C-HDD-12TB-ETBA-CM", None, Decimal("0"), Decimal("0"), 2),
            ("C-NVM-7.68TB-AB1A-CM", None, Decimal("0"), Decimal("0"), 2),
            ("C-HBA-3816-1N-C-CM", None, Decimal("0"), Decimal("0"), 1),
            ("C-NIC-25G4E1-CM", None, Decimal("0"), Decimal("0"), 1),
            ("C-PWR-4FC13C14A-CM", None, Decimal("0"), Decimal("0"), 2),
            ("S-HW-PRD", 60, Decimal("4019.99"), Decimal("2411.99"), 1),
            ("Support-Term", 60, Decimal("0.00"), Decimal("0.00"), 60),
            ("C-TPM-2.0-U-C-CM", None, Decimal("77.89"), Decimal("62.31"), 1),
            ("Platform Integration", 0, Decimal("4003.51"), Decimal("0.00"), 1),
        ],
    ),
    (
        "nutanix_hardware_only_xlsx",
        "XQ-4108785.xlsx",
        Decimal("22491.87"),
        [
            ("NX-1175S-G10-6517P-CM", None, Decimal("25021.99"), Decimal("20017.57"), 1),
            ("C-MEM-32GB-6400-CM", None, Decimal("0"), Decimal("0"), 4),
            ("C-HDD-12TB-ETBA-CM", None, Decimal("0"), Decimal("0"), 2),
            ("C-NVM-7.68TB-AB1A-CM", None, Decimal("0"), Decimal("0"), 2),
            ("C-HBA-3816-1N-C-CM", None, Decimal("0"), Decimal("0"), 1),
            ("C-NIC-25G4E1-CM", None, Decimal("0"), Decimal("0"), 1),
            ("C-PWR-4FC13C14A-CM", None, Decimal("0"), Decimal("0"), 2),
            ("S-HW-PRD", 60, Decimal("4019.99"), Decimal("2411.99"), 1),
            ("Support-Term", 60, Decimal("0"), Decimal("0"), 60),
            ("C-TPM-2.0-U-C-CM", None, Decimal("77.89"), Decimal("62.31"), 1),
            ("Platform Integration", 0, Decimal("4003.51"), Decimal("0.00"), 1),
        ],
    ),
]


@pytest.mark.parametrize(("slug", "filename", "quoted_total", "expected"), CASES)
def test_parser_extracts_expected_line_items(slug: str, filename: str, quoted_total: Decimal, expected: list[tuple[str, int | None, Decimal, Decimal, int]]) -> None:
    result = get_parser(slug).parse(SAMPLES / filename)

    assert result.metadata.quoted_total == quoted_total
    assert result.validation.computed_total == quoted_total
    assert result.validation.matches is True
    assert [(item.vpn, item.term, item.msrp, item.cost, item.qty) for item in result.line_items] == expected


def test_nutanix_renewal_pdf_extracts_serials_dates_and_totals() -> None:
    result = get_parser("nutanix_renewal_pdf").parse(SAMPLES / "XQ-4128926.pdf")

    assert result.metadata.quoted_total == Decimal("60205.68")
    assert result.validation.matches is True
    assert [
        (item.vpn, item.serial_number, item.start_date.isoformat(), item.end_date.isoformat(), item.msrp, item.cost, item.qty)
        for item in result.line_items
    ] == [
        ("RSW-NCM-STR-PR", "24SW000351227,LIC-02472987", "2026-07-13", "2027-07-12", Decimal("77.00"), Decimal("54.41"), 160),
        ("RSW-NCI-ULT-PR", "24SW000351236,LIC-02472996", "2026-07-13", "2027-07-12", Decimal("575.00"), Decimal("371.83"), 32),
        ("RSW-NCI-ULT-PR", "24SW000351221,LIC-02472983", "2026-07-13", "2027-07-12", Decimal("575.00"), Decimal("429.11"), 72),
        ("RSW-NCM-STR-PR", "24SW000351228,LIC-02472985", "2026-07-13", "2027-07-12", Decimal("77.00"), Decimal("54.41"), 160),
    ]

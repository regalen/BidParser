from __future__ import annotations

from app.parsers.base import BaseParser
from app.parsers.nutanix_hardware_only_pdf.parser import NutanixHardwareOnlyPdfParser
from app.parsers.nutanix_hardware_only_xlsx.parser import NutanixHardwareOnlyXlsxParser
from app.parsers.nutanix_renewal_pdf.parser import NutanixRenewalPdfParser
from app.parsers.nutanix_software_only_pdf.parser import NutanixSoftwareOnlyPdfParser
from app.parsers.nutanix_software_only_xlsx.parser import NutanixSoftwareOnlyXlsxParser


PARSER_REGISTRY: list[type[BaseParser]] = [
    NutanixSoftwareOnlyPdfParser,
    NutanixSoftwareOnlyXlsxParser,
    NutanixRenewalPdfParser,
    NutanixHardwareOnlyPdfParser,
    NutanixHardwareOnlyXlsxParser,
]


def get_parser(slug: str) -> type[BaseParser]:
    for parser in PARSER_REGISTRY:
        if parser.slug == slug:
            return parser
    raise KeyError(f"Unknown parser slug: {slug}")

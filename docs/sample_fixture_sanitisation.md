# Sample fixture sanitisation record

The committed fixtures are required by the parser/output test suite. They are anonymised test
data, not source quotations: the inputs exercise extraction and the output workbooks are golden
files compared cell by cell.

## Scope completed

All committed material in `samples/inputs/`, `samples/outputs/`, `samples/template/`, and the
matching browser-download copies in `frontend/public/samples/` was reviewed and sanitised.

| Fixture groups | Sanitised fields |
| --- | --- |
| Nutanix, Lenovo, Zebra, Datalogic, Epson and Strike PDFs | customer and reseller names, contacts, email addresses, phone numbers, addresses, quote/bid identifiers and filenames |
| HP, HPE, Cisco, Lenovo and Nutanix spreadsheets | distributor/reseller/end-user names, contact details, postal addresses, deal/bid/quote identifiers, embedded document metadata and filenames |
| Dell JSON fixtures | customer/account details, contact details, service tags and quote identifiers |
| Golden outputs and CRM templates | customer/contact/identifier values and embedded workbook metadata |

Supplier product names, public legal text and structural anchors are retained where a parser uses
them. Sensitive identifiers use synthetic values; contact addresses use the reserved
`example.invalid` domain.

## Approved retained supplier information

The fixture audit permits public supplier-facing information that is necessary to preserve a
document's structure or parser coverage. This may include supplier legal-entity wording, public
corporate contact details in standard terms or footers, supplier product catalogues, and
representative commercial values such as prices, discounts, margins, quote/deal identifiers and
partner-number fields. These are not customer, reseller or employee identifiers.

Customer, reseller and contact values in the fixture-specific portions of documents remain
synthetic or redacted. Any future fixture that contains non-public commercial information or an
identifiable customer, reseller or individual must still be sanitised before it enters the working
tree.

## Adding or replacing fixtures

Fixture sanitisation is a privacy and security requirement, not a filename convention. Review the
source file, its metadata, its filename, any golden output, and every browser-download copy before
commit. Do not place a raw customer document in the working tree, even temporarily. Use synthetic
fixtures where possible; when retaining a real-world structure is necessary, sanitise it first and
run the parser/output tests afterward.

The contributor checklist and mandatory rules are in `CONTRIBUTING.md` and `AGENTS.md`.

## Integrity checks

- Every renamed fixture reference was updated in tests, documentation and the frontend sample map.
- Each public sample selected by the frontend is byte-identical to its root input fixture.
- Parser/output suite: 661 passed.
- Frontend production build: passed.

Repository history is intentionally out of scope for this change. It must be rewritten separately
before publishing if prior commits may contain the original fixtures.

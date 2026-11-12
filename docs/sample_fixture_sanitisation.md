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
| Trellix PDFs | Fully synthetic names, identifiers, serials, prices, totals, and PDF content; no source metadata retained |
| Trellix XLSM workbooks | Newly generated synthetic names, identifiers, serials, dates, prices, totals, and workbook metadata; no VBA or private source sheets retained |
| Juniper XLSX workbooks | Newly generated synthetic customer/partner details, transaction/quote identifiers, prices, totals, and `regalen` workbook metadata; only public Juniper catalog SKUs and product descriptions retained |
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

The Trellix fixtures were authored as new PDFs outside the repository after reviewing all four
private report variants. `Trellix_Quote_900001.pdf` reproduces the multi-page table and Data Center
column; `Trellix_Quote_900002.pdf` reproduces the alternate Original Serial/Currency columns and
adds a hardware-quantity and latest-serial case. Both use synthetic quote/SKU/grant/serial values
and deliberately invented prices. Their visible text and PDF metadata were reviewed before copying
them into `samples/inputs/`; the first browser sample is byte-identical to its input. The CRM
goldens were generated from these inputs with the shared writer and contain no customer data or
inherited workbook metadata from the private reference.

`Trellix_Quote_900003.xlsm` and `Trellix_Quote_900004.xlsm` were generated from scratch with
invented quote, SKU, grant, and serial identifiers; customer-free descriptions; invented dates
and prices; cached formula values; and `regalen` workbook creator metadata. Their XLSM package
type is macro-enabled, but no VBA project is present. The first browser sample is byte-identical
to its root input. CRM goldens were written from these synthetic inputs with the shared writer.
The four private workbooks in the ignored inbox were used only for local extraction checks.

`Juniper_CPQ_900001.xlsx` was generated from scratch with synthetic customer/partner names and
addresses, a synthetic transaction identifier (`CPQ-900001`), invented unit list and extended net
prices, and `regalen` workbook creator metadata. Public Juniper catalog SKUs and standard product
descriptions (`EX4400-48MP`, `S-EX-P-C3-5-SD`, `S-EX-FBT-P`, `CBL-EX-PWR-C13-C14`,
`JPSU-1600-C-AC-AFO`, `EX4400-EM-4Y`, `SFPP-10G-SR-C`, `JNP-100G-DAC-3M`, `JNP-100G-DAC-1M`) are
retained to exercise structural option-summary exclusion and product line extraction. The browser
sample in `frontend/public/samples/` is byte-identical to its root input, and both CRM goldens were
generated from the synthetic input via the shared writer.

`Juniper_Indirect_900001.xlsx` was generated from scratch with synthetic distributor/reseller/customer
names, `example.invalid` placeholder contact emails, synthetic quote/deal/ship-and-debit identifiers
(`90000101`, `CONFIG INSTANCE = 2`), invented unit and extended prices, and `regalen` workbook
creator metadata. Public Juniper catalog SKUs and standard descriptions (`AP27-WW`, `SUB-1S-1Y`,
`SUB-1S-3Y`, `SUB-1S-5Y`) are retained to exercise repeated-SKU flat extraction and subscription
duration comments. The browser sample in `frontend/public/samples/` is byte-identical to its root
input, and both CRM goldens were generated from the synthetic input via the shared writer. The two
reference workbooks in the ignored `juniper/` directory were used only for local extraction checks.

## Adding or replacing fixtures

Fixture sanitisation is a privacy and security requirement, not a filename convention. Review the
source file, its metadata, its filename, any golden output, and every browser-download copy before
commit. Do not place a raw customer document in the working tree, even temporarily. Use synthetic
fixtures where possible; when retaining a real-world structure is necessary, sanitise it first and
run the parser/output tests afterward.

The contributor checklist and mandatory rules are in `CONTRIBUTING.md` and `AGENTS.md`.

## Initial sanitisation integrity checks

The results below record the initial sanitisation pass before Juniper was added; they are not the current suite counts.

- Every renamed fixture reference was updated in tests, documentation and the frontend sample map.
- Each public sample selected by the frontend is byte-identical to its root input fixture.
- Parser/output suite: 699 passed.
- Frontend production build: passed.

Repository history is intentionally out of scope for this change. It must be rewritten separately
before publishing if prior commits may contain the original fixtures.

### Juniper Iris

`Juniper_Iris_Synthetic.xlsx` was authored from scratch outside the repository with invented
MSRP values (410 and 280 USD), quantities (2 and 1), and total (1100 USD). Public Juniper SKUs
and generic supplier descriptions retain the table structure. Creator/editor metadata is `regalen`.
The browser sample is byte-identical to the input; the shared writer generated the matching
ForeignUplift golden using FX 1.5, Uplift 5, and IM% 30.25, with calculated unit costs
285.98 and 195.30 USD and an empty IM% column. The private Iris workbook
is used only for local acceptance and its personal metadata is not copied into fixtures.

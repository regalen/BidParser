# Trellix Quote (XLSM)

- **Parser:** `TrellixQuoteXlsmParser`
- **Slug:** `trellix_quote_xlsm`
- **Accepted MIME:** `application/vnd.ms-excel.sheet.macroEnabled.12`
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Additional settings:** none
- **Selection:** Trellix Auto is preselected; manual Quote (XLSM) remains available.

## Recognition and extraction

The parser opens the macro-enabled OpenXML workbook as data; it does not run VBA. It selects a
visible sheet with `Quote ID`, `Quote Currency`, `Channel SKU`, and Trellix identity. The
`Channel SKU` heading anchors the product table. Header names determine the columns, so inserted
rows or shifted columns do not alter extraction. A placeholder row immediately after the heading
has no SKU and is skipped. Each subsequent row with a Channel SKU is one item in source order.

`Start Date` and `End Date` each occur twice in the export. The **first**, displayed pair is the
PDF-equivalent source; the second pair contains underlying dates. When the displayed Start Date is
blank but its underlying date is present, CRM Start Date stays blank. Formula cells are read from
their cached display values. `Selling Term` uses its displayed `0.000` formatting in Comments.

## Field mapping

| Source | `LineItem` / rule |
|---|---|
| `Channel SKU` | `Vpn`; remove all whitespace |
| `Product Description` | `Description` |
| `QTY Hardware`, `QTY Software or Support (Nodes)` | `Qty`; exactly one must be a positive integer |
| `Cost Per Unit` | `Cost` |
| `Total MSRP` | `Msrp = Total MSRP / Qty`, including non-unit selling terms |
| `Latest Serial Number` | `SerialNumber`; blank becomes null |
| First `Start Date`, first `End Date` | Nullable `DateOnly` |
| `Program Type`, formatted `Selling Term`, `Grant #'s` | `Comments`, in that order, joined with `" | "`; omit blanks and remove grant whitespace |
| Document order | `LineSequence = "1", "2", …` |

`Term` stays null. `Raw` retains non-empty source cells under their column headings. A missing
required header is a `detect` error; malformed item values are `extract` errors. Currency must be
AUD or a `currency` error is raised.

## Metadata and validation

The anchored `Quote ID` supplies both `QuoteNumber` and revisionless bid identity via
`BidMetadataCleaner`; unreadable identity degrades to null bid fields. The XLSM export has no
quote-level `Total Distribution Cost`. Its quoted total is `Σ(Final Disti Cost)` over product
rows, compared with `Σ(Cost Per Unit × Qty)` at the shared 0.01 tolerance. The zero-cost line in
the synthetic fixture is a genuine free item and uses the shared `0.0001` output sentinel.

Output columns, template behavior, and the `TRELLIX` vendor label match Quote (PDF). Neither
parser has seeded result guidance or On Cost capability. See `docs/output_mapping.md` for the CRM
columns.

## Retained synthetic fixtures

| Fixture | Coverage |
|---|---|
| `Trellix_Quote_900003.xlsm` | Four lines, shifted header row, placeholder, formulas with cached values, first and underlying date pairs, non-unit selling term, hardware quantity, serial, and zero price; quoted total AUD 99.99 |
| `Trellix_Quote_900004.xlsm` | One line, differently shifted columns, hidden synthetic metadata sheet, blank end date; quoted total AUD 36.25 |

The first browser sample is byte-identical to its input fixture. Both workbooks were built from
invented names, identifiers, dates, and prices and contain no VBA or source metadata. The four
private examples remain in the ignored inbox and are not published.

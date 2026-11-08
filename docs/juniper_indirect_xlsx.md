# Juniper Indirect (XLSX)

- **Parser:** `JuniperIndirectXlsxParser`
- **Slug:** `juniper_indirect_xlsx`
- **Accepted MIME:** `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- **CRM templates:** `Foreign Uplift` (default), `Foreign No Calculation`
- **Additional settings:** none
- **Selection:** Juniper Auto (`juniper_auto`) is preselected; manual `Indirect (XLSX)` remains available.

## Recognition and extraction

The parser scans visible worksheets for a header row containing the eight required Indirect columns:
`SKU`, `SKU DESCRIPTION`, `QUANTITY`, `UNIT NET PRICE`, `LIST PRICE $`, `EXTENDED NET TOTAL`,
`SERVICE DURATION`, and `CURRENCY`. Header matching is case-insensitive and normalizes whitespace,
while retaining each column's cleaned source label verbatim as the `LineItem.Raw` key.

Rows below the header row are processed in source order:

- Wholly blank rows across the mapped columns are skipped.
- Every non-empty row is extracted as a flat product line in source order. Repeated SKUs (such as
  multiple hardware lines paired with different subscription terms) are preserved without grouping
  or parent/child hierarchy.

## Field mapping

| Source | `LineItem` / rule |
|---|---|
| `SKU` | `Vpn` (required) |
| `SKU DESCRIPTION` | `Description` (required) |
| `QUANTITY` | `Qty` (required positive integer) |
| `UNIT NET PRICE` | `Cost` (required non-negative decimal) |
| `LIST PRICE $` | `Msrp` (required non-negative decimal) |
| `SERVICE DURATION` | `Term` (nullable positive integer; blank stays `null`, never inferred from SKU or description) |
| Document order | `LineSequence` stays `null` so the foreign CRM profile emits numeric `1, 2, …` in Column A |

`TermRendersAsComment` is `true`, so any populated positive `Term` renders as `"{n} Months"` in
Column R (`Comments`) while blank terms leave Column R empty. Genuine `$0.00` prices remain `0m` on
`LineItem` and receive the `0.0001` sentinel at write time in Columns T/U.

Missing table headers raise a `detect` error; malformed product rows raise an `extract` error.

## Metadata, currency, and per-row validation

- **Bid metadata:** `SHIP AND DEBIT ID` and `CONFIG INSTANCE` are read from the first extracted
  product line when those columns are present and passed through `BidMetadataCleaner.Clean(shipAndDebitId, configInstance)`.
  Missing metadata is non-fatal and degrades to `null` bid fields while `QuoteNumber` falls back to
  the source filename stem.
- **Currency:** Every extracted product row must declare `"USD"` in `CURRENCY` (case-insensitive).
  Missing or non-USD values raise a `currency` error.
- **Per-row validation:** Indirect exports carry no quote-level total, so `QuotedTotal` remains
  `null`. Each extracted line's `Cost × Qty` is compared against its own `EXTENDED NET TOTAL` within
  `$0.01` tolerance. Any inconsistent line sets `ValidationResult.Matches = false` and records a
  line-specific warning in `ValidationResult.Warnings`—even when opposing discrepancies on different
  rows cancel out in the net difference—producing a saveable validation mismatch rather than a hard
  parse error.

Both `Foreign Uplift` and `Foreign No Calculation` write to the `Foreign Uplift` worksheet with
`JUNIPER` in Column B and `"USD"` in Column S. See `docs/output_mapping.md` for full column layout.

## Retained synthetic fixtures

| Fixture | Coverage |
|---|---|
| `Juniper_Indirect_900001.xlsx` | Synthetic 6-line Indirect Quotation with 66-column header layout, 3 repeated `AP27-WW` hardware lines (blank durations), 3 subscription lines (`SUB-1S-1Y`, `SUB-1S-3Y`, `SUB-1S-5Y` with durations `12`, `36`, `60`), `SHIP AND DEBIT ID = 90000101`, and `CONFIG INSTANCE = 2`; computed total USD 2,105.00 |

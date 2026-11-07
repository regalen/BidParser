# Juniper CPQ (XLSX)

- **Parser:** `JuniperCpqXlsxParser`
- **Slug:** `juniper_cpq_xlsx`
- **Accepted MIME:** `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- **CRM templates:** `Foreign Uplift` (default), `Foreign No Calculation`
- **Additional settings:** none
- **Selection:** Juniper Auto is preselected once Indirect is registered; manual `CPQ (XLSX)` remains available.

## Recognition and extraction

The parser scans visible worksheets for a header row containing the six required CPQ columns:
`SKU`, `Description`, `Service Duration`, `List Price Per`, `Quantity`, and `Extended Net Price`.
Header matching is case-insensitive and normalizes leading/trailing whitespace, while retaining each
column's cleaned source label verbatim as the `LineItem.Raw` key.

Rows between the header row and the `Totals:` summary row are processed in source order:

- Wholly blank rows across the mapped columns are skipped.
- **Structural option summaries** carry a `SKU`, positive integer `Quantity`, and non-negative
  `Extended Net Price`, while leaving `Description`, `Service Duration`, and `List Price Per`
  blank. They represent configuration-group rollup lines and are excluded by content rather than
  row position or duplicate SKU.
- Every remaining non-empty row before `Totals:` is treated as a product row. Duplicate SKUs across
  product rows are preserved in source order.

## Field mapping

| Source | `LineItem` / rule |
|---|---|
| `SKU` | `Vpn` (required) |
| `Description` | `Description` (required) |
| `Quantity` | `Qty` (required positive integer) |
| `Extended Net Price` / `Quantity` | `Cost = Extended Net Price / Quantity` (unrounded division; non-negative) |
| `List Price Per` | `Msrp` (required non-negative decimal) |
| `Service Duration` | `Term` (nullable positive integer; blank stays `null`, never inferred from SKU or description) |
| Document order | `LineSequence` stays `null` so the foreign CRM profile emits numeric `1, 2, …` in Column A |

`TermRendersAsComment` is `true`, so any populated positive `Term` renders as `"{n} Months"` in
Column R (`Comments`) while blank terms leave Column R empty. Genuine `$0.00` prices remain `0m` on
`LineItem` and receive the `0.0001` sentinel at write time in Columns T/U.

Missing table headers raise a `detect` error; malformed product rows or missing totals after header
recognition raise an `extract` error.

## Metadata, currency, and validation

- **Bid metadata:** `Transaction #:` is located by label anchor and cleaned via
  `BidMetadataCleaner.CleanRevisionless`, storing revision `"1"`. Missing metadata is non-fatal and
  degrades to `null` bid fields while `QuoteNumber` falls back to the source filename stem.
- **Currency:** `"USD"` by format contract (the CPQ Bill of Materials export has no currency field).
- **Validation:** `Σ(Cost × Qty)` is compared against the mapped `Extended Net Price` cell on the
  row anchored by `Totals:` using `ParseValidation.Validate` (`0.01` tolerance).

Both `Foreign Uplift` and `Foreign No Calculation` write to the `Foreign Uplift` worksheet with
`JUNIPER` in Column B and `"USD"` in Column S. See `docs/output_mapping.md` for full column layout.

## Retained synthetic fixtures

| Fixture | Coverage |
|---|---|
| `Juniper_CPQ_900001.xlsx` | Synthetic 9-line CPQ Bill of Materials with padded headers, 1 structural option summary row (`EX4400-48MP`) sharing its SKU with the first product row, blank service durations, and `Totals:` row; USD 52,200.00 |

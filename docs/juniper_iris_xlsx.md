# Juniper Iris (XLSX)

- Parser: `JuniperIrisXlsxParser`, slug `juniper_iris_xlsx`.
- Template: `Foreign Uplift` only.
- MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- Additional settings: per-line pricing review (`SupportsLinePricing => true`). Exchange rate is header-level; IM% (Discount Off MSRP) and Uplift (margin on sale price) are reviewed per line in the pricing popup.
- Explicit selection is required on web and desktop. `Detect()` only supports wrong-file-type suggestions.

## Recognition and extraction

Search visible worksheets for a row with `Part Number`, `Description`, `Quantity`, `Unit Price`,
`Manufacturer`, `Discount`, `Total`, and `Price List`. Match headers case-insensitively after trimming.
Locate all columns by those labels, never by fixed positions. Process product rows in source order
until a row containing `Quote Total`; skip wholly blank rows and preserve repeated part numbers.

| Source | Domain value |
|---|---|
| Part Number | `Vpn`, required |
| Description | `Description`, full source text, required |
| Quantity | `Qty`, positive integer |
| Unit Price | `Msrp`, nonnegative decimal |

`Cost` is zero as an internal placeholder. The parser declares `CalculatesCostFromMsrp`; the
shared writer calculates output cost from MSRP and the user’s IM% at write time. No term is inferred from
SKU or description. Raw source values retain the original header keys.

Every row must have manufacturer `Juniper Networks` or `Juniper` and a price-list label ending
in `(USD)`. Other vendors, mixed-vendor workbooks, and other currencies are rejected in full.
Source Discount must be zero (numeric or percent-formatted); nonzero discounts are rejected.
Product Total and Quote Total must be readable, nonnegative decimals.

Missing headers raise `detect`; malformed rows, discounts, empty tables, or absent/invalid totals
raise `extract`; unsupported currency raises `currency`.

## Validation, metadata, and output

Construct validation directly: compare `Σ(Msrp × Qty)` with the Quote Total row's mapped `Total`
cell, using tolerance 0.01 USD. A mismatch is saveable through the ordinary result workflow.
Product `Total` values are source provenance, not a substitute for computing MSRP extensions.

Bid number and revision are null. Quote identity and output naming use the existing source-stem
fallback. USD is the only supported currency. Because `SupportsLinePricing` is `true`, header-level
Exchange Rate, Uplift, and IM% inputs do not appear on the main form; instead, the pricing review
popup collects Exchange Rate at header level and per-line IM% (`LineItem.ImPercent`) and Uplift
(`LineItem.Margin`, where Uplift is margin on the sale price: `0 ≤ Uplift < 100`, at most 2 dp).
Header IM% and Uplift inputs in the popup act as bulk **Apply** helpers across all lines. Every
non-cancelled Iris line with `Msrp > 0` requires a per-line IM% (nonnegative, at most two decimal
places, with no upper limit). BidParser calculates Foreign Cost per unit in column T as
`Msrp × (1 − IM% / 100)`, rounded to two decimal places with midpoint rounding away from zero
(`LinePricingMath.CostFromMsrp`). For MSRP 427, IM% 15 gives 362.95, and IM% 27.53 gives 309.45.
A calculated zero uses the standard zero-price sentinel. Column K writes each line's Uplift
(`LineItem.Margin`), column V writes the header Exchange Rate, and IM% column X remains blank.
Parsed MSRP and source-total validation remain unchanged. See [output mapping](output_mapping.md#juniper-iris-foreign-uplift).

The retained fixture `Juniper_Iris_Synthetic.xlsx` contains two products totaling 1100 USD.
See [fixture sanitisation](sample_fixture_sanitisation.md#juniper-iris) for publication review.

# Juniper Iris (XLSX)

- Parser: `JuniperIrisXlsxParser`, slug `juniper_iris_xlsx`.
- Template: `Foreign %Off RRP with Uplift` only.
- MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
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

`Cost` is zero only as an internal placeholder and is never exported. No term is inferred from
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
fallback. USD is the only supported currency. Exchange Rate, Uplift, and IM% are user inputs;
IM% must be nonnegative with at most two decimal places and has no upper limit. CRM calculates
cost. See [output mapping](output_mapping.md#juniper-iris-foreign-off-rrp-with-uplift).

The retained fixture `Juniper_Iris_Synthetic.xlsx` contains two products totaling 1100 USD.
See [fixture sanitisation](sample_fixture_sanitisation.md#juniper-iris) for publication review.

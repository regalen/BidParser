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
An optional `Line#` column drives output sequencing (below); `Design Group` and `Sub Total` are
ignored.

| Source | Domain value |
|---|---|
| Part Number | `Vpn`, required |
| Description | `Description`, full source text, required |
| Quantity | `Qty`, positive integer |
| Unit Price | `Msrp`, nonnegative decimal; `0` for an included component |
| Line# | `LineSequence`, flattened to parent/child (optional column) |

`Cost` is zero as an internal placeholder. The parser declares `CalculatesCostFromMsrp`; the
shared writer calculates output cost from MSRP and the user’s IM% at write time. No term is inferred from
SKU or description. Raw source values retain the original header keys.

Every row must have manufacturer `Juniper Networks` or `Juniper`. Every priced row must have a
price-list label ending in `(USD)`. Other vendors, mixed-vendor workbooks, and other currencies are
rejected in full. Source Discount must be zero (numeric or percent-formatted); nonzero discounts are
rejected. Product Total and Quote Total must be readable, nonnegative decimals.

### Included components

Iris lists bundled components (for example an access-point bracket or the services inside a
subscription bundle) with Unit Price `incl.` and blank Price List, Discount, and Total: their price
sits on the parent line. A row is an **included component** when its Unit Price is `incl.` (any case,
trailing dot optional), or when both Unit Price and Price List are blank. Its Price List, Discount,
and Total are not checked, but Part Number, Description, Manufacturer, and Quantity are. It becomes
a genuine zero-price line (`Msrp = 0`, `Cost = 0`): it needs no IM%, the writer emits the
zero-price sentinel `0.0001` for Foreign MSRP (U) and Foreign Cost (T), and it adds nothing to the
`Σ(Msrp × Qty)` validation total. A blank Unit Price with a non-blank Price List is still a
malformed row (`extract`).

### Line sequencing

Iris numbers lines up to three levels deep (`1.0`, `1.2`, `1.2.1`). CRM templates sequence two
levels, so `Line#` is flattened into the same `N` / `N.01` parent/child form HP, HPE, Lenovo, and
Dell CTO use:

- A `Line#` with no non-zero component after the first (`1`, `1.0`) opens a parent, numbered by a
  running counter.
- Every deeper row, at any depth, becomes the next child of the open parent:
  `1.0, 1.1, 1.2, 1.2.1, 1.2.2` → `1, 1.01, 1.02, 1.03, 1.04`.
- A child whose top-level number differs from the open parent's (e.g. `2.1` with no `2.0`), or a
  blank/unreadable `Line#`, opens a new parent rather than attaching to the wrong group.
- Without a `Line#` column, `LineSequence` is null and the writer falls back to its numeric counter.

Unlike HP/HPE bundle details, Iris children are separately priced: they keep their own MSRP and
require IM% like any other line. Only included components are zero-priced.

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
non-cancelled Iris line with `Msrp > 0` requires a per-line IM% (`0 ≤ IM% ≤ 100`, at most two
decimal places; 100 makes the line free and writes the zero-price sentinel, and anything above 100
is rejected because it would produce a negative cost). The web popup refuses keystrokes and pastes
that could never be valid (a minus sign, a value over the limit, a third decimal place, letters)
and shows the limit briefly; its totals read "—" until every line is complete and valid, so a
partial sum is never shown. BidParser calculates Foreign Cost per unit in column T as
`Msrp × (1 − IM% / 100)`, rounded to two decimal places with midpoint rounding away from zero
(`LinePricingMath.CostFromMsrp`). For MSRP 427, IM% 15 gives 362.95, and IM% 27.53 gives 309.45.
A calculated zero uses the standard zero-price sentinel. Column K writes each line's Uplift
(`LineItem.Margin`), column V writes the header Exchange Rate, and IM% column X remains blank.
Parsed MSRP and source-total validation remain unchanged. See [output mapping](output_mapping.md#juniper-iris-foreign-uplift).

The retained fixture `Juniper_Iris_Synthetic.xlsx` contains two top-level products (`Line#` 1.0
and 2.0, sequenced `1` and `2`) totaling 1100 USD. Included components and three-level numbering are
covered by fully synthetic workbooks built inside `JuniperIrisXlsxParserTests`.
See [fixture sanitisation](sample_fixture_sanitisation.md#juniper-iris) for publication review.

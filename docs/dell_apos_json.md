# Dell APOS (API) Extraction Spec

## Overview

- **Slug:** `dell_apos_json`
- **Vendor:** `Dell`
- **CRM Template:** `No Calculation` only (via `CrmWriter`)
- **Accepted MIME:** `application/json` (`.json`)
- **Input Format:** JSON response fetched directly from the Dell Quote API by quote ID; manual JSON upload is not exposed.

## Extraction Mapping

Dell APOS quotes consist of top-level quote metadata, `items[]`, and nested skus (`items[].skus[]`). Unlike CTO quotes, APOS quotes do not emit item parents; eligible child SKUs are emitted as flat top-level line items.

### Header & Metadata
- **Quote Number:** `quoteNumber` (converted to string). Fallback: filename stem.
- **Supplier:** `"Dell"`
- **Currency:** `currency` (defaults to `"AUD"` if null/empty).
- **Quoted Total:** top-level `salesPrice`.

### Line Items
All `skus[]` across all `items[]` are flattened, then **sorted stably by `serviceTags.serviceTagNumber`** using `Ordinal` string comparison. Output line sequence numbers are flat integer indices (`1`, `2`, `3`, ...).

**No SKU is ever filtered out.** The parent/child rules in `docs/dell_cto_json.md` — the `Displays`
rule and the restates-its-parent rule — are CTO-only and must not be applied here. APOS emits no
parent lines, so its SKUs *are* the priced output: dropping one removes real money from
`Σ(cost × qty)` and breaks reconciliation against the header's `salesPrice`. An APOS item's price is
the roll-up of its SKUs, so a single-SKU item legitimately restates its parent's SKU number and both
prices, and must still be emitted.

| Output Field | Source Path | Notes |
|---|---|---|
| `line_sequence` | `1`, `2`, `3`, ... | Flat sequence assigned after sorting by service tag |
| Vendor | `"DELL"` | Written to Column B by `CrmWriter` |
| `vpn` | `skus[].skuNumber` | Part number |
| `description` | `skus[].description` + `" - "` + `skus[].lineOfBusiness` | Uses only `skus[].description` when `lineOfBusiness` is `"Misc"`, absent, or blank |
| `qty` | `skus[].quantity` | Quantity |
| `msrp` | `skus[].unitListPrice` | Unit MSRP |
| `cost` | `skus[].unitSalesPrice` | Unit Cost |
| Margin (col K) | User Uplift % | Written by `CrmWriter` on the `Uplift` profile only |
| `start_date` (col P) | `skus[].serviceTags.newContractStartDate` | Null if `0001-01-01T00:00:00` sentinel |
| `end_date` (col Q) | `skus[].serviceTags.newContractEndDate` | Null if `0001-01-01T00:00:00` sentinel |
| `serial_number` (col M) | `skus[].serviceTags.serviceTagNumber` | Device serial / Service Tag number |

## Validation

- `computed_total = Σ(sku cost × sku qty)`
- Compare `computed_total` against `quoted_total = salesPrice` (top-level) with tolerance `0.01`.

## Recognition & Cross-Detection

- Recognised when JSON contains valid `items[].skus[]` **and at least one source SKU contains a
  non-empty `serviceTags.serviceTagNumber`**. Classification happens before output filtering. Null,
  missing, empty, and whitespace-only values do not count as service tags.
- If no sku carries a `serviceTagNumber` (such as a Dell CTO JSON file), or JSON is malformed, throws `ParseError("detect", "File is not a Dell APOS quote.", "Could not read Dell APOS JSON structure.")`.
- `Detect()` returns `0.9` for APOS files and `0.0` for non-APOS files (including CTO files).

## Bid metadata

The Quote API response's `quoteNumber` and `quoteVersion` supply the bid fields — never the submitted quote id. Best-effort: a missing `quoteVersion` keeps the number with revision `1`, and a missing `quoteNumber` leaves both null rather than failing the parse.

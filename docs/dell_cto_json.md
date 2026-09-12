# Dell CTO (API) Extraction Spec

## Overview

- **Slug:** `dell_cto_json`
- **Vendor:** `Dell`
- **CRM Template:** `No Calculation` only (via `CrmWriter`)
- **Accepted MIME:** `application/json` (`.json`)
- **Input Format:** JSON response fetched directly from the Dell Quote API by quote ID; manual JSON upload is not exposed.

## Extraction Mapping

Dell CTO quotes consist of top-level quote metadata, top-level item parents (`items[]`), and nested item skus (`items[].skus[]`).

### Header & Metadata
- **Quote Number:** `quoteNumber` (converted to string). Fallback: filename stem.
- **Supplier:** `"Dell"`
- **Currency:** `currency` (defaults to `"AUD"` if null/empty).
- **Quoted Total:** `salesPrice`

### Line Items
Every item in `items[]` produces one parent line item. Eligible child items from `skus[]` follow
their parent immediately; only the two SKU rules below suppress children.

| Output Field | Parent Source | Child Source | Notes |
|---|---|---|---|
| `line_sequence` | `1`, `2`, ... | `1.01`, `1.02`, ... | Parent integer sequence, child 2-digit padded index |
| Vendor | `"DELL"` | `"DELL"` | Written to Column B by `CrmWriter` |
| `vpn` | `baseSkuNumber` | `skuNumber` | Part number |
| `description` | `customProductName` when non-blank; otherwise `productDescription` | `description` | Product description |
| `qty` | `quantity` | `quantity` | Quantity |
| `msrp` | `unitListPriceIncludingShipping` | `0m` | Parent writes MSRP; child writes `0m` (emits `0.0001` sentinel) |
| `cost` | `unitSalesPriceIncludingShipping` | `0m` | Parent writes Cost; child writes `0m` (emits `0.0001` sentinel) |
| `comments` | Est. delivery line (see below) | `null` | Parent-only; written to Col R by `CrmWriter` |
| Margin (col K) | User Uplift % | User Uplift % | Written by `CrmWriter` on the `Uplift` profile only |

### SKU filtering

No parent item is filtered. In particular, an item's `id` appearing as another item's `parentId`
does not make it a container and does not remove it from output.

Child SKUs are filtered independently under these two rules:

1. When the parent `items[].lineOfBusiness` is exactly `"Displays"`, all of that parent's child
   SKUs are dropped.
2. Otherwise, an individual child SKU is dropped only when it restates its parent — all three exact
   comparisons match, prices compared like for like:
   - `items[].baseSkuNumber == skus[].skuNumber`
   - `items[].unitListPriceIncludingShipping == skus[].unitListPrice`
   - `items[].unitSalesPriceIncludingShipping == skus[].unitSalesPrice`

   A missing value on either side is never a match: an absent field must not default to `0` and
   collapse into an equality. Dropping a child cannot move the quote total, because CTO children are
   always written at `cost = 0`; the parent carries the price.

There are no other item or SKU filters. In particular, zero-dollar children and entire SKU blocks
are not suppressed merely because they appear redundant. Filtering is unconditional through the
API. `ParseOptions.IncludeSubComponentDetail` still bypasses the two SKU rules and restores the full
source item/SKU tree, but `/api/parse` pins it to `false`, so that path remains test-only.

### Estimated delivery comment (parent lines only)

Each parent line's `comments` field carries an estimated-delivery note:

- **Source date:** the latest `items[].shipments[].estimatedDeliveryDateRange.max` across *all* of the item's shipments. `MinValue` (`0001-01-01`, i.e. an unset date) is ignored; if the item has no shipments or no real delivery date, no comment is written.
- **Format:** `Est. delivery on {dd-MMM-yyyy} if purchased today` (invariant-culture month abbreviation), e.g. `Est. delivery on 14-Jan-2027 if purchased today`.
- Child (sku) lines never carry this comment.
- **APOS does not implement this** — it is CTO-only.

### Rebate eligibility (parent lines only)

- `isRebateEligible` is optional and appears only for quotes with rebate-ineligible parent/base SKUs.
- When it is `false`, the parent `comments` value includes `Not Dell Rebate Eligible`. If an estimated-delivery comment is also present, the two comments are separated by ` | `.
- The parse-success popup warns: `This quote contains items that are not eligible for Rebate/MDF from Dell due to special pricing. Review the comments on the impacted line items in your quote.`
- **APOS does not implement this** — it is CTO-only. The flag is neither expected nor looked for in APOS payloads.

## Validation

- `computed_total = Σ(parent cost × qty)`
- Compare `computed_total` against top-level `salesPrice` with tolerance `0.01`.
- Emitted child line items carry cost `0m`, so they do not add to `computed_total`.

## Recognition & Wrong File-Type Error

- Recognised as CTO when no child SKU has a non-empty `serviceTags.serviceTagNumber`. Null, missing,
  empty, and whitespace-only service-tag values do not classify the quote as APOS.
- If JSON is malformed or lacks `items[]` or items lack `baseSkuNumber` / `skus`, throws `ParseError("detect", "File is not a Dell CTO quote.", "Could not read Dell CTO JSON structure.")`.
- `ParseService` catches this recognition failure and reclassifies it as stage `"fileType"`.

## Bid metadata

The Quote API response's `quoteNumber` and `quoteVersion` supply the bid fields — never the submitted quote id. Best-effort: a missing `quoteVersion` keeps the number with revision `1`, and a missing `quoteNumber` leaves both null rather than failing the parse.

**Sample File:** XQ-4108785.pdf
**Template Name:** Hardware Only (PDF)

This PDF contains multiple stacked quote sections (Quote A, Quote B, Quote C, Quote D). The section we extract from is **Quote D** — "For distributor to quote to the reseller only" — which is the reseller-facing breakdown of the hardware engagement. (Quote C in the same PDF is a separate budgetary breakdown of pure components; we are not parsing it.)

**Important — locate everything by string anchors, never by fixed page numbers or word positions.** The same PDF contains multiple quote sections whose positions and column x-ranges shift across samples (more line items in earlier sections push later sections onto different pages, and column widths can differ between sections). Hard-coded positions will break across real-world quotes.

**Locating Quote D**
1. Scan all words across pages for the literal sequence `Quote D For distributor to quote to the reseller only`. This is the section banner.
2. From the banner's y position, scan downward (continuing across pages if needed) for the line-item header row — the word `Product` immediately followed (same y-band ±3pt, increasing x) by `Code`.
3. From the header row, build a column-anchor map for the seven labels we care about: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Total Discount`, `Net Unit Price`, `Quantity`, `Total Net Price`. Use each label's leftmost x to define its column; the next label's leftmost x ends the column. The rightmost column extends to the right margin.
4. Collect body rows below the header until the literal `TOTAL:` marker — this both terminates Quote D and carries its quoted total.

**Extracting Line Items**
Every line item in Quote D is kept — there are **no filler rows to skip** in this format. In particular, the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) is a real line item and must be retained. (This is the opposite rule to Software Only PDF, where the analogous `Term-Months` row is skipped.)

Rows can wrap across multiple visual lines: both the Part Number and Description columns wrap independently. Use the same anchor + continuation pattern as Software Only — a row with a non-empty `Product Code` cell starts a new line item, and subsequent rows with an empty `Product Code` are continuation rows whose populated cells append to the current line item.

**Part Number**
From the `Product Code` column. The Part Number itself can wrap across two visual lines (e.g. `NX-1175S-G10-` on one line and `6517P-CM` on the next, or `C-NVM-7.68TB-` and `AB1A-CM`). Concatenate the anchor + continuation snippets with **no separator** (the wrap usually happens mid-token after a hyphen). Trim whitespace at the end. Some Product Codes are non-SKU labels (`Support-Term`, `Platform Integration`); keep them verbatim.

**Description**
From the `Product` column. Wraps across 1–4 visual lines per item. Join anchor + continuation snippets with single spaces and collapse internal whitespace.

**Term (Months)**
From the `Term (Months)` column. This column may be empty on some rows (typical for hardware components) — emit the term as null/empty rather than 0 when the cell is empty. When populated it is an integer (e.g. `60`).

**List Price**
From the `List Unit Price` column. Values are strings prefixed with `USD` and may include thousands separators (e.g. `USD 25,021.99` or `USD 77.89`). Strip the `USD`, commas, and whitespace and parse as a number. **When the cell is empty (bundled-component rows that carry no own price), emit `0` — not null.**

**Cost Price**
From the `Net Unit Price` column. Same string format as `List Unit Price` (e.g. `USD 20,017.57`, `USD 2,411.99`). Strip the `USD`, commas, and whitespace. **When the cell is empty, emit `0` — not null** (same reason as List Price).

**Quantity**
From the `Quantity` column. Integer in the source. Note: on the `Support-Term` row the `Quantity` cell carries the term value (e.g. `60`) rather than a count of items — keep what the cell says.

**Validation**
The quoted total appears below Quote D's data as the literal sequence `TOTAL:` followed by `USD <amount>` (the `TOTAL:` token and the amount may sit on the same row or wrap across two — handle either). Strip the `TOTAL:`, the `USD`, and commas. In the sample, `TOTAL: USD 22,491.87` becomes `22491.87`.

Compute the line total by multiplying `Cost Price` by `Quantity` for every kept row and summing. Bundled-component rows contribute `$0 × qty = $0`, the `Support-Term` row contributes `$0 × 60 = $0`, and the `Platform Integration` row contributes `$0 × 1 = $0`. The remaining priced rows must sum to the quoted total. For `XQ-4108785.pdf` Quote D the sum is `22,491.87`, matching the quoted total exactly.

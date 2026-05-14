**Sample File:** XQ-4108785.xlsx
**Template Name:** Hardware Only (XLSX)

This is the XLSX counterpart of the Hardware Only (PDF) format — same underlying engagement, but the workbook also breaks out the reseller-facing pricing. The workbook contains multiple stacked quote sections (Quote A, Quote B, Quote C, Quote D); the section we extract from is **Quote D** ("For distributor to quote to the reseller only"), not Quote C.

**Important — locate everything by string anchors, never by fixed row/column references.** The same workbook contains multiple quote sections whose row positions and column letters shift across samples (e.g. Quote C's header table has `Product Code` in column H, but Quote D's has it in column E). Hard-coded positions will break across real-world quotes.

**Locating Quote D**
1. Scan the sheet for a cell whose value is the literal `Quote D For distributor to quote to the reseller only`. This is the section banner.
2. From the banner row, scan downward for the next cell whose value is `Product Code`. That cell's row is the line-item header row.
3. From the header row, build a label-to-column map for the six fields we care about: `Product Code`, `Product Description`, `Term (Months)`, `List Price`, `Sale Price`, `Quantity`. Use the header text to find each column letter — do not assume column letters from another quote.
4. Iterate data rows below the header row.
5. Stop when you reach a cell whose value starts with the literal string `TOTAL ` (this marks the end of Quote D's data and gives you the quoted total in the same cell).

**Extracting Line Items**
Every non-empty row between the header row and the `TOTAL ` row is a line item. Unlike Software Only, **all rows are kept** — there is no filler row to skip in this format. In particular, the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) is a real line item in this format and must be retained.

**Part Number**
From the `Product Code` column. Trim whitespace. Some Product Codes are plain text labels (e.g. `Support-Term`, `Platform Integration`) rather than SKU-shaped strings — keep them as-is.

**Product Description**
From the `Product Description` column. Trim whitespace. Single cell per row, no wrapping.

**Term (Months)**
From the `Term (Months)` column. This column may be empty on some rows (typical for hardware components) — when empty, emit the term as null/empty rather than 0. When populated it is already an integer in the source (e.g. `60`).

**List Price**
From the `List Price` column. Values are strings prefixed with `$` and may include thousands separators (e.g. `$25,021.99` or `$77.89`). Strip the `$`, commas, and whitespace and parse as a number. **When the cell is empty (bundled-component rows that carry no own price), emit `0` — not null.** Bundled components are rolled into a parent product's price; treating them as `$0.00` keeps the validation arithmetic clean.

**Sale Price**
From the `Sale Price` column. Same string format as `List Price` (e.g. `$20,017.57`, `$2,411.99`). Strip the `$`, commas, and whitespace and parse as a number. **When the cell is empty, emit `0` — not null** (same reason as List Price).

**Quantity**
From the `Quantity` column. Numeric in the source. Note: on the `Support-Term` row the `Quantity` cell carries the term value (e.g. `60`) rather than a count of items — keep what the cell says.

**Validation**
The quoted total is the cell whose value starts with `TOTAL ` immediately below Quote D's data rows. Strip the `TOTAL ` prefix, the `$`, and any commas — in the sample, `TOTAL $22,491.87` becomes `22491.87`.

Compute the line total by multiplying `Sale Price` by `Quantity` for every kept row and summing. Bundled-component rows contribute `$0 × qty = $0`, the `Support-Term` row contributes `$0 × 60 = $0`, and the `Platform Integration` row contributes `$0 × 1 = $0`. The remaining priced rows must sum to the quoted total. For `XQ-4108785.xlsx` Quote D the sum is `22,491.87`, matching the quoted total exactly.

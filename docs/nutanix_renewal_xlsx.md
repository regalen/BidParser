**Sample File:** XQ-4176792.xlsx
**Template Name:** Renewal (XLSX)

This is the XLSX counterpart of the Renewal (PDF) format — same supplier (Nutanix), same renewal line items (subscription renewals carrying serial/license numbers and start/end dates), different envelope. Because every value sits in its own spreadsheet cell, none of the PDF wrapping / `USD`-token-fusion handling is required.

**Important — locate everything by string anchors, never by fixed row/column references.** The line-item table is preceded by a quote-metadata block whose height varies (distributor/reseller/end-user addresses, contact info), and column letters can differ between supplier samples. Hard-coded positions will break across real-world quotes.

**Locating the line-item table**
1. Scan the sheet for a cell whose value is the literal string `Quote Number` — the top-left cell of the line-item header row.
2. That cell's row is the header row. Build a label→column map by walking every non-empty cell in that row.
3. Iterate data rows immediately below the header row. Required labels: `Product Code`, `Serial Number`, `Start Date`, `End Date`, `Term Adjusted List Unit Price`, `Net Unit Price`, `Quantity`. Optional: `Product Description`, `Platform`.
4. Stop iterating at the first wholly-empty row, or a cell whose value starts with `TOTAL ` (whichever comes first). A blank row can sit between the last line item and the `TOTAL $…` row, so the parser also falls back to scanning the whole sheet for the first `TOTAL ` cell (the same fallback used by Software Only (XLSX)).

**Part Number**
From `Product Code`. Trim whitespace. Renewal SKUs are prefixed `RS-`/`RSW-` (e.g. `RS-HW-PRD-ST`, `RSW-NCI-PRO-PR`).

**Description**
Combine `Product Description` with `Platform`. When the row carries a `Platform` value (hardware rows, e.g. `NX-3060-G7-AF`), append it as `{Product Description} (Platform: {platform})`. Software-subscription rows leave `Platform` blank → the bare `Product Description`. This differs from Renewal (PDF), whose source has no description column and therefore emits only `Platform: {value}`.

**Serial Number**
From `Serial Number`. The embedded license is comma-joined in one cell, e.g. `26SW000487027, LIC-02574676`. Strip internal whitespace so the value becomes `26SW000487027,LIC-02574676`, matching the single comma-joined Renewal (PDF) convention. We do **not** split it into separate serial/license fields.

**Start Date / End Date**
From `Start Date` / `End Date`. Use the native date when the cell holds one; otherwise parse the displayed string as `MM/dd/yyyy` (supplier default), falling back to `M/d/yyyy`, `yyyy-MM-dd`, `dd/MM/yyyy`. Stored as `DateOnly`; the output template displays `DD/MM/YYYY`.

**MSRP**
From `Term Adjusted List Unit Price`. Strip `$`, commas and whitespace and parse — `$1,107.36` → `1107.36`.

**Cost Price**
From `Net Unit Price`, same format — `$803.30` → `803.30`. This is the cost price for the line.

There is no `Term (Months)` column in this format; `Term` is left null.

**Quantity**
From `Quantity`. Stored as a number.

**Validation**
The quoted total is the cell whose value starts with `TOTAL ` below the line items — strip the leading `TOTAL `, the `$` and commas: `TOTAL $68,160.08` → `68160.08`. Compute the line total as Σ(`Net Unit Price` × `Quantity`); it must match to two decimal places. For `XQ-4176792.xlsx` that sum is `803.30 × 4 + 225.51 × 288 = 68,160.08`, matching the quoted total exactly.

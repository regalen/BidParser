**Sample File:** XQ-4076249.xlsx
**Template Name:** Software Only (XLSX)

This is the XLSX counterpart of the Software Only (PDF) format — same supplier (Nutanix), same line items, different envelope.

**Important — locate everything by string anchors, never by fixed row/column references.** The line-item table is preceded by a quote-metadata block whose height varies (distributor/reseller/end-user addresses, contact info), so the header row's position is not fixed. Column letters can also differ between supplier samples. Hard-coded positions will break across real-world quotes.

**Locating the line-item table**
1. Scan the sheet for a cell whose value is the literal string `Quote Number`. This is the top-left cell of the line-item header row (the line-item grid starts with `Quote Number` as its first column).
2. That cell's row is the header row. Build a label-to-column map by walking every cell in that row and recording each non-empty string label against its column letter.
3. Iterate data rows immediately below the header row. The required field labels in the map are: `Product Code`, `Product Description`, `Term (Months)`, `List Price`, `Sale Price`, `Quantity`.
4. Stop iterating when you reach either (a) the first wholly-empty row, or (b) a cell whose value starts with the literal string `TOTAL ` (whichever comes first). The `TOTAL ` cell carries the quoted total in the same string.

**Optional columns** (extended layout, mirroring the extended Software Only PDF):

Some real quotes include extra columns alongside the required set — `Selected Start Date`, `Total Discount`, `Total Net Price`. The header-map approach handles them transparently:

- `Selected Start Date` — when present, parse the cell as a `DateOnly`. If the cell holds a native date, use it directly; otherwise parse the displayed string with `MM/dd/yyyy` (the supplier's default), falling back to `M/d/yyyy`, `yyyy-MM-dd`, or `dd/MM/yyyy`. The value lands in `LineItem.StartDate` and writes to the output template's `Start Date` column (column P) with the standard `DD/MM/YYYY` display format.
- `Total Discount` and `Total Net Price` — labels are unmapped in the parser; the cells are read into `Raw` for debugging but not extracted to any output field.

**Extracting Line Items**
Every row between the header row and the stop condition is a candidate line item. Keep rows where the `Product Code` cell trims to `Term-Months` as real line items with `Vpn="Term-Months"`, `Description="Term in months"`, `Term` and `Qty` set to the term value, and `Cost=Msrp=0`.

**Part Number**
From the `Product Code` column. Trim whitespace. The values extracted from `XQ-4076249.xlsx` (including `Term-Months` filler rows) are:

SW-NCM-STR-PR
Term-Months
SW-NCI-PRO-PR
Term-Months
SW-NCI-PRO-PR
Term-Months
SW-NCI-E-PRO-PR
Term-Months
SW-NCM-E-STR-PR
Term-Months

**Product Description**
From the `Product Description` column. Single cell per row — no wrapping. Trim whitespace and emit.

**Term (Months)**
From the `Term (Months)` column. Already a number in the source (e.g. `60`). Empty cells (allowed under the extended layout, e.g. non-subscription services) emit `null`. For `Term-Months` rows, this value is extracted and used for both `Term` and `Qty`. In the sample, every item has term `60`.

**List Price**
From the `List Price` column. Stored as a string with a dollar sign and thousands separators, e.g. `$383.00` or `$2,275.00`. Strip the `$`, commas, and whitespace and parse as a number — `$2,275.00` becomes `2275.00`, `$383.00` becomes `383.00`. This is the supplier's MSRP / list value.

**Sale Price**
From the `Sale Price` column. Same string format as `List Price`, e.g. `$101.11` or `$600.60`. Strip the `$`, commas, and whitespace — `$101.11` becomes `101.11`. This is the cost price for the line.

**Quantity**
From the `Quantity` column. Stored as a number (e.g. `2096`, `864`). Skip on `Term-Months` filler rows. In the sample the kept quantities are:

2096
864
1232
145
145

**Validation**
The quoted total is the cell whose value starts with the literal string `TOTAL ` immediately below the line-item rows. (There is typically a second occurrence further down on a labelled `TOTAL` row — that's a fallback, not the primary anchor.) Strip the leading `TOTAL`, the `$`, and any commas — `TOTAL $1,625,358.51` becomes `1625358.51`.

Compute the line total by multiplying `Sale Price` by `Quantity` for each kept row and summing. The result should match the parsed quote total to two decimal places. For `XQ-4076249.xlsx` that sum is `1,625,358.51`, matching the quote total exactly.

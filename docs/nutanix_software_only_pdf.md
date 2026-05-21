**Sample files:** `XQ-4076249.pdf` (compact 6-column layout), `XQ-4165884.pdf` (extended 9-column layout)
**Template Name:** Software Only (PDF)

This format ships in two layout variants that the same parser handles.

**Compact layout** (`XQ-4076249.pdf`): six logical columns laid out side-by-side:
`Product Code` | `Product` | `Term (Months)` | `List Unit Price` | `Net Unit Price` | `Quantity`.

**Extended layout** (`XQ-4165884.pdf`): nine logical columns — the compact set plus three insertions:
`Product Code` | `Product` | `Term (Months)` | `Selected Start Date` | `List Unit Price` | `Total Discount` | `Net Unit Price` | `Quantity` | `Total Net Price`.

In the extended layout the supplier template squeezes column widths, which causes two visual differences the parser must tolerate:

1. **Stacked column header.** "Product Code" is rendered as `Product` stacked above `Code` (two visual lines, same x0) instead of side-by-side. The header anchor accepts either layout.
2. **Wrapped Product Code body.** Long SKUs (`CNS-INF-A-WRK-DSGN-BAS-MS-SD-VIRT`, `PS-RES-IRE-CONS-QRTR-12MO`, …) wrap across 2-4 visual rows, with each fragment ending in a hyphen except the last (`CNS-INF-A-`, `WRK-DSGN-`, `BAS-MS-`, `SD-VIRT`). The parser joins these fragments with no separator.
3. **Split USD label / numeric value.** The anchor row carries the `USD` label and the `Quantity`; the numeric value (e.g. `3,275.00`, `157,199.04`) lands on the next visual row. The parser merges per-column cells from continuation rows so the final value is e.g. `"USD 3,275.00"` before currency cleaning.

`Total Discount` and `Total Net Price` columns, when present, are detected only to keep column ranges accurate — their values are not written to any output field.

**Locating the line-item table**

Find the header anchor by searching for the literal word `Product` followed by `Code` in the same column. `Code` is accepted either on the same baseline (compact layout) or directly beneath `Product` with overlapping x-extent (extended layout).

Header column ranges are derived by clustering the header band's words by overlapping x-extent — each multi-line label (`Selected` / `Start` / `Date`; `Term` / `(Months)`; `Total` / `Discount`; …) collapses into one cluster whose leftmost x0 anchors the column.

**Extracting Line Items**

For each body row, classify as anchor or continuation:

- **Anchor row** — Product Code matches `^[A-Z0-9-]+$` AND the row carries an anchor signal (Quantity or Term cell is non-empty). Start a new line item, seeded with this row's Product Code and Product cell.
- **Continuation row** — anything else. Append non-empty Product Code fragments to the current item's code parts (concatenated with no separator) and Product fragments to its description parts (joined with single spaces). Numeric cells (List, Net, Total, Quantity, Term, Selected Start Date) are appended into the current item's cells with a space separator — required to handle the split USD-label / numeric-value pattern.

**Filler / sub-header skip:** any row whose Product cell is exactly `Term in months`, OR whose Product Code is non-empty but doesn't match the SKU shape while still carrying an anchor signal, is treated as a filler sub-header and ignored entirely. This catches both the single-row `Term-Months` form and the wrapped `Term-` / `Months` form. The second wrap row (`Months` only, no anchor signal) falls through harmlessly because `Months` fails the SKU shape check.

**Page-footer skip:** rows whose joined text matches `^Page\s+\d+\s+of\s+\d+$` are dropped before classification.

**Per-field handling**

- **Part Number (`vpn`)** — concatenate all collected Product Code fragments with no separator.
- **Description (`description`)** — join collected Product fragments with single spaces, collapsing internal whitespace.
- **Term (`term`)** — `Term (Months)` cell parsed as nullable int (empty cell → null; e.g. `EDU-ONSITE-FEE` has no term).
- **List Price (`msrp`)** — `List Unit Price` cell with `USD`, `,`, whitespace stripped; defaults to 0 when empty (bundled-component rows).
- **Sale Price (`cost`)** — `Net Unit Price` cell with same cleaning; defaults to 0 when empty.
- **Quantity (`qty`)** — `Quantity` cell parsed as int.
- **Start Date (`start_date`)** — `Selected Start Date` cell parsed via `MM/dd/yyyy` to `DateOnly`; null when the column is absent or empty. Lands in the output's `Start Date` column (column P) with the standard `DD/MM/YYYY` display format.

**Validation**

The quoted total comes from the `TOTAL:` marker after the last line item — the parser scans for `TOTAL:` followed by `USD` and the amount, tolerating page-break wrap. Computed total = Σ(`cost` × `qty`) across line items. The two must match within `0.01`.

Expected output (golden values, already validated by hand):

`XQ-4076249.pdf` (compact, 5 items, computed total = `USD 1,625,358.51`):

| Part Number      | Term | List Price | Sale Price | Quantity | Start Date |
|------------------|------|------------|------------|----------|------------|
| SW-NCM-STR-PR    | 60   | 383        | 101.11     | 2096     | —          |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 864      | —          |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 1232     | —          |
| SW-NCI-E-PRO-PR  | 60   | 3455       | 912.12     | 145      | —          |
| SW-NCM-E-STR-PR  | 60   | 583        | 153.91     | 145      | —          |

`XQ-4165884.pdf` (extended, 11 items, computed total = `USD 320,562.54`):

| Part Number                       | Term | List Price | Sale Price | Quantity | Start Date  |
|-----------------------------------|------|------------|------------|----------|-------------|
| SW-NDB-PR                         | 36   | 3275.00    | 545.83     | 288      | 2026-07-31  |
| FLEX-CST-CR                       | 12   | 100.00     | 85.00      | 60       | —           |
| CNS-INF-A-WRK-DSGN-BAS-MS-SD-VIRT | —    | 38105.00   | 34294.50   | 1        | —           |
| CNS-INF-A-SVC-DEP-ONP-AHV         | —    | 3440.00    | 3096.00    | 3        | —           |
| CNS-INF-A-SVC-DEP-ONP-AHV         | —    | 3440.00    | 3096.00    | 3        | —           |
| CNS-INF-A-SVC-DRD-LEAP            | —    | 9980.00    | 8982.00    | 1        | —           |
| CNS-INF-A-SVC-MIG-VMS-VIRT        | —    | 3745.00    | 3370.50    | 2        | —           |
| EDU-C-ADM5-PVT-PK                 | —    | 28875.00   | 26355.00   | 1        | —           |
| EDU-ONSITE-FEE                    | —    | 0.00       | 0.00       | 1        | —           |
| EDU-C-NDMA-INV                    | —    | 2310.00    | 2079.00    | 1        | —           |
| PS-RES-IRE-CONS-QRTR-12MO         | —    | 68040.00   | 61236.00   | 1        | —           |

---
name: nutanix_renewal_pdf
description: "Extraction algorithm for Nutanix subscription Renewal quote PDFs containing serial/license numbers."
---
# Nutanix Renewal (PDF) Parser Skill

Use this skill when implementing or modifying the extraction engine for the "Renewal (PDF)" Nutanix format.

## 1. Anchors and Columns

- **Header Anchor**: Locate the word `"No"` in the top-left corner of the line items table.
  - *Note*: Headers themselves span multiple stacked visual lines (e.g., `"Term"`, `"Adjusted"`, `"List Unit"`, `"Price"` are stacked vertically). Column bounds logic must handle multi-line headers.
- **Columns**:
  - `No`
  - `Product Code` (maps to `vpn`)
  - `Serial Number` (maps to `serial_number`)
  - `Start Date` (maps to `start_date`)
  - `End Date` (maps to `end_date`)
  - `Term Adjusted List Unit Price` (maps to `msrp`)
  - `Total Discount`
  - `Net Unit Price` (maps to `cost`)
  - `Qty` (maps to `qty`)
  - `Total Net Price`

---

## 2. Row Classification Logic

Scan rows below the header row:

1. **Anchor Row**: If the `No` cell parses to a positive integer AND `Product Code` is non-empty, classify it as a new **Anchor Row** (creates a new `LineItem`).
2. **Continuation Row**: If the `No` cell is empty but other columns are populated, classify it as a **Continuation Row** (append the text to the corresponding columns of the current anchor row. E.g., serial number wrapped fragments).
3. **Ignored Row**: Any other cell patterns are ignored.

---

## 3. Data Extraction and Cleaning

- **Part Number** (`vpn`): Trim the text from `Product Code`.
- **Serial Number** (`serial_number`): The serial number wraps across lines (e.g., `24SW000351227,\nLIC-02472987`). Join these segments with **no separator** (preserving the comma at the end of the first line). Trim all whitespace and collapse internal spaces. Do not split into separate serial/license fields.
- **Start Date / End Date** (`start_date`, `end_date`): Read values in `MM/DD/YYYY` format and parse via `DateOnly.ParseExact(val, "MM/dd/yyyy")`.
- **List Price** (`msrp`): Clean currency (`USD`, `$`), commas, and whitespace from `Term Adjusted List Unit Price` and parse as `decimal`.
- **Sale Price** (`cost`): Clean currency, commas, and whitespace from `Net Unit Price` and parse as `decimal`.
- **Quantity** (`qty`): Clean and parse `Qty` value as `int`.

---

## 4. Edge Cases to Handle

- **Serial Number Wrap**: Ensure the wrapped serial lines are joined with no separator to correctly preserve the comma syntax.
- **Visual Overlap / Run-together Text**: Some columns run together visually due to narrow margins (e.g., `USD 54.41 160` for cost and quantity). Always rely on the derived header x-ranges to isolate text, not whitespace separation.
- **`TOTAL:` Wrapping**: The `TOTAL: USD [amount]` block can wrap onto two lines (e.g., `TOTAL:` and `USD` on one line, and the numeric amount on the next).

---

## 5. Golden Sample Validation

Verify extraction against the golden sample `XQ-4128926.pdf`. Expected output:

| Part Number | Serial Number | Start Date | End Date | List Price | Sale Price | Quantity |
|---|---|---|---|---|---|---|
| RSW-NCM-STR-PR | 24SW000351227,LIC-02472987 | 2026-07-13 | 2027-07-12 | 77 | 54.41 | 160 |
| RSW-NCI-ULT-PR | 24SW000351236,LIC-02472996 | 2026-07-13 | 2027-07-12 | 575 | 371.83 | 32 |
| RSW-NCI-ULT-PR | 24SW000351221,LIC-02472983 | 2026-07-13 | 2027-07-12 | 575 | 429.11 | 72 |
| RSW-NCM-STR-PR | 24SW000351228,LIC-02472985 | 2026-07-13 | 2027-07-12 | 77 | 54.41 | 160 |

- **Validation Total**: Computed total = Quoted total = `USD 60,205.68`.

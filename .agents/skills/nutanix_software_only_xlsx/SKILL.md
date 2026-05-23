---
name: nutanix_software_only_xlsx
description: "Extraction algorithm for Nutanix Software Only subscription quote XLSX workbooks."
---
# Nutanix Software Only (XLSX) Parser Skill

Use this skill when implementing or modifying the extraction engine for the "Software Only (XLSX)" Nutanix format.

## 1. Sheet Selection & Sheet Scanning

- **Active Sheet**: Open the active sheet using ClosedXML; do not hardcode specific worksheet names.
- **Section / Header Identification**:
  1. Scan cells to locate the literal string `"Quote Number"`. This cell marks the leftmost column of the header row.
  2. Map the header columns dynamically by reading the header row cell values. Do not assume hardcoded column offsets or letters.
  - **Required labels**: `Product Code`, `Product Description`, `Term (Months)`, `List Price`, `Sale Price`, `Quantity`.
  - Other headers like `Quote Number`, `Quote Name`, `Total Discount (%)` should be ignored.
  3. Stop data row iteration when encountering a completely empty row, or a cell starting with `"TOTAL "`.

---

## 2. Row Classification Logic

Process rows starting directly below the identified header row:

1. **Filler Row Skip**: If the `Product Code` value trims to `"Term-Months"`, **skip** the row. This is a filler row appearing after every real item and must be discarded.
2. **Line Item Anchor**: If the `Product Code` cell is non-empty and does not equal `"Term-Months"`, it is a valid **Anchor Row**. In XLSX format, there is exactly one row per item (no continuation lines).

---

## 3. Data Extraction and Cleaning

- **Part Number** (`vpn`): Trim the text from the `Product Code` column.
- **Description** (`description`): Trim the text from the `Product Description` column.
- **Term** (`term`): Parse the numeric `Term (Months)` value as `int`.
- **List Price** (`msrp`): Clean the string value (e.g., `$383.00`) by stripping `$`, commas, and whitespace, then parse as `decimal`.
- **Sale Price** (`cost`): Clean the `Sale Price` string using the same method and parse as `decimal`.
- **Quantity** (`qty`): Parse the numeric `Quantity` value as `int`.

---

## 4. Total and Edge Case Handlings

- **Total Extraction**: Scan below the last data row for a cell containing the literal string `"TOTAL "` (e.g., `"TOTAL $1,625,358.51"`). Strip `"TOTAL"`, `$`, and commas, then parse as `decimal`. Fall back to finding a `"TOTAL"` label cell and reading the adjacent cell if the single-cell format is missing.
- **Varying Layouts**: Column layout and offsets may shift (e.g., `Total Discount (%)` sits between List and Sale Price in some samples). Column index resolution must be fully dynamic based on header labels.

---

## 5. Golden Sample Validation

Verify extraction against the golden sample `XQ-4076249.xlsx`. Expected output:

| Part Number | Term | List Price | Sale Price | Quantity |
|---|---|---|---|---|
| SW-NCM-STR-PR | 60 | 383.00 | 101.11 | 2096 |
| SW-NCI-PRO-PR | 60 | 2275.00 | 600.60 | 864 |
| SW-NCI-PRO-PR | 60 | 2275.00 | 600.60 | 1232 |
| SW-NCI-E-PRO-PR | 60 | 3455.00 | 912.12 | 145 |
| SW-NCM-E-STR-PR | 60 | 583.00 | 153.91 | 145 |

- **Validation Total**: Computed total = Quoted total = `$1,625,358.51`.

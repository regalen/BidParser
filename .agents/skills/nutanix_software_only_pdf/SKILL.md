---
name: nutanix_software_only_pdf
description: "Extraction algorithm for Nutanix Software Only subscription quote PDFs."
---
# Nutanix Software Only (PDF) Parser Skill

Use this skill when implementing or modifying the extraction engine for the "Software Only (PDF)" Nutanix format.

## 1. Anchors and Columns

- **Header Anchor**: Locate the word `"Product"` immediately followed by `"Code"` on the same visual y-band ($\pm 3\text{pt}$) with increasing x coordinates.
- **Columns**: 
  - `Product Code` (maps to `vpn`)
  - `Product` (maps to `description`)
  - `Term (Months)` (maps to `term`)
  - `List Unit Price` (maps to `msrp`)
  - `Net Unit Price` (maps to `cost`)
  - `Quantity` (maps to `qty`)

---

## 2. Row Classification Logic

Scan words line by line from the header y position downward. Categorize each line:

1. **Sub-header skip**: If the `Product Code` text trims to `"Term-Months"` or the `Product` text is `"Term in months"`, **skip** the row. This is a recurring header repeated across page breaks.
2. **Anchor Row**: If the trimmed `Product Code` matches the regular expression `^[A-Z0-9-]+$`, class it as an **Anchor Row** (creates a new `LineItem`).
3. **Continuation Row**: If `Product Code` is empty but `Product` is non-empty, class it as a **Continuation Row** (append the text to the current anchor row's description).
4. **Ignored Row**: Any other cell patterns are ignored.

---

## 3. Data Extraction and Cleaning

- **Part Number** (`vpn`): Trim the text from the `Product Code` column.
- **Description** (`description`): Flatten by joining the text snippets from the anchor row and any subsequent continuation rows with a single space. Collapse internal multiple spaces.
- **Term** (`term`): Clean and parse the `Term (Months)` value as an `int`.
- **List Price** (`msrp`): Strip currency symbols (`USD`, `$`), commas, and whitespace, then parse as `decimal`.
- **Sale Price** (`cost`): Clean and parse the `Net Unit Price` column as `decimal`.
- **Quantity** (`qty`): Clean and parse as `int`.

---

## 4. Edge Cases to Handle

- **Description Wrap**: Handle 3–5 lines of wrapped description per line item (correctly appended as continuation lines).
- **Sub-header Repetition**: Discard `"Term-Months"` sub-headers appearing at the top of subsequent pages.
- **`TOTAL:` Wrapping**: Handle cases where `TOTAL:` and the currency/amount wrap onto a later page.
- **Thousands Separators**: Handle prices containing commas (e.g., `2,275.00`).

---

## 5. Golden Sample Validation

Verify extraction against the golden sample `XQ-4076249.pdf`. Expected output:

| Part Number | Term | List Price | Sale Price | Quantity |
|---|---|---|---|---|
| SW-NCM-STR-PR | 60 | 383 | 101.11 | 2096 |
| SW-NCI-PRO-PR | 60 | 2275 | 600.60 | 864 |
| SW-NCI-PRO-PR | 60 | 2275 | 600.60 | 1232 |
| SW-NCI-E-PRO-PR | 60 | 3455 | 912.12 | 145 |
| SW-NCM-E-STR-PR | 60 | 583 | 153.91 | 145 |

- **Validation Total**: Computed total = Quoted total = `USD 1,625,358.51`.

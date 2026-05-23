---
name: nutanix_hardware_only_xlsx
description: "Extraction algorithm for Nutanix Hardware multi-quote XLSX workbooks (Quote D only)."
---
# Nutanix Hardware Only (XLSX) Parser Skill

Use this skill when implementing or modifying the extraction engine for the "Hardware Only (XLSX)" Nutanix format.

## 1. Section and Header Location

A single spreadsheet file contains multiple stacked quote sections (Quote A, B, C, D). **Only parse Quote D**.
1. **Quote D Banner**: Scan the sheet for a cell with the value `"Quote D For distributor to quote to the reseller only"`. This row marks the start of the Quote D section.
2. **Header Row**: Scan downward from the banner row to find the next cell containing `"Product Code"`. This is the header row.
3. **Dynamic Column Mapping**: Build a label-to-column map dynamically.
  - *Warning*: Do not assume column positions carry over from earlier sections (e.g., in some sheets, Quote C uses column H for `Product Code`, while Quote D uses column E).
4. **Section Boundaries**: Halt processing when encountering a cell starting with `"TOTAL "` below the data rows.

---

## 2. Row Classification Logic

- **Data Rows**: Every non-empty row between the header row and the `"TOTAL "` row represents a valid line item.
- **Kept Rows**: Do not skip filler rows. In hardware quotes, the `Support-Term` row represents a valid line item and must be kept.

---

## 3. Data Extraction and Cleaning

- **Part Number** (`vpn`): Trim the text from the `Product Code` column. Keep SKU strings and plain labels verbatim.
- **Description** (`description`): Trim the text from the `Product Description` column.
- **Term** (`term`): Per-row nullable. Parse as `int` if populated in the source; emit as `null` if empty (typical for hardware component rows).
- **List Price / Sale Price** (`msrp`, `cost`): Clean currency (`$`), commas, and whitespace, then parse as `decimal`. **If the cell is empty (bundled components carrying no individual price), emit `0.00` (do not emit null).**
- **Quantity** (`qty`): Parse as `int`. On the `Support-Term` row, the `Quantity` cell holds the term value (`60`)—keep it as-is.

---

## 4. Edge Cases to Handle

- **Multi-Quote Isolation**: Ensure Quote A, B, and C rows are ignored. Write negative assertion tests checking that Quote C components (like `NX-1175S-G10-6517P-CM` at Quote C's cost of `5,903.72`) are not captured.
- **Column Drift**: Always rebuild the label-to-column map for the Quote D section rather than reusing the Quote C columns.
- **Bundled Component Pricing**: Set empty prices to `0.00`.
- **Term Nullability**: Retain `null` for empty term values rather than forcing them to `0`.

---

## 5. Golden Sample Validation

Verify extraction against the golden sample `XQ-4108785.xlsx` (Quote D). Expected output:

| Part Number | Term | List Price | Sale Price | Quantity |
|---|---|---|---|---|
| NX-1175S-G10-6517P-CM | — | 25021.99 | 20017.57 | 1 |
| C-MEM-32GB-6400-CM | — | 0.00 | 0.00 | 4 |
| C-HDD-12TB-ETBA-CM | — | 0.00 | 0.00 | 2 |
| C-NVM-7.68TB-AB1A-CM | — | 0.00 | 0.00 | 2 |
| C-HBA-3816-1N-C-CM | — | 0.00 | 0.00 | 1 |
| C-NIC-25G4E1-CM | — | 0.00 | 0.00 | 1 |
| C-PWR-4FC13C14A-CM | — | 0.00 | 0.00 | 2 |
| S-HW-PRD | 60 | 4019.99 | 2411.99 | 1 |
| Support-Term | 60 | 0.00 | 0.00 | 60 |
| C-TPM-2.0-U-C-CM | — | 77.89 | 62.31 | 1 |
| Platform Integration | 0 | 4003.51 | 0.00 | 1 |

- **Validation Total**: Computed total = Quoted total = `$22,491.87`.

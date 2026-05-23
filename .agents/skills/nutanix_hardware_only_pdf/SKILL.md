---
name: nutanix_hardware_only_pdf
description: "Extraction algorithm for Nutanix Hardware multi-quote PDFs (Quote D only)."
---
# Nutanix Hardware Only (PDF) Parser Skill

Use this skill when implementing or modifying the extraction engine for the "Hardware Only (PDF)" Nutanix format.

## 1. Section and Header Location

A single PDF file contains multiple stacked quote sections (Quote A, B, C, D). **Only parse Quote D**.
1. **Quote D Banner**: Scan all pages to locate the exact sequence of words: `"Quote D For distributor to quote to the reseller only"`. This marks the beginning of Quote D.
2. **Header Anchor**: From the banner's y position, scan downward to find the word `"Product"` immediately followed by `"Code"` on the same y-band ($\pm 3\text{pt}$).
3. **Column Ranges**: Derive column x-ranges for:
  - `Product Code` (maps to `vpn`)
  - `Product` (maps to `description`)
  - `Term (Months)` (maps to `term`)
  - `List Unit Price` (maps to `msrp`)
  - `Total Discount`
  - `Net Unit Price` (maps to `cost`)
  - `Quantity` (maps to `qty`)
  - `Total Net Price`
4. **Section Boundaries**: Scan data rows until encountering the literal `"TOTAL:"` marker (which also carries Quote D's quoted total).

---

## 2. Row Classification Logic

- **Anchor Row**: If the `Product Code` cell is non-empty, classify it as a new **Anchor Row** (creates a new `LineItem`). SKU-shaped strings (e.g., `NX-1175S-G10-6517P-CM`) and plain labels (e.g., `Support-Term`, `Platform Integration`) both qualify.
- **Continuation Row**: If `Product Code` is empty but other columns are populated, classify it as a **Continuation Row**. Append text per column (both `Product Code` and `Product` columns wrap independently in this format).
- **Kept Rows**: Keep **every** classified row. Do not skip filler rows (e.g., the `Support-Term` row is a valid line item here).

---

## 3. Data Extraction and Cleaning

- **Part Number** (`vpn`): Concatenate anchor and continuation `Product Code` snippets with **no separator** (e.g., `NX-1175S-G10-` + `6517P-CM` $\rightarrow$ `NX-1175S-G10-6517P-CM`), and trim. Non-SKU labels are kept verbatim.
- **Description** (`description`): Join snippets with a single space and collapse internal spaces.
- **Term** (`term`): Per-row nullable. Parse as `int` if populated; emit as `null` if the cell is empty.
- **List Price / Sale Price** (`msrp`, `cost`): Clean currency (`USD`, `$`), commas, and whitespace, then parse as `decimal`. **If the cell is empty (bundled hardware component), emit `0.00` (do not emit null).**
- **Quantity** (`qty`): Parse as `int`. On the `Support-Term` row, this cell holds the term value (`60`) instead of a count—keep it as-is.

---

## 4. Edge Cases to Handle

- **Multi-Quote Isolation**: Ensure Quote A, B, and C rows are ignored. Write negative assertion tests checking that Quote C components (like `NX-1175S-G10-6517P-CM` at Quote C's cost of `5,903.72`) are not captured.
- **Part Number Wraps**: Ensure SKUs wrapped across visual lines are concatenated with no separator.
- **Bundled Parts**: Zero-out List Price and Sale Price when cells are empty.
- **`Support-Term` Row**: Keep this row; do not skip it.

---

## 5. Golden Sample Validation

Verify extraction against the golden sample `XQ-4108785.pdf` (Quote D). Expected output:

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

- **Validation Total**: Computed total = Quoted total = `USD 22,491.87`.

---
description: "Rules for generating output Excel workbooks, writing to templates, and field mapping conventions"
alwaysApply: true
---
# BidParser - Output Generation Rules

This rule defines the conventions, mappings, and configuration for generating standardized output Excel workbooks.

## 1. CRM Template Mapping & Writers

- **Template Registry**: Every parser returns a `CrmTemplate` value on its `IParser` implementation (e.g., all five Nutanix parsers declare `CrmTemplate = "Foreign Uplift"`).
- **Template Writer**: Standardized internal template writing is handled by specific output writers (e.g., `ForeignUpliftWriter` writes output row-by-row into `samples/template/ANZ-GENERIC_ForeignUplift.xlsx`).
- **New Templates**: If a new CRM template is added:
  1. Add the template name constant to `src/BidParser.Domain/Constants/CrmTemplates.cs`.
  2. Implement the corresponding writer under `src/BidParser.Output/`.
  3. Register the mapping inside `ParseService`.

---

## 2. Field Mapping & Layout Rules

All output fields and column mapping rules are locked. Never hardcode cell coordinate overrides within parsers or custom writers without reviewing `docs/output_mapping.md`.

Key output rules for the `Foreign Uplift` template:
- **List Price (MSRP)**: MSRP column H must stay empty (leave blank).
- **Serial Number / Comments**: `serial_number` values are written into the *Comments* column, not the *Serial Number* column.
- **Term Column**: The term (in months) is written to the template only when it is $\ge 1$. If the term is null or 0, leave it blank.
- **File Naming Convention**: The output file must be named strictly according to the format: `<basename>_parsed.xlsx` (e.g., `XQ-4076249_parsed.xlsx`).

---

## 3. Regression Testing & Golden Fixtures

- **Golden Fixtures**: Golden output sheets are stored in `samples/outputs/` as `<basename>_parsed.xlsx` (one file per quote number).
- **Format Equivalence**: PDF and XLSX parsers for the same quote (e.g., `XQ-4108785.pdf` and `XQ-4108785.xlsx`) must match the exact same golden spreadsheet output cell-by-cell.
- **Regression Tests**: Ensure cell-by-cell equivalence validation tests run as part of the test suite (`tests/BidParser.Parsing.Tests/`). If output rules change, the golden fixtures must be re-generated.

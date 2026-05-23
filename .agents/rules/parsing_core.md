---
description: "Core parsing architecture, contracts, naming maps, common PDF/XLSX approaches, and extensibility guidelines"
alwaysApply: true
---
# BidParser - Parsing Core Rules

This rule defines the core architecture, data contracts, naming conventions, and extraction algorithms for quote parsing.

## 1. Architecture & Design Principles

- **Pluggable Registry**: The system utilizes a pluggable `IParser` registry. Adding a parser means implementing the `IParser` interface and registering it explicitly in `src/BidParser.Parsing/Registry/ParserRegistry.cs`.
- **Soft Hint Detection**: Parser selection in the UI is a soft hint. `BaseParser.detect()` returns a confidence score (0.0 to 1.0). If confidence > 0.7, the dropdown pre-fills, but the user always confirms before parsing is triggered. Silent auto-routing of uploads is strictly prohibited.
- **Anchor-Based Extraction**: Every parser must identify headers, totals, rows, and columns dynamically by scanning for anchor strings. Never hardcode row indexes, column letters, or fixed coordinate offsets. The metadata, row counts, and column positions vary across quote samples.

---

## 2. Core Parser Data Contract

Every parser returns a `ParseResult` serialized to the frontend:

```
ParseResult
├── Metadata        QuoteMetadata (QuoteNumber, Supplier, Currency, QuotedTotal, SourceFilename, ParserSlug)
├── LineItems       IReadOnlyList<LineItem>
└── Validation      ValidationResult (ComputedTotal, QuotedTotal, Matches, Difference, Warnings)
```

### `LineItem` Schema
`LineItem` is a superset of fields across formats:
- **Required**: `vpn` (Vendor Part Number), `cost` (Sale/Customer Price), `qty` (Quantity).
- **Optional (populated based on format)**:
  - `description`: Product description (flat string, wrapped snippets must be collapsed into a single space-separated string).
  - `term`: Term in months (`int` or null).
  - `msrp`: List/catalogue price (`decimal` or null).
  - `serial_number`: Serial number including embedded license (`string` or null).
  - `start_date`: Subscription start date (`DateOnly` or null, serialized as ISO `YYYY-MM-DD`).
  - `end_date`: Subscription end date (`DateOnly` or null, serialized as ISO `YYYY-MM-DD`).
  - `raw`: A dictionary representation of the original source columns for debugging (`Dictionary<string, string>`).

### Dates & Serialization
- Date values are handled and stored internally as `DateOnly` types.
- Frontend is responsible for rendering dates in user-friendly formats (e.g., `DD/MM/YYYY`). Do not format dates inside parsers or domain models.

---

## 3. Canonical Naming System

We use a strict mapping between Title Case display headers (UI table) and snake_case properties:

| Concept | Display Header | Field Name |
|---|---|---|
| Part number | `Part Number` | `vpn` |
| Description | `Description` | `description` |
| Term in months | `Term` | `term` |
| List / catalogue price | `List Price` | `msrp` |
| Customer price | `Sale Price` | `cost` |
| Quantity | `Quantity` | `qty` |
| Serial number (incl. license) | `Serial Number` | `serial_number` |
| Subscription start | `Start Date` | `start_date` |
| Subscription end | `End Date` | `end_date` |

*Note: Old field names (`part_number`, `cost_price`, `term_months`, `quantity`) must never be used in code, tests, or documentation.*

---

## 4. Common PDF Parsing Approach

All PDF parsers use **UglyToad.PdfPig** via `PdfWordCollector` with the following workflow:
1. **Coordinate Alignment**: `PdfWord` construction flips the Y-axis so the codebase uses a top-left origin (matching pdfplumber conventions).
2. **Word Collection**: Collect `PdfWord` instances across pages while preserving page indices.
3. **Header Location**: Find the header row's y-bounds using first-cell anchor words (e.g., `"Product"` + `"Code"`).
4. **Column Ranges**: Derive x-ranges dynamically as `[header_x0, next_header_x0)`. The last column extends to page width.
5. **Row Clustering**: Group words into rows based on the `top` coordinates (using a tolerance of ~3pt). 
6. **Data Processing**: Sort words in each row by `x0`, bucket them into columns, and concatenate with single spaces. Stop scanning when the `"TOTAL:"` anchor token is reached.
7. **Total Scanning**: Locate the quoted total after the last body row, tolerating wrapping across pages.

---

## 5. Common XLSX Parsing Approach

All XLSX parsers use **ClosedXML** (`new XLWorkbook(path)`) and must follow these patterns:
1. **String Extraction**: Always use `cell.GetFormattedString()` to read cell values. Avoid `XLCellValue` which returns raw typed numbers and can break string handling.
2. **Header Anchor**: Locate the header row by searching for cell contents matching anchor strings (e.g., `Product Code`). Do not assume fixed row numbers due to variable header/metadata rows.
3. **Column Mapping**: Build a dynamic column map (label-to-column-number/letter) to handle columns that drift or reorder.
4. **Iteration Limit**: Process rows below the header, halting at the first completely empty row or a total marker row (e.g., `TOTAL $...`).
5. **Total Extraction**: Extract quoted totals by locating cells containing `TOTAL` and cleaning their values via `DecimalCleaner.Parse`.

---

## 6. Extensibility Workflow

To add a new parser format:
1. **Parser Class**: Create `src/BidParser.Parsing/<Vendor>/<Slug>/Vendor<Slug>Parser.cs` implementing `IParser`.
   - Use centralized constants from `BidParser.Domain.Constants` (do not inline vendor name, CRM templates, or parser slugs).
2. **Registry**: Register the parser instance in `src/BidParser.Parsing/Registry/ParserRegistry.cs`.
3. **Test Fixtures**: Add a sample file to `samples/inputs/`, create the expected `*_parsed.xlsx` in `samples/outputs/`, and add a regression test in `tests/BidParser.Parsing.Tests/`.
4. **Spec Document**: Write a format spec markdown file under `docs/` (e.g., `docs/vendor_format.md`).
5. **CRM Template Writer**: If a new CRM template is required, implement a new writer in `src/BidParser.Output/` and register it in `ParseService`.

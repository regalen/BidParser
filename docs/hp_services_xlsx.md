# HP Services (XLSX) — Extraction Spec

Parser slug: `hp_services_xlsx`  
Display name: `Services (XLSX)`  
Vendor: `HP`  
Accepted MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`  
Default CRM template: `No Calculation`  
Available CRM templates: `No Calculation`, `Uplift`  
Supports split: `true` (Split dimension: `SAID` / Service Agreement ID)  
Output naming style: `SolutionScoped`  

---

## Source file format

HP hardware warranty renewal / extension exports ("Fileexport…" workbooks). Two sample quotes validated:

| File | Contract ID | SAIDs | Items | Quoted Total (AUD) |
|---|---|---|---|---|
| `CH9000000001.xlsx` | `CH9000000001` | 1 (`1073 3019 2253`) | 13 | 11928.95 |
| `CH9000000002.xlsx` | `CH9000000002` | 7 (`1073 3013 9550`, `1073 3009 7589`, `1073 3013 5249`, `1073 3013 5309`, `1073 3013 3523`, `1073 3013 3693`, `1073 3013 3753`) | 17 | 6432.00 |

### Physical shape

- **Single worksheet** with dynamic generated name (e.g. `Fileexport134207912124794736`, `Fileexport134218900492859737`). Resolved via `workbook.Worksheets.First()`, never by name.
- **Header labels in row 1**, spanning 52 columns A..AZ.
- Data starts at row 2 and extends until the first wholly empty row.
- Dates are native Excel datetimes.

### Exact header labels (row 1, verbatim)

```
A  Contract ID                      B  Group Description
C  Service Agreement ID             D  Support Account Reference
E  Document ID                      F  Document Type
G  Coverage Start                   H  Coverage End
I  Total Net Price                  J  PO Number
K  Equipment Number                 L  Product Number
M  Product Description              N  Quantity
O  Support Package                  P  Support Package Description
Q  Service Product Number           R  Service Product Description
S  Service Level                    T  Warranty End Date
U  Support Life End Date            V  Serial Number
W  Product Type                     X  Product Line Code/Description
Y  Hardware System Contact          Z  Hardware System Contact Phone Number
AA Software System Contact          AB Software System Contact Phone Number
AC System Manager                   AD System Manager Phone Number
AE Equipment Location Company       AF Equipment Location Address 2
AG Equipment Location Address 3     AH Equipment Location Address 4
AI Equipment Location Address 5     AJ Billing Cycle Code
AK Line Item Total Net Price        AL Line Item Total List Price
AM Line Item Monthly List Price     AN Line Item Monthly Discount
AO Line Item Monthly Net Price      AP Line Item Support Start Date
AQ Line Item Support End Date       AR Environment ID
AS Sales Organization               AT Reseller ID
AU Reseller Company Name            AV Reseller Address
AW Reseller City                    AX Reseller District
AY Reseller Region                  AZ Reseller Postal Code
```

*Column letters are documentation only. All extraction is anchor-based via `WorkbookReader.HeaderMap` and `HeaderMap.Require`.*

---

## Extraction algorithm

1. `WorkbookReader.Open(path)`; `sheet = workbook.Worksheets.First()`.
2. `anchor = WorkbookReader.FindCell(sheet, "Contract ID")` — throws `ParseError("detect", …)` if missing.
3. `headers = WorkbookReader.HeaderMap(sheet, anchor.Address.RowNumber)` + `RequireLabels` for all 13 required columns:
   `"Contract ID"`, `"Service Agreement ID"`, `"Coverage Start"`, `"Coverage End"`,
   `"Total Net Price"`, `"Product Number"`, `"Product Description"`, `"Quantity"`,
   `"Service Product Number"`, `"Service Product Description"`, `"Warranty End Date"`,
   `"Serial Number"`, `"Line Item Total Net Price"`.
4. Iterate rows from `headers.RowNumber + 1` to `lastRow`; break on the first wholly empty row.
5. Extract each field and apply row filtering rules.

### Row-by-row rules

| Field | Source / Rule |
|---|---|
| **Line sequence** (col A) | Running counter `"1"`, `"2"`, `"3"`, … across the workbook. Flat structure — no parent/child hierarchy. |
| **vpn** (col D) | `Service Product Number` (e.g. `"HA151AC"`, `"UJ561AC"`). |
| **description** (col E) | Composed description: `{Service Product Description} - {Product Number} {Product Description} (Warranty End Date: dd/MM/yyyy)`. Fragments are conditional. |
| **qty** (col F) | `Quantity`; if blank or `0` → default to `1`. |
| **cost** (col I) | `Line Item Total Net Price`. |
| **msrp** (col H) | `null` (always blank). |
| **serial_number** (col M) | `Serial Number` (null when blank). |
| **start_date** (col P) | `Coverage Start` as `DateOnly`. |
| **end_date** (col Q) | `Coverage End` as `DateOnly`. |
| **solution_id** | `Service Agreement ID` (verbatim, including spaces). Used as split key. |

### Row skip rule (why a `0` price counts as absent)

The export includes per-SAID summary rows where `Product Type` is `HS`, `Product Number` and `Serial Number` are blank, and `Line Item Total Net Price` is `0`.

The parser skips a row only when **Serial Number AND Product Number AND Line Item Total Net Price** are all absent (where `cost == 0` counts as absent).

**Why this matters:**
CH9000000001 contains a priced service row (`UJ561AC / "HP PC & Notebook Return to HW Supp"`) with no Product Number and no Serial Number, but a real price of `$2,316.95`. Under this AND rule, the zero-cost summary rows are dropped while the priced service charge is preserved: `9 × 1008 + 3 × 180 + 2316.95 = 11,928.95`, reconciling with the SAID's `Total Net Price`.

### Description construction

Composed from up to three parts:
1. `Service Product Description`
2. If either `Product Number` or `Product Description` is present: ` - {Product Number} {Product Description}` (space-joined if both present)
3. If `Warranty End Date` is present: ` (Warranty End Date: dd/MM/yyyy)`

Worked examples:

| Case | Output Description |
|---|---|
| Normal covered hardware | `HP Hardware Maintenance Onsite Support - 8M0K5EC HP EB630G10 i5-1345U 13 16GB/512 PC (Warranty End Date: 26/07/2025)` |
| Blank warranty end date (`A71DPPT`) | `HP Hardware Maintenance Onsite Support - A71DPPT HP ZBPG11 U7-155H 16 16GB/512 PC` |
| Service-only charge (`UJ561AC`) | `HP PC & Notebook Return to HW Supp` |

---

## Quoted total and validation

Each data row carries `Total Net Price`, which represents the **per-SAID quoted total** (repeated across rows of that SAID).

The parser tracks the first occurrence of each distinct `Service Agreement ID` and sums their `Total Net Price` values to compute `QuotedTotal`.
Validation evaluates `ParseValidation.Validate(items, quotedTotal)` against `0.01` tolerance.

- `CH9000000001`: 1 SAID → quoted total `11928.95` == computed total `11928.95` (Matches = `true`).
- `CH9000000002`: 7 SAIDs (`1152.00 + 336.00 + 768.00 + 2688.00 + 384.00 + 336.00 + 768.00 = 6432.00`) == computed total `6432.00` (Matches = `true`).

---

## SAID split and output filenames

`HpServicesXlsxParser` declares `SupportsSolutionIdSplit => true`, `SolutionSplitLabel => "SAID"`, and `OutputNameStyle => OutputNameStyle.SolutionScoped`.

- **UI split checkbox label**: "Split output by SAID" / "One workbook per SAID, delivered as a .zip".
- **Whole-quote download filename**: `CH9000000002_NoCalculation.xlsx` (no revision suffix).
- **Split archive filename**: `CH9000000002_NoCalculation.zip`.
- **Individual workbooks in split archive**: `{solutionToken}_{FileToken}.xlsx` (e.g. `1073_3013_5309_NoCalculation.xlsx`), where spaces in the SAID are mapped to underscores (`1073 3013 5309` → `1073_3013_5309`).
- In each split workbook, `LineSequence` is renumbered from `"1"`.

---

## Output column mapping

Uses the ANZ-GENERIC 27-column layout (`CrmWriter`):

| Col | Header | No Calculation | Uplift |
|---|---|---|---|
| A | Item | `LineSequence` (`"1"`, `"2"`, …) | `LineSequence` (`"1"`, `"2"`, …) |
| B | Vendor Name | `"HP"` | `"HP"` |
| D | Vendor Part Number | `vpn` (`Service Product Number`) | `vpn` (`Service Product Number`) |
| E | Description | Composed description | Composed description |
| F | Qty. | `qty` (from `Quantity`, default 1) | `qty` |
| H | MSRP | **blank** | **blank** |
| I | Cost | `cost` (`Line Item Total Net Price`) | `cost` |
| K | Margin | **blank** | User-supplied `margin` % |
| M | Serial Number | `SerialNumber` (col V) | `SerialNumber` |
| P | Start Date | `StartDate` (`Coverage Start`, DD/MM/YYYY) | `StartDate` |
| Q | End Date | `EndDate` (`Coverage End`, DD/MM/YYYY) | `EndDate` |

All other columns: blank. End-loop row after the last line item carries `*` in col B and the standard warning in col D.

---

## Bid metadata

- `BidNumber`: `Contract ID` passed through `BidMetadataCleaner.CleanRevisionless`.
- `BidRevision`: `"1"` (revisionless format).
- `QuoteNumber`: `Contract ID`, falling back to source filename stem.

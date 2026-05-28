# Lenovo BRDA DCG (XLSX) — extraction spec

Lenovo Bid Request Data Approval (BRDA) — Data Center Group — workbook variant. Lenovo ships this format as a **legacy `.xls`** (OLE Compound Document), so the parser reads via `ExcelDataReader.CreateBinaryReader`, not ClosedXML. `System.Text.Encoding.CodePages` is registered in the parser's static constructor — ExcelDataReader needs it for `.xls` string decoding.

The two CRM templates `No Calculation` and `Uplift` are supported (same as HP Bid XLSX). Output is produced via `AnzGenericWriter`; vendor name in column B is written as `LENOVO` by `ParseService` (the dispatch uppercases the vendor).

## Sheet and header location (anchor-based)

1. Open the workbook (`.xls`) via `ExcelReaderFactory.CreateBinaryReader`, read the first sheet (Lenovo's template ships a single sheet named `Lenovo Bid Platform Bid Request`).
2. Scan rows top-to-bottom for the header row — the row that contains all three labels `PN`, `Description`, and `Requested Quantity` (case-insensitive, after trimming).
3. Build a column map from the header row:
   - `PN` → Part Number
   - `Description` → Description
   - `Requested Quantity` → Qty
   - First `Adjusted Buy Price (AUD)` cell → unit price (per unit)
   - Second `Adjusted Buy Price (AUD)` cell → extended price (qty × unit)

The metadata block above the header contains the **Bid Request Number** (extracted via regex `Bid Request Number:\s*([A-Za-z0-9_-]+)`) and rows 1–5 with prepared-by / customer / expiry information that the parser does not consume.

## Body rows

Iterate rows from `headerRow + 1`. Each row is classified by these rules, in order:

| Condition | Action |
|---|---|
| `unit price` cell equals `Total:` | **Terminator** — read the extended-price column as the quoted total (rounded to 2 dp) and stop. |
| All five mapped columns blank | Skip. |
| `PN` starts with `Set from Configurator` | Skip — section marker (e.g. `Set from Configurator(Config 1) SIDX02SDL3`). |
| `Description` equals `Subtotal` | Skip — per-config subtotal row (the PN cell repeats the parent CTO code). |
| `PN` equals `Feature Code` and `Description` equals `Description` | Skip — repeated child sub-header above each child block. |
| `unit price` populated **and `> 0`** | **PARENT row**: new line item; `LineSequence = parentIndex.ToString()`; `Cost = unit price`. |
| Otherwise (blank price or explicit `0.0`) | **CHILD row** under the most recent PARENT: `LineSequence = "{parent}.{NN:D2}"`; `Cost = 0` (the writer applies the `0.000001` sentinel). |

**Critical:** an explicit `0.0` in the unit-price cell does **not** promote a row to PARENT. Some rows like `5374CM1 — software1 Configuration Instruction` carry an explicit `0.0` price but are configuration components of the parent CTO; they must remain children with the sentinel cost.

## Quote total

The footer carries `Total:` in the unit-price column and the AUD total in the extended-price column (sample: `Total: 103,542.60`). The cell may have a floating-point artefact (`103542.60000000003`); the parser rounds to 2 dp before storing.

## Per-field handling

- **Part Number** (`vpn`): trim `PN`.
- **Description** (`description`): trim `Description` (single cell, no wrap).
- **Quantity** (`qty`): `Requested Quantity` → `int` (Lenovo writes qty as a string like `"1"` and `"2"`; `DecimalCleaner.ParseInt` handles both string and numeric cells).
- **Cost** (`cost`): unit price column for parents; `0` for children.
- **Term / MSRP / Serial / Dates**: not present in this format — left null.
- **Currency**: `AUD` (the price columns are labelled `Adjusted Buy Price (AUD)`).

## Line sequence numbering

Parents are numbered as integers from 1 in document order. Children are numbered `parent.NN` where `NN` is the 1-based index of the child within its parent (`5.01`, `5.02`, …). `LineSequence` is a string — `AnzGenericWriter` writes it to column A verbatim.

## Edge cases the tests cover

- 2 CTO configurations (sharing the same parent PN `7D7ACTO1WW`) — line numbering is global, not per-config, so the second CTO becomes parent `5`, not `1` of config 2.
- Leaf parents with no children (`5PS7C00099`, `5WS7C00090` warranty SKUs) — no child rows follow, the next non-skip row is the next parent and gets the next integer.
- Subtotal rows (`7D7ACTO1WW | Subtotal | 59703.80`) skipped, not double-counted.
- Repeated `Feature Code | Description | Qty` headers above each child block — skipped.
- Configurator marker rows (`Set from Configurator(Config N) <code>`) skipped.
- `5374CM1` rows with explicit `0.0` price — classified as children, not parents.
- Floating-point artefact in the `Total:` cell (`103542.60000000003`) — rounded to `103542.60`.

## Expected output for `BRDAD010458440.xls` (hand-validated)

- 8 parents, 54 children, 62 line items.
- Parents (in order): `7D7ACTO1WW` 36,399.96 · `7S0XCTO5WW` 322.14 · `5PS7C00099` 236.31 · `5WS7C00090` 6,880.39 · `7D7ACTO1WW` 49,742.85 · `7S0XCTO5WW` 322.14 · `5PS7C00099` 236.31 · `5WS7C00090` 9,402.50.
- Quoted total = computed total = `AUD 103,542.60`.

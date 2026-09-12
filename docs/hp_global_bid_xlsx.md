# HP Global Bid (XLSX) — Extraction Spec

Parser slug: `hp_global_bid_xlsx`  
Vendor: `HP`  
Accepted MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`  
Default CRM template: `No Calculation`  
Available CRM templates: `No Calculation`, `Uplift`

---

## Source file format

HP Global Bid export workbooks (filename pattern `translate_quote_<deal>_v<version>_all.xlsx`).

| File | Deal Number | Items | Notes |
|---|---|---|---|
| `translate_quote_98000001_v25_all.xlsx` | 98000001 | 24 | Qty always 1; AUD computed total 35,233.34 |

### Structure

Multi-sheet workbook. Two sheets are relevant:

- **`About this deal`** — deal metadata, including `Deal Number`.
- **`Product numbers`** — line-item table. This is the primary extraction target.

The parser opens the `"Product numbers"` sheet by name; falls back to the first sheet if not found.

### `Product numbers` sheet layout

The header row is located by the anchor cell `"Product number"` (exact string match, case-sensitive). Row number is not fixed — do not hard-code.

Required column labels (exact, as they appear after `TextCleaner.Clean`):

| Column label | Field |
|---|---|
| `Product number` | `vpn` |
| `Description` | `description` |
| `Converted net price [AUD]` | `cost` |
| `Remaining qty` | used in `comments` |

`qty` is **not** read from a source column — it is always `1`. `Aggregated item quantity`
and `Full term (Months)` are **not parsed** (the column may still be present in the
file; it is simply ignored — its value remains available in `Raw`).

**AUD validation:** if `Converted net price [AUD]` is absent from the header row, the parser throws `ParseError("currency", "Quote is not denominated in AUD.", …)`. This prevents non-AUD Global Bid files from being silently mis-parsed (they use a different cost column header).

---

## Extraction algorithm

1. `WorkbookReader.Open(path)`
2. Locate the `"Product numbers"` sheet by name; fall back to `workbook.Worksheets.First()`.
3. `WorkbookReader.FindCell(sheet, "Product number")` → `headerCell`; throws `ParseError("detect", …)` if not found.
4. `WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber)` → `headerMap`.
5. Check `headerMap.Columns.ContainsKey("Converted net price [AUD]")`; throw `ParseError("currency", …)` if absent.
6. `WorkbookReader.RequireLabels(headerMap, …)` for the four required columns.
7. Iterate rows from `headerMap.RowNumber + 1` to `lastRow`; **break on the first wholly-empty row**.
8. For each row: skip if `Product number` is blank. Otherwise extract the line item (see below).

### Per-row extraction

| Field | Source | Notes |
|---|---|---|
| `vpn` | `Product number` | Trimmed; skip row if blank |
| `description` | `Description` | Trimmed |
| `cost` | `Converted net price [AUD]` | `DecimalCleaner.Parse(text, defaultZero: true)` |
| `qty` | — | Always `1` (`Aggregated item quantity` is not read) |
| `comments` | `Remaining qty` | `"{remaining} Remaining"` |
| `msrp` | — | Always `null` |
| `term` | — | `Full term (Months)` is not parsed or written anywhere |
| `line_sequence` | — | Not set by parser; `CrmWriter` auto-increments from 1 |

### Quote number

1. Open the `"About this deal"` sheet.
2. Find the cell with value `"Deal Number"`.
3. Read the cell one column to the right.
4. Fallback: `Path.GetFileNameWithoutExtension(path)`.

### Validation

No quoted total exists in Global Bid files. `ValidationResult` is constructed directly:

```
Matches = true
Difference = 0
QuotedTotal = null
ComputedTotal = Σ(cost × qty), rounded to 2 dp (MidpointRounding.AwayFromZero); qty is always 1
```

Do **not** call `ParseValidation.Validate(items, null)` — that would return `Matches = false` and trigger the mismatch modal.

---

## Golden expected output

### `translate_quote_98000001_v25_all.xlsx` (24 items)

First 5 items:

| Seq | VPN | Description | Cost | Qty | Comments |
|---|---|---|---|---|---|
| 1 | D95A8UC | … | 1,900.95 | 1 | `620 Remaining` |
| 2 | 9E0G5AA | … | 347.56 | 1 | `… Remaining` |
| 3 | 8X223AA | … | 161.51 | 1 | `20 Remaining` |
| 4 | 9D9V7AA | … | 269.54 | 1 | `… Remaining` |
| 5 | A4LZ8AA | … | 5,325.12 | 1 | `… Remaining` |

Qty is `1` for every item; comments are always `"{remaining} Remaining"`. All items have `msrp = null`.

Computed total: `AUD 35,233.34` (no quoted total).

---

## Output column mapping

Uses the same `CrmWriter` and ANZ-GENERIC 27-column layout as HP Bid. Differences from HP Bid: `Comments` column R is populated; no `min_qty`.

| Col | Header | Value | Notes |
|---|---|---|---|
| A | Item | auto-increment 1, 2, 3… | Parser leaves `LineSequence` unset; writer fallback counter |
| B | Vendor Name | `"HP"` | |
| D | Vendor Part Number | `vpn` | |
| E | Description | `description` | |
| F | Qty. | `qty` | Always `1` |
| H | MSRP | blank | Always null |
| I | Cost | `cost` | Non-zero; no `0.0001` sentinel needed (Global Bid rows always have a price) |
| K | Margin | `margin` (Uplift only) | |
| R | Comments | `comments` | `"{remaining} Remaining"` |
| W | Min Order Qty | blank | Not extracted |

Call: `CrmWriter.Write(items, outputPath, crmTemplate, new CrmWriterOptions(VendorName: "HP", Margin: margin))` — the `Uplift` profile writes col K, `No Calculation` leaves it blank.

## Bid metadata

On `About this deal`, the values adjacent to `Deal Number` and `Deal Version` (matched case-insensitively) supply the bid fields. Best-effort: a missing sheet or either missing value degrades the label — an absent version keeps the number with revision `1` — rather than failing the parse. `QuoteNumber` keeps its separate filename fallback.

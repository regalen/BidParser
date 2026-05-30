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
| `translate_quote_47500427_v25_all.xlsx` | 47500427 | 24 | AUD computed total 34,746,055.00 |

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
| `Aggregated item quantity` | `qty` |
| `Remaining qty` | used in `comments` |

Optional column:

| Column label | Field |
|---|---|
| `Full term (Months)` | used in `comments` |

**AUD validation:** if `Converted net price [AUD]` is absent from the header row, the parser throws `ParseError("currency", "Quote is not denominated in AUD.", …)`. This prevents non-AUD Global Bid files from being silently mis-parsed (they use a different cost column header).

---

## Extraction algorithm

1. `WorkbookReader.Open(path)`
2. Locate the `"Product numbers"` sheet by name; fall back to `workbook.Worksheets.First()`.
3. `WorkbookReader.FindCell(sheet, "Product number")` → `headerCell`; throws `ParseError("detect", …)` if not found.
4. `WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber)` → `headerMap`.
5. Check `headerMap.Columns.ContainsKey("Converted net price [AUD]")`; throw `ParseError("currency", …)` if absent.
6. `WorkbookReader.RequireLabels(headerMap, …)` for the five required columns.
7. Iterate rows from `headerMap.RowNumber + 1` to `lastRow`; **break on the first wholly-empty row**.
8. For each row: skip if `Product number` is blank. Otherwise extract the line item (see below).

### Per-row extraction

| Field | Source | Notes |
|---|---|---|
| `vpn` | `Product number` | Trimmed; skip row if blank |
| `description` | `Description` | Trimmed |
| `cost` | `Converted net price [AUD]` | `DecimalCleaner.Parse(text, defaultZero: true)` |
| `qty` | `Aggregated item quantity` | `DecimalCleaner.ParseOptionalInt` → default 0 |
| `comments` | `Remaining qty` + optional `Full term (Months)` | `"{term} Months \| {remaining} Remaining"` when `term > 0`; `"{remaining} Remaining"` otherwise |
| `msrp` | — | Always `null` |
| `term` | — | Not written to `LineItem.Term`; term value used only in `comments` |
| `line_sequence` | — | Not set by parser; `AnzGenericWriter` auto-increments from 1 |

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
ComputedTotal = Σ(cost × qty), rounded to 2 dp (MidpointRounding.AwayFromZero)
```

Do **not** call `ParseValidation.Validate(items, null)` — that would return `Matches = false` and trigger the mismatch modal.

---

## Golden expected output

### `translate_quote_47500427_v25_all.xlsx` (24 items)

First 5 items:

| Seq | VPN | Description | Cost | Qty | Comments |
|---|---|---|---|---|---|
| 1 | D95A8UC | … | 1,900.95 | 2,500 | `620 Remaining` |
| 2 | 9E0G5AA | … | 347.56 | 1,000 | `… Remaining` |
| 3 | 8X223AA | … | 161.51 | 100 | `24 Months \| 20 Remaining` |
| 4 | 9D9V7AA | … | 269.54 | 1,000 | `… Remaining` |
| 5 | A4LZ8AA | … | 5,325.12 | 25 | `… Remaining` |

7 of 24 items have a term value and use the pipe-separated comment format. All items have `msrp = null`.

Computed total: `AUD 34,746,055.00` (no quoted total).

---

## Output column mapping

Uses the same `AnzGenericWriter` and ANZ-GENERIC 27-column layout as HP Bid. Differences from HP Bid: `Comments` column R is populated; no `min_qty`.

| Col | Header | Value | Notes |
|---|---|---|---|
| A | Item | auto-increment 1, 2, 3… | Parser leaves `LineSequence` unset; writer fallback counter |
| B | Vendor Name | `"HP"` | |
| D | Vendor Part Number | `vpn` | |
| E | Description | `description` | |
| F | Qty. | `qty` | |
| H | MSRP | blank | Always null |
| I | Cost | `cost` | Non-zero; no `0.0001` sentinel needed (Global Bid rows always have a price) |
| K | Margin | `margin` (Uplift only) | |
| R | Comments | `comments` | Term + remaining, or remaining only |
| W | Min Order Qty | blank | Not extracted |

Call: `AnzGenericWriter.Write(items, outputPath, crmTemplate, includeMargin: crmTemplate == "Uplift", margin, vendorName: "HP")`

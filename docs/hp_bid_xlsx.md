# HP Bid (XLSX) — Extraction Spec

Parser slug: `hp_bid_xlsx`  
Vendor: `HP`  
Accepted MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`  
Default CRM template: `No Calculation`  
Available CRM templates: `No Calculation`, `Uplift`

---

## Source file format

HP deal-export workbooks. Two samples validated:

| File | Deal Number | Items | Notes |
|---|---|---|---|
| `Deals20260518T034809_HPI.xlsx` | 48035102 | 479 | Part Number, Bundle, Bundle Detail rows |
| `Deals20260518T043243_HPI.xlsx` | 48034525 | 5   | Part Number rows only |

### Structure

- Single sheet named **`deals.0`**.
- Rows 1–53: deal metadata block (includes `Deal Number`, `Deal Version`, `End Customer`, `Deal Description`, etc.).
- **Row 54**: line-item header row. Located by the `"Line Type"` anchor — do not hard-code.
- No quoted total row anywhere.

### Exact header labels (row 54)

| Col | Header (exact) | Used for |
|---|---|---|
| A | `Line Type` | row classification |
| C | `Product Number/ID` | vpn base |
| D | `Option Code` | vpn suffix (concat with `#` when non-empty) |
| E | `Product Description` | description |
| G | `Price` | cost |
| H | `Bundle Detail Qty` | qty + min_qty for Bundle Detail rows |
| I | `Min Order Qty` | qty + min_qty for Part Number / Bundle rows |
| J | `Max Deal Qty` | comments (`"Max Qty: {n}"`) for Part Number / Bundle rows |

The header row also contains ~17 other columns (`Product Line`, `Addl Discount %`, `Line Number`, etc.) — build the `HeaderMap` from the full row and ignore those not listed above.

### `Line Type` values

Three distinct values: **`Part Number`**, **`Bundle`**, **`Bundle Detail`**.

---

## Extraction algorithm

1. `WorkbookReader.Open(path)`; `sheet = workbook.Worksheets.First()`
2. `headerCell = WorkbookReader.FindCell(sheet, "Line Type")` — throws `ParseError("detect", …)` if missing
3. `WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber)` + `RequireLabels` for all 8 required column headers
4. Iterate rows from `headerRow + 1` to `lastRow`; **break on the first wholly-empty row**
5. For each row, read `Line Type`; apply the table below

### Row-by-row rules

| Field | `Part Number` / `Bundle` | `Bundle Detail` |
|---|---|---|
| **Line sequence** → Item col A | Next number in one running sequence (`1`, `2`, `3`, …). | Next number in the same running sequence (no longer sub-numbered under the Bundle). |
| **vpn** | `Product Number/ID`; if `Option Code` non-empty: `"Product Number/ID#Option Code"` | same rule |
| **description** | `Product Description` | `Product Description` |
| **cost** | `Price` (default 0 when blank) | **0** — the component price is dropped (the Bundle parent line carries the total). Source `Price` is still kept in `Raw["Price"]`. |
| **qty** | `Min Order Qty`; if 0 → **1** | `Bundle Detail Qty` |
| **min_qty raw** | `Min Order Qty` | `Bundle Detail Qty` |
| **min_qty final** | if raw == 0 → **1**; else raw value | same rule |
| **comments** | `"Max Qty: {Max Deal Qty}"` (blank when `Max Deal Qty` absent) | `null` — Bundle Detail rows carry no `Max Deal Qty` |
| **msrp** | `null` (always blank) | `null` (always blank) |

All three line types are kept as line items (none are skipped).

**Qty source:** for Part Number / Bundle lines, `qty` comes from `Min Order Qty` (with the
`0 → 1` substitution), so `qty` equals `min_qty` for those rows. `Max Deal Qty` (the deal
quantity) is no longer the line quantity; it is surfaced in `comments` as `"Max Qty: {n}"`.

**Bundle Detail pricing:** a Bundle Detail is a component breakdown of its Bundle. The Bundle line carries the deal price for the whole bundle, so each Bundle Detail's own `Price` is intentionally dropped (`cost = 0`) to avoid double-counting in the computed total. On export, `AnzGenericWriter` writes the `0.0001` sentinel in the Cost column for these zero-cost lines because the downstream import rejects a literal `0` (and rounds the sentinel back to `0`).

### Quote Number

Find the cell whose value is `"Deal Number"` (within the metadata block, rows 1–53) and read the adjacent cell to the right. If blank or not found, fall back to `Path.GetFileNameWithoutExtension(path)`.

### Quoted total

HP files carry no quoted total. `QuotedTotal = null`. The `ValidationResult` is constructed directly with `Matches = true` and `Difference = 0` so no mismatch warning is shown to the user.

---

## Golden expected output

### `Deals20260518T043243_HPI.xlsx` (Part Number only, 5 items)

| Seq | VPN | Description | Cost | Qty | MinQty | Comments |
|---|---|---|---|---|---|---|
| 1 | 5TW10AA | HP USB-C Dock G5 | 165.83 | 1 | 1 | Max Qty: 100 |
| 2 | 9D9S0UT | HP USB-C/A Universal Dock G2 | 336.70 | 1 | 1 | Max Qty: 100 |
| 3 | BV2Q6PT | HP ZBook Fury 16 G11 Mobile Wkstn | 3393.88 | 1 | 1 | Max Qty: 100 |
| 4 | BQ4E3PT | HP EliteBook 840 G11 NB PC | 2258.18 | 1 | 1 | Max Qty: 500 |
| 5 | BV8B6PT | HP EliteDesk 805 G9 SFF | 2160.00 | 1 | 1 | Max Qty: 250 |

Qty is `1` for every line (`Min Order Qty` is 0 on all rows → 1). Computed total: `8314.59`
(Σ cost × 1; no quoted total).

### `Deals20260518T034809_HPI.xlsx` (Part Number + Bundle + Bundle Detail, 479 items)

First rows (illustrating all three line types and `#`-concatenated Option Code):

| Seq | Line Type | VPN | Cost | Qty | MinQty | Comments |
|---|---|---|---|---|---|---|
| 1 | Part Number | 9D9L6UT | 213.92 | 1 | 1 | Max Qty: 2012 |
| 2 | Part Number | 9D9L6A9 | 184.78 | 1 | 1 | Max Qty: 2012 |
| 3 | Part Number | 9D9V7AA | 240.00 | 1 | 1 | Max Qty: 2012 |
| 4 | Bundle | 55623728 | 2387.94 | 1 | 1 | Max Qty: 880 |
| 4.01 | Bundle Detail | C89FGAV | 0 | 1 | 1 | _(blank)_ |
| 4.02 | Bundle Detail | 8C9M7AV | 0 | 1 | 1 | _(blank)_ |
| … | … | … | … | … | … | … |
| 4.06 | Bundle Detail | **4SS11AV#ABG** | 0 | 1 | 1 | _(blank)_ |

Part Number / Bundle `qty` comes from `Min Order Qty` (0 → 1); their `Max Deal Qty` is
written to `Comments`. Bundle Detail `cost` is `0` in the model (component price dropped —
see above) and exports as the `0.0001` sentinel. Computed total: `34402.45` (Part Number +
Bundle costs × 1; no quoted total).

---

## Output column mapping

See `docs/output_mapping.md` — HP section. Both templates use the ANZ-GENERIC 27-column layout (`AnzGenericWriter`). The key difference between templates:

| Col | Header | No Calculation | Uplift |
|---|---|---|---|
| A | Item | LineSequence | LineSequence |
| B | Vendor Name | `"HP"` | `"HP"` |
| D | Vendor Part Number | vpn | vpn |
| E | Description | description | description |
| F | Qty. | qty | qty |
| H | MSRP | **blank** | **blank** |
| I | Cost | cost | cost |
| K | Margin | **blank** | margin % |
| R | Comments | comments (`"Max Qty: {n}"`) | comments (`"Max Qty: {n}"`) |
| W | Min Order Qty | min_qty | min_qty |

No FX rate, no foreign currency columns. Zero costs (every Bundle Detail, since its price is dropped) export as the `0.0001` sentinel in column I; non-zero Part Number / Bundle costs are written as-is. Column R carries `"Max Qty: {Max Deal Qty}"` for Part Number / Bundle lines and is blank for Bundle Detail.

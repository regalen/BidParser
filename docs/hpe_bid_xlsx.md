# HPE Bid (XLSX) — Extraction Spec

Parser slug: `hpe_bid_xlsx`  
Vendor: `HPE`  
Accepted MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`  
Default CRM template: `No Calculation`  
Available CRM templates: `No Calculation`, `Uplift`

Structurally a sibling of [HP Bid (XLSX)](hp_bid_xlsx.md) (same `Part Number` / `Bundle` /
child line model, same ANZ-GENERIC output), but with **different header/column labels**. HPE
populates `msrp`, takes `qty` only from `Quantity`, and leaves Min Order Qty and Comments blank.

---

## Source file format

HPE deal-export workbooks. Two samples validated:

| File | Deal Number | Items | Notes |
|---|---|---|---|
| `HPE_Deal_9500000001_v2.xlsx` | 9500000001 | 66 | Bundle × 3, BundleDetails × 63 |
| `HPE_Deal_9500000002_v1.xlsx` | 9500000002 | 4  | Part Number rows only |

### Structure

- First sheet (the deal sheet; a second `Information` sheet is ignored).
- A long key/value metadata block at the top: **key in column A, value in column B** (e.g.
  `DealNumber` → `9500000001`, `EndCustomer`, `QuoteID`, …).
- The line-item header row is located by the **`"LineType"`** anchor (one word — *not* HP's
  `Line Type`). Do not hard-code its row.
- No quoted total row anywhere.

### Exact header labels (anchor row)

| Header (exact) | Used for |
|---|---|
| `LineType` | row classification |
| `ProductNumber` | vpn for `Part Number` rows |
| `OptionCode` | optional vpn suffix for `BundleDetails` rows |
| `BundleID` | vpn for `Bundle` rows |
| `ComponentID` | vpn for `BundleDetails` rows |
| `Quantity` | qty (all line types) |
| `ProductDescription` | description |
| `ListPrcEst` | msrp for `Part Number` / `Bundle` rows |
| `Offering` | cost for `Part Number` / `Bundle` rows |

The row carries other columns (`MinOrderQty`, `MaxDealQty`, `ProductLine`, `DiscPct`,
`LineNumber`, `EffectiveDatesFrom`, …) — build the `HeaderMap` from the full row and ignore
those not listed.

### `LineType` values

Three distinct values: **`Part Number`**, **`Bundle`**, **`BundleDetails`** (one word).

---

## Extraction algorithm

1. `WorkbookReader.Open(path)`; `sheet = workbook.Worksheets.First()`
2. `headerCell = WorkbookReader.FindCell(sheet, "LineType")` — throws `ParseError("detect", …)` if missing
3. `WorkbookReader.HeaderMap(...)` + `RequireLabels` for all 9 required headers above
4. Iterate rows from `headerRow + 1` to `lastRow`; **break on the first wholly-empty row**
5. For each row, read `LineType`; apply the table below; unknown line types are skipped

### Row-by-row rules

| Field | `Part Number` | `Bundle` | `BundleDetails` |
|---|---|---|---|
| **Line sequence** → Item col A | top-level counter (`1`, `2`, …) | top-level counter; also opens a child group (reset child counter) | `{parentSeq}.{childCounter:D2}` (`1.01`, `1.02`, …) |
| **vpn** | `ProductNumber` | `BundleID` | `ComponentID`; append `"#{OptionCode}"` when `OptionCode` is non-empty |
| **description** | `ProductDescription` | `ProductDescription` | `ProductDescription` |
| **msrp** | `ListPrcEst` (default 0 when blank) | `ListPrcEst` | **0** — component msrp dropped (parent carries it) |
| **cost** | `Offering` (default 0 when blank) | `Offering` | **0** — component cost dropped (parent carries it) |
| **qty** | `Quantity` (`0 → 1`) | `Quantity` (`0 → 1`) | `Quantity` (`0 → 1`) |
| **min_qty** | `null` | `null` | `null` |
| **comments** | `null` | `null` | `null` |

**qty source:** unlike HP Bid (which derives qty from Min Order Qty), HPE qty comes only from the
`Quantity` column for every line type. `MinOrderQty` and `MaxDealQty` are not extracted into
`LineItem` fields; the CRM Min Order Qty and Comments columns remain blank.

**BundleDetails pricing:** a BundleDetails line is a component breakdown of its Bundle. The
Bundle line carries the deal price for the whole bundle, so each component's own `ListPrcEst`
and `Offering` are dropped to `0` to avoid double-counting in the computed total (the source
values are still kept in `Raw`). On export, `CrmWriter` writes the `0.0001` sentinel in
both the MSRP (col H) and Cost (col I) columns for these zero values, because the downstream
import rejects a literal `0` (and rounds the sentinel back to `0`). The same sentinel applies
to any `Part Number` / `Bundle` line whose `ListPrcEst` / `Offering` is `0`.

### Quote Number

Find the cell whose value is `"DealNumber"` (in the metadata block) and read the adjacent
cell to the right. If blank or not found, fall back to `Path.GetFileNameWithoutExtension(path)`.

### Quoted total

HPE files carry no quoted total. `QuotedTotal = null`. The `ValidationResult` is constructed
directly with `Matches = true` and `Difference = 0` so no mismatch warning is shown.

---

## Golden expected output

### `HPE_Deal_9500000002_v1.xlsx` (Part Number only, 4 items)

| Seq | VPN | Description | MSRP | Cost | Qty |
|---|---|---|---|---|---|
| 1 | R8Q70A | Aruba 6200M 48G CL4 PoE 4SFP+ Sw | 20514.00 | 5128.50 | 5 |
| 2 | JL087A | Aruba X372 54VDC 1050W PS | 2552.00 | 638.00 | 5 |
| 3 | JL087A | Aruba X372 54VDC 1050W PS AU en | 0 → `0.0001` | 0 → `0.0001` | 5 |
| 4 | JL669B | Aruba X751 FB Fan Tray | 1106.00 | 276.50 | 5 |

Qty is `5` for every line (from `Quantity`). Computed total: `30215.00` (Σ cost × qty; no
quoted total). Note rows 2 and 3 share VPN `JL087A` — row 3 is a `Part Number` carrying an
`OptionCode` (`ABG`), which is not appended because option suffixes apply only to `BundleDetails`.

### `HPE_Deal_9500000001_v2.xlsx` (Bundle + BundleDetails, 66 items)

| Seq | Line Type | VPN | MSRP | Cost | Qty |
|---|---|---|---|---|---|
| 1 | Bundle | 52080474 | 87995.00 | 26632.75 | 1 |
| 1.01 | BundleDetails | AK379B | 0 → `0.0001` | 0 → `0.0001` | 1 |
| 1.02 | BundleDetails | R6Q75A | 0 → `0.0001` | 0 → `0.0001` | 2 |
| … | … | … | … | … | … |
| 1.08 | BundleDetails | HU4B2A3#QC1 | 0 → `0.0001` | 0 → `0.0001` | 1 |
| 2 | Bundle | 52079278 | 320784.00 | 58842.80 | 1 |
| 3 | Bundle | 52079277 | 299357.36 | 46237.81 | 1 |

Bundle `vpn` comes from `BundleID` (the numeric bundle id), not the row's `ProductNumber`.
Computed total: `131713.36` (Σ Bundle cost × qty; BundleDetails contribute 0; no quoted total).

---

## Output column mapping

See [`output_mapping.md`](output_mapping.md) — HPE section. Both templates use the
ANZ-GENERIC 27-column layout (`CrmWriter`).

| Col | Header | No Calculation | Uplift |
|---|---|---|---|
| A | Item | LineSequence | LineSequence |
| B | Vendor Name | `"HPE"` | `"HPE"` |
| D | Vendor Part Number | vpn | vpn |
| E | Description | description | description |
| F | Qty. | qty (from `Quantity`) | qty |
| H | MSRP | msrp (`0 → 0.0001`) | msrp |
| I | Cost | cost (`0 → 0.0001`) | cost |
| K | Margin | **blank** | margin % |
| R | Comments | **blank** | **blank** |
| W | Min Order Qty | **blank** | **blank** |

No FX rate, no foreign currency columns. Unlike HP Bid, **MSRP (col H) is populated** for HPE
(from `ListPrcEst`), with the same `0 → 0.0001` sentinel rule as Cost.

## Bid metadata

The values adjacent to `DealNumber` and `DealVersion` (matched case-insensitively) supply `BidNumber` and `BidRevision`. Best-effort: an absent `DealVersion` keeps the number with revision `1`; an absent `DealNumber` leaves both null. Neither fails the parse. `QuoteNumber` keeps its separate filename fallback.

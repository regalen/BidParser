# HP OneConfig (XLSX) — extraction spec

## Format overview

A single-sheet Excel workbook produced by HP's OneConfig quoting tool. Each file describes exactly one configured product (the "Config") and the bill of components that build it.

The workbook has two sections separated by an empty row:

| Section | Header row | Data |
|---|---|---|
| Config | Row N — `Config ID`, `Config Name`, `KMAT`, `Total Price` | One row immediately below the header |
| Components | Row M — `Component Category`, `Part Number`, `Description`, `Quantity`, `Price` | One row per component below the header |

Both header rows are located by anchor label (`Config ID` and `Part Number`), never by fixed row number.

## Sample

`samples/inputs/55648855.xlsx` — 1 parent + 30 children. Config ID `55648855`, Config Name `HP EliteBook 6 G2i 14 AI`, Total Price `6,042.77 AUD`.

## Extraction algorithm

### Step 1 — Config header

Scan the active (first) worksheet for a cell whose trimmed text equals `Config ID`. That cell's row is the Config header. Build a `HeaderMap` for that row; require `Config ID`, `Config Name`, and `Total Price` (ignore `KMAT`).

### Step 2 — Config data row

The row immediately below the Config header.

- `vpn` ← `Config ID` cell (trimmed, non-empty; parser throws if empty)
- `description` ← `Config Name` cell (trimmed)
- `msrp` ← `Total Price` cell → `DecimalCleaner.Parse` (handles `6,042.77` format)
- `qty = 1` (always)
- `cost = 0` (the writer emits the parent's real price on column H MSRP, not Cost)
- `LineSequence = "1"`

Multi-Config guard: scan the rest of the sheet for a second cell whose trimmed text equals `Config ID`. If found, throw `ParseError("config", "OneConfig must contain exactly one Config ID row.", …)`.

### Step 3 — Components header

Scan downward from the Config data row for a cell whose trimmed text equals `Part Number`. That cell's row is the Components header. Build a `HeaderMap`; require `Part Number`, `Description`, `Quantity` (`Price` is mapped but ignored).

### Step 4 — Component rows

Iterate rows below the Components header. Stop at the first wholly-empty row (or sheet end). Skip rows where `Part Number` is empty.

For each kept row, emit a child `LineItem`:

- `LineSequence = "1.NN"` where `NN` is a two-digit zero-padded counter (`1.01`, `1.02`, …, `1.30`)
- `vpn` ← `Part Number` (trimmed)
- `description` ← `Description` (trimmed)
- `qty` ← `Quantity` → `DecimalCleaner.ParseOptionalInt` (defaults to 1)
- `msrp = 0`, `cost = 0` — source `Price` column is intentionally discarded

### Step 5 — Result

- `QuoteNumber = Config ID`
- `Supplier = "HP"`, `Currency = "AUD"`
- `QuotedTotal = null` (no validation)
- `ValidationResult { Matches = true, Difference = 0, QuotedTotal = null }`

## Output template

`ANZ-GENERIC_PercentOffWithUplift.xlsx` — single template `% Off RRP with Uplift`.

Writer: `PercentOffWithUpliftWriter`. Column mapping:

| Col | Header | Value |
|---|---|---|
| A | `Item` | `LineSequence` (`1`, `1.01`, …) |
| B | `Vendor Name` | `HP` |
| D | `Vendor Part Number` | `vpn` |
| E | `Description` | `description` |
| F | `Qty.` | `qty` |
| H | `MSRP` | parent: real `msrp`; children: `0.000001` sentinel |
| I | `Cost` | blank (intentionally empty) |
| K | `Margin` | user-supplied `margin` (always written) |
| X | `IM%` | user-supplied `im_percent` (always written) |

The `0.000001` sentinel replaces any zero MSRP because the downstream import rejects a literal `0`. Row L1 carries `(Optional for Software and/or Services)`. The last row is the standard end-loop sentinel (`B* = "*"`, `D* = DO NOT DELETE…`).

## Parser constants

- Slug: `hp_oneconfig_xlsx`
- CRM template: `% Off RRP with Uplift` (single template, no dropdown)
- Vendor: `HP`
- MIME: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`

## Parse parameters

| Field | Required | Notes |
|---|---|---|
| `margin` | Yes | Written to col K for all rows |
| `im_percent` | Yes | Written to col X for all rows; persisted as `User.ImPercent` |
| `fx_rate` | No | Not used (AUD, no FX conversion) |
| `crm_template` | No | Always `% Off RRP with Uplift` — omit or pass the literal string |

# Lenovo LBP-I IDG Quote (PDF) — extraction spec

- **Parser class:** `BidParser.Parsing.Lenovo.LbpiIdgPdf.LenovoLbpiIdgPdfParser`
- **Slug:** `lenovo_lbpi_idg_pdf` (`ParserSlugs.LenovoLbpiIdgPdf`)
- **Vendor:** Lenovo IDG (`Vendors.LenovoIdg`) — Col B of the CRM workbook still reads
  `LENOVO` via `OutputVendorName => Vendors.LenovoOutput`; see
  [Lenovo ISG / Lenovo IDG vendor split](project_memory.md#lenovo-isg--lenovo-idg-vendor-split-and-outputvendorname)
  in `project_memory.md`.
- **Accepted MIME:** `application/pdf`
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Report type:** `Standard`
- **Split by Solution ID:** not supported — this format has no Solution IDs
  (`SupportsSolutionIdSplit = false`)

## File format and anchors

LBP-I IDG quotes are Lenovo Intelligent Devices Group bids — laptops, monitors, docks —
on a different template from the LBP-I/LBP-E **ISG** (Infrastructure Solutions Group)
parsers: no Solution IDs, a different product-grid header, and `Bid Request No.` bid
metadata instead of `Quote No.:`. Page 1 is a boilerplate cover letter with no quote data;
every anchor lives on page 2 onward.

- **`PRODUCT AND SERVICE DETAILS`** — 7 columns:
  `# | Part Number | Description | Qty | Unit price excl. GST (AUD) | Total price excl. GST (AUD) | Total price incl. GST (AUD)`.
  The header repeats at every page break; a `Grand Total  AUD <excl>  AUD <incl>` row
  repeats at the bottom of every page (identical values each time — last one wins).
- **`CONFIGURATION DETAILS`** (absent on parts-only quotes) — 4 columns:
  `Line Item# | Components | Description | Qty`. One section per configured (CTO) product
  line, opened by a row carrying `<line#> <part number> <description> <qty>` — that opening
  row's `Line Item#` is the join key back to the product grid's `#`; it is not itself
  emitted. Not every product line has a section.
- **`MTM`** (**optional**) — a `Line Item# | Part Number | Specification Description` recap
  table listing the machine type model each product line resolves to. It is not component
  data and is otherwise ignored. The anchor is the heading plus the first two words of its
  own header row (`MTM Line Item#`), not the bare `MTM` token: Lenovo uses "MTM" as ordinary
  prose, and a lone token would also match a component label or a product description and end
  the region early — silently, because the dropped components are zero-cost and the quote
  still reconciles without them. **Lenovo omits the entire table when no product line resolves
  to a machine type model** (an unreleased CTO placeholder does not) — `BRPAS019100003V1.pdf`
  is such a quote, and requiring the recap made it unparseable.
- **`TERMS AND CONDITIONS`** — the boilerplate clause heading that closes the quote body on
  every quote seen. Matched as its three uppercase tokens; `FindSequence` compares ordinally,
  so the clause body's own lower-case "Lenovo Terms and Conditions" cannot collide, and it
  sits below the heading in any case.

**Grid terminators.** `FindGridEnd` bounds a grid at whichever of the `MTM` recap or the
`TERMS AND CONDITIONS` heading comes **first** — the earlier index, not a fallback used only
when `MTM` is missing, so a stray `MTM Line Item#` below the terms heading cannot reopen the
region across the clauses and emit terms prose as component rows. It terminates the
`CONFIGURATION DETAILS` region, or the product grid when that section is absent. Each grid
searches from its own start, so a terminator above `CONFIGURATION DETAILS` cannot terminate
the section below it.

The `#` column is **not contiguous** — both sample fixtures jump `1 → 3` (there is no line
2) — so it is never used as an array index; parents instead get a sequential
`LineSequence` ("1", "2", …) independent of the source `#`.

`Detect()` returns 0.9 when `PRODUCT AND SERVICE DETAILS`, the `# / Part Number /
Description / Qty` header, and the `Bid Request No.` anchor are all present — the
signature that separates it from LBP-I ISG's `Line Item` header and `Lenovo Global
Technology` letterhead. No change was needed to `LenovoLbpiIsgPdfParser.Detect()`: its
letterhead check already scores 0.0 on IDG quotes.

## PDF quirks this parser handles

- **Genuinely separate ruled columns — no merged-cell splitting.** Unlike LBP-I ISG, Part
  Number/Description and Line Item#/Components are two independent columns each, not one
  merged cell split by token. Both grids still **centre** their header text over cells
  whose printed width varies between quotes (`Description` starts at x=335 in one sample
  quote while its values start at x=184; the config grid's `Components` column runs
  `[90, 182.8]` wide in one fixture and `[105.5, 269.8]` in the other), so a fixed header
  offset cannot locate the value bands. `PdfTableHelpers.CentredColumnRanges` recovers
  every boundary from the recurrence `b_i = 2·centre_i − b_{i-1}`, walked outward from one
  known seam:
  - **Product grid** seams on the `Unit price excl. GST` header's own left edge (the ~27pt
    gap to the `Qty` values makes it unambiguous); `Unit Price`/`Total Excl`/`Total Incl`
    land at pixel-identical X in both fixtures, so only `Part Number`/`Description` need
    the recurrence at all.
  - **Config grid** seams on the *section-row line numbers* — the only content the
    `Line Item#` column carries — not on the `Line` header word's own left edge: that header
    text is centred in a column far wider than itself, so its own X0 is not a safe anchor
    (using it instead reproduces every boundary ~8pt too far right). The scan is confined to
    integer tokens left of the `Item#` header's right edge, which no `Components` content can
    reach; a bare minimum over every word in the region would instead let unrelated page
    furniture set the seam, and this template already prints a marketing paragraph 5pt left
    of the grid. A region with no such line number raises `ParseError("extract", …)`.
  - The `(AUD)` sub-header line under `Unit`/`Total price excl./incl. GST` is ignored for
    centre purposes — its horizontal extent always sits inside its parent header's main
    line, so it cannot change the computed centre beyond the recurrence's own tolerance
    (empirically under 1.5pt in both fixtures).
- **The header itself repeats at every page break as three separate raw rows**, too far
  apart in midline to merge during row reconstruction: the `# Part Number Description
  Qty` line, the wrapped `Unit/Total price excl./incl. GST` line above it, and the
  `(AUD)` line below it. All three shapes must be filtered before logical-row grouping —
  the middle and bottom ones carry no `Line Item` cell of their own, so an incomplete
  filter lets them get silently absorbed into the nearest real row's `Unit Price`/`Total`
  cells (surfacing as a `FormatException` parsing a price, not a silently wrong value).
- **Logical rows via a flat pitch threshold, not vertically-centred repair.** A raw row
  (one per `RowsBetween` text line) starts a new logical row iff it is on the same page as
  the previous kept row and its midline gap from that row is at least `BlockGap = 12.0pt`,
  or the page changed and the row is itself an anchor — a page change alone never starts a
  new logical row, which is what carries a wrapped description across a page break.
  Measured pitches justify the threshold: intra-block (wrap) gaps run 5.0–10.5pt,
  inter-block (real new row) gaps run 13.2–13.5pt, in both grids of both fixtures — ~1.5pt
  of clearance either side. The populations separate because the wrap gap is the font's own
  leading while the inter-block gap is that leading plus a constant cell padding, so the
  margin does not shrink as a block gains wrapped lines. The failure modes are asymmetric:
  too high merges two rows and drops one (the total stops reconciling — loud), too low
  splits a block and emits a parent with an **empty description** but correct price and
  quantity (the total still reconciles — silent), which is why 12.0 sits nearer the upper
  bound. This
  single rule — simpler than LBP-I ISG's `RowBlocks`/`AssignDescriptionOwners`
  forward-backward repair — handles every observed wrap pattern: a description wrapping
  around a vertically-centred label, a label wrapping around a centred description, a
  product description wrapping across a page break, and label-less component rows (see
  below), which sit at full pitch and so already stay their own logical row.
- **Label-less components.** Some `CONFIGURATION DETAILS` rows carry a value but no label
  (`No OB M.2 SSD RAID`, `No AI Agent`, …). Because the `Components` and `Description`
  columns are independently bucketed (not merged), these need no special grouping case —
  only special *classification*: when the `Components` cell is empty, the row still
  becomes its own child, emitted with the literal placeholder VPN `"SPEC"` and the
  `Description` cell as its value. `LineItem.Raw` never fabricates a `Components` entry
  for these — it reflects the source cell's real emptiness.
- **Non-ASCII glyphs.** CRM rejects `®`/`™`. `AsciiCleaner.StripNonAscii` (opt-in, not
  folded into the shared `TextCleaner`) removes every character outside printable ASCII
  from `Vpn` and `Description` after `TextCleaner.JoinSpaced`'s hyphen-break repair has
  already run — `"4 Cell Li-ion 90Wh"` is unaffected. `LineItem.Raw` always keeps the
  original characters.

## Row classification (product grid)

| Row kind | Signature | Action |
|---|---|---|
| Grand Total | `Unit Price` cell reads `Grand Total` (repeats identically at every page break) | Capture quoted total + currency from the `Total Excl` cell (`"AUD 1,455,094.20"`); skip |
| Header repeat (3 shapes) | `Line Item == "#"`, or `Part Number == "Part Number"`, or `Unit Price` starts with `Unit` or equals `(AUD)` | Skip |
| Numbered line | `Line Item` parses as an integer and `Unit Price` is non-empty | A priced parent line |
| Non-anchored logical row | Everything else (e.g. the marketing paragraph between the grid and `CONFIGURATION DETAILS`) | Dropped after logical-row grouping |

Missing `PRODUCT AND SERVICE DETAILS` or an unresolvable product-grid header →
`ParseError("detect", …)` (wrong file-type flow). A Grand Total currency other than AUD →
`ParseError("currency", …)`. A missing Grand Total row → `ParseError("totals", …)`.

Once the document is recognised, extraction **fails closed**: a product grid with none of a
`CONFIGURATION DETAILS` heading, an `MTM` recap or a `TERMS AND CONDITIONS` heading to bound
it, a `CONFIGURATION DETAILS` heading whose `Line Item# | Components | Description | Qty`
header cannot be resolved or that has neither an `MTM` recap nor a `TERMS AND CONDITIONS`
heading below it, a config region carrying no section line number to seam its columns on,
a resolved config grid that yields no sections or any section with no components, and a
configuration section keyed to a line number no product line carries — all raise
`ParseError("extract", …)`. A missing `CONFIGURATION DETAILS` section is legal
(a parts-only quote): zero children, quote still reconciles on parent lines alone.

## Parent and child rules

One parent per product-grid line, in document order — no Solution ID grouping:

- Parent: `Vpn`/`Description` from the `Part Number`/`Description` columns (ASCII-stripped);
  `Cost` = `Unit price excl. GST (AUD)` parsed and rounded 2dp away from zero; `Qty` from
  the grid; `LineSequence` is the sequential parent index, **not** the source `#` (kept
  non-contiguous in `Raw["Line Item"]`). Duplicate part numbers on separate lines stay
  separate output lines.
- Children come from the parent's `CONFIGURATION DETAILS` section, in section order,
  immediately after their parent: `Vpn` = the `Components` label, or `"SPEC"` when blank;
  `Description` = the `Description` cell; `Cost = 0m` in memory (`CrmWriter` emits the
  `0.0001` sentinel); `Qty` from `ComponentQty` — the printed `Qty` cell, or `1` when it is
  blank **or a literal `0`**; `LineSequence = "{parent}.{child:D2}"`.
  The configurator prints `0` on an unselected option slot (`HDD Bay NVMe SSD` /
  `No HDD Bay NVME SSD` / `0`), which means exactly what the far more common unselected row
  that prints no quantity at all means — and those already default to `1`. Passing the `0`
  through would put a quantity-`0` line in col F of the CRM workbook, which is not a thing a
  quote line can be; children are zero-cost presentation rows, and the ordered quantity is
  the parent's.
- `SolutionId`, `Comments`, `Msrp`, `Term`, dates, `MinQty`, `SerialNumber` are always
  null — none of these concepts exist in this format.

Bid metadata: the header renders as `Bid Request No.BRPAS019100001 V1` (PdfPig keeps
`No.` and the bid number as separate tokens, unlike LBP-I ISG's `Quote No.:` field, so no
merged-token fallback is needed). `QuoteNumber` is the bid number with the version token
appended (`BRPAS019100001` + `V1` → `BRPAS019100001V1`), falling back to the filename
stem; extraction is best-effort and never throws for a missing match. Currency is always
AUD (there is no `Currency:` header field in this format — the Grand Total row's leading
token is the only source, and it is what the currency guard validates).

## Validation and expected fixtures

`ParseValidation.Validate(items, quotedTotal)` where `quotedTotal` is the Grand Total row's
`Total Excl` figure; children are zero-cost so the computed total is Σ(parent cost × qty).

| Sample | Shape | Parents | Children | Items | Quoted and computed total |
|---|---|---:|---:|---:|---:|
| `BRPAS019100001V1.pdf` | Laptops, 13 lines, 1 configured (67 components) | 13 | 67 | 80 | 1,455,094.20 |
| `BRPAS019100002V1.pdf` | Desktop + monitors, 3 lines, 1 configured (143 components incl. 6 `SPEC`) | 3 | 143 | 146 | 41,060.30 |
| `BRPAS019100003V1.pdf` | Workstation, 1 line, configured (115 components); **no `MTM` recap** | 1 | 115 | 116 | 90,796.00 |

`BRPAS019100001V1` parent 1 (`21RSCTO1WW`, "Notebook ThinkPad P16v Gen 3 21RSCTO1WW") carries
all 67 children; the remaining 12 parents have none. `BRPAS019100002V1` parent 1
(`30HTS03W00`) carries all 143 children, including the 6 label-less `SPEC` rows.
`BRPAS019100003V1` is the single-line no-`MTM` case: its one parent (`30HJCTO1WW`,
"Workstation TS P8_PROM21_ES_TW_R") carries all 115 children, 7 of which print a literal
`Qty` of `0` and are emitted at `1`.

## CRM output

Identical field mapping to LBP-I ISG's No Calculation/Uplift output — the shared
ANZ-GENERIC writer places `LineSequence` in column A as text, `Vpn` in D, `Description` in
E, `Qty` in F, `Cost` in I, child cost written as the `0.0001` sentinel. Col B (Vendor
Name) reads `LENOVO` via `OutputVendorName`, identical to both ISG formats. No Solution ID
split option is offered (`SupportsSolutionIdSplit = false`).

# Lenovo LBP-I ISG Quote (PDF) — extraction spec

- **Parser class:** `BidParser.Parsing.Lenovo.LbpiIsgPdf.LenovoLbpiIsgPdfParser`
- **Slug:** `lenovo_lbpi_isg_pdf` (`ParserSlugs.LenovoLbpiIsgPdf`)
- **Vendor:** Lenovo (`Vendors.Lenovo`)
- **Accepted MIME:** `application/pdf`
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Report type:** `Standard`
- **Split by Solution ID:** supported (`SupportsSolutionIdSplit = true`)

## File format and anchors

LBP-I ISG quotes are PDFs under the `Lenovo Global Technology (Australia - New Zealand)`
letterhead, quote numbers `BRDAS…`. A key/value header block (`Quote No.:`,
`Total Price:`, `Currency:`, dates) is followed by two grids:

- **`PRODUCT AND SERVICE DETAILS`** — 6 columns:
  `Line Item | Part Number | Description | Qty | Unit price excl. GST (AUD) | Total price excl. GST (AUD)`.
- **`CONFIGURATION DETAILS`** (absent on parts-only quotes) — 4 columns:
  `No. | Components | Description | Qty`. **`No.` equals the product grid's `Line Item`
  number** — that is the join key; never join by part number (the same VPN recurs under
  different line numbers with different component sets).

Anchor-based throughout: the section headings and header-row words are located by token
sequence, and column boundaries are derived from the header words' X coordinates — offset
from the header edges, because every column centres its values and the wider ones overhang
their header text (see **PDF quirks** below). The
product grid ends at `CONFIGURATION DETAILS`, or at the `Please transmit this quote`
paragraph on parts-only quotes; the config grid ends at `Please transmit this quote`.

`Detect()` returns 0.9 when both the `PRODUCT AND SERVICE DETAILS` sequence and
`Lenovo Global Technology` are present — the signature that powers the Lenovo **Auto**
entry alongside the LBP-E XLS parser's header-row signature.

## PDF quirks this parser handles

- **Whitespace words.** This producer makes PdfPig emit whitespace-only tokens between
  words; they are dropped at collection (they break token-sequence matching and their
  zero-height boxes skew row-height statistics).
- **Vertically centred wrapped descriptions.** A wrapped description puts fragments
  *above and below* its anchor row; `RowBlocks` (the Zebra pattern) assigns each
  continuation row to its anchor, and fragments join in reading order.
- **Page-break repeats.** In long documents the `Grand Total` row and the column-header
  rows repeat at every page break *mid-grid*; both are recognised and skipped as noise.
  A description can even continue on the next page after those repeats. `RowBlocks`'
  cross-page rule only reaches the row immediately adjacent to an anchor, so the parser
  adds two pitch-based repair passes (`AssignDescriptionOwners`) that re-chain the second
  and later wrapped lines of a block split by a page break onto the block whose lines
  they visibly continue — in both grids, without touching the shared helper.
- **Centred columns, values wider than their headers.** Both grids centre the header
  text *and* the values in each column, so a value wider than its header spills evenly
  past both header edges — the column boundaries cannot be read off the header words
  verbatim. In the product grid a 10-character VPN overhangs `Part Number` by ~5pt each
  side and overruns the `Number` header's right edge on five of the seven samples, so
  **`Part Number` and `Description` are read as one column** and split by token
  afterwards (`SplitPartNumberColumn`), keyed off the row's `Line Item` cell: a numbered
  line and a Solution ID row lead with a part number, wrapped fragments and page-break
  header repeats do not. The `Line Item`/`Part Number` boundary is set a full
  `PartNumberOverhang` left of the `Part` header so a longer VPN still lands whole,
  which leaves a three-digit line number ~5pt of clearance.
- **A drifting `Components` header.** Lenovo centres the config grid's `Components`
  header over a column whose width follows the `Description` content, so the header word
  sits anywhere from 5pt (`BRDAS019000003V1`) to 11pt (`BRDAS019000007V1`) right of its
  own values — further than the gap to the `No.` column, which leaves no X boundary
  between the two that holds across quotes. `No.` and `Components` are therefore read as
  a **single merged cell** and split by token (`SplitLeadingCell`): a leading integer
  names the section only when a second token follows it, because a component code can
  itself be all digits (`5977`, `6400`, `6201`). A long VPN on a section row can be cut
  by the `Description` boundary (`"1 7DGDCTO1"`), which costs nothing — the leading
  integer survives and section rows are never emitted.
- **Hanging lone hyphens.** A wrapped line ending `… DM3010H - Complete` reports the
  hyphen's bounding box low enough to fall into the *next* visual row's midline band.
  A pre-pass snaps any short-glyph word back onto the line of its horizontally adjacent
  full-height neighbour, preserving reading order. Price-placeholder hyphens (`-` in the
  price columns) have no adjacent neighbour and are untouched.

## Row classification (product grid)

| Row kind | Signature | Action |
|---|---|---|
| Grand Total | Cells contain `Grand Total` | Capture quoted total from `AUD Ex GST-> <n>` (repeats are identical; last wins); skip |
| Header repeat | `Line Item` / `Part Number` header text | Skip |
| Solution ID row | Blank Line Item, Part Number matches `^SID[A-Za-z0-9]+$` | Open a solution; its Total-price cell is the solution total |
| Numbered line | Integer Line Item | A product line of the current solution (or a flat line when no SID rows exist) |
| Continuation | Description text only | Wrapped description fragment, attached via `RowBlocks` |

Missing section anchor or unresolvable product-grid header → `ParseError("detect", …)`
(wrong file-type flow). A `Currency:` header value other than AUD →
`ParseError("currency", …)`. A missing Grand Total row → `ParseError("totals", …)`.

Once the document is recognised, extraction **fails closed** with `ParseError("extract", …)`
rather than continuing on partial structure: a product grid with no terminator (neither
`CONFIGURATION DETAILS` nor the transmittal paragraph — reading on would group T&C clauses
as rows); a `CONFIGURATION DETAILS` heading whose `No. | Components | Description | Qty`
header cannot be resolved or whose grid has no transmittal terminator; a resolved config
grid that yields **no sections at all**, or **any section with no components** (children
are zero-cost, so an empty component map lets the quote reconcile and ship with every
component missing — the silent loss `BRDAS019000007V1` hit in production); a configuration
section keyed to a line number no product line carries (a join or line-number extraction
failure); a Solution ID row without a total price or without any numbered lines; and a
solution parent line without a positive quantity (a blank quantity parses as 0 and reaches
this staged guard instead of an unstaged exception).

## Parent and child rules

**One parent per Solution ID.** Within a solution, the first numbered line is the parent;
every other numbered line and every configuration component is a child:

- Parent: `Cost = round(solutionTotal / parentQty, 2, AwayFromZero)` — the SID row's own
  qty is usually 1, so the division is what produces the per-unit cost (e.g.
  356,882.96 ÷ 8 = 44,610.37). `Comments = "Solution ID: <SID>"`; `LineSequence = "{n}"`.
- Children: `Cost = 0m` in memory (`CrmWriter` emits the `0.0001` sentinel),
  `LineSequence = "{parent}.{child:D2}"`, `Comments = null`.
- Order: the parent's config components first, then each later numbered line (keeping its
  **grid quantity as printed** — absolute, not per-unit) followed by its own config
  components. Config quantities are taken as printed (`Months  60` carries the term).
- `SolutionId` is set on the parent and **every** child — required for the split pipeline.
- Numbered lines without a config section ("only exist in PRODUCT AND SERVICE DETAILS",
  e.g. installation/warranty services) are still emitted as children.

**Parts-only quotes** (no SID rows anywhere): every numbered line is a parent carrying its
own unit price; `SolutionId`/`Comments` stay null, so a split request produces one
`NoSolutionID` workbook.

Metadata: `QuoteNumber` is the `Quote No.:` value with the version token appended
(`BRDAS019000003` + `V1` → `BRDAS019000003V1`), falling back to the filename stem.
Currency is always AUD.

Every item's `Raw` carries its source cells — identifiers, quantity, prices where present,
and the reconstructed source `Description` — so a wrapped-description or column-boundary
defect can always be compared against the cleaned `LineItem` fields.

## Validation and expected fixtures

`ParseValidation.Validate(items, quotedTotal)` where `quotedTotal` is the `AUD Ex GST->`
figure; children are zero-cost so the computed total is Σ(parent cost × qty). If a future
quote's solution total does not divide evenly by the parent quantity, the parent cost is
rounded to two decimals, so the recomposed total can drift from the quoted total by up to
half a cent per unit; drift within the canonical `0.01` validation tolerance is accepted
as a match (e.g. a total of 100.00 over qty 3 computes 99.99), and only larger drift
surfaces through the validation-mismatch flow.

| Sample | Shape | Parents | Children | Items | Quoted and computed total |
|---|---|---:|---:|---:|---:|
| `BRDAS019000004V1.pdf` | Parts-only | 3 | 0 | 3 | 77,545.95 |
| `BRDAS019000005V1.pdf` | Parts-only | 1 | 0 | 1 | 38,896.08 |
| `BRDAS019000001V1.pdf` | 1 solution, SID row qty 20 | 1 | 65 | 66 | 640,046.00 |
| `BRDAS019000003V1.pdf` | 2 solutions | 2 | 148 | 150 | 393,231.78 |
| `BRDAS019000002V1.pdf` | 5 solutions, 13 pages, mid-grid repeats | 5 | 401 | 406 | 3,787,260.72 |
| `BRDAS019000007V1.pdf` | 1 solution, drifted `Components` header (11pt) | 1 | 83 | 84 | 95,588.19 |
| `BRDAS019000006V1.pdf` | 2 solutions, drifted `Components` header (8pt) | 2 | 94 | 96 | 154,445.77 |

`BRDAS019000007V1` parent: `7DGDCTO1WW`, qty 3, cost 31,862.73 (= 95,588.19 ÷ 3,
`SIDX02YUF5`); its 83 children run 66 config components deep before the grid's second
numbered line. `BRDAS019000006V1` parents: `7DG9CTO1WW` qty 2 at 46,888.53
(`SIDX02YUEQ`, 70 children) and `7DCACTO1WW` qty 1 at 60,668.71 (`SIDX02YUER`, 24).
`BRDAS019000001V1` parent: `7DGDCTO1WW`, qty 20, cost 32,002.30 (= 640,046.00 ÷ 20).
`BRDAS019000002V1` parents: 4 × `7DCVCTO1WW` at 888,519.66 (SIDX02Q2PI/PG/PH/PK) plus
`7DCSCTO1WW` qty 2 at 116,591.04 (= 233,182.08 ÷ 2, SIDX02Q2PJ).

## CRM output

Identical to LBP-E ISG: the shared ANZ-GENERIC writer places `LineSequence` in column A as
text, `Vpn` in D, `Description` in E, `Qty` in F, `Cost` in I, and `Comments` in R. Parent
costs are written as parsed; every child cost becomes `0.0001`. Uplift additionally
populates margin column K. MSRP, term, dates, serial number, and minimum quantity remain
blank. Splitting by Solution ID behaves exactly as documented in
`docs/lenovo_lbpe_isg_xls.md` (grouping, renumbering, ZIP delivery, `NoSolutionID` token).

## Bid metadata

`Quote No.: <number> V<revision>` supplies the two bid fields separately while legacy `QuoteNumber` remains concatenated (`BRDAS019000001V1`). The `V<revision>` group is **optional** — it predates bid metadata, so files without one occur; such a file keeps its bid number and defaults to revision `1`.

# Zebra PCR (Price Concession) (PDF + XLS) — Extraction Spec

Parser slugs: `zebra_pcr_pdf`, `zebra_pcr_xls`
Vendor: `Zebra`
Accepted MIME: `application/pdf` (PDF), `application/vnd.ms-excel` (XLS)
Default CRM template: `No Calculation`
Available CRM templates: `No Calculation`, `Uplift`
Report type: `HardwareSoh`

Two parsers, one format. Both normalise their source into a shared `ItemRow`
list and delegate to `ZebraPriceConcessionExtractor.Build` for identical
`ParseResult` output. XLS and PDF for the same PCR produce identical field values.

---

## Source file format

Zebra PartnerConnect **Price Concession** (PCR) letters, distributed via the
Zebra partner portal as a PDF letter and/or an `.xls` export.

| File | PCR # | Items | Notes |
|---|---|---|---|
| `Zebra_PC_97000001_V2.0.pdf` / `.xls` | 97000001 | 3 | All active; AUD |
| `Zebra_PC_97000002_V2.0.pdf` / `.xls` | 97000002 | 5 | Media/wristband consumables; all active |
| `Zebra_PC_97000003_V1.0.pdf` / `.xls` | 97000003 | 8 | Two-page PDF with a page-break description split |
| `Zebra_PC_97000004_V1.0.pdf` | 97000004 | 13 | Fused-header PDF; top-aligned description block and boundary-straddling words |

> **The `.xls` files are HTML, not OLE.** Zebra's portal exports styled HTML
> tables under a `.xls` filename — they cannot be read by ExcelDataReader or
> ClosedXML. The XLS parser uses **HtmlAgilityPack**. `ParseService`'s magic-byte
> check was relaxed to accept both OLE-compound-doc and HTML signatures for
> `application/vnd.ms-excel`; real-OLE `.xls` files (Lenovo LBP-E ISG and Cisco CCW) still pass
> the OLE check unchanged.

### Structure

A details block (Account, Reseller, **Currency**, dates, PCR ID) followed by a
**Price Concession Items** table with 10 columns:

```
Part No. | Description | Min. First Order Only | Min. Qty | Max. Qty
         | List Price | Standard Discount % | Total Discount % | Unit Special Price | Cancelled
```

Only six of these are extracted: `Part No.` → vpn, `Min. Qty` → qty **and**
min_qty, `Max. Qty` → comments (`"Max Qty: {n}"`), `List Price` → msrp,
`Unit Special Price` → cost, `Cancelled` → the cancelled flag. `Description` is captured; the discount-% columns and
`Min. First Order Only` are boundary-only (used to bucket columns, not read).

---

## Extraction algorithm

### Detection & Auto file type

Zebra offers an **"Auto (detect format)"** dropdown option (`ParserSlugs.ZebraAuto`, `zebra_auto`), preselected by default when selecting the Zebra vendor. Auto detects whether the uploaded quote is PDF or XLS and resolves to the matching parser (`MinConfidence = 0.7`).

- **PDF signature** (`ZebraPriceConcessionPdfParser.Detect`): collects PdfPig words; returns `0.85` if `Price Concession Items` sequence + currency code (`AUD`/`USD`) are found; `0.75` if only `Price Concession Items` sequence is found; `0.0` otherwise.
- **XLS signature** (`ZebraPriceConcessionXlsParser.Detect`): parses HTML tables; returns `0.85` if ≥2 `<table>` elements are present, an items header row contains both `Part No.` and `Unit Special Price`, and `Price Concession` text is present; `0.75` if missing the title text; `0.0` for real OLE `.xls` files or invalid HTML.

### UI defaults

Selecting vendor **Zebra** applies the administrator-managed `onCostPct` default (seeded as `2.85`). The field remains user-editable per parse and is reset when switching vendors. Nothing is persisted on the user row.

### PDF parse

1. **Anchor** — `FindSequence(words, ["Price", "Concession", "Items"])`; missing → `ParseError("detect", …)`.
2. **Header row** — `FindPartNoHeader`: the word `"Part"` followed within 8 words by `"No."` on the same page at a similar Y (±4 pt), `No.` to the right.
3. **Currency** — scan the details block above the header for the `"Currency"` label, then the code (`AUD`/`USD`/`EUR`/`GBP`/`NZD`) within a few words; default `"AUD"`.
4. **Quote number** — a word matching `^#:(\d+)` (from `PC Request ID #:97000001…`); fallback `QuoteNumberFromFilename`.
5. **Columns** — `BuildColumns` resolves the ten column left edges from the document itself; no sample-derived X fallback exists. Candidate edges come from three independent sources, merged within 2 pt: the header cells (a fused first line such as `Min.Max.List` still leaves the wrapped second line — `Qty`, `Qty`, `Price` — at its own column's X), every `Y`/`N` flag word (unambiguous, so one occurrence counts), and numeric body words whose left edge repeats on at least half the item rows (repetition is required because a description can contain a number; a one-item table has none, which is why the other two sources exist). Edges are then tied to columns: readable header anchors first, then by content — the table opens and closes on a `Y`/`N` column and `Unit Special Price` is the column immediately left of the closing one — and the columns still unassigned are filled one gap between pins at a time, so a column that contributes no edge (`List Price` and `Standard Discount %` fuse into one word in some documents) cannot shift the columns after it. A grid that is incomplete for any column the extractor reads (`Min. Qty`, `Max. Qty`, `List Price`, `Unit Special Price`, `Cancelled`), or that is not left-to-right, raises a `detect` error rather than parsing empty cells as zero prices.
6. **Rows** — `RowsBetween(words, header.Top, header.PageIndex, columns, stopToken: "Concession:")` (`"This Price Concession:"` follows the table).
7. **Merge** — `MergeRows` folds description continuation lines into one row per item (see quirks).

### XLS parse

1. Read the file as text; `HtmlDocument.LoadHtml`; select `//table` (need ≥ 2).
2. **Table 0** = details block → currency (`Currency` row, cell[2]).
3. **Table 1** = items. Find the header row (a `<tr>` containing both `Part No.` and `Unit Special Price`), map its columns by header text (`Contains`, case-insensitive), then read every subsequent non-empty row. Missing required column (`Part No.`, `Description`, `Max. Qty`, `Unit Special Price`, `Cancelled`) → `ParseError("detect", …)`.
4. Quote number from filename (`QuoteNumberFromFilename`); no in-document PCR scan.

### Shared extractor (`ZebraPriceConcessionExtractor.Build`)

- `LineSequence` = running 1-based index (`"1"`, `"2"`, …).
- **Active rows**: cost from `Unit Special Price`, qty **and** min_qty from `Min. Qty` (qty defaults to 1 when blank; min_qty stays nullable), msrp from `List Price` (nullable), comments = `"Max Qty: {Max. Qty}"` (null when `Max. Qty` is blank). `Max. Qty` is *not* the line quantity — CRM prices against the minimum order quantity, and the deal ceiling is carried as the comment. Prices use `ParseFirstDecimal` (regex `\d[\d,]*\.\d{2}`) to survive PdfPig number fusion.
- **Cancelled rows** (`Cancelled` == `"Y"`, case-insensitive): `Cost = 0`, `Msrp = 0`, `Qty = 1`, `MinQty = null`, `IsCancelled = true`, `Comments = "Cancelled (Standard Price)"`. The writer emits `TemplateLayout.NoBidPrice` — a **literal 0**, never the zero-price sentinel — for cost/MSRP. The line belongs on the quote but is not bid-priced, so we *want* CRM to reject the 0 and pull the line's standard price from SAP. This is the one format that uses the no-bid zero; see "Zero pricing: the two zeros" in `docs/output_mapping.md`.
- **No quoted total** — construct `ValidationResult` directly with `Matches = true`, `Difference = 0`, `QuotedTotal = null` (never call `ParseValidation.Validate(items, null)`, which would trip the frontend mismatch modal).

---

## PdfPig quirks

The shared PdfPig compatibility behaviour is documented in
[`troubleshooting.md`](troubleshooting.md#parsing). The PDF path guards against these
NearestNeighbour-extractor artifacts:

- **Single-space words** — styled table cells can emit whitespace-only words; blank words are filtered before sequence-search and column bucketing.
- **Fused List Price / Standard Discount %** — e.g. `"1,830.24"` + `"71.43"` render as `"1,830.2471.43"`. `ParseFirstDecimal`'s first-match regex recovers the list price.
- **Fused Unit Special Price / Cancelled** — e.g. `"145.00"` + `"N"` render as `"145.00N"`. `SplitFusedCancelled` peels a trailing `Y`/`N` back into the Cancelled field when Cancelled is otherwise empty.
- **Wrapped descriptions** — the `Part No.` row can be centred, top-aligned, or bottom-aligned in its description block. `MergeRows` delegates to the shared `RowBlocks`, which on a single page cuts between anchors at the largest consecutive row-midline gap, then re-joins fragments in reading order.
- **Page-break description split** — a description fragment can sit at the bottom of page N with its `Part No.` row on page N+1. Across a page break `RowBlocks` treats only the immediately adjacent row as a candidate, scored as one line-height; ties favour the previous item so a genuine trailing line that happens to end a page stays put.
- **Boundary-straddling words** — PdfPig can merge adjacent cells into a word (`PWR-BGA12V108W0WW` + the description's next word `It`). `PdfWordCollector` retains per-letter bounds and `RowsBetween` splits only where a resolved column boundary falls in a letter gap; a global letter-gap threshold cannot work, because in `1,830.2471.43` the gap at the fusion (2.34 pt) is smaller than the gap around the word's own decimal point (2.43 pt).
- **Short-glyph words drop out of their line** — PdfPig's boxes are glyph-tight, so a word with neither ascender nor descender (`or a`, `up`, `power`) is ~2 pt shorter and hangs ~3.5 pt below its neighbours. Current Zebra fixtures do not trigger the collapsed-page compatibility path, so `RowsBetween` groups them by the tight-box **midpoint**, not the top edge, and retains the short-glyph repair. A top-edge tolerance split such words into their own row, which then re-joined in X order and scrambled the description. The tolerance is squeezed from the other side too — these tables run a line pitch as tight as **6.35 pt**.

---

## Golden expected output (characterisation)

`Zebra_PC_97000004_V1.0_NoCalculation.xlsx` is the workbook golden for the fused-header PDF.
The existing PDF/XLS fixtures continue to be asserted on parser field values.

### `Zebra_PC_97000001` (3 items, all active, AUD)

| Seq | VPN | Qty | Min Qty | Cost | MSRP |
|---|---|---|---|---|---|
| 1 | DS8178-HCBU210MS5W | 400 | 1 | 475.86 | 1,830.24 |
| 2 | ZQ61-HAXAA04-00 | 400 | — | 719.37 | 1,798.42 |
| 3 | Z1AE-ZQ6H-3C0 | 400 | — | 266.13 | 466.89 |

`Validation.Matches = true`, `QuotedTotal = null`.

### `Zebra_PC_97000003` (8 items)

`ZD4AH22-D0PE00EZ` is the page-break item: description merges the page-1 leading
fragment (`Direct Thermal Printer ZD411 … Bundle`) with the page-2 `Part No.`
row; `Cost = 417.99`, `Qty = 200`, `Msrp = 949.97`.

---

## Output column mapping

Both slugs write via `CrmWriter` (ANZ-GENERIC 27-column layout, AUD — no FX
conversion). Full column table and the cancelled-row / `On Cost %` (col Z,
user-supplied `onCostPct`) rules are in **`docs/output_mapping.md`** (Zebra
section) — do not re-derive cell positions here.

Call: `CrmWriter.Write(items, outputPath, crmTemplate, new CrmWriterOptions(VendorName: "ZEBRA", Margin: margin, OnCost: onCost))` — `ParseService` uppercases the vendor, so column B is `ZEBRA`.

## Bid metadata

Document prose `PC Request ID #:<number>,Revision #:<revision>` supplies both bid fields (shared by the PDF and XLS parsers via `ZebraPriceConcessionExtractor`); decimal revisions are preserved (`2.0` stays `2.0`). Best-effort — an unmatched anchor leaves both fields null rather than failing the parse.

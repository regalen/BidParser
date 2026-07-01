# Lenovo BRDA DCG (PDF) — Extraction Spec

Parser slug: `lenovo_brda_dcg_pdf`  
Vendor: `Lenovo`  
Accepted MIME: `application/pdf`  
Default CRM template: `No Calculation`  
Available CRM templates: `No Calculation`, `Uplift`

---

## Source file format

Lenovo BRDA (Business Ready Deal Architecture) DCG (Data Centre Group) quote PDFs, distributed via the Lenovo partner portal.

| File | Quote Number | Items | Notes |
|---|---|---|---|
| `BRDAS010260417V1.pdf` | BRDAS010260417V1 | 152 | 2 configs, 13 parents, 137 children (parent-VPN self-components deduped) |
| `BRDAS010545504V1.pdf` | BRDAS010545504V1 | 3 | Simple variant — no CONFIGURATION DETAILS section; all rows are top-level parents with real unit prices; AUD 77,545.95 |
| `BRDAS010546096V1.pdf` | BRDAS010546096V1 | 1 | Simple variant — single parent item; AUD 38,896.08 |

### Structure

Multi-page PDF. Two variants exist:

**Complex variant** (with CONFIGURATION DETAILS): Two main table sections separated by visual headers.

1. **PRODUCT AND SERVICE DETAILS** — section 1 header. Contains the top-level line items (CONFIG blocks, each with PARENT products listed beneath). CONFIG rows carry the real unit price; PARENT rows have `"-"` for unit price.
2. **CONFIGURATION DETAILS** — section 2 header. Contains the per-component breakdown of each CONFIG/PARENT, grouped by a sequential line number.

**Simple variant** (without CONFIGURATION DETAILS): Only the PRODUCT AND SERVICE DETAILS section is present. All numbered rows are top-level parent items, each with a real unit price (no `"-"`). No CONFIG rows. Section 2 is absent and the parser returns an empty children map.

### Section 1 — PRODUCT AND SERVICE DETAILS

Table columns (anchored on header words, not fixed positions):

| Column | Header words | Content |
|---|---|---|
| Line Item | `Line` + `Item` (same y-band) | Blank for CONFIG rows; integer for PARENT rows |
| Part Number | `Part` + `Number` | VPN / config ID |
| Description | Text between "Part Number" and "Qty" columns | Product description (may wrap) |
| Qty | `Qty` | Quantity |
| Unit Price | `Unit` (above `Line`) | Price per unit; `"-"` for PARENT rows |
| Total Price | `Total` | — |

**Row types:**
- **CONFIG** — `Line Item` is blank, `Part Number` is non-empty, and `Unit Price` is a parseable number. Represents the whole config block with its aggregate price. Only present in the complex variant.
- **PARENT** — `Line Item` is a positive integer. In the complex variant, `Unit Price` is `"-"` (cost is zero — price is carried by the CONFIG). In the simple variant, `Unit Price` is a real number.
- **Continuation** — Both `Line Item` and `Part Number` are blank, `Description` is non-empty. Appended to the current item's description.
- **Pre-description** — A description-only row that appears *before* its PARENT in y-order (the PDF renders the description cluster ~7 pt above the VPN line). Buffered while the current item is a CONFIG and flushed when the next PARENT anchor is found.

**Column boundary notes:** Several header words in this PDF have X0 values slightly to the right of the corresponding data words. Use word X1+1 (right-edge + 1 pt) as the left boundary for downstream columns rather than the next header word's X0:
- Part Number column left = `"Item".X1 + 1` (not `"Part".X0`)
- Description column left = `"Number".X1 + 1` (not `"Description".X0`)

### Section 2 — CONFIGURATION DETAILS

Table columns:

| Column | Header | Content |
|---|---|---|
| No | `No.` | Group number matching a PARENT's Line Item in section 1 |
| Components | `Components` | Component VPN |
| Description | Between `Components` and `Qty` | Component description |
| Qty | `Qty` | Per-unit quantity |

**Column boundary notes (floating-point precision):**
- The `No` column left boundary is set to `"No.".X0 - 1.0` to catch group-number words whose PDF x-coordinate is fractionally smaller than the header's.
- The `Components` column left boundary is `"No.".X1 + 1`.
- The `Description` column left boundary is `"Components".X1 + 1` (NOT `"Description".X0`).
- The `Qty` column left boundary is `"Qty".X0 - 1.0` for the same floating-point tolerance reason.

**Group header row:** In this PDF, the group number and the first component VPN appear on the same visual row (y-difference ≤ 3.5 pt). The parser handles both simultaneously: set `currentParentLineNo` from the group number cell, and start the first `CurrentChildItem` from the components cell.

**Repeated headers:** The section 2 column header row is repeated at the top of each PDF page. These are skipped when `componentsCell == "Components"` or `noCell == "No"` / `"No."`.

---

## Extraction algorithm

### Detect

1. Extract all words with `PdfWordCollector.CollectWords(path)`.
2. Search for `["PRODUCT", "AND", "SERVICE", "DETAILS"]` on the same y-band (y-tolerance 5 pt).
3. Search for `["CONFIGURATION", "DETAILS"]` on the same y-band.
4. Return `0.85` if both anchors are found (complex variant), `0.75` if only section 1 is found (simple variant), `0.0` otherwise.

### Parse — Section 1

1. `FindSectionAnchor(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"])` → `anchorIdx`.
2. `FindSection1Header(words, anchorIdx)` — scan forward for word `"Line"` with `"Item"` nearby (same page, ≤ 4 pt, to the right). Returns the `"Line"` word.
3. `BuildSection1Columns(words, headerWord)` — scan the header band (±10 pt above / +30 pt below `"Line".Top`) for `"Item"`, `"Number"`, `"Qty"`, `"Unit"`, `"Total"`. Build `ColumnRanges` using X1+1-based boundaries for Part Number and Description.
4. `PdfTableHelpers.RowsBetween(words, headerWord.Top, headerWord.PageIndex, columns, stopToken: "Grand")`.
5. Iterate rows; classify each into CONFIG, PARENT, or Continuation (with pre-description buffering — see above).
6. `ExtractQuotedTotal` — scan from `anchorIdx` for `"Ex"` then a word starting `"GST"` then the next parseable decimal amount.

### Parse — Section 2

If the CONFIGURATION DETAILS anchor is not found (simple variant), immediately return an empty `children` map and skip all steps below.

1. `FindSectionAnchor(words, ["CONFIGURATION", "DETAILS"])` → `anchorIdx` (null → return `[]`).
2. `FindSection2Header(words, anchorIdx.Value)` — scan forward for word `"No"` / `"No."` with `"Components"` nearby.
3. `BuildSection2Columns(words, headerWord)` — X1+1 boundaries for Components and Description; subtract 1.0 from No and Qty left boundaries for floating-point tolerance.
4. `PdfTableHelpers.RowsBetween(words, headerWord.Top, headerWord.PageIndex, columns, stopToken: "Please")`.
5. Iterate rows; track `currentParentLineNo` on group-header rows; accumulate `CurrentChildItem` instances.

### Quote Number

Scan for `"Quote"` followed by a word starting `"No"` on the same y-band (≤ 5 pt). Collect subsequent non-blank tokens until a horizontal gap > 50 pt is found (which separates the left-column value from the right-column metadata keys). Concatenate the parts (handles `"BRDAS010260417"` + `"V1"` → `"BRDAS010260417V1"`). Fallback: `Path.GetFileNameWithoutExtension(path)`.

### Assembly

1. Iterate section 1 entries in order, assigning every emitted line the next number in a single running sequence (`1`, `2`, `3`, …).
2. CONFIG entries → top-level item; description = config's own VPN.
3. PARENT entries → top-level item; look up `children[entry.LineNo]` from the section 2 results and emit each child as the next number in the same running sequence (children are no longer sub-numbered as `parent.NN`).

---

## Quote number

`BRDAS010260417V1` extracted from the `Quote No.:` field in the document header.

---

## Golden expected output

### `BRDAS010260417V1.pdf` (2 configs, 152 items total)

In Lenovo BRDA DCG section 2, the first component of every PARENT is the parent's
own VPN — a redundant self-component row. The parser drops any child whose VPN
matches its parent's VPN, so the assembled output below does NOT include those
duplicates.

**Config 1: SIDX02Q2PL** (AUD 356,882.96 × 1)

| Seq | VPN | Qty | Cost | Description |
|---|---|---|---|---|
| 1 | SIDX02Q2PL | 1 | 356,882.96 | SIDX02Q2PL |
| 2 | 7DG9CTO1WW | 8 | — | ThinkSystem SR630 V4-3yr Base Warranty-Compute Server |
| 2.01–2.56 | … | … | — | 56 components (self-component 7DG9CTO1WW deduped) |
| 3 | 5641PX3 | 8 | — | XClarity Pro, Per Endpoint w/3 Yr SW S&S |
| 3.01–3.02 | … | … | — | 2 components (self-component 5641PX3 deduped) |
| 4–7 | … | … | — | XClarity Controller, NBD Resp, Installation, Keep Your Drive |

**Config 2: SIDX02Q2PM** (AUD 36,348.82 × 1)

| Seq | VPN | Qty | Cost |
|---|---|---|---|
| 8 | SIDX02Q2PM | 1 | 36,348.82 |
| 9 | 7DG9CTO1WW | 2 | — |
| 9.01–9.56 | … | … | — |
| 10–15 | … | … | — |

Quoted total: `AUD 393,231.78`. Computed total: `AUD 393,231.78`. Validation matches: `true`.

---

## Output column mapping

Lenovo BRDA DCG uses `AnzGenericWriter` (ANZ-GENERIC 27-column layout). AUD — no FX conversion. `No Calculation` template only (no Uplift — the Lenovo price is already AUD reseller cost).

| Col | Header | Value |
|---|---|---|
| A | Item | LineSequence |
| B | Vendor Name | `"Lenovo"` |
| D | Vendor Part Number | vpn |
| E | Description | description |
| F | Qty. | qty |
| H | MSRP | blank |
| I | Cost | cost (or `0.0001` sentinel for zero-cost rows) |
| W | Min Order Qty | blank |

Call: `AnzGenericWriter.Write(items, outputPath, "No Calculation", includeMargin: false, margin: 0m, vendorName: "Lenovo")`

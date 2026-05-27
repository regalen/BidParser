# Plan — Lenovo "BRDA DCG (PDF)" parser

## Context

We are adding **Lenovo** as the third supplier vendor (after Nutanix and HP) and its first
file type, **BRDA DCG (PDF)**. The sample is
`/home/adem/Downloads/BRDAS010260417V1_COENGINEER PTY LTD_INGRAM MICRO AUSTRALIA  P_L_2026 04 17.pdf`
and the user's hand-built target output is `/home/adem/Downloads/Book1.xlsx`.

These quotes have **two cross-referenced sections** that must be related to each other:

1. **PRODUCT AND SERVICE DETAILS** — a table whose bold `[Part Number]` rows are *configuration
   IDs* (e.g. `SIDX02Q2PL`) that carry the real price, followed by numbered `[Line Item]` rows
   (1, 2, 3 …) that are the parent products of that config (priced `-` = $0.00).
2. **CONFIGURATION DETAILS** — child components grouped under a `[No.]` reference that matches the
   parent's `[Line Item]` number. Children carry no price ($0.00).

The output is a flat list with a hierarchical line sequence: configs/parents get a running integer
(`1, 2, 3 …`) and children get `parent.NN` (`2.01, 2.02 …`) — **exactly the HP `Bundle` /
`Bundle Detail` model already implemented**. Lenovo is **AUD**, so it reuses `AnzGenericWriter` and
the `0.000001` zero-cost sentinel, just like HP.

Intended outcome: drop in one parser + fixtures + registry entry (plus three tiny wiring touchpoints
a *new vendor* requires), with the dashboard cascade and output writer working unchanged.

## Decisions (confirmed with user)

- **CRM templates**: `No Calculation` + `Uplift` — mirror HP. Reuse `AnzGenericWriter`; the parser
  overrides `AvailableTemplates` so the frontend renders the template dropdown + the no-FX
  (AUD) settings block automatically.
- **Validation**: parse the `Grand Total … AUD Ex GST-> 393,231.78` as `QuotedTotal` and validate
  `computed = Σ(cost × qty)` against it (0.01 tolerance) via the existing `ParseValidation.Validate`.
  The two CONFIG rows (356,882.96 + 36,348.82) sum to exactly the Ex-GST total; parents/children
  are $0, so they don't disturb the sum. A mismatch raises the normal blocking modal.
- **Parent VPN + description source**: **PRODUCT AND SERVICE DETAILS** (section 1) is authoritative.
  Wrapped descriptions are reconstructed by space-joining continuation rows (same technique as the
  Nutanix Hardware Only PDF parser). The golden fixture is regenerated from actual parser output and
  hand-checked; minor hyphen/spacing differences from `Book1.xlsx` are accepted.

## Extraction algorithm

All anchor-based — no fixed pages, rows, or column letters (per the project's mandatory rule).
Built on the existing PDF toolkit: `PdfWordCollector.CollectWords`, `PdfTableHelpers.FindSequence`,
`PdfTableHelpers.ColumnRanges`, `PdfTableHelpers.RowsBetween`, and the `CurrentItem` continuation-merge
pattern from `src/BidParser.Parsing/Nutanix/HardwareOnlyPdf/NutanixHardwareOnlyPdfParser.cs`.

### Section 1 — PRODUCT AND SERVICE DETAILS (parents + configs + total)

1. Anchor on the word sequence `PRODUCT AND SERVICE DETAILS`.
2. Locate the header row below it: `Line Item` | `Part Number` | `Description` | `Qty` |
   `Unit price excl. GST (AUD)` | `Total price excl. GST (AUD)`. The two price headers span two
   visual lines — derive each column's x0 from its leftmost header word (`Unit`/`Total`).
   Build x-ranges with `PdfTableHelpers.ColumnRanges`.
3. `RowsBetween(words, header.Top, header.PageIndex, columns, stopToken: "Grand")` — the
   `Grand Total` row terminates section 1. Because `RowsBetween` orders by page then top and
   truncates at the first `"Grand"`, the page-1 section is captured without bleeding into the
   section-2 pages.
4. **Quoted total**: scan for `GST->` preceded by `Ex` and take the following amount
   (`393,231.78`). `DecimalCleaner.Parse` strips the comma. (There are two `GST->` tokens — `Inc`
   and `Ex`; pick the `Ex` one.)

Row classification (using the `CurrentItem` anchor + continuation-merge pattern):
- `[Line Item]` is a positive integer → **PARENT** (`vpn = Part Number`, `description = Description`,
  `qty = Qty`, `cost = 0`; the `-` price maps to 0). Record its PDF line number.
- `[Line Item]` empty **and** `[Part Number]` non-empty **and** `[Unit price]` numeric → **CONFIG**
  (`vpn = description = Part Number` (the config id), `qty = Qty` (= 1), `cost = Unit price`).
- `[Line Item]` empty **and** `[Part Number]` empty **and** `[Description]` non-empty → **continuation**
  of the current parent (append to description; merge any cell — e.g. a `Qty` that wrapped onto the
  second visual line — into the current item's empty cells, exactly like the Hardware Only parser).
- Track CONFIG→parent grouping and emission order from section-1 order.

### Section 2 — CONFIGURATION DETAILS (children)

1. Anchor on `CONFIGURATION DETAILS`.
2. Locate the header row: `No.` | `Components` | `Description` | `Qty`. Build x-ranges. The header
   repeats on every page — rows where the `Components` cell equals `"Components"` (or `No.` cell ==
   `"No."`) are skipped.
3. `RowsBetween(words, header.Top, header.PageIndex, columns, stopToken: "Please")` — the
   `Please transmit this quote …` line on the last page terminates section 2 (the terms/conditions
   prose is excluded). Spans pages 1–5; multi-page collection is built into `RowsBetween`.

Row classification:
- `[No.]` is a positive integer → **group header** = parent restated. Set the current parent
  reference to that number. **Do not emit** (the parent already came from section 1). Numeric child
  codes like `5977`, `6400`, `1340`, `3444` sit in the `Components` column, not `No.`, so x-ranges
  keep them out of this branch.
- `[No.]` empty + `[Components]` non-empty → **CHILD** of the current parent
  (`vpn = Components`, `description = Description`, `qty = Qty`, `cost = 0`).
- `[No.]` empty + `[Components]` empty + `[Description]` non-empty → continuation of the current
  child's description (e.g. the 3-line `SBRS` Veeam description). Space-join.

Build `Dictionary<int parentLineNo, List<child>>`.

### Assembly / emission (matches `Book1.xlsx`)

Iterate section-1 entries in order, maintaining one running integer `globalSeq`:
- **CONFIG** → `globalSeq++`; emit with `LineSequence = globalSeq.ToString()`.
- **PARENT** → `globalSeq++`; emit with `LineSequence = globalSeq.ToString()`; then for each child of
  this parent's PDF line number, `childIdx++`, emit `LineSequence = $"{globalSeq}.{childIdx:D2}"`.

So config `SIDX02Q2PL` = `1`; its parents = `2…7`; `SIDX02Q2PM` = `8`; its parents = `9…15`; each
parent's children = `<parentSeq>.01, .02 …`. The child prefix is the parent's **output** sequence
number (e.g. PDF line 7 → output `9` → children `9.01 …`), as in `Book1.xlsx`.
(Note: `Book1.xlsx` has two manual artefacts — a `11.02` that should be `10.02`, and float display
drift like `2.0300000000000007`; the rule above is the intended numbering.)

Per `LineItem`: `Term = null`, `Msrp = null`, `MinQty = null` (no Min-Order-Qty concept → output
col W stays blank), `Raw` = source cells. Currency `AUD`. `QuoteNumber` from the `Quote No.:`
metadata anchor, falling back to the filename basename (HP-style).

## Files to change

**Domain constants** (add the new vendor + slug — never inline the literals):
- `src/BidParser.Domain/Constants/Vendors.cs` → add `public const string Lenovo = "Lenovo";`
- `src/BidParser.Domain/Constants/ParserSlugs.cs` → add
  `public const string LenovoBrdaDcgPdf = "lenovo_brda_dcg_pdf";`
- `CrmTemplates.cs` — no change (`NoCalculation`, `Uplift` already exist).

**New parser**:
- `src/BidParser.Parsing/Lenovo/BrdaDcgPdf/LenovoBrdaDcgPdfParser.cs` — implements `IParser`:
  `Slug = ParserSlugs.LenovoBrdaDcgPdf`, `DisplayName = "BRDA DCG (PDF)"`, `Vendor = Vendors.Lenovo`,
  `AcceptedMime = "application/pdf"`, `CrmTemplate = CrmTemplates.NoCalculation`,
  `AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift]`. Optional `Detect`
  returning high confidence when the doc contains `PRODUCT AND SERVICE DETAILS` +
  `CONFIGURATION DETAILS`.

**Registry**:
- `src/BidParser.Parsing/Registry/ParserRegistry.cs` → add `using` + append
  `new LenovoBrdaDcgPdfParser()`.

**Output** — **no change**. `AnzGenericWriter.Write(...)` already writes `LineSequence` to col A,
vendor to col B, vpn/desc/qty to D/E/F, cost to col I with the `0.000001` sentinel, and Margin to
col K for Uplift. `ParseService.ParseAsync` already dispatches `NoCalculation`/`Uplift` generically
with `vendor.ToUpperInvariant()` → col B becomes `LENOVO`. No ParseService change.

**Backend vendor allowlist** (a new vendor requires this one line):
- `src/BidParser.Api/Endpoints/MeEndpoints.cs:10` — add `Vendors.Lenovo` to the `KnownVendors`
  set, so `/me/settings` accepts Lenovo as a remembered `default_vendor`.

**Frontend** (the AUD/no-FX settings block is currently labelled "HP settings"; Lenovo reuses it):
- `frontend/src/components/HpSettingsBlock.tsx` — replace the hard-coded `HP settings` label with a
  `vendorLabel` prop (render `{vendorLabel} settings`).
- `frontend/src/components/ParseSettingsCard.tsx` — pass `vendorLabel={selectedParser.vendor}` to the
  block. (Branching already keys on `isMultiTemplate`, so Lenovo gets the dropdown + no-FX block
  automatically — no other frontend change.)

**Samples / tests / docs**:
- `samples/inputs/` — copy the sample PDF in (clean name, e.g. `BRDAS010260417V1.pdf`).
- `samples/outputs/<basename>_parsed.xlsx` — golden output, regenerated from parser output and
  hand-validated.
- `tests/BidParser.Parsing.Tests/LenovoBrdaDcgPdfParserTests.cs` — assertions (see below).
- `tests/BidParser.Parsing.Tests/TemplateWriterTests.cs` — add a Lenovo `AnzGenericWriter` golden
  cell-by-cell comparison case (matching the existing per-vendor pattern).
- `docs/lenovo_brda_dcg_pdf.md` — extraction spec mirroring the existing format docs.
- `AGENTS.md` — add the format section, the sample→format mapping row, the new vendor/slug to the
  constants notes, and refresh the now-stale "single vendor: Nutanix only" MVP line.

## Expected golden values (hand-validated targets)

- **Line-item count**: 2 configs + 13 parents + all children. Configs `SIDX02Q2PL` (seq 1, cost
  356,882.96, qty 1) and `SIDX02Q2PM` (seq 8, cost 36,348.82, qty 1).
- **Validation**: `ComputedTotal = QuotedTotal = 393,231.78`, `Matches = true`, currency `AUD`.
- **Sequencing**: parent line 1 → seq `2` with children `2.01 … 2.56`; parent line 7 → seq `9` with
  children `9.01 …`; last parent (line 13) → seq `15`.
- **Spot checks**: child `C0TQ` (RDIMM) qty 8 under seq 2; `SBRS` (Veeam, 3-line description) is a
  single child under seq 11; numeric child codes `5977`, `6400`, `1340`, `3444` appear as children,
  never as parents.

## Test cases (`LenovoBrdaDcgPdfParserTests.cs`)

- Metadata: `QuoteNumber`, `Supplier = Vendors.Lenovo`, `Currency = "AUD"`, `ParserSlug`.
- `AvailableTemplates == [NoCalculation, Uplift]` and `CrmTemplate == NoCalculation`.
- Validation: `ComputedTotal == QuotedTotal == 393231.78m`, `Matches == true`.
- The two CONFIG rows have the right vpn/cost/qty and `LineSequence` `1` and `8`.
- Sequence pattern: parents are whole integers; children are `<parent>.NN`; first child of seq-2 is
  `2.01`, and seq-9 (PDF line 7) opens a fresh `9.01`.
- A child carries `cost == 0` and a config carries the real cost.
- `MinQty == null` for all items (col W blank on export).

## Verification

1. `dotnet test BidParser.sln` — new Lenovo parser + writer tests pass; existing 129 tests unaffected.
2. Generate the golden once from parser output, hand-check against `Book1.xlsx` (allowing the noted
   description/typo artefacts), then commit it and assert against it in the writer test.
3. Manual end-to-end: `dotnet run --project src/BidParser.Api` + `cd frontend && npm run dev`; log in,
   confirm **Lenovo → BRDA DCG (PDF)** appears in the cascade, the **No-FX settings block** shows
   (labelled "Lenovo settings") with the **CRM template dropdown** (No Calculation / Uplift) and a
   **Margin** input only under Uplift, upload the sample, confirm the green **match** toast (AUD), and
   open the downloaded `*_parsed.xlsx` to verify col A sequencing (`1, 2, 2.01 …`), `LENOVO` in col B,
   config prices in col I, and the `0.000001` sentinel on the $0 parent/child rows.

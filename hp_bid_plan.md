# HP Bid (XLSX) — Implementation Plan

> Handoff document. Self-contained: a model with no prior chat context can execute this. Read `AGENTS.md` first (project source of truth), then this file. Project is ASP.NET Core 10 backend + React/Vite/TS frontend.

## 1. Goal & context

Onboard a **second vendor, HP** (today only Nutanix exists), starting with one input format: **"HP Bid (XLSX)"** — an HP deal-export workbook. BidParser parses supplier quotes into a normalised `LineItem` list and writes a standardised CRM-import XLSX.

Two aspects make HP different from every existing parser and require small architectural additions:

1. **New schema field `min_qty`** (minimum-order quantity per line) — the user explicitly asked to add it to the standard `LineItem` schema.
2. **User-selectable output template.** HP lines export into one of **two** CRM templates the user chooses at parse time: `ANZ-GENERIC_NoCalculation.xlsx` and `ANZ-GENERIC_Uplift.xlsx` (identical layout; Uplift additionally populates a Margin column). Today the template is auto-selected, one per parser, with no user choice.

All extraction must be **anchor-based** — locate the header row by header text, map columns by label, never hard-code cell addresses or row numbers.

### Locked decisions (confirmed with user)
- **MSRP stays blank** for HP (no source column defined in the rules).
- **Template choice = a dropdown** shown when HP is selected (one file type, separate template selector). Not two file-type entries.
- The computed **line sequence goes into the Item column (A)** (e.g. `1`, `1.01`, `1.02`, `2`).
- The output writer **rebuilds the sheet in code** (mirrors the existing `ForeignUpliftWriter`). It does **not** load the template `.xlsx` files at runtime.

### Assumptions (low-stakes, change if wrong)
- `QuoteMetadata.Currency = "AUD"` (HP ANZ deals).
- `QuoteMetadata.QuoteNumber` = value of the `Deal Number` metadata cell (anchor lookup), falling back to filename base.
- HP files contain **no quoted total** → `QuotedTotal = null`; validation makes no comparison (mismatch impossible). `ParseValidation.Validate(items, null)` already supports this.

---

## 2. Source file facts (verified by inspecting the samples)

Two sample inputs (currently in `/home/adem/Downloads`, copy into `samples/inputs/`):
- `Deals20260518T034809_HPI.xlsx` — contains `Part Number` **and** `Bundle`/`Bundle Detail` rows (533 rows total).
- `Deals20260518T043243_HPI.xlsx` — `Part Number` rows only (59 rows total).

For both files:
- Single sheet named **`deals.0`**.
- Rows 1–53 are a deal-metadata block (includes labels like `Deal Number`, `Deal Version`, `End Customer`, `Deal Description`).
- **Header row is row 54.** (Anchor on it — do NOT hard-code row 54.)
- No total row anywhere.

**Exact header labels** (row 54) — anchor on these exact strings:

| Col | Header (exact) | Used for |
|---|---|---|
| A | `Line Type` | classification anchor |
| C | `Product Number/ID` | vpn base |
| D | `Option Code` | vpn suffix |
| E | `Product Description` | description |
| G | `Price` | cost |
| H | `Bundle Detail Qty` | qty/min_qty for Bundle Detail |
| I | `Min Order Qty` | min_qty for Part Number/Bundle |
| J | `Max Deal Qty` | qty for Part Number/Bundle |

(The row also has ~27 other columns — `Product Line`, `Addl Discount %`, `Line Number`, etc. — ignore them. Build the `HeaderMap` from the whole row and only pull the labels above.)

`Line Type` distinct values: **`Part Number`**, **`Bundle`**, **`Bundle Detail`**.

**Sample data** (file 1, rows 55–62) confirming behaviour:

| Row | Line Type | Product Number/ID | Option Code | Product Description | Price | Min Order Qty | Max Deal Qty | Bundle Detail Qty |
|---|---|---|---|---|---|---|---|---|
| 55 | Part Number | 9D9L6UT | — | HP S5 Pro 524pf FHD MNTR | 213.92 | 0 | 2012 | — |
| 58 | Bundle | 55623728 | — | 55623728-HP EliteBook 8 G2a 14 (...) | 2387.94 | 0 | 880 | — |
| 59 | Bundle Detail | C89FGAV | — | BU IDS UMA RAI5435 8 14 G2a | 713.26 | — | — | 1 |
| 60 | Bundle Detail | 8C9M7AV | — | No Country of Origin Restriction | 0.03 | — | — | 1 |

Note Option Code is blank in these samples (so vpn = Product Number/ID), but the parser must still implement the `#`-concatenation rule for when it is present (e.g. `4SS11AV#ABG`).

---

## 3. Extraction rules

Header located by `FindCell(sheet, "Line Type")`; build `HeaderMap` from that row. Iterate rows below it until the first wholly-empty row (`WorkbookReader.RowIsEmpty`). All three line types are kept as line items.

Per row, by `Line Type`:

| Field | `Part Number` / `Bundle` | `Bundle Detail` |
|---|---|---|
| **Line sequence** → Item (A) | next whole int (`1`,`2`,…). A `Bundle` also opens a child group and resets the child counter. | `{parentSeq}.{NN}` zero-padded 2-digit (`1.01`, `1.02`, …) |
| **vpn** | `Product Number/ID`, or `Product Number/ID + "#" + Option Code` when Option Code non-empty | same rule |
| **description** | `Product Description` | `Product Description` |
| **cost** | `Price` | `Price` |
| **qty** | `Max Deal Qty` | `Bundle Detail Qty` |
| **min_qty** | `Min Order Qty` | `Bundle Detail Qty` |
| **msrp** | `null` (blank) | `null` (blank) |

`min_qty` rule: **if the sourced value is 0, substitute 1; otherwise use the exact number.** Apply to the final `min_qty` regardless of source column.

`Bundle Detail` rows attach to the most recently seen `Bundle` parent for sub-sequencing. (In a `Part Number`-only file there are no children.)

---

## 4. Output column mapping (both HP templates)

The three CRM templates (`ForeignUplift`, `NoCalculation`, `Uplift`) all share the **identical 27-column header layout** (header on row 2; row 1 col L carries `(Optional for Software and/or Services)`). HP writes to **local** columns (not the Foreign columns the Nutanix `ForeignUpliftWriter` uses):

| Col | Header | HP value |
|---|---|---|
| A | `Item` | `LineSequence` string (`1`, `1.01`, …) |
| B | `Vendor Name` | `"HP"` |
| D | `Vendor Part Number` | `vpn` |
| E | `Description` | `description` |
| F | `Qty.` | `qty` |
| H | `MSRP` | **blank** |
| I | `Cost` | `cost` |
| K | `Margin` | `margin` — **Uplift only**; blank for No Calculation |
| W | `Min Order Qty` | `min_qty` |

All other columns blank. After the last line item, write the **end-loop sentinel row**: col B = `*`, col D = the `EndLoopWarning` constant (`"DO NOT DELETE THIS LINE. Indicate * on column B to mark the end loop. Add / remove lines above as necessary."`). No FX, no foreign columns, **no sentinel-zero (0.000001) substitution** — that was a Foreign-template-only rule.

`NoCalculation` and `Uplift` differ **only** by the Margin column → use one writer with an `includeMargin` flag and a `sheetName` ("No Calculation" / "Uplift").

---

## 5. Current code — exact paths & patterns to mirror

| Concern | File | Notes |
|---|---|---|
| LineItem model | `src/BidParser.Domain/Models/LineItem.cs` | `sealed record`; props are `Vpn`(req), `Description`, `Term`, `Msrp`, `Cost`(req), `Qty`(req), `SerialNumber`, `StartDate`, `EndDate`, `Raw`. Add `MinQty`/`LineSequence` here. |
| QuoteMetadata | `src/BidParser.Domain/Models/QuoteMetadata.cs` | `QuoteNumber`, `Supplier`, `Currency`, `QuotedTotal?`, `SourceFilename`, `ParserSlug`. |
| Constants | `src/BidParser.Domain/Constants/{Vendors,CrmTemplates,ParserSlugs}.cs` | `public const string` pattern. Current: `Vendors.Nutanix="Nutanix"`, `CrmTemplates.ForeignUplift="Foreign Uplift"`, slugs like `nutanix_software_only_xlsx`. |
| IParser | `src/BidParser.Domain/Abstractions/IParser.cs` | Members: `Slug, DisplayName, Vendor, AcceptedMime, CrmTemplate` + `Parse(path)` + `Detect(path)=>0.0`. |
| Reference parser to mirror | `src/BidParser.Parsing/Nutanix/SoftwareOnlyXlsx/NutanixSoftwareOnlyXlsxParser.cs` | Best template for an XLSX parser. Uses `WorkbookReader.Open/FindCell/HeaderMap/RequireLabels/RowIsEmpty`, `DecimalCleaner`, `RawDict`. |
| XLSX helpers | `src/BidParser.Parsing/Xlsx/WorkbookReader.cs`, `HeaderMap.cs` | `Open`, `FindCell(sheet,exact)`, `FindCellStarting`, `HeaderMap(sheet,row)`, `RequireLabels`, `RowIsEmpty`, `CellText`, `CellValue`. `HeaderMap.Require(label)` → column number. |
| Cleaning | `src/BidParser.Parsing/Cleaning/{DecimalCleaner,TextCleaner}.cs` | `DecimalCleaner.Parse(value, defaultZero=false)`, `.ParseInt(value)`, `.ParseOptionalInt(value)`. `TextCleaner.Clean`. |
| Registry | `src/BidParser.Parsing/Registry/ParserRegistry.cs` | Explicit `IReadOnlyList<IParser>` collection initialiser. Append new parser here. |
| Existing writer | `src/BidParser.Output/ForeignUpliftWriter.cs` | `static WriteForeignUplift(items, outputPath, margin=5.00m, fxRate=1.000m, vendorName="NUTANIX", currency="USD", parserSlug=null)`. Builds workbook from scratch: `Headers[]` (27), row-1 col-12 note, data rows, end-loop row. **Mirror this for the new writer.** |
| Output naming | `src/BidParser.Output/OutputNaming.cs` | `<basename>_parsed.xlsx`. |
| Wiring | `src/BidParser.Infrastructure/Services/ParseService.cs` | `ParseAsync(user, fileStream, uploadFilename, vendor, parserSlug, fxRate, margin, maxUploadBytes, ct)`. `ResolveParser` (slug + vendor match), validates extension + magic bytes (XLSX = `50 4B 03 04`), then **hardcodes** `ForeignUpliftWriter.WriteForeignUplift(...)` at lines ~45–52. Writes `ParseJob` + `ParseMetric`, persists user defaults, records mismatch monitoring. |
| /parse endpoint | `src/BidParser.Api/Endpoints/ParseEndpoints.cs` | Multipart fields: `file`, `vendor`, `parser_slug`, `fx_rate`, `margin`. Response headers `X-Validation`, `X-Computed-Total`, `X-Quoted-Total`; streams the xlsx. |
| /parsers endpoint | `src/BidParser.Api/Endpoints/ParsersEndpoints.cs` | Returns `ParserInfo(Slug, DisplayName, Vendor, AcceptedMime, CrmTemplate)` list. |
| Contracts | `src/BidParser.Api/Contracts/` | Typed response records; JSON is `SnakeCaseLower` globally. |
| Margin user default | `src/BidParser.Infrastructure/Entities/User.cs` (`Margin`), `src/BidParser.Api/Endpoints/MeEndpoints.cs` (`/me/settings`). |
| Frontend types | `frontend/src/types.ts` | `ParserInfo { slug, display_name, vendor, accepted_mime, crm_template }`. |
| Frontend dashboard | `frontend/src/pages/DashboardPage.tsx` | Builds FormData (`file,vendor,parser_slug,fx_rate,margin`) and calls `api.parse`. |
| Frontend settings UI | `frontend/src/components/ParseSettingsCard.tsx`, `NutanixSettingsBlock.tsx`, `CrmTemplateCallout.tsx` | Callout currently shows the single template read-only ("Auto" badge). |
| API client | `frontend/src/api/client.ts` | `parse(formData)` posts to `/api/parse` with `X-Requested-With: BidParser`. |
| Parsing tests | `tests/BidParser.Parsing.Tests/` | Golden-input parser tests + template-writer cell-by-cell tests vs `samples/outputs/`. |
| API tests | `tests/BidParser.Api.Tests/` | `WebApplicationFactory`; `TestInfrastructure.cs` has stub parser/registry. |

---

## 6. Implementation steps

### Step 1 — Domain & constants
- `LineItem.cs`: add `public int? MinQty { get; init; }` and `public string? LineSequence { get; init; }` (both nullable, `init`; null for all Nutanix items).
- `Vendors.cs`: `public const string Hp = "HP";`
- `ParserSlugs.cs`: `public const string HpBidXlsx = "hp_bid_xlsx";`
- `CrmTemplates.cs`: `public const string NoCalculation = "No Calculation";` and `public const string Uplift = "Uplift";`

### Step 2 — Multi-template support on `IParser`
Add a default interface member to `IParser.cs`:
```csharp
IReadOnlyList<string> AvailableTemplates => [CrmTemplate];
```
Named `AvailableTemplates` (NOT `CrmTemplates`) to avoid colliding with the `CrmTemplates` constants class. Existing parsers inherit the single-template default unchanged.

### Step 3 — HP parser
New file `src/BidParser.Parsing/Hp/BidXlsx/HpBidXlsxParser.cs` implementing `IParser`, mirroring `NutanixSoftwareOnlyXlsxParser`:
- Properties: `Slug => ParserSlugs.HpBidXlsx`; `DisplayName => "HP Bid (XLSX)"`; `Vendor => Vendors.Hp`; `AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"`; `CrmTemplate => CrmTemplates.NoCalculation` (the default); `AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift]`.
- `Parse(path)`:
  1. `WorkbookReader.Open(path)`; `sheet = workbook.Worksheets.First()`.
  2. `headerCell = WorkbookReader.FindCell(sheet, "Line Type") ?? throw new ParseError("detect", ...)`.
  3. `headerMap = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber)`; `RequireLabels(headerMap, "Line Type","Product Number/ID","Option Code","Product Description","Price","Max Deal Qty","Bundle Detail Qty","Min Order Qty")`.
  4. Iterate `row = headerMap.RowNumber+1 .. lastRow`; break on `RowIsEmpty`. Read `Line Type`; classify and apply the Section 3 table. Track a top-level counter and (for Bundle children) a per-parent child counter; build `LineSequence` strings.
  5. vpn: `code = Text(...,"Product Number/ID")`; `opt = Text(...,"Option Code")`; `vpn = opt.Length>0 ? $"{code}#{opt}" : code`.
  6. qty/min_qty via `DecimalCleaner.ParseInt`; apply `min_qty == 0 ? 1 : min_qty`.
  7. cost via `DecimalCleaner.Parse(Text(...,"Price"), defaultZero:true)`; `Msrp = null`.
  8. Populate `Raw` via the same `RawDict` helper pattern.
  9. QuoteNumber: try `FindCell(sheet,"Deal Number")` and read the adjacent value cell; fallback `Path.GetFileNameWithoutExtension(path)`.
  10. `QuotedTotal = null`; `validation = ParseValidation.Validate(items, null)`. Return `ParseResult` with `Metadata { QuoteNumber, Supplier=Vendor, Currency="AUD", QuotedTotal=null, SourceFilename=Path.GetFileName(path), ParserSlug=Slug }`.

### Step 4 — Registry
`ParserRegistry.cs`: add `using` + append `new HpBidXlsxParser()` to the `Parsers` collection.

### Step 5 — Output writer (rebuild-from-scratch)
New file `src/BidParser.Output/AnzGenericWriter.cs`, mirroring `ForeignUpliftWriter` structure:
```csharp
public static string Write(
    IEnumerable<LineItem> items, string outputPath,
    string sheetName, bool includeMargin,
    decimal margin = 5.00m, string vendorName = "HP")
```
- Reuse the 27-header layout. (Extract `ForeignUpliftWriter.Headers` + `EndLoopWarning` into a shared internal constant — e.g. `TemplateLayout` — or duplicate the array; prefer sharing.)
- Row 1 col 12 = `(Optional for Software and/or Services)`; row 2 = headers.
- Data rows from row 3: A=`item.LineSequence` (string; fallback to running int if null), B=`vendorName`, D=`item.Vpn`, E=`item.Description` (if not null), F=`item.Qty`, **H left empty**, I=`item.Cost`, K=`margin` only when `includeMargin`, W=`item.MinQty` (if not null).
- End-loop sentinel row: B=`*`, D=`EndLoopWarning`.
- Ensure output directory exists; `workbook.SaveAs(outputPath)`.

### Step 6 — Wiring (ParseService + endpoints)
- `ParseService.ParseAsync`: add `string? crmTemplate` parameter. After `ResolveParser`, compute effective template: `var template = string.IsNullOrEmpty(crmTemplate) ? parser.CrmTemplate : crmTemplate;` then validate `parser.AvailableTemplates.Contains(template)` else `throw new ParseValidationException(400, "Unknown CRM template for this parser.")`. Replace the hardcoded writer call with:
  ```csharp
  switch (template) {
      case CrmTemplates.ForeignUplift:
          ForeignUpliftWriter.WriteForeignUplift(result.LineItems, outputPath, margin, fxRate, vendor.ToUpperInvariant(), result.Metadata.Currency, parser.Slug); break;
      case CrmTemplates.NoCalculation:
          AnzGenericWriter.Write(result.LineItems, outputPath, "No Calculation", includeMargin:false, margin, vendor.ToUpperInvariant()); break;
      case CrmTemplates.Uplift:
          AnzGenericWriter.Write(result.LineItems, outputPath, "Uplift", includeMargin:true, margin, vendor.ToUpperInvariant()); break;
      default: throw new ParseValidationException(400, "Unsupported CRM template.");
  }
  ```
  (Optional: persist the chosen `template` on `ParseJob`/`ParseMetric` for audit — out of scope unless wanted; would need an EF migration.)
- `ParseEndpoints.cs`: read optional `crm_template` form field; pass to `ParseAsync`. Make `fx_rate`/`margin` tolerant of absence (default `fx_rate→1`, `margin→0`) since HP No Calculation needs neither and Uplift needs only margin.
- `ParsersEndpoints.cs`: add `IReadOnlyList<string> AvailableTemplates` to `ParserInfo` (serialises `available_templates`); map `p.AvailableTemplates`. Keep `crm_template` (default) for back-compat.

### Step 7 — Frontend
- `types.ts`: add `available_templates: string[]` to `ParserInfo`.
- `DashboardPage.tsx`: add `selectedTemplate` state, default to the selected parser's first available template, reset when the parser changes; `form.set('crm_template', selectedTemplate)` on submit.
- `ParseSettingsCard.tsx`: when `selectedParser.available_templates.length > 1`, render a template `<select>` (No Calculation / Uplift) instead of the read-only `CrmTemplateCallout`; otherwise keep the callout (Nutanix unchanged).
- HP settings: **no FX-rate input**; show the **margin input only when the chosen template is `Uplift`**. Add an `HpSettingsBlock` (analogue of `NutanixSettingsBlock`) and branch on `vendor` in `ParseSettingsCard`. Margin pre-fills from `user.margin`.

### Step 8 — Sample files & golden fixtures
- Copy into `samples/inputs/`: `Deals20260518T034809_HPI.xlsx`, `Deals20260518T043243_HPI.xlsx` (from `/home/adem/Downloads`).
- Copy into `samples/template/`: `ANZ-GENERIC_NoCalculation.xlsx`, `ANZ-GENERIC_Uplift.xlsx` (from `/home/adem/Downloads`) — test references.
- Hand-validate and commit golden outputs under `samples/outputs/` with template-distinct names (one input → two outputs), e.g. `<basename>_NoCalculation_parsed.xlsx`, `<basename>_Uplift_parsed.xlsx`.

### Step 9 — Tests
- `tests/BidParser.Parsing.Tests/HpBidXlsxParserTests`: for both samples assert vpn (`#` concat path), qty/min_qty per line type, `0→1` substitution, `LineSequence` values incl. `1.01`/`1.02` children, all rows kept (incl. bundle parents+children). Writer tests for `AnzGenericWriter` — No Calculation (Margin blank) vs Uplift (Margin populated), cell-by-cell vs golden outputs.
- `tests/BidParser.Api.Tests/`: `/parsers` exposes HP with `available_templates: ["No Calculation","Uplift"]`; `/parse` roundtrip for each `crm_template`; missing/invalid template behaviour (default vs 400); vendor/slug mismatch still guarded.

### Step 10 — Docs
- New `docs/hp_bid_xlsx.md` mirroring the Nutanix specs (extraction algorithm + a golden expected-output table).
- `docs/output_mapping.md`: add a section (or sibling doc) for the No Calculation / Uplift local-template mapping (Cost→I, MSRP blank, Margin only on Uplift, `min_qty`→W, `LineSequence`→Item).
- `AGENTS.md`: update constants list, add HP parser bullet under project status, extend the sample→format table, document the new schema fields (`min_qty`, `LineSequence`/Item) in the canonical-naming section, document `AvailableTemplates`/template-selection in the extensibility + CRM-template-mapping sections.

---

## 7. Verification
- `dotnet test BidParser.sln` — existing suite plus new HP parsing/writer/API tests pass.
- Run locally: `dotnet run --project src/BidParser.Api` and `cd frontend && npm run dev`. Log in; select **HP → HP Bid (XLSX)**; confirm the template dropdown appears (No Calculation / Uplift). Upload `Deals20260518T034809_HPI.xlsx`:
  - **No Calculation**: downloaded xlsx has Item = `1,…,1.01,1.02,…`, Cost in col I, MSRP (H) blank, Margin (K) blank, Min Order Qty in col W, and a trailing `*` end-loop row.
  - **Uplift**: same plus Margin (K) populated; confirm the margin input shows and no FX-rate input shows.
- Confirm Nutanix is unchanged: its single-template callout still renders and its output is byte-identical to before.

---

## 8. Quick checklist

**Status: COMPLETE — all 129 tests passing (62 parsing + 67 API) as of 2026-05-27.**

- [x] `LineItem` gains `MinQty`, `LineSequence`
- [x] `Vendors.Hp`, `ParserSlugs.HpBidXlsx`, `CrmTemplates.NoCalculation`, `CrmTemplates.Uplift`
- [x] `IParser.AvailableTemplates` default member
- [x] `HpBidXlsxParser` + registry entry
- [x] `AnzGenericWriter`
- [x] `ParseService` template switch + `crmTemplate` param
- [x] `/parse` `crm_template` field; `/parsers` `available_templates`
- [x] Frontend: template dropdown, HP settings block (margin-only-on-Uplift, no FX)
- [x] Samples + golden outputs committed
- [x] Parsing/writer/API tests (13 parser tests + 4 writer golden tests + 7 API tests = 24 new tests)
- [x] Docs (`hp_bid_xlsx.md`, `output_mapping.md`, `AGENTS.md`)

---

## 9. Implementation notes (deviations / discoveries)

### Assumption in §3 step 10 was wrong
The plan assumed `ParseValidation.Validate(items, null)` already handles the no-quoted-total case gracefully. It does not — it returns `Matches = false` and adds a "Quoted total not found." warning whenever `quotedTotal is null`, which triggers the frontend's mismatch-warning modal on every HP parse.

**Fix applied:** `HpBidXlsxParser.Parse` constructs `ValidationResult` directly instead of calling `ParseValidation.Validate`:
```csharp
var computed = items.Sum(item => item.Cost * item.Qty);
computed = decimal.Round(computed, 2, MidpointRounding.AwayFromZero);
var validation = new ValidationResult
{
    ComputedTotal = computed,
    QuotedTotal = null,
    Matches = true,
    Difference = 0m
};
```
This pattern should be used for any future parser that legitimately has no quoted total.

### TemplateLayout shared header array
`ForeignUpliftWriter`'s private `Headers` array was extracted into a new internal class `src/BidParser.Output/TemplateLayout.cs` so `AnzGenericWriter` can reference the same 27-element array without duplication. `ForeignUpliftWriter` now references `TemplateLayout.Headers`.

### Golden output naming
The plan suggested `<basename>_NoCalculation_parsed.xlsx` / `<basename>_Uplift_parsed.xlsx`. This was adopted exactly:
- `samples/outputs/Deals20260518T034809_HPI_NoCalculation_parsed.xlsx`
- `samples/outputs/Deals20260518T034809_HPI_Uplift_parsed.xlsx`
- `samples/outputs/Deals20260518T043243_HPI_NoCalculation_parsed.xlsx`
- `samples/outputs/Deals20260518T043243_HPI_Uplift_parsed.xlsx`

### AGENTS.md extensibility section updated
Added two notes to the extensibility steps:
1. `AvailableTemplates` override guidance (single-element default vs multi-template override)
2. No-quoted-total guidance (construct `ValidationResult` directly rather than calling `ParseValidation.Validate`)

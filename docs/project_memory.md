# BidParser — Detailed Project Reference

This file holds the verbose reference material that used to live in `CLAUDE.md`:
per-format extraction algorithms, golden expected outputs, the full API surface,
response conventions, auth/deployment detail, and the sample→format mapping. The
lean `CLAUDE.md` links here. Search this file when you need format specifics or
endpoint contracts.

## Project artefacts (detailed)

- `src/BidParser.Api/` — ASP.NET Core 10 Minimal API. Endpoints for `/auth/*`, `/me`, admin `/users`, `/parsers` (also returns the hardcoded `report_type` per parser from `ReportTypes.For(slug)`), `/parse`, `/history`, admin `/metrics/*`, admin `/monitoring/*`, and health check. Auth via custom `SessionCookieAuthHandler` + Data Protection cookies. CSRF filter, dual rate limiters (custom `AuthRateLimiter` on `/auth/*` plus the .NET built-in `"parse"` token-bucket policy on `/api/parse`), `GlobalExceptionHandler` returning 500 ProblemDetails (no exception details leaked), `SecurityHeadersMiddleware` (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, HSTS-on-HTTPS), locked-down `ForwardedHeaders` (KnownProxies allowlist, rejects `"*"` in production), decimal JSON converters, error normalisation, structured logging. Typed response records live in `Contracts/` (`ApiError`, `OkResponse`, `ParseErrorDetail`, `ParseErrorResponse`, `PasswordValidationError`, `MetricsSummaryResponse` family, `MonitoringRunItem`, `MonitoringRunsResponse`). Hosts the React SPA from `wwwroot/` in production.
- `src/BidParser.Domain/` — `LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`, `IParser`, `IParserRegistry`. `Constants/` centralises vendor (`Vendors.Nutanix`, `Vendors.Hp`, `Vendors.Hpe`, `Vendors.Lenovo`, `Vendors.Zebra`), CRM template (`CrmTemplates.ForeignUplift`, `CrmTemplates.NoCalculation`, `CrmTemplates.Uplift`, `CrmTemplates.PercentOffWithUplift`), and parser slug (`ParserSlugs.NutanixSoftwareOnlyPdf`, …, `ParserSlugs.HpBidXlsx`, `ParserSlugs.HpGlobalBidXlsx`, `ParserSlugs.HpOneConfigXlsx`, `ParserSlugs.HpeBidXlsx`, `ParserSlugs.LenovoBrdaDcgPdf`, `ParserSlugs.LenovoBrdaDcgXlsx`, `ParserSlugs.ZebraPriceConcessionPdf`, `ParserSlugs.ZebraPriceConcessionXls`) string literals — never hand-roll these in new code. `IParser` exposes `AvailableTemplates` (default `[CrmTemplate]`). `LineItem` has an optional `Comments` field (written to col R in `AnzGenericWriter` when non-null).
- `src/BidParser.Infrastructure/` — `AppDbContext` (EF Core + SQL Server, `AddDbContextPool`), `User`/`ParseJob`/`ParseMetric`/`FailedParseJob` entities, `InitialCreate` + `AddReportTypeConfig` + `RemoveReportTypeConfig` migrations (SQL Server schema, `SQL_Latin1_General_CP1_CI_AS` collation on case-insensitive columns), `FileStorage`, `ParseService` (validates uploads by extension AND magic bytes — `%PDF` / `PK\x03\x04`; on post-save failure keeps the source for admin download and invokes `FailedParseJobRecorder`), `FailedParseJobRecorder` (fresh `AppDbContext` scope so the request-scoped context's pending user-default mutations are discarded), `RetentionService` (project-then-`ExecuteDeleteAsync`; purges both `ParseJob` and `FailedParseJob` rows plus their files). `User.Role` is the `UserRole` enum (`Admin`, `User`), stored lowercase via `HasConversion`. `FailedParseJob.Category` is the `FailureCategory` enum stored the same way; serialised as snake_case (`magic_byte_mismatch` / `parser_error` / `unhandled_exception`), not the C# Pascal name. Entity timestamps are stamped exclusively by `AppDbContext.StampTimestamps()` — no `= DateTime.UtcNow` initializers on entities.
- `src/BidParser.Parsing/` — Cleaning helpers, PDF word collection via PdfPig (`PdfWordCollector`, `PdfPigWordSplitter`, `PdfTableHelpers`), XLSX helpers via ClosedXML (`WorkbookReader`, `HeaderMap`), legacy `.xls` via ExcelDataReader (Lenovo BRDA DCG XLS + Zebra Price Concession XLS — registers `CodePagesEncodingProvider`), six Nutanix parsers, three HP parsers (`HpBidXlsxParser`, `HpGlobalBidXlsxParser`, `HpOneConfigXlsxParser`), one HPE parser (`HpeBidXlsxParser`), two Lenovo parsers (`LenovoBrdaDcgPdfParser`, `LenovoBrdaDcgXlsxParser`), two Zebra parsers (`ZebraPriceConcessionPdfParser`, `ZebraPriceConcessionXlsParser`), explicit `ParserRegistry`. **Shared by both solutions.**
- `src/BidParser.Output/` — `ForeignUpliftWriter` (Nutanix), `AnzGenericWriter` (HP No Calculation / Uplift), `PercentOffWithUpliftWriter` (HP OneConfig), shared `TemplateLayout` (internal, 27-column header array used by all writers, plus the `ZeroPriceSentinel = 0.0001m` constant — single source of truth for the zero-dollar sentinel), `OutputNaming`. All writers emit `TemplateLayout.ZeroPriceSentinel` (`0.0001`) for any zero-dollar price (downstream import rejects literal `0`, rounds the sentinel back to `0`): `ForeignUpliftWriter` on Foreign Cost/MSRP columns (T/U), `AnzGenericWriter` on local Cost column (I) — where every HP `Bundle Detail` lands — and on MSRP column (H) when the parser populates it (HPE `BundleDetails` and any zero `ListPrcEst`; HP/Lenovo leave MSRP null so col H stays blank), `PercentOffWithUpliftWriter` on MSRP column (H) — children always emit the sentinel, the parent writes its real `Total Price`. Global rule across all templates; change the constant in one place to retune. **Shared by both solutions.**
- `src/BidParser.Core/` (**desktop only**) — `ParseRunner.Run(inputPath, vendor, slug, fxRate, margin, imPercent, onCostPercent, crmTemplate, outputPath = null)` — the pure orchestration layer: extension validation, magic-byte check, `parser.Parse()`, template resolution, writer dispatch switch, cancelled-line extraction, wrong-file-type flow. The optional `outputPath` lets the desktop "Save As" flow set the destination; when null it falls back to `<basename>_parsed.xlsx` beside the input. Returns `ParseOutcome { Validation, Currency, CancelledLines, OutputPath }`. Also defines `CancelledLine(Line, Vpn)` and `ParseValidationException`. No DB, no auth, no `User`. **Note:** when a new CRM template/writer is added to `ParseService.ParseAsync`, the same `switch` case must be added here.
- `src/BidParser.Wpf/` (**desktop only**, `net10.0-windows`) — WPF MVVM shell. `MainViewModel` (`ViewModels/MainViewModel.cs`) manages: vendor/file-type/template dropdowns sourced directly from `ParserRegistry`; conditional field visibility (`ShowFxRate` / `ShowMargin` / `ShowImPercent` / `ShowOnCostPercent`) that ports the **vendor-driven** branching of the web `ParseSettingsCard.tsx` — Zebra → template dropdown + optional On Cost % (Uplift adds Uplift); HP (multi `HpBidXlsx`/`HpGlobalBidXlsx` or single `HpOneConfigXlsx`) → Uplift for `Uplift`/`% Off RRP with Uplift`, Discount Off MSRP for `% Off RRP with Uplift`; Lenovo → template dropdown (No Calculation needs no fields, Uplift adds Uplift); Nutanix → FX Rate + Uplift (FX Rate is **Nutanix-only** — used by its ForeignUplift writer; Lenovo's No Calculation / Uplift writers never use it); `SetInputFile(path)` (file size ≤ 10 MB guard); `ConvertCommand` (first invokes the View's `SaveFilePrompt` to show a Windows **Save As** dialog pre-filled with the `OutputNaming` default beside the input — cancel aborts before any work; then runs `ParseRunner.Run` with the chosen path on a background thread via `Task.Run`, marshals result back to UI thread via WPF's SynchronizationContext); `OpenFolderCommand` / `OpenFileCommand` (`explorer.exe /select,…`); `ResetCommand` (clears the file, numeric inputs, and result/error, then re-selects the default vendor→file-type→template); `OpenReleasesCommand` (opens `https://github.com/regalen/BidParser/releases/latest` in the default browser, best-effort). `ParseState` enum: `Idle → Running → Success | Warning | Error`. The window footer renders `AppVersionDisplay` (the assembly `InformationalVersionAttribute` minus any `+<git-sha>` suffix, prefixed `v` when numeric; release stamps it via `-p:Version=<tag>`, csproj default `0.0.0-dev` locally), a GitHub link, and `CopyrightText` (`Copyright © <current year> Ingram Micro. All rights reserved.`, year resolved at launch). `MainWindow.xaml.cs` handles drag-drop, the `OpenFileDialog`, and the `SaveFileDialog` (wired to the view-model's `SaveFilePrompt`). **Branding:** `Assets/app.ico` (16–256 multi-resolution, derived from the shared `logo.png`) is the csproj `<ApplicationIcon>` (taskbar/Explorer/Alt-Tab) and the `Window.Icon`; `Assets/logo.png` is the in-window header logo beside the title; both are `<Resource>` items. Regenerate the icon with `magick logo.png -define icon:auto-resize=256,128,64,48,32,16 Assets/app.ico`. No DI container, no config files, no persistent state.
- `ParseMetric` — append-only utilisation ledger (`src/BidParser.Infrastructure/Entities/ParseMetric.cs`). Snapshots user/vendor/parser/totals at parse time. Survives 90-day retention and user deletes via nullable FKs (`parse_metrics → parse_jobs` is `ON DELETE SET NULL`; `parse_metrics → users` is `ClientSetNull` — EF nulls it in memory on user delete, and `DeleteUserAsync` calls `ExecuteUpdateAsync` first to ensure untracked rows are also cleared). Written transactionally with `ParseJob` for successful parses only. Powers `/admin/metrics`. Time-series buckets use `DateTime.ToLocalTime()` in the API layer — container `TZ` controls which calendar day each parse lands on.
- `FailedParseJob` — records parse failures and successful-but-mismatched parses for admin review. `FailureCategory` enum: `MagicByteMismatch`, `ParserError`, `UnhandledException`, `ValidationMismatch`. Exception categories written from `ParseService.ParseAsync`'s catch block via `FailedParseJobRecorder.RecordAsync`. `ValidationMismatch` entries written in the **success path** (after `ParseJob` commit) via `RecordMismatchAsync` when `!Validation.Matches`; they reference the same source file as the `ParseJob` (no copy), populate `ComputedTotal`/`QuotedTotal`, set `ErrorDetail` to a human-readable totals summary. Recorder uses a fresh `AppDbContext` scope; mismatch recording is best-effort (exceptions caught and logged via `ILogger<ParseService>` with parse-job id, user id, parser slug). `SaveUploadAsync` runs **outside** the recorder's `try` so pre-save failures produce no row. Lifecycle: row and referenced file purged together at `RETENTION_DAYS`; for `ValidationMismatch` the source file is shared with `ParseJob` — double-delete attempts during retention are silently ignored. Powers `/admin/monitoring` alongside `ParseJob`: the `GET /monitoring/runs` query **excludes** `ValidationMismatch` rows so each mismatch surfaces once via its `ParseJob` (which carries the output file); the exception categories are the only `FailedParseJob` rows shown there.
- **Report type** — a **hardcoded** mapping from a parser slug (vendor + file type combination) to a "report type" string, in `src/BidParser.Domain/Constants/ReportTypes.cs` (`ReportTypes.For(slug)`; values `Standard` / `Start End Date` / `Hardware SOH`). Single source of truth shared by both products: surfaced to web users through the `report_type` field on `/api/parsers` (shown in `ParseResultModal`) and read directly by the desktop (shown in the result panel). Unmapped slugs render no guidance. Independent of the user-selected CRM template. There is **no** DB table, entity, admin endpoint, or admin page — the former `ReportTypeConfig`/`report_type_configs` was dropped by the `RemoveReportTypeConfig` migration.
- `tests/BidParser.Parsing.Tests/` — xUnit: cleaning helpers, all Nutanix + HP + Lenovo BRDA DCG + Zebra parsers against golden inputs, template writer cell-by-cell equivalence against `samples/outputs/`. Referenced by both solutions.
- `tests/BidParser.Api.Tests/` — xUnit + `WebApplicationFactory` integration tests: migration/bootstrap, auth flow, users admin, parse roundtrip + error matrix (magic-byte mismatch, generic-exception 500, per-user rate limit), HP parse roundtrip for both CRM templates + unknown-template 400 + omitted-template defaulting + `X-Currency: AUD` + `/parsers` exposing `available_templates` + `/me/settings` accepting HP, history list/filter/pagination/downloads, health endpoint security headers, `ForwardedHeaders` trust boundary, retention cleanup (both `ParseJob` and `FailedParseJob`), `ParseMetric` ledger writes + backfill + retention FK-nulling, `/api/metrics/summary` aggregations + filters + empty-range `mismatch_rate="0"` + local-day TZ bucketing, `/api/metrics/export` admin gate + vendor filter + filename date range, `FailedParseJobRecorder` category mapping + pre-save failures recording nothing + user-defaults not persisted on failure, `/api/monitoring/runs` admin gate + ParseJob/FailedParseJob unification + `ValidationMismatch` dedup (single `validation_mismatch` row with `output_available`) + `status`/`vendor`/`userId` filters, `/api/monitoring/jobs/{id}/source|output` streaming + 404, `/api/monitoring/failures/{id}/source` admin gate + streaming + 404. Shared `CustomTestFixture`/`TestRegistry`/`TestParser` in `TestInfrastructure.cs`.
- `frontend/` — React 19 / Vite / TypeScript app. Visual design matches ProductLens (slate-50 background, white cards with `border-slate-200` + `shadow-sm`, `#0077d4` accent, Inter typography, uppercase tracked labels). Shared utility classes in `src/styles.css` (`.label`, `.field`, `.button`, `.button-primary`, `.button-danger`, `.icon-button`, `.card`, `.toast`). Contains API client, auth context, route shell, login + forced password change screens (centered card, accent-blue icon tile, shared `Footer`; change-password screen has a live rule checklist for length/uppercase/digit/symbol/match), sticky white `AppHeader` with `AccountChip` (display name + `@username` + ghost-red logout) and an `AdminMenu` dropdown on the cog (admin only — Users / Metrics / Monitoring; keyboard-navigable ↑/↓/Home/End/Esc), V4 side-panel dashboard, cascading vendor → file-type selection from `/api/parsers`, per-vendor settings blocks (`NutanixSettingsBlock`: FX rate + margin; `HpSettingsBlock`: no FX rate, margin input only for `Uplift`), a CRM-template dropdown for multi-template parsers driven by `available_templates`, single-file dropzone/progress state, a single post-parse `ParseResultModal` (no auto-download): shows success or folded warnings (validation mismatch totals and/or cancelled lines), the hardcoded report type to use when sending the quote to the customer (from `/api/parsers`, hidden when unmapped), and explicit Download + Close buttons; the currency hard-failure keeps its own `CurrencyErrorModal`. Totals label with the `X-Currency` header (`AUD` HP, `USD` Nutanix — never hard-coded; CRM template names in `src/constants.ts`), recent-uploads pagination (page size adapts to viewport height, capped `MAX_PAGE_SIZE = 10` in `RecentUploadsTable.tsx`; columns File name / Vendor / File type / CRM template / When / Files), download actions, auth-expiry redirects, admin user settings card grid (`/admin/users`, legacy `/settings` redirects here), `MetricsDashboard` at `/admin/metrics` (recharts bar chart, KPI strip, three breakdown cards, URL-param-driven date-range + filters, XLSX export), `MonitoringPage` at `/admin/monitoring` (unified **Parser runs** log over `/api/monitoring/runs` — successes + all failure categories; status dropdown + reused `DateRangeControl`/`FilterChips` + click-to-filter on User/Vendor cells, all URL-param-driven; `RunsTable` with status badges incl. green `success`, Input/Output download links — Output only for `kind:"job"` — and an expandable row showing structured stage/hint/message + `<pre>` of `error_detail` + Copy-trace for failure rows only). Components in `src/components/metrics/`, `src/components/monitoring/`, shared `Footer`. `recharts ^2` is the only chart dependency. **Branding:** `public/logo.png` is the 512×512 master (blue repeat/swap icon); `favicon.ico` (16/32/48), `apple-touch-icon.png` (180), `icon-192.png`, and `site.webmanifest` are derived from it and served at the site root (Vite copies `public/` into `dist/`, then the Docker build copies it into `wwwroot/`). `index.html` links the favicons + manifest and sets `theme-color` `#0063FF` (the logo blue). The logo renders via `<img src="/logo.png">` in `AppHeader` and `LoginPage`. Regenerate the derived assets with ImageMagick: `magick logo.png -define icon:auto-resize=48,32,16 favicon.ico`, `magick logo.png -resize 180x180 apple-touch-icon.png`, `magick logo.png -resize 192x192 icon-192.png`.

### Implementation notes

- Parser slugs, package names, spec docs are vendor-prefixed. Use `nutanix_software_only_pdf`, `nutanix_software_only_xlsx`, `nutanix_renewal_pdf`, `nutanix_renewal_xlsx`, `nutanix_hardware_only_pdf`, `nutanix_hardware_only_xlsx`; never reintroduce vendorless slugs.
- Frontend proxies `/api` to `http://127.0.0.1:5000` by default. If port 5000 is occupied: `VITE_API_PROXY_TARGET=http://127.0.0.1:<port>`.
- In Docker, `DB_CONNECTION_STRING` is assembled by `docker-compose.yml` from `MSSQL_SA_PASSWORD` and `MSSQL_DB` and passed to the app container; `UPLOAD_DIR` defaults to `/data/files`. The app container mounts `/data` (named volume `bidparser-data` or `DATA_DIR` bind mount). SQL Server data lives in a separate `bidparser-mssql-data` volume mounted at `/var/opt/mssql` in the `mssql` container.
- `RetentionBackgroundService` (sleep-first, 24h cadence) calls `RetentionService.CleanupOldParseJobsAsync` to delete expired `ParseJob` rows + source+output files, and expired `FailedParseJob` rows + source files, after `RETENTION_DAYS`. `ParseMetric` rows retained indefinitely — `parse_job_id` nulled by SQL Server `ON DELETE SET NULL` when the parent `ParseJob` is purged.
- `ChangePasswordPage` relies on the `App.tsx` route guard (every other route redirects to `/change-password` when `must_change_password=true`) — no `useBlocker` (needs a data router; breaks under declarative `BrowserRouter`).
- The vendor-specific settings block renders only when **both** a vendor and a file type are selected.
- `FileTypeSelect.tsx` renders a `Sample File` link beneath it once a parser is chosen, pointing at a static copy under `/samples/<name>`. Mapping in the `SAMPLE_FILES` record at the top of `FileTypeSelect.tsx`; every parser slug needs an entry with a matching file committed under `frontend/public/samples/`. When adding a parser, add both the `SAMPLE_FILES` entry and the file.
- UI label vocabulary differs from field names: `margin` renders as **`Uplift`** in both settings blocks; HP OneConfig `im_percent` renders as **`Discount Off MSRP`**. API request fields (`margin`, `im_percent`) and DB columns (`parse_jobs.margin`, `users.im`, `parse_metrics.margin`) are unchanged — rename is presentation-only. The three numeric inputs (Exchange rate, Uplift, Discount Off MSRP) **always start empty on page load**; not pre-filled from saved defaults, no "Save defaults" button; Upload & parse stays disabled until the template's required numerics are populated.
- `User.Name` is a nullable display name surfaced in `AccountChip` and `SettingsPage`. Admin creates require it; bootstrap admin starts with `Name="Administrator"`.
- Golden fixtures in `samples/outputs/` are named `<basename>_parsed.xlsx` (one per quote number, not per input file). PDF and XLSX parsers for the same quote compare against the same golden. Multi-template parsers (HP Bid XLSX, Lenovo BRDA DCG XLS) emit one golden **per template**: `<basename>_NoCalculation_parsed.xlsx` and `<basename>_Uplift_parsed.xlsx`.
- Hardware parser tests include a negative assertion: NX-1175S-G10-6517P-CM has Quote D's cost (USD 20,017.57), not Quote C's (USD 5,903.72).

## Sample → format mapping

| Sample file        | Format                 | Notes                                  |
|--------------------|------------------------|----------------------------------------|
| `XQ-4076249.pdf`   | Software Only (PDF)    | Compact 6-column layout                |
| `XQ-4076249.xlsx`  | Software Only (XLSX)   |                                        |
| `XQ-4108785.pdf`   | Hardware Only (PDF)    |                                        |
| `XQ-4108785.xlsx`  | Hardware Only (XLSX)   |                                        |
| `XQ-4128926.pdf`   | Renewal (PDF)          |                                        |
| `XQ-4166696.pdf`   | Renewal (PDF)          | Wrapped currency amount (USD on separate line)  |
| `XQ-4029825.pdf`   | Renewal (PDF)          | Platform-column variant: extra `Platform` column, wrapped Product Code, hardware rows emit `Description = "Platform: {value}"` |
| `XQ-4176792.xlsx`  | Renewal (XLSX)         | XLSX envelope of Renewal; `Product Description` + `Platform` combined into Description; blank row before `TOTAL` |
| `XQ-4157308.pdf`   | Software Only (PDF)    | Extended 9-column layout, 1 line item  |
| `XQ-4165884.pdf`   | Software Only (PDF)    | Extended 9-column layout, wrapped SKUs |
| `XQ-4175235-….pdf` | Hardware Only (PDF)    | Multi-page Quote D (pages 4–5/7); page-footer falls inside Term column; quoted total `USD 247,510.94` |
| `XQ-4175219-….pdf` | Hardware Only (PDF)    | Multi-page Quote D (pages 7–10/12); wrapped `USD <amount>` for `NX-8150-G10-6728P-CM` net unit price; quoted total `USD 3,601,962.18` |
| `Deals20260518T034809_HPI.xlsx` | HP Bid (XLSX) | 479 items: Part Number × 3, Bundle × 14, Bundle Detail × 462; includes `#`-concatenated Option Code example |
| `Deals20260518T043243_HPI.xlsx` | HP Bid (XLSX) | 5 Part Number rows only; Part-Number-only file |
| `55648855.xlsx`                  | HP OneConfig (XLSX) | 1 Config + 30 components; AUD 6,042.77 Total Price |
| `HPE_Deal_1601962887_v2.xlsx`    | HPE Bid (XLSX)      | 66 items: Bundle × 3, BundleDetails × 63; anchor `"LineType"`; computed total 131,713.36 |
| `HPE_Deal_1602186424_v1.xlsx`    | HPE Bid (XLSX)      | 4 Part Number rows only; qty from `Quantity` (×5); computed total 30,215.00 |
| `translate_quote_47500427_v25_all.xlsx` | HP Global Bid (XLSX) | 24 items; deal 47500427 v.25; qty always 1; AUD computed total 35,233.34 |
| `BRDAS010260417V1.pdf` | Lenovo BRDA DCG (PDF) | 152 items: 2 configs, 13 parents, 137 children (self-component dedup); AUD 393,231.78 |
| `BRDAS010545504V1.pdf` | Lenovo BRDA DCG (PDF) | Simple variant (no CONFIGURATION DETAILS); 3 top-level items with real unit prices; AUD 77,545.95 |
| `BRDAS010546096V1.pdf` | Lenovo BRDA DCG (PDF) | Simple variant; 1 top-level item; AUD 38,896.08 |
| `BRDAD010458440.xls`   | Lenovo BRDA DCG (XLS) | Legacy .xls binary; 62 items: 8 parents, 54 children; AUD 103,542.60 |
| `Zebra_PC_81391641.pdf` | Zebra Price Concession (PDF) | 3 active items; AUD; page-break description split on ZD4AH22 |
| `Zebra_PC_81391641.xls` | Zebra Price Concession (XLS) | HTML-disguised XLS; 3 active items; matches PDF extraction |
| `Zebra_PC_81413855.pdf` | Zebra Price Concession (PDF) | 5 items, all active |
| `Zebra_PC_81413855.xls` | Zebra Price Concession (XLS) | 5 items, all active |
| `Zebra_PC_81422095.pdf` | Zebra Price Concession (PDF) | 8 items; cross-page description split; AUD |
| `Zebra_PC_81422095.xls` | Zebra Price Concession (XLS) | 8 items |

## ParseResult contract

```
ParseResult
├── Metadata        QuoteMetadata (QuoteNumber, Supplier, Currency, QuotedTotal, SourceFilename, ParserSlug)
├── LineItems       IReadOnlyList<LineItem>
└── Validation      ValidationResult (ComputedTotal, QuotedTotal, Matches, Difference, Warnings)

LineItem — superset of fields across formats. Only VPN, Cost, Qty are required;
the rest are nullable and populated per format:
  Required: VPN, Cost, Qty
  Software Only (PDF + XLSX):  Description, Term, Msrp
  Renewal:                     Msrp, SerialNumber, StartDate, EndDate
  Hardware Only (PDF + XLSX):  Description, Term, Msrp    // Term per-row nullable
  HP Bid (XLSX):               Description, MinQty, LineSequence, Comments
  HPE Bid (XLSX):              Description, Msrp, MinQty, LineSequence, Comments
  HP Global Bid (XLSX):        Description, LineSequence, Comments
  HP OneConfig (XLSX):         Description, Msrp, LineSequence
  Lenovo BRDA DCG (PDF+XLS):  Description, Msrp, MinQty, LineSequence
  Zebra PCR (PDF+XLS):        Description, Msrp, MinQty, LineSequence, Comments, IsCancelled
  Common debug:                Raw (Dictionary<string,string> of original column text)

IsCancelled (bool, default false): set by Zebra parser for Cancelled=Y rows. The writer
leaves col H/I/W blank (no sentinel), sets Qty=1, writes Comments="Cancelled (Standard
Price)". All other parsers leave this false; it is inert for them.

Across formats: List Price / List Unit Price / MSRP / Term Adjusted List Unit Price
→ Msrp; Sale Price / Net Unit Price / Cost Price → Cost.
```

Dates stored internally as `DateOnly` (serialised ISO `YYYY-MM-DD`); display formatting (`DD/MM/YYYY`) is a frontend concern.

Format detection is a **soft hint, not routing**: `BaseParser.detect()` returns 0.0–1.0 confidence; the UI pre-fills the dropdown when confidence > 0.7 but the user always confirms. Silent mis-routing is worse than one extra click.

## Common PDF parsing approach

All PDF formats use **UglyToad.PdfPig** (word-level bounding boxes) via `PdfWordCollector`. PdfPig uses bottom-left origin; Y is flipped in `PdfWord` construction so downstream code uses top-left origin (like pdfplumber). `PdfPigWordSplitter` re-splits merged tokens using per-letter `GlyphRectangle` bounds.

- Collect `PdfWord` records from all pages, preserving page index.
- Find header row by its first-cell anchor word(s) (`"Product"`+`"Code"` for Software Only, `"No"` for Renewal).
- Derive column x-ranges from header word x0s — each range is `[its x0, next header's x0)`; rightmost extends to page width.
- Collect body words below the header y across pages until a `"TOTAL:"` token.
- Cluster words into rows by `top` (tolerance ~3pt). Bucket each row's words into columns by `x0`; join intra-bucket words with single spaces.
- Locate quoted total by scanning after the last body row for `TOTAL:` + `USD` + amount, tolerating page-break wrap.

Shared helpers in `src/BidParser.Parsing/Pdf/`: `PdfWordCollector`, `PdfWord`, `PdfPigWordSplitter`, `PdfTableHelpers`.

## Common XLSX parsing approach

XLSX formats use **ClosedXML** (`new XLWorkbook(path)`). Use `cell.GetFormattedString()` everywhere to match openpyxl's stringy values; do **not** use the typed `XLCellValue`.

- Open workbook, pick the relevant sheet (usually the only one, or named like the quote number).
- Locate the header row by scanning for a cell matching a known anchor label (e.g. `Product Code`). No fixed row number — metadata above the table varies.
- Capture the header row's column numbers; map each known label to its column.
- Iterate data rows below the header. Stop at the first wholly-empty row, or a footer (e.g. cell containing `TOTAL $...`).
- Locate the total by scanning for a cell starting with `TOTAL ` followed by a currency string, or a row labelled `TOTAL` with an adjacent value cell.
- Currency strings use `$` and thousands separators (`$2,275.00`), not `USD `. `DecimalCleaner.Parse` strips `$`, `,`, `USD`, whitespace.

Shared helpers in `src/BidParser.Parsing/Xlsx/`: `WorkbookReader`, `HeaderMap`.

## Software Only (PDF) — extraction algorithm

Header anchor: `"Product"` immediately followed (same y-band ±3pt, increasing x) by `"Code"`. Columns: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Net Unit Price`, `Quantity`.

Row classification:
- `Product Code` trims to `"Term-Months"` OR `Product` is `"Term in months"` → **Term-Months row** (KEEP as a line item with sentinel-zero pricing, reset continuation grouping).
- `Product Code` matches `^[A-Z0-9-]+$` after trim → **anchor row** (new `LineItem`).
- `Product Code` empty AND `Product` non-empty → **continuation row** (append `Product` text to current anchor's description).
- Otherwise → ignore.

Per-field: flatten description by joining continuation snippets with single spaces, collapse internal whitespace; strip `USD`/commas from prices → `Decimal`; `term`/`qty` → `int`.

Edge cases: 3–5 line description wrap; `Term-Months` row kept; `TOTAL:` wrapping onto a later page; thousands separators (`2,275.00`).

Expected output for `XQ-4076249.pdf` (golden):

| Part Number      | Term | List Price | Sale Price | Quantity |
|------------------|------|------------|------------|----------|
| SW-NCM-STR-PR    | 60   | 383        | 101.11     | 2096     |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 864      |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 1232     |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-E-PRO-PR  | 60   | 3455       | 912.12     | 145      |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCM-E-STR-PR  | 60   | 583        | 153.91     | 145      |
| Term-Months      | 60   | 0          | 0          | 60       |

Computed total = quoted total = `USD 1,625,358.51`.

## Software Only (XLSX) — extraction algorithm

Same line items, same supplier, different envelope; produces the same `LineItem` shape as the PDF. Open the active sheet (single sheet named after the quote in the sample); don't hard-code a name.

Section + header location (anchor-based):
1. Scan for a cell whose value is the literal `Quote Number`. This is the top-left cell of the line-item header row — the grid's leftmost column is `Quote Number`, not `Product Code`.
2. That row is the header row. Build a label→column-letter map by walking every cell in the row.
3. Required labels: `Product Code`, `Product Description`, `Term (Months)`, `List Price`, `Sale Price`, `Quantity`. Ignore the other labels (`Quote Number`, `Quote Name`, `Line Name`, `Payment Terms`, `Total Discount (%)`, `Amount`, …).
4. Iterate data rows below the header. Stop at the first wholly-empty row or first cell starting with `TOTAL `.

Row classification:
- `Product Code` trims to `Term-Months` → **Term-Months row** (KEEP, sentinel-zero pricing).
- `Product Code` non-empty and not `Term-Months` → **anchor row** (new `LineItem`). One row per item — no continuation rows (cells don't wrap).

Per-field: `vpn` = trim `Product Code`; `description` = trim `Product Description`; `term` = `Term (Months)` numeric → `int`; `msrp` = `List Price` ($-string) → `Decimal`; `cost` = `Sale Price` → `Decimal`; `qty` = `Quantity` → `int`.

Total: scan down from the last data row for a cell starting with `TOTAL ` (e.g. `TOTAL $1,625,358.51`), strip `TOTAL`/`$`/commas → `Decimal`. A second `TOTAL` label further down is a fallback only.

Edge cases: header position not fixed; `Term-Months` rows kept; `$`-prefixed currency with thousands separators; `Total Discount (%)` sits between `List Price` and `Sale Price` so the map must use header text — never assume adjacency or fixed columns.

Expected output for `XQ-4076249.xlsx` (golden values match the PDF version, total `$1,625,358.51`).

## Renewal (PDF) — extraction algorithm

Header anchor: word `"No"` top-left of the table header. Columns: `No`, `Product Code`, `Serial Number`, `Start Date`, `End Date`, `Term Adjusted List Unit Price`, `Total Discount`, `Net Unit Price`, `Qty`, `Total Net Price`. Header labels span multiple visual lines (`Term`/`Adjusted`/`List Unit`/`Price` stacked) — the column-anchor logic must tolerate multi-line header text.

Row classification:
- `No` is a positive integer AND `Product Code` non-empty → **anchor row**.
- `No` empty AND any other column non-empty → **continuation row** (append per column — `Serial Number` wraps so the second token arrives this way).
- Otherwise → ignore.

Per-field:
- `vpn`: trim `Product Code`.
- `serial_number`: source cell wraps across two lines (e.g. `24SW000351227,\nLIC-02472987`). Join fragments with **no separator** (comma is at end of first line), trim, collapse internal whitespace → single string (`24SW000351227,LIC-02472987`). Do **not** split into serial/license fields.
- `start_date`/`end_date`: source `MM/DD/YYYY`; `DateOnly.ParseExact("MM/dd/yyyy")`. Frontend displays `DD/MM/YYYY`.
- `msrp`: strip `USD`/commas from `Term Adjusted List Unit Price` → `Decimal`.
- `cost`: strip `USD`/commas from `Net Unit Price` → `Decimal`.
- `qty`: `Qty` → `int`.

No Term-Months sub-header. `Total Net Price` wraps but isn't extracted (validation comes from `TOTAL:`).

**Platform-column variant** (`XQ-4029825`): an optional `Platform` column sits between `No` and `Product Code`. Detect from the header band (search `"Platform"`); add to column-range map only when present. When a row carries a non-empty Platform value (itself potentially wrapping, joined without separator), `LineItem.Description` = `"Platform: {value}"` (e.g. `"Platform: NX-8035N-G8-HY"`); rows with no Platform value leave `Description` null. Product Code also wraps (`RSW-NCI-`/`ULT-PR`); fragments joined without separator via `JoinUnspaced`.

**USD-prefix fusion:** before bucketing, `NutanixRenewalPdfParser.FuseCurrencyTokens` walks the word stream and pairs each `"USD"` with its nearby numeric amount (forward window 6 words; spatial tolerance `Top ∈ [USD.Top − 3.5, USD.Top + 15.0]`, same page). The pair collapses into one synthetic `PdfWord` anchored at the *amount's* coordinates, so wrapped amounts land in the correct price column. Without this, `USD` and the numeric straddle the boundary and `DecimalCleaner.Parse` throws on a bare `'USD'`.

Edge cases: serial wrap; `Net Unit Price`/`Qty` running together (`USD 54.41 160`) disambiguated by x-ranges; `TOTAL: USD ...` wrapping; serial with no embedded license; wrapped-currency layout (`XQ-4166696.pdf`).

Expected output for `XQ-4128926.pdf` (golden):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 24SW000351227,LIC-02472987  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |
| RSW-NCI-ULT-PR  | 24SW000351236,LIC-02472996  | 2026-07-13  | 2027-07-12  | 575        | 371.83     | 32       |
| RSW-NCI-ULT-PR  | 24SW000351221,LIC-02472983  | 2026-07-13  | 2027-07-12  | 575        | 429.11     | 72       |
| RSW-NCM-STR-PR  | 24SW000351228,LIC-02472985  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |

Computed total = quoted total = `USD 60,205.68`.

Expected output for `XQ-4166696.pdf` (wrapped-currency):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 25SW000430057,LIC-02537784  | 2026-06-17  | 2028-12-01  | 189        | 54.64      | 80       |
| RSW-NCI-PRO-PR  | 25SW000430055,LIC-02537786  | 2026-06-17  | 2028-12-01  | 1121       | 661.61     | 80       |
| RSW-NCM-STR-PR  | 25SW000430056,LIC-02537783  | 2026-10-28  | 2028-12-01  | 161        | 40.20      | 400      |
| RSW-NCI-PRO-PR  | 25SW000430054,LIC-02537785  | 2026-10-28  | 2028-12-01  | 955        | 755.64     | 400      |

Computed total = quoted total = `USD 375,636.00`.

Expected output for `XQ-4029825.pdf` (Platform-column variant):

| Part Number     | Description               | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|---------------------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCI-ULT-PR  | _(empty)_                 | 25SW000437991,LIC-02543011  | 2026-08-16  | 2029-12-31  | 1943       | 354.77     | 448      |
| RSW-NCI-ULT-PR  | _(empty)_                 | 25SW000437992,LIC-02543012  | 2026-08-16  | 2029-12-31  | 1943       | 601.52     | 192      |
| RSW-NCI-PRO-PR  | _(empty)_                 | 22SW000262928,LIC-01461229  | 2026-11-03  | 2029-12-31  | 1440       | 889.43     | 128      |
| RS-HW-PRD-MY    | Platform: NX-8035N-G8-HY | 22SH3G410326                | 2026-11-03  | 2029-07-31  | 2676.24    | 1957.37    | 1        |
| RS-HW-PRD-MY    | Platform: NX-8035N-G8-HY | 22SH3G410327                | 2026-11-03  | 2029-07-31  | 2676.24    | 1957.37    | 1        |

Computed total = quoted total = `USD 392,190.58`.

## Renewal (XLSX) — extraction algorithm

The XLSX envelope of the Renewal format (`NutanixRenewalXlsxParser`, slug `nutanix_renewal_xlsx`). Same supplier and field set as Renewal (PDF), but the spreadsheet carries each value in one cell, so none of the PDF wrapping / `USD`-fusion machinery is needed. **Anchor on string labels, never fixed positions** — the metadata block above the table varies in height and column letters differ between quotes.

Locating + extracting:
1. Find the cell whose value is the literal `Quote Number`; its row is the header row. Build a label→column map across that row.
2. Required labels: `Product Code`, `Serial Number`, `Start Date`, `End Date`, `Term Adjusted List Unit Price`, `Net Unit Price`, `Quantity`. Optional: `Product Description`, `Platform`.
3. Iterate rows below the header; stop on the first wholly-empty row **or** a `TOTAL ` cell. A blank row can separate the last item from the `TOTAL $…` row, so fall back to scanning the whole sheet for the first `TOTAL ` cell (same pattern as Software Only (XLSX)).

Per-field:
- **Part Number** ← `Product Code`.
- **Description** ← `Product Description`, with `Platform` appended as `… (Platform: {value})` when the platform cell is non-empty (software-subscription rows leave Platform blank → bare description). This differs from Renewal (PDF), whose source has no description column and emits only `Platform: {value}`.
- **Serial Number** ← `Serial Number`; internal whitespace stripped so the embedded license joins as `26SW000487027,LIC-02574676` (matching the PDF convention).
- **Start/End Date** ← `Start Date` / `End Date` (native date cell, else `MM/dd/yyyy` with the usual fallbacks).
- **MSRP** ← `Term Adjusted List Unit Price`; **Cost** ← `Net Unit Price` (both `$`-prefixed, parsed with `defaultZero`). No `Term` column → `Term` left null.
- **Quantity** ← `Quantity`.

Expected output for `XQ-4176792.xlsx` (golden):

| Part Number    | Serial Number              | Start Date | End Date   | List Price | Sale Price | Quantity |
|----------------|----------------------------|------------|------------|------------|------------|----------|
| RS-HW-PRD-ST   | 21FM6K270093               | 2026-07-12 | 2027-07-11 | 1107.36    | 803.30     | 1        |
| RS-HW-PRD-ST   | 21FM6K270094               | 2026-07-12 | 2027-07-11 | 1107.36    | 803.30     | 1        |
| RS-HW-PRD-ST   | 21FM6K270091               | 2026-07-12 | 2027-07-11 | 1107.36    | 803.30     | 1        |
| RS-HW-PRD-ST   | 21FM6K270092               | 2026-07-12 | 2027-07-11 | 1107.36    | 803.30     | 1        |
| RSW-NCI-PRO-PR | 26SW000487027,LIC-02574676 | 2026-07-12 | 2027-07-11 | 455        | 225.51     | 288      |

Computed total = quoted total = `$68,160.08`.

## Hardware Only (PDF) — extraction algorithm

PDF bundles stacked quote sections (Quote A/B/C/D). **Parse Quote D only** — the reseller-facing breakdown. Quote C is a separate budgetary breakdown of pure components; ignore it.

Section + header location (anchor-based):
1. Scan all words for the literal `Quote D For distributor to quote to the reseller only` — the Quote D banner.
2. From the banner's y, scan downward (across pages) for the header row — `Product` immediately followed by `Code`.
3. Derive column x-ranges for: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Total Discount`, `Net Unit Price`, `Quantity`, `Total Net Price`.
4. Collect body rows below the header until the literal `TOTAL:` — both terminates Quote D and carries its quoted total.

Row classification:
- `Product Code` non-empty after trim → **anchor row**. Both SKU strings (`NX-1175S-G10-6517P-CM`) and plain labels (`Support-Term`, `Platform Integration`) qualify.
- `Product Code` empty AND another column non-empty → **continuation row**. Append per column — both `Product Code` and `Product` wrap independently.
- Otherwise → ignore.

**Every classified line item is kept** — no filler rows skipped. The `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) is a real line item.

Per-field:
- `vpn`: concatenate anchor + continuation `Product Code` snippets with **no separator** (`NX-1175S-G10-` + `6517P-CM`). Trim trailing whitespace. Keep non-SKU labels verbatim.
- `description`: join anchor + continuation `Product` snippets with single spaces; collapse whitespace.
- `term`: per-row nullable. `int` when populated; null when empty.
- `msrp`: strip `USD`/commas from `List Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null.**
- `cost`: strip `USD`/commas from `Net Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null.**
- `qty`: `int`. On `Support-Term` this cell holds the term value (`60`) — keep as-is.

Edge cases: rows from Quote A/B/C must not appear (negative-assertion test); part number wrap; description wrap 1–4 lines; bundled-component rows (price cells empty → `0`); `Support-Term` kept; `TOTAL:` may be on the same row as `USD <amount>` or wrap.

Expected output for `XQ-4108785.pdf` Quote D (golden):

| Part Number              | Term | List Price | Sale Price | Quantity |
|--------------------------|------|------------|------------|----------|
| NX-1175S-G10-6517P-CM    | —    | 25021.99   | 20017.57   | 1        |
| C-MEM-32GB-6400-CM       | —    | 0.00       | 0.00       | 4        |
| C-HDD-12TB-ETBA-CM       | —    | 0.00       | 0.00       | 2        |
| C-NVM-7.68TB-AB1A-CM     | —    | 0.00       | 0.00       | 2        |
| C-HBA-3816-1N-C-CM       | —    | 0.00       | 0.00       | 1        |
| C-NIC-25G4E1-CM          | —    | 0.00       | 0.00       | 1        |
| C-PWR-4FC13C14A-CM       | —    | 0.00       | 0.00       | 2        |
| S-HW-PRD                 | 60   | 4019.99    | 2411.99    | 1        |
| Support-Term             | 60   | 0.00       | 0.00       | 60       |
| C-TPM-2.0-U-C-CM         | —    | 77.89      | 62.31      | 1        |
| Platform Integration     | 0    | 4003.51    | 0.00       | 1        |

Computed total = quoted total = `USD 22,491.87`.

## Hardware Only (XLSX) — extraction algorithm

XLSX counterpart of Hardware Only (PDF). Both extract Quote D and produce the same line items and total (`$22,491.87`). Workbook has stacked Quote A/B/C/D; only Quote D in scope.

Section + header location (anchor-based):
1. Scan for a cell whose value is `Quote D For distributor to quote to the reseller only` — that row is the banner.
2. From the banner row, scan down for the next cell `Product Code` — that row is the header.
3. Build a label→column-letter map. **Don't assume columns carry over from Quote C** — Quote C has `Product Code` in column H, Quote D in column E.
4. Stop at the first cell below the header starting with `TOTAL ` — carries the quoted total.

Row classification: every non-empty row between header and `TOTAL ` is a line item. **No filler rows skipped** — the `Support-Term` row is kept.

Per-field: `vpn` = trim `Product Code` (keep non-SKU labels verbatim); `description` = trim `Product Description`; `term` = int when populated, null when empty (do not invent); `msrp`/`cost` = $-string → `Decimal`, **empty → `Decimal("0")`**; `qty` = `int` (Support-Term holds term value `60`).

Edge cases: Quote A/B/C rows must not appear; column-letter drift inside the workbook; bundled-component rows → `0`; per-row term nullability; Support-Term qty holds `60`.

Expected output for `XQ-4108785.xlsx` Quote D matches the PDF version, total `$22,491.87`.

## API surface (all under `/api`)

| Method | Path | Auth | Notes |
|---|---|---|---|
| `POST` | `/auth/login` | none | Body `{username, password}`. Rate-limited. Returns the user object and sets the session cookie. |
| `POST` | `/auth/logout` | user | Clears the current session cookie only. |
| `POST` | `/auth/change-password` | user | Body `{old_password, new_password}`. Enforces password rules. Clears `must_change_password`. Re-issues the session cookie (new password-hash stamp) so the acting session survives while all other sessions for the user are revoked. |
| `GET`  | `/me` | user | Returns the current user shape. |
| `PATCH` | `/me/settings` | user | Body `{default_vendor?, fx_rate?, margin?, im_percent?}`. Endpoint accepts numerics (tests/tooling) but the SPA only mutates `default_vendor` — implicitly, inside `ParseService` on each successful parse. |
| `GET` | `/parsers` | user | Returns the registry — each entry has `slug`, `display_name`, `vendor`, `accepted_mime`, `crm_template`, `available_templates`, and `report_type` (from the hardcoded `ReportTypes.For(slug)` map, `null` when unmapped). Drives the vendor → file-type cascade and the result-popup report-type guidance. |
| `POST` | `/parse` | user | Multipart: `file`, `vendor`, `parser_slug`, `fx_rate`, `margin`. Max 10 MB both sides. See response headers below. |
| `GET` | `/history` | user | `?limit=&offset=&q=` — user-scoped. `q` is a case-insensitive substring filter on `source_filename`. Each row carries `crm_template` (the template written at parse time, persisted on `ParseJob.CrmTemplate`). `when` is a server-computed relative-time string. |
| `GET` | `/history/{id}/source` | user | Streams the stored original. 404 if foreign user or expired. |
| `GET` | `/history/{id}/output` | user | Streams the parsed `*_parsed.xlsx`. Same gating. |
| `GET` | `/users` | admin | User CRUD. `POST` requires `{username, name, role}`; sets a random one-time temp password + `must_change_password=True` and returns `{user, temp_password}`. `PATCH` accepts `{username?, name?, role?, reset_password?}` and returns `{user, temp_password}` (`temp_password` non-null only when `reset_password=true`). |
| `GET` | `/metrics/summary` | admin | `?from=YYYY-MM-DD&to=YYYY-MM-DD&vendor=&userId=&parserSlug=`. Default window last 30 days, server-local TZ inclusive. Returns `{range, kpis, by_user, by_vendor, by_parser, time_series}`. `mismatch_rate` is 4-dp string (`"0"` on empty range). `time_series` buckets by local calendar day via `DateTime.ToLocalTime()` in the API layer — container `TZ` controls bucketing. |
| `GET` | `/metrics/export` | admin | Same params as `/summary`. Streams a ClosedXML workbook (`Utilisation` sheet) as `utilisation_<from>_<to>.xlsx`. Real Excel dates, money numeric, `Totals Match` bool. Ordered `CreatedAt DESC`. |
| `GET` | `/monitoring/runs` | admin | Unified runs log. `?status=&vendor=&userId=&parserSlug=&from=YYYY-MM-DD&to=YYYY-MM-DD&limit=&offset=` (limit 1–100, default 25). Newest-first. Merges successful/mismatched `ParseJob` rows (`kind:"job"`, status `success`/`validation_mismatch`, `output_available` true) with genuine `FailedParseJob` failures (`kind:"failure"`, status = category, `output_available` false). **Excludes `FailedParseJob` rows where `Category == ValidationMismatch`** so each mismatch appears once (sourced from its `ParseJob`, which has the output file). `status` ∈ {`success`, `validation_mismatch`, `magic_byte_mismatch`, `parser_error`, `unhandled_exception`}; it selects which table(s) to read. `parser_display_name` from `IParserRegistry`; `source_available`/`output_available` are live `File.Exists`. Each table is read to `offset+limit` then merged/sorted/paged in memory (retention-bounded volume). |
| `GET` | `/monitoring/jobs/{id}/source` | admin | Streams the retained `ParseJob` source file (no user-ownership check, unlike `/history`). 404 when row or file is gone. |
| `GET` | `/monitoring/jobs/{id}/output` | admin | Streams the `ParseJob` output as `<basename>_parsed.xlsx`. 404 when row or file is gone. |
| `GET` | `/monitoring/failures/{id}/source` | admin | Streams the retained `FailedParseJob` source file (failure-row input). 404 when the row is missing OR the file is gone — doesn't leak which. |

**`/parse` response headers** on success: `X-Validation: match | mismatch`, `X-Currency`, `X-Computed-Total`, `X-Quoted-Total`, and `X-Cancelled-Lines` (only when cancelled lines exist; format `line:VPN;line:VPN;…`). `X-Currency` carries `QuoteMetadata.Currency` (`USD` Nutanix, `AUD` HP) — never hard-code `USD` in the UI. The SPA always shows the single `ParseResultModal` (no auto-download): match → success state; mismatch and/or cancelled lines → folded warnings; download is gated behind the modal's Download button. The report type to use comes from `/api/parsers` (`report_type`), not a parse header. **`X-Quoted-Total` empty-header semantic:** when `QuotedTotal == null` the header is **present with an empty string value**, not omitted (the SPA distinguishes "header missing" from "header present, empty"). ASP.NET strips empty-string headers by default — preserve with `new StringValues(new string[] { "" })`.

**`/parse` failure mode:** when the parser raises, the backend returns `422` with `{detail: {stage, hint, message}}`. The uploaded source is **discarded** — no `ParseJob` row, no original retained. The frontend renders the error inline in the dropzone, preserving the form.

## Response conventions

**Three error body shapes** (the SPA branches on the shape; new endpoints must use the matching record from `src/BidParser.Api/Contracts/`):

- `ApiError { Detail: string }` → `{"detail":"<message>"}` — most errors (401, 404, 409, 429, "Unknown vendor." 400, CSRF 403, …).
- `PasswordValidationError { Detail: string[] }` → `{"detail":["msg1","msg2",…]}` — **only** `POST /auth/change-password` on rule failure. Each string is one violation; SPA joins with a space. Don't stringify into a single detail.
- `ParseErrorResponse { Detail: { Stage, Hint, Message } }` → `{"detail":{"stage":"…","hint":"…","message":"…"}}` — **only** `POST /parse` 422.

Success responses with no useful body use `OkResponse { Ok: true }` → `{"ok":true}`. No anonymous `new { detail = "…" }` objects.

**Decimal serialisation** — money/rates serialise as strings with fixed scale via per-field `[JsonConverter]` attributes (converters in `src/BidParser.Api/Serialization/`):

| Field(s) | Scale | Format | Notes |
|---|---|---|---|
| `fx_rate` | 4 dp | `"0.7400"` | EF `HasPrecision(12, 4)`; round with `decimal.Round(v, 4, MidpointRounding.AwayFromZero)`. |
| `margin` | 2 dp | `"7.50"` | EF `HasPrecision(12, 2)`. |
| `computed_total`, `quoted_total`, `X-Computed-Total`, `X-Quoted-Total` | 2 dp | `"1625358.51"` | `ToString("F2", InvariantCulture)`. |

**JSON casing:** `JsonNamingPolicy.SnakeCaseLower` globally — `Detail` → `"detail"`, `MustChangePassword` → `"must_change_password"`. New DTOs follow this without per-property `[JsonPropertyName]`.

## Wrong file-type detection

When a user picks the wrong file type, the selected parser fails at recognition and
`ParseService` turns that into a helpful "wrong file type" message instead of a recorded
failure.

- **Signal.** A recognition failure surfaces as `ParseError(stage: "detect")` — thrown when
  the table anchor is missing (`?? throw new ParseError("detect", …)`) or a required column
  is absent (`HeaderMap.Require` now throws `ParseError("detect", …)` rather than
  `InvalidOperationException`). Only `stage == "detect"` is reclassified; every other stage
  (`"currency"`, `"extract"`, `"upload"`, `"config"`, …) stays a genuine recorded failure.
- **Classification + cleanup.** `ParseService` catches `ParseError when (Stage == "detect")`
  *before* the generic handler: deletes the output **and the stored upload**, records nothing
  (no `FailedParseJob`, `ParseJob`, or `ParseMetric`), and rethrows
  `ParseError(stage: "file_type", message, message)`.
- **Naming (hybrid).** `DetectSuggestedType` runs `Detect()` over the **same-vendor,
  same-MIME** siblings of the selected parser (excluding it), takes the top score, and names
  it when `≥ 0.7` (`WrongFileTypeConfidence`). Message: *"The file is not recognised as
  {selected} and appears to be a {suggested}. Select the correct file type and try again."*;
  when nothing is confident: *"The file is not recognised as {selected}. Check the selected
  file type and try again."*
- **Candidate set / coverage.** Only vendors with ≥2 formats per MIME can ever name a
  sibling: **Nutanix PDF** (Software/Renewal/Hardware), **Nutanix XLSX** (Software/Renewal/
  Hardware), **HP XLSX** (Bid/Global Bid/OneConfig). Lenovo & Zebra have one format per MIME →
  always the generic message.
- **`Detect()` signatures** (each `try/catch → score`, `0.0` on failure):
  - *Nutanix XLSX:* Hardware = `Quote D` banner present; Renewal = `Quote Number` grid with
    `Net Unit Price` + `Serial Number` + `Term Adjusted List Unit Price`; Software = same grid
    with `Term (Months)` + `List Price` + `Sale Price`, no banner, no renewal columns.
  - *Nutanix PDF* (over whitespace-normalised `WordStreamText`): Hardware = `…distributor to
    quote to the reseller only` banner; Renewal = a `Serial` column word, no banner; Software =
    contains `Nutanix`, no banner, no `Serial`.
  - *HP XLSX:* Bid = `Line Type` header; Global Bid = `Product number` on the `Product
    numbers` sheet; OneConfig = `Config ID` header.
- **Frontend.** `DashboardPage.fileTypeErrorMessage` matches `stage === "file_type"` and shows
  `FileTypeErrorModal` (message-only; the dropdown is left unchanged).
- **Tests.** `WrongFileTypeDetectionTests` (parsing project, runnable) asserts the
  cross-detection matrix; `WrongFileTypeTests` (API project, needs Docker) asserts the 422
  `file_type` response, that nothing is recorded, and the upload is deleted.

## CRM template mapping

All six Nutanix parsers declare `CrmTemplate = "Foreign Uplift"` and `AvailableTemplates = ["Foreign Uplift"]`; `ForeignUpliftWriter.WriteForeignUplift` produces their workbook.

HP Bid (XLSX): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`. User selects template via dropdown. `AnzGenericWriter.Write(items, path, sheetName, includeMargin, margin, vendorName)` produces both; `Uplift` populates Margin column (K), `No Calculation` leaves it blank. `Bundle Detail` rows carry `cost = 0` (the `Bundle` parent holds the deal total); `AnzGenericWriter` writes the `0.0001` sentinel in Cost column (I) for any zero cost.

HP OneConfig (XLSX): `CrmTemplate = "% Off RRP with Uplift"` (single template). `PercentOffWithUpliftWriter.Write(items, path, margin, imPercent, vendorName)`. The parent Config row writes its real `Total Price` on column H (MSRP); children write the `0.0001` sentinel on H. Column I (Cost) blank. Columns K (Margin) and X (IM%) written for every row, required parse params. IM% persisted as `User.ImPercent`, serialised `im_percent`.

`ParseService.ParseAsync` accepts an optional `crmTemplate`. When omitted/empty it defaults to `parser.CrmTemplate`, is validated against `parser.AvailableTemplates`, and the matching writer branch is dispatched. `/api/parsers` exposes `available_templates` so the frontend renders a static callout (single-template) or a dropdown (multi-template).

## MVP scope & guardrails

Locked-in product decisions. Anything outside is deferred — flag scope drift before building.

- **Vendors ship one format at a time**, registry-driven. Nutanix, HP, Lenovo, and Zebra parsers currently ship; new vendors/formats are added as a parser class + fixture + registry entry (see "Adding a parser format" in CLAUDE.md).
- **Single-file upload** per parse. "DRAG MULTIPLE FILES TO BATCH PARSE" is a future-state affordance.
- **Result-popup flow**, no review/edit screen. Upload & parse → progress panel → blocking `ParseResultModal` with an explicit Download button (no auto-download), the admin-configured report type to use (hidden when unconfigured), and Close. Validation server-side; warnings (mismatch totals and/or cancelled lines) are folded into the same popup. The currency hard-failure still uses `CurrencyErrorModal`.
- **Per-user remembered vendor** — only the last-used vendor persists on each User row (`ParseService` sets `user.DefaultVendor = vendor` on success). Numeric inputs (FX rate/margin/IM%) are **never** persisted back — the dashboard re-prompts every parse. The User table still has nullable `fx_rate`/`margin`/`im` columns and `/me/settings` still accepts them (tests/tooling), but the SPA neither reads nor writes them.
- **Env-var admin bootstrap** — `ADMIN_USERNAME`/`ADMIN_PASSWORD` seed the first admin on a fresh DB (defaults `admin`/`changeme`, `must_change_password=True`). Ignored once any user row exists.
- **Stack**: ASP.NET Core 10 + React/Vite/TypeScript, single Docker image.

Out of scope: multi-file batch upload, CSV formats, review/approve gate, multi-tenancy, email, SSO, audit log beyond ParseJob history.

## Authentication & authorisation

- **Passwords**: bcrypt cost 12. Rules enforced on `/auth/change-password` only — the env-var admin bootstrap still uses `ADMIN_PASSWORD` (`changeme` default) + `must_change_password`, but admin **create/reset generate a random one-time temp password** (`RandomNumberGenerator`, 10 hex chars) returned once in the response (`UserWithTempPassword { user, temp_password }`) for the admin to hand over; there is no fixed shared credential. Rules: ≥ 8 chars, ≥ 1 uppercase, ≥ 1 digit, ≥ 1 symbol.
- **Sessions**: Data Protection cookies (`bidparser_session`), HttpOnly, SameSite=Lax, hard 12-hour expiry from login (no sliding refresh). `Secure` flag set only when the request resolves to HTTPS after `ForwardedHeadersMiddleware` (local HTTP dev works; production behind NPM gets `Secure=True` via `X-Forwarded-Proto`). `SESSION_SECRET` is the app-name discriminator, not the signing key. **Token creation/parsing is centralised in `SessionTokenService`** (single `SessionPayload { UserId, IssuedAt, Stamp }` record); the payload **binds the session to a fingerprint of the password hash** (`Stamp = PasswordHash[^8..]`). `HandleAuthenticateAsync` rejects a token whose stamp no longer matches the stored hash, so any password change or admin reset **revokes every existing session** for that user with no server-side store. `ChangePasswordAsync` re-issues the acting session's cookie so it survives its own change. (Rollout: cookies issued before this change lack a stamp and fail to parse → all users re-authenticate once after deploy.)
- **Login timing**: unknown usernames still spend one bcrypt verification (against a fixed dummy hash) so response timing doesn't reveal which usernames exist.
- **CSRF**: every non-GET endpoint requires `X-Requested-With: BidParser` (set by `frontend/src/api/client.ts`). With SameSite=Lax, sufficient for an internal app.
- **Rate limiting on `/auth/*`**: 5/min, two buckets — per remote IP across `/auth/login`+`/auth/change-password`, AND per submitted username on `/auth/login` (pre-auth). Either tripping → `429` + `Retry-After` + generic body. In-memory leaky bucket; doesn't survive restart.
- **`must_change_password` gate**: when set, the backend returns `403 password_change_required` for every endpoint except `/auth/*` and `/me`. Frontend redirects to `/change-password`; `App.tsx` route guards keep the user there.
- **Authorization policies are per-endpoint, not path-globbed** (a path-glob would let locked users hit `/me/settings`):
  - `LoggedIn` — valid session cookie; `must_change_password` **ignored**. Used by `/auth/logout`, `/auth/change-password`, `GET /me`.
  - `ActiveUser` — `LoggedIn` AND `must_change_password=false`. Everywhere else under `/api` except admin.
  - `Admin` — `ActiveUser` + `role == "admin"`.
- **Last-admin guard**: `PATCH`/`DELETE /api/users/{id}` return `409` if the op would leave zero admins, or if it targets the calling admin.
- **Password recovery**: no self-service. Admin `PATCH /api/users/{id}` with `{reset_password: true}` generates a random one-time temp password (returned once as `temp_password`) + `must_change_password=True`, and revokes the target's existing sessions (password-hash stamp change).

## Operational config & deployment

`docs/DEPLOYMENT.md` is the operator runbook (env-var table, NPM proxy config, `/data` layout, first-login walkthrough). Keep it in sync when the deployment story changes.

- **Single container**, multi-stage Dockerfile: `node:20-alpine` builds the SPA → `dotnet/sdk:10.0` builds/publishes the API → `dotnet/aspnet:10.0` runtime serves on `:3447`. SPA copied into `wwwroot/`; `UseStaticFiles` + `MapFallbackToFile("index.html")` for SPA routing.
- **Schema migrations** run in `MigratorHostedService` at startup (`Database.MigrateAsync()`). `BootstrapAdminHostedService` seeds the admin row when zero users exist.
- **Publishes `3447:3447`**, behind nginx-proxy-manager for TLS. The reverse proxy must set `client_max_body_size` ≥ `MAX_UPLOAD_MB` (default 10) — NPM defaults to 1 MB.
- **`Secure` cookie** set only when `X-Forwarded-Proto=https` reaches the app after `ForwardedHeadersMiddleware`.
- **Persistent state**: app container `/data` holds `dp-keys/`, `files/originals/`, and `files/outputs/` (named volume `bidparser-data` or `DATA_DIR` bind mount). SQL Server data lives in the separate `bidparser-mssql-data` volume at `/var/opt/mssql` in the `mssql` container. **`/data/dp-keys` must persist** — it holds the Data Protection keyring; losing it logs everyone out.
- **`SESSION_SECRET`** is the Data Protection app-name discriminator, **not** a key. The keyring in `/data/dp-keys` is the actual signing material. Rotating `SESSION_SECRET` scopes new cookies away from old (logs everyone out). Deleting `/data/dp-keys` invalidates the keyring.
- **Env-var defaults**: `ADMIN_USERNAME=admin`, `ADMIN_PASSWORD=changeme`, `MAX_UPLOAD_MB=10`, `RATE_LIMIT_AUTH_PER_MIN=5`, `RETENTION_DAYS=90`, `SESSION_LIFETIME_HOURS=12`, `TZ=Australia/Sydney` (controls server-local time for `/admin/metrics` daily buckets; `tzdata` ships with the aspnet image). `SESSION_SECRET` has a dev default (`dev-only-change-me`) and must be overridden in production.
- **GitHub Actions CI/CD** (`.github/workflows/build.yml`): every push and PR runs the `test` job (`dotnet test BidParser.sln`). The `build-and-push` job (linux/amd64 Docker image) runs only for `v*` tag pushes. Each tagged release publishes three tags to `ghcr.io`: `<semver>`, `sha-<short-sha>`, `latest`. The job sets `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: 'true'` because the four pinned `docker/*` actions still ship Node 20 (forced to Node 24 on 2026-06-02, removed 2026-09-16). Remove once those actions ship Node 24-native majors.
- **UI version string** in `frontend/src/components/Footer.tsx` comes from `import.meta.env.VITE_APP_VERSION`, injected via the `APP_VERSION` Docker build-arg. CI resolves it to the tag SemVer on releases (`v0.2.0` → `0.2.0`), `dev-<short-sha>` on branch/PR builds. No file edits to bump — push a `v*` tag.

## Release versioning (SemVer 2.0.0)

- Tags `vMAJOR.MINOR.PATCH` (`v0.1.0`, `v1.0.0`).
- `MAJOR` for incompatible API/config/deployment changes, `MINOR` for backwards-compatible features, `PATCH` for backwards-compatible fixes.
- `0.y.z` while still in home-network/internal testing.
- Pre-release identifiers allowed (`v1.0.0-rc.1`, `v0.2.0-alpha.1`), sorted per SemVer 2.0.0.
- Every GitHub Release points at a matching pushed tag; pushing the tag triggers the Docker build.

## Spec docs index

- `docs/nutanix_software_only_pdf.md` — Software Only (PDF), Nutanix subscription quotes.
- `docs/nutanix_software_only_xlsx.md` — Software Only (XLSX), same data as the PDF, workbook envelope.
- `docs/nutanix_renewal_pdf.md` — Renewal (PDF), subscription renewals with serial/license numbers.
- `docs/nutanix_renewal_xlsx.md` — Renewal (XLSX), workbook envelope of the renewal format.
- `docs/nutanix_hardware_only_pdf.md` — Hardware Only (PDF), multi-quote PDF; parse Quote D only.
- `docs/nutanix_hardware_only_xlsx.md` — Hardware Only (XLSX), multi-quote workbook; parse Quote D only.
- `docs/hp_bid_xlsx.md` — HP Bid (XLSX). Deal-export workbooks with Part Number, Bundle, and Bundle Detail rows; no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/hp_global_bid_xlsx.md` — HP Global Bid (XLSX). `Product numbers` sheet; header anchor `"Product number"`; AUD-only (`Converted net price [AUD]` column required, else `ParseError("currency")`); qty always 1; comments encode remaining qty (`"{remaining} Remaining"`; `Full term (Months)` no longer parsed); no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/hp_oneconfig_xlsx.md` — HP OneConfig (XLSX). Single Config per file, parent VPN from `Config ID`, MSRP from `Total Price`, 30 children zeroed. Output `% Off RRP with Uplift`; requires `margin` + `im_percent`.
- `docs/hpe_bid_xlsx.md` — HPE Bid (XLSX). HPE deal-export workbooks; header anchor `"LineType"` (one word); `Part Number`/`Bundle`/`BundleDetails` rows; vpn from `ProductNumber`/`BundleID`/`ComponentID` (OptionCode ignored); msrp from `ListPrcEst`, cost from `Offering`, qty from `Quantity` (not Min Order Qty), comments `"Max Qty: {MaxDealQty}"`; BundleDetails msrp/cost → `0.0001` sentinel; no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/lenovo_brda_dcg_xlsx.md` — Lenovo BRDA DCG (XLS). Legacy `.xls` (OLE Compound Document) read via ExcelDataReader; PARENT/CHILD classified by whether the unit-price cell is `> 0` (explicit `0.0` is a child — `5374CM1` "Configuration Instruction" rows depend on this). LineSequence is a single flat running sequence across parents and children (`"1"`, `"2"`, `"3"`, …); parent vs child is distinguished by cost, not by a dotted sequence. Quoted total from the `Total:` row's unit-price column, rounded 2 dp.
- `docs/lenovo_brda_dcg_pdf.md` — Lenovo BRDA DCG (PDF). Two variants: complex (PRODUCT AND SERVICE DETAILS + CONFIGURATION DETAILS — CONFIGs/PARENTs/children, detect 0.85) and simple (PRODUCT AND SERVICE DETAILS only — numbered parent items with real unit prices, no children, detect 0.75). Quirks: X1+1-based column boundaries for Part Number/Description; floating-point 1 pt offset for No/Qty in section 2; pre-description buffering; section 2 absent → empty children map.
- `docs/zebra_price_concession.md` — Zebra Price Concession (PDF + XLS). HTML-disguised XLS (HtmlAgilityPack); PDF uses anchor-based column detection with blank-word filtering and fused-number splitting (see memory/zebra-parser-pdfpig-quirks.md). Field map: Part No.→vpn, Max. Qty→qty, Min. Qty→min_qty, List Price→msrp (col H), Unit Special Price→cost (col I). Cancelled=Y rows: blank col H/I/W, qty=1, Comments="Cancelled (Standard Price)", warning modal. On Cost % written to col Z when provided. Available templates: No Calculation and Uplift.
- `docs/output_mapping.md` — how parsed `LineItem` fields map into `ANZ-GENERIC_ForeignUplift.xlsx`, output filename convention, locked output rules (MSRP column H stays empty; `serial_number` → Comments not Serial Number; term written only when `>= 1`). Read before generating any `*_parsed.xlsx`.
- `docs/design/` — Claude Design handoff for the V4 side-panel UI; read `docs/design/README.md` before frontend work.

### Output templates

- `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` — Nutanix output. Mapping in `docs/output_mapping.md`.
- `samples/template/ANZ-GENERIC_NoCalculation.xlsx` — HP, no Margin column populated. `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_Uplift.xlsx` — HP, Margin column populated. `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_PercentOffWithUplift.xlsx` — HP OneConfig (Margin col K + IM% col X populated; MSRP on H, Cost col I blank). `docs/hp_oneconfig_xlsx.md`.

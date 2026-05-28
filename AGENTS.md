# AGENTS.md

This file is the canonical project reference for AI coding agents working in this repository.

> [!NOTE]
> All rules, conventions, and extraction skills defined in this file have been migrated to the structured Antigravity format under [.agents/rules/](file:///home/adem/Documents/Projects/parser/.agents/rules/) and [.agents/skills/](file:///home/adem/Documents/Projects/parser/.agents/skills/). For Antigravity agents, those files are applied automatically or dynamically. Keep this file updated in sync with any changes made to the `.agents/` directories.

## Project Status

The backend has been re-platformed from Python/FastAPI to ASP.NET Core 10. The current artefacts are:

- `src/BidParser.Api/` — ASP.NET Core 10 Minimal API app. Endpoints for `/auth/*`, `/me`, admin `/users`, `/parsers`, `/parse`, `/history`, admin `/metrics/*`, admin `/monitoring/*`, and health check. Auth via custom `SessionCookieAuthHandler` + Data Protection cookies. CSRF filter, dual rate limiters (custom `AuthRateLimiter` on `/auth/*` plus the .NET built-in `"parse"` token-bucket policy on `/api/parse`), `GlobalExceptionHandler` returning 500 ProblemDetails (no exception details leaked), `SecurityHeadersMiddleware` (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, HSTS-on-HTTPS), locked-down `ForwardedHeaders` (KnownProxies allowlist, rejects `"*"` in production), decimal JSON converters, error normalisation, and structured logging across endpoints. Typed response records live in `Contracts/` (`ApiError`, `OkResponse`, `ParseErrorDetail`, `ParseErrorResponse`, `PasswordValidationError`, `MetricsSummaryResponse` family, `FailedParseJobItem`, `FailedParseJobListResponse`). Hosts the React SPA from `wwwroot/` in production.
- `src/BidParser.Domain/` — `LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`, `IParser`, `IParserRegistry`. `Constants/` centralises vendor (`Vendors.Nutanix`, `Vendors.Hp`, `Vendors.Lenovo`), CRM template (`CrmTemplates.ForeignUplift`, `CrmTemplates.NoCalculation`, `CrmTemplates.Uplift`, `CrmTemplates.PercentOffWithUplift`), and parser slug (`ParserSlugs.NutanixSoftwareOnlyPdf`, …, `ParserSlugs.HpBidXlsx`, `ParserSlugs.HpOneConfigXlsx`, `ParserSlugs.LenovoBrdaDcgPdf`, `ParserSlugs.LenovoBrdaDcgXlsx`) string literals — never hand-roll these strings in new code. `IParser` exposes `AvailableTemplates` (default `[CrmTemplate]`) so parsers that support multiple output templates override it.
- `src/BidParser.Infrastructure/` — `AppDbContext` (EF Core + SQLite, registered via `AddDbContextPool`), `User`/`ParseJob`/`ParseMetric`/`FailedParseJob` entities, EF migrations (`InitialCreate`, `HistoryCompositeIndex` for `(user_id, created_at DESC)`, `SourceFilenameNoCase` for `TEXT COLLATE NOCASE` on `source_filename`, `AddParseMetricsLedger` with inline SQL backfill, `AddFailedParseJobs`), SQLite WAL interceptor, `FileStorage`, `ParseService` (validates uploads by extension AND magic bytes — `%PDF` / `PK\x03\x04`; on post-save failure keeps the source for admin download and invokes `FailedParseJobRecorder`), `FailedParseJobRecorder` (uses a fresh `AppDbContext` scope so the request-scoped context's pending user-default mutations are discarded), `RetentionService` (project-then-`ExecuteDeleteAsync`; purges both `ParseJob` and `FailedParseJob` rows plus their files). `User.Role` is the `UserRole` enum (`Admin`, `User`), stored as lowercase string via `HasConversion`. `FailedParseJob.Category` is the `FailureCategory` enum stored the same way; the API surface serialises it as snake_case (`magic_byte_mismatch` / `parser_error` / `unhandled_exception`), not the C# Pascal-case name. Entity timestamps are stamped exclusively by `AppDbContext.StampTimestamps()` — no `= DateTime.UtcNow` initializers on entities.
- `src/BidParser.Parsing/` — Cleaning helpers, PDF word collection via PdfPig (`PdfWordCollector`, `PdfPigWordSplitter`, `PdfTableHelpers`), XLSX helpers via ClosedXML (`WorkbookReader`, `HeaderMap`), legacy `.xls` reading via ExcelDataReader (Lenovo BRDA DCG XLSX only — registers `CodePagesEncodingProvider` for `.xls` string decoding), five Nutanix parsers, two HP parsers (`HpBidXlsxParser`, `HpOneConfigXlsxParser`), two Lenovo parsers (`LenovoBrdaDcgPdfParser`, `LenovoBrdaDcgXlsxParser`), explicit `ParserRegistry`.
- `src/BidParser.Output/` — `ForeignUpliftWriter` (Nutanix, ClosedXML), `AnzGenericWriter` (HP No Calculation / Uplift, ClosedXML), `PercentOffWithUpliftWriter` (HP OneConfig, ClosedXML), shared `TemplateLayout` (internal, 27-column header array used by all writers, and the `ZeroPriceSentinel = 0.0001m` constant — single source of truth for the zero-dollar sentinel), `OutputNaming`. All writers emit `TemplateLayout.ZeroPriceSentinel` (`0.0001`) for any zero-dollar price (the downstream import rejects a literal `0` and rounds the sentinel back to `0`): `ForeignUpliftWriter` on the Foreign Cost/MSRP columns (T/U), `AnzGenericWriter` on the local Cost column (I) — which is where every HP `Bundle Detail` lands, `PercentOffWithUpliftWriter` on the MSRP column (H) — children always emit the sentinel, the parent writes its real `Total Price`. This is a global rule across all templates (`Foreign Uplift`, `No Calculation`, `Uplift`, `% Off RRP with Uplift`); change the constant in one place to retune.
- `ParseMetric` is an append-only utilisation ledger (`src/BidParser.Infrastructure/Entities/ParseMetric.cs`). Snapshots user/vendor/parser/totals at parse time. Survives both 90-day retention and user deletes via nullable `OnDelete(SetNull)` FKs. Written transactionally with `ParseJob` for successful parses only. Powers `/admin/metrics` (`/api/metrics/summary`, `/api/metrics/export`). Time-series buckets are server-local via SQLite's `date(created_at, 'localtime')`, so the container's `TZ` env var controls which calendar day each parse lands on.
- `FailedParseJob` records both parse failures and successful-but-mismatched parses for admin review. `FailureCategory` enum values: `MagicByteMismatch`, `ParserError`, `UnhandledException`, `ValidationMismatch`. Exception categories are written from `ParseService.ParseAsync`'s catch block via `FailedParseJobRecorder.RecordAsync`. `ValidationMismatch` entries are written in the **success path** (after `ParseJob` commit) via `RecordMismatchAsync` when `!Validation.Matches`; they reference the same source file as the `ParseJob` (no copy), populate `ComputedTotal`/`QuotedTotal` decimal fields, and set `ErrorDetail` to a human-readable totals summary. The recorder uses a fresh `AppDbContext` scope; mismatch recording is best-effort (exceptions are caught and logged via `ILogger<ParseService>` — with parse-job id, user id, and parser slug — so the user's successful parse is unaffected). `SaveUploadAsync` runs **outside** the recorder's `try` so pre-save failures produce no row. Lifecycle: both the row and its referenced file are purged together at `RETENTION_DAYS`; for `ValidationMismatch` the source file is shared with `ParseJob` — double-delete attempts during retention are silently ignored. Powers `/admin/monitoring` (`/api/monitoring/failures`, `/api/monitoring/failures/{id}/source`).
- `tests/BidParser.Parsing.Tests/` — xUnit tests: cleaning helpers, all five Nutanix parsers + both HP parsers + both Lenovo BRDA DCG parsers (PDF and XLSX) against golden inputs, template writer cell-by-cell equivalence against `samples/outputs/`.
- `tests/BidParser.Api.Tests/` — xUnit + `WebApplicationFactory` integration tests: migration/bootstrap, auth flow, users admin, parse roundtrip + error matrix (including magic-byte mismatch, generic-exception 500 via global handler, per-user rate limit), HP parse roundtrip for both CRM templates + unknown-template 400 + omitted-template defaulting + `X-Currency: AUD` header + `/parsers` exposing `available_templates` + `/me/settings` accepting HP as a vendor, history list/filter/pagination/downloads, health endpoint security headers, `ForwardedHeaders` trust boundary, retention cleanup (covers both `ParseJob` and `FailedParseJob`), `ParseMetric` ledger writes + backfill + retention FK-nulling, `/api/metrics/summary` aggregations + filters + empty-range `mismatch_rate="0"` + local-day TZ bucketing, `/api/metrics/export` admin gate + vendor filter + filename date range, `FailedParseJobRecorder` category mapping for all three categories + pre-save failures recording nothing + user-defaults not persisted on failure, `/api/monitoring/failures` admin gate + pagination + parser-display lookup + `source_available` flip + snake_case category, `/api/monitoring/failures/{id}/source` streaming + 404. Shared `CustomTestFixture`/`TestRegistry`/`TestParser` live in `TestInfrastructure.cs` for any test that needs to swap in a stub parser.
- `frontend/` — React/Vite/TypeScript app. Visual design language matches ProductLens (slate-50 page background, white cards with `border-slate-200` + `shadow-sm`, `#0077d4` accent, Inter typography, uppercase tracked labels). Shared utility classes live in `src/styles.css` (`.label`, `.field`, `.button`, `.button-primary`, `.button-danger`, `.icon-button`, `.card`, `.toast`). Contains the API client, auth context, route shell, login + forced password change screens (each rendered on the ProductLens slate-50 background with a centered card, accent-blue icon tile, and the shared `Footer`; the change-password screen has a live rule checklist for length/uppercase/digit/symbol/match), sticky white `AppHeader` with an `AccountChip` (display name + `@username` + ghost-red logout) and an `AdminMenu` dropdown on the cog button (admin only — Users / Metrics / Monitoring; keyboard-navigable with ↑/↓/Home/End/Esc), V4 side-panel dashboard, cascading vendor → file-type selection from `/api/parsers`, per-vendor settings blocks (`NutanixSettingsBlock`: FX rate + margin; `HpSettingsBlock`: no FX rate, with a margin input shown only for the `Uplift` template), a CRM-template dropdown for multi-template parsers (HP: No Calculation / Uplift) driven by `available_templates`, FX/margin defaults from `/api/me`, single-file dropzone/progress state, auto-download on parse, validation mismatch modal (blocking acknowledgement before download) + success toasts (both label totals with the `X-Currency` response header — `AUD` for HP, `USD` for Nutanix — never a hard-coded currency; CRM template names are shared constants in `src/constants.ts`), recent-uploads pagination (page size adapts to viewport height, capped at `MAX_PAGE_SIZE = 10` in `RecentUploadsTable.tsx`) with download actions, auth-expiry redirects, admin user settings as a card grid (`/admin/users`, the legacy `/settings` URL redirects here), `MetricsDashboard` at `/admin/metrics` (recharts bar chart, KPI strip, three breakdown cards, URL-param-driven date-range + filters, XLSX export), and `MonitoringPage` at `/admin/monitoring` (paginated failures table with relative-time, snake_case category badges with per-category colors, expandable row showing structured stage/hint/message + a `<pre>` of `error_detail` + Copy-trace, source-file download). Components are extracted into individual files per the plan's repo layout (`src/components/metrics/`, `src/components/monitoring/`), with a shared `Footer` used across all routes. `recharts ^2` is the only chart dependency.
- `samples/inputs/` — real supplier quote files. Seven PDFs (`XQ-4076249.pdf`, `XQ-4108785.pdf`, `XQ-4128926.pdf`, `XQ-4157308.pdf`, `XQ-4165884.pdf`, `XQ-4166696.pdf`, `BRDAS010260417V1.pdf`), two Nutanix XLSXs (`XQ-4076249.xlsx`, `XQ-4108785.xlsx`), two HP Bid XLSXs (`Deals20260518T034809_HPI.xlsx`, `Deals20260518T043243_HPI.xlsx`), and one HP OneConfig XLSX (`55648855.xlsx`). `XQ-4108785.pdf` and `XQ-4108785.xlsx` are the *same engagement* delivered in two envelopes; both parsers extract from **Quote D** (reseller-facing breakdown) within those files and produce matching totals (`USD 22,491.87`). `XQ-4157308.pdf` and `XQ-4165884.pdf` are both Software Only (PDF) samples using the **extended 9-column layout** (adds `Selected Start Date`, `Total Discount`, `Total Net Price`); `XQ-4157308.pdf` is the minimal case (1 line item, side-by-side `Product Code` header) and `XQ-4165884.pdf` is the heavy case (11 line items, stacked `Product`/`Code` header, wrapped multi-line SKUs). `BRDAS010260417V1.pdf` is the Lenovo BRDA DCG sample (2 configs, 152 items after parent-VPN self-component dedup, AUD 393,231.78).
- `samples/outputs/` — golden `XQ-*_parsed.xlsx` fixtures, one per quote number (not per input file — PDF and XLSX variants of the same quote share one golden file since they produce identical output). Used by the template-writer regression tests; regenerated whenever an output rule changes. Naming follows the spec: `<basename>_parsed.xlsx` (e.g. `XQ-4076249_parsed.xlsx`).
- `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` — the Nutanix output template. Field mapping locked in `docs/output_mapping.md`.
- `samples/template/ANZ-GENERIC_NoCalculation.xlsx` — HP output template (no Margin column populated). Field mapping in `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_Uplift.xlsx` — HP output template (Margin column populated). Field mapping in `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_PercentOffWithUplift.xlsx` — HP OneConfig output template (Margin col K + IM% col X always populated; MSRP on H, Cost col I blank). Field mapping in `docs/hp_oneconfig_xlsx.md`.
- `docs/nutanix_software_only_pdf.md` — human-written extraction spec for the "Software Only (PDF)" format (Nutanix subscription quotes).
- `docs/nutanix_software_only_xlsx.md` — human-written extraction spec for the "Software Only (XLSX)" format (same data as Software Only PDF, delivered as a workbook).
- `docs/nutanix_renewal_pdf.md` — human-written extraction spec for the "Renewal (PDF)" format (Nutanix subscription renewals with serial/license numbers).
- `docs/nutanix_hardware_only_pdf.md` — human-written extraction spec for the "Hardware Only (PDF)" format (Nutanix multi-quote PDF; we only parse Quote D, the reseller-facing breakdown).
- `docs/nutanix_hardware_only_xlsx.md` — human-written extraction spec for the "Hardware Only (XLSX)" format (Nutanix multi-quote workbook; we only parse Quote D, the reseller-facing breakdown — same sub-quote as the PDF version).
- `docs/hp_oneconfig_xlsx.md` — extraction spec for the HP OneConfig (XLSX) format. Single Config per file, parent VPN from `Config ID`, MSRP from `Total Price`, 30 children zeroed. Output template `% Off RRP with Uplift`; requires `margin` and `im_percent` per parse.
- `docs/lenovo_brda_dcg_xlsx.md` — extraction spec for the Lenovo BRDA DCG (XLSX) format. Legacy `.xls` (OLE Compound Document) read via ExcelDataReader; PARENT/CHILD classified by whether the unit-price cell is `> 0` (an explicit `0.0` is a child, not a parent — `5374CM1` "Configuration Instruction" rows depend on this). LineSequence is integer for parents (`"1"`, `"2"`), `parent.NN` for children (`"1.01"`, `"1.02"`). Quoted total comes from the `Total:` row in the unit-price column; rounded to 2 dp to flatten the workbook's float artefact.
- `docs/lenovo_brda_dcg_pdf.md` — extraction spec for the Lenovo BRDA DCG (PDF) format. Two-section PDF: "PRODUCT AND SERVICE DETAILS" (CONFIGs + PAREENTs) and "CONFIGURATION DETAILS" (per-component children). Key quirks: X1+1-based column boundaries for Part Number and Description; floating-point 1 pt offset for No and Qty columns in section 2; pre-description buffering for parent rows whose description cluster precedes the VPN row in y-order.
- `docs/output_mapping.md` — defines how parsed `LineItem` fields are written into the `ANZ-GENERIC_ForeignUplift.xlsx` template, the output filename convention, and the locked output rules (e.g. MSRP column H stays empty; `serial_number` lands in Comments not Serial Number; term written only when `>= 1`). Read this before generating any `*_parsed.xlsx` file.
- `docs/design/` — Claude Design handoff bundle for the V4 side-panel UI. Read `docs/design/README.md` before implementing frontend work.

Implementation notes:

- Parser slugs, package names, and spec docs are vendor-prefixed for future supplier expansion. Use `nutanix_software_only_pdf`, `nutanix_software_only_xlsx`, `nutanix_renewal_pdf`, `nutanix_hardware_only_pdf`, and `nutanix_hardware_only_xlsx`; do not reintroduce vendorless parser slugs.
- The frontend is locally runnable with Vite and proxies `/api` to the backend at `http://127.0.0.1:5000` by default. If port 5000 is occupied, run Vite with `VITE_API_PROXY_TARGET=http://127.0.0.1:<port>` to point at an alternate backend.
- In Docker, the image defaults place `DATABASE_URL` and `UPLOAD_DIR` under `/data` (`/data/db.sqlite` and `/data/files`). Do not set these explicitly in `docker-compose.yml` unless changing the internal container layout. `docker-compose.yml` mounts `/data` from either the default `bidparser-data` Docker volume or a user-supplied `DATA_DIR` bind mount.
- `RetentionBackgroundService` (sleep-first, 24 h cadence) calls `RetentionService.CleanupOldParseJobsAsync` to delete expired `ParseJob` rows and their source+output files, and expired `FailedParseJob` rows and their source files, after `RETENTION_DAYS`. `ParseMetric` rows are retained indefinitely — their `parse_job_id` is automatically nulled by the SQLite `ON DELETE SET NULL` FK when the parent `ParseJob` is purged.
- `ChangePasswordPage` relies on the `App.tsx` route guard (every other route redirects to `/change-password` when `must_change_password=true`) to keep the user on the page until they pick a new password — no `useBlocker` is used (it requires a data router and breaks under the declarative `BrowserRouter`).
- The vendor-specific settings block (Nutanix `EXCHANGE RATE` + `MARGIN` inputs and the emerald `CRM IMPORT TEMPLATE` callout) renders in the dashboard only when **both** a vendor and a file type are selected. Until then, only the two cascading selects appear above the Upload & parse button.
- `User.Name` is a nullable display name surfaced in the UI (`AccountChip` headline, `SettingsPage` user cards) and on future reporting. Admin-issued creates require it; the bootstrap admin starts with `Name="Administrator"`.
- Golden fixture files in `samples/outputs/` are named `<basename>_parsed.xlsx` (one per quote number, not per input file). PDF and XLSX parsers for the same quote both compare against the same golden file.
- Hardware parser tests include a negative assertion checking that NX-1175S-G10-6517P-CM has Quote D's cost (USD 20,017.57), not Quote C's (USD 5,903.72).

Current implementation checkpoint:

- Backend is ASP.NET Core 10 (re-platformed from the original Python/FastAPI implementation).
- Production hardening complete: locked-down forwarded headers (KnownProxies allowlist, rejects `"*"` in production), `GlobalExceptionHandler` returning safe ProblemDetails (no exception messages leaked), per-user token-bucket rate limit on `/api/parse`, magic-byte upload validation (`%PDF` / `PK\x03\x04`), `SecurityHeadersMiddleware` (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, HSTS on HTTPS), composite `(user_id, created_at DESC)` history index, case-insensitive filename search (`TEXT COLLATE NOCASE` + escaped `EF.Functions.Like`), structured logging across endpoints with no secrets, full `CancellationToken` propagation, typed response records (`Contracts/`), centralised vendor/slug/template constants (`Domain/Constants/`), primary constructors on services, and `AddDbContextPool` registration.
- All 173 tests pass (`dotnet test BidParser.sln`): 101 parsing + 72 API integration.
- Frontend is React 19 + Vite + TypeScript + `recharts ^2`, proxying `/api` to `http://127.0.0.1:5000` in dev.
- GitHub Actions CI/CD publishing is enabled: pushes to `main` and `v*` SemVer tags build and publish Docker images to GHCR.

Sample → format mapping:

| Sample file        | Format                 | Notes                                  |
|--------------------|------------------------|----------------------------------------|
| `XQ-4076249.pdf`   | Software Only (PDF)    | Compact 6-column layout                |
| `XQ-4076249.xlsx`  | Software Only (XLSX)   |                                        |
| `XQ-4108785.pdf`   | Hardware Only (PDF)    |                                        |
| `XQ-4108785.xlsx`  | Hardware Only (XLSX)   |                                        |
| `XQ-4128926.pdf`   | Renewal (PDF)          |                                        |
| `XQ-4166696.pdf`   | Renewal (PDF)          | Wrapped currency amount (USD on separate line)  |
| `XQ-4029825.pdf`   | Renewal (PDF)          | Platform-column variant: extra `Platform` column, wrapped Product Code, hardware rows emit `Description = "Platform: {value}"` |
| `XQ-4157308.pdf`   | Software Only (PDF)    | Extended 9-column layout, 1 line item  |
| `XQ-4165884.pdf`   | Software Only (PDF)    | Extended 9-column layout, wrapped SKUs |
| `Deals20260518T034809_HPI.xlsx` | HP Bid (XLSX) | 479 items: Part Number × 3, Bundle × 14, Bundle Detail × 462; includes `#`-concatenated Option Code example |
| `Deals20260518T043243_HPI.xlsx` | HP Bid (XLSX) | 5 Part Number rows only; Part-Number-only file |
| `55648855.xlsx`                  | HP OneConfig (XLSX) | 1 Config + 30 components; AUD 6,042.77 Total Price |
| `BRDAS010260417V1.pdf` | Lenovo BRDA DCG (PDF) | 152 items: 2 configs, 13 parents, 137 children (self-component dedup); AUD 393,231.78 |
| `BRDAD010458440.xls`   | Lenovo BRDA DCG (XLSX) | Legacy .xls binary; 62 items: 8 parents, 54 children; AUD 103,542.60 |

## What is being built

An internal web app for sales operations. Users upload a supplier quote (PDF or XLSX in various supplier-specific layouts), the app extracts and validates the line items, and the user reviews them before a (future) export step writes the standardised XLSX. Stack: ASP.NET Core 10 backend + React/Vite/TypeScript frontend, deployed as Docker via `docker-compose` on an internal server.

Work is **iterative, one format at a time**. Formats spec'd so far: "Software Only (PDF)", "Software Only (XLSX)", "Renewal (PDF)", "Hardware Only (PDF)", and "Hardware Only (XLSX)". The architecture is built around a pluggable `IParser` registry so adding the next format means dropping in one parser class, one set of fixtures, and one registry entry — nothing else.

**Anchor-based extraction is mandatory.** Every parser must locate sections, headers, totals, and column positions by searching for anchor strings in the source document. Never hard-code row numbers, column letters, or fixed offsets. The same workbook can contain multiple quote sections where row positions shift across samples (more line items above push everything down) and column letters differ between sections (Quote C uses column H for `Product Code`; Quote D in the same file uses column E). Hard-coded positions will break across real-world quotes.

## Architecture (planned)

Single contract that every parser returns and the frontend consumes:

```
ParseResult
├── Metadata        QuoteMetadata (QuoteNumber, Supplier, Currency, QuotedTotal, SourceFilename, ParserSlug)
├── LineItems       IReadOnlyList<LineItem>
└── Validation      ValidationResult (ComputedTotal, QuotedTotal, Matches, Difference, Warnings)

LineItem — superset of fields across formats. Only VPN, Cost, Qty are
required; the rest are nullable and populated per format:
  Required: VPN, Cost, Qty
  Software Only (PDF + XLSX):  Description, Term, Msrp
  Renewal:                     Msrp, SerialNumber, StartDate, EndDate
  Hardware Only (PDF + XLSX):  Description, Term, Msrp    // Term is per-row nullable
  Common debug:                Raw (Dictionary<string, string> of original column text)

Across formats: List Price / List Unit Price / MSRP / Term Adjusted List Unit Price
→ Msrp; Sale Price / Net Unit Price / Cost Price → Cost. Software Only PDF and
XLSX produce the same shape; Hardware Only PDF and XLSX likewise produce the same
shape (both extract from Quote D and yield 11 rows totalling $22,491.87).
```

Dates are stored internally as `DateOnly` (serialised as ISO `YYYY-MM-DD` in JSON); display formatting (`DD/MM/YYYY` per the Renewal spec) is a frontend concern, not a model concern.

### Canonical naming (display headers and field names)

User-locked vocabulary. Display headers (Title Case, used in the UI table and any chat-rendered tables) and internal field names (snake_case, used by the domain model, JSON API, and tests) are decoupled but one-to-one:

| Concept | Display header | Field name |
|---|---|---|
| Part number | `Part Number` | `vpn` _(Vendor Part Number)_ |
| Description | `Description` | `description` |
| Term in months | `Term` | `term` |
| List / catalogue price | `List Price` | `msrp` |
| Customer price | `Sale Price` | `cost` |
| Quantity | `Quantity` | `qty` |
| Serial number (incl. embedded license, if any) | `Serial Number` | `serial_number` |
| Subscription start | `Start Date` | `start_date` |
| Subscription end | `End Date` | `end_date` |
| Minimum order quantity | `Min Order Qty` | `min_qty` | HP only; null for Nutanix |
| Output line sequence | `Item` | `line_sequence` | HP only; written to col A of ANZ-GENERIC templates; null for Nutanix |

Parsers still detect *source* labels in the document (`"Net Unit Price"`, `"List Unit Price"`, `"Sale Price"`, `"MSRP"`, etc.) when locating columns — those are extraction-time anchors, not internal field names. The source label is captured in `raw[source_label]` for debugging; the cleaned value is written to the canonical field. **Old names (`part_number`, `cost_price`, `term_months`, `quantity`) must not appear in new code or docs.**

Key extension point: `src/BidParser.Parsing/Registry/ParserRegistry.cs` holds an explicit `IReadOnlyList<IParser>`. Auto-discovery is deliberately avoided — registration order is visible. Each parser lives in its own subfolder (`src/BidParser.Parsing/Nutanix/<Slug>/`).

Format detection is a **soft hint, not a routing decision**: `BaseParser.detect()` returns a 0.0–1.0 confidence and the UI pre-fills the dropdown when confidence > 0.7, but the user always confirms before parsing runs. Silent mis-routing is worse than one extra click.

Validation logic is identical across formats: `computed_total = Σ(cost × qty)` compared to `quoted_total` with `0.01` tolerance. The UI gates the (stub) Approve button on a match unless the user explicitly overrides.

## Common PDF parsing approach

All PDF formats use **UglyToad.PdfPig** (MIT, word-level bounding boxes) via `PdfWordCollector`. PdfPig uses a bottom-left coordinate origin; Y is flipped in `PdfWord` construction so all downstream code uses top-left origin (same as pdfplumber). `PdfPigWordSplitter` re-splits tokens PdfPig merges using per-letter `GlyphRectangle` bounds. The shared technique:

- Open the PDF and collect `PdfWord` records from all pages with their page index preserved.
- Find the header row by locating its first-cell anchor word(s) (e.g. `"Product"` + `"Code"` for Software Only, `"No"` for Renewal).
- Derive column x-ranges from the header word x0s — each column's range is `[its x0, next header's x0)`; rightmost extends to page width.
- Collect body words below the header y across pages until a `"TOTAL:"` token.
- Cluster words into rows by `top` (tolerance ~3pt). Bucket each row's words into columns by `x0`; join intra-bucket words with single spaces.
- Locate the quoted total by scanning after the last body row for `TOTAL:` + `USD` + amount, tolerating page-break wrap.

Shared helpers live in `src/BidParser.Parsing/Pdf/`: `PdfWordCollector`, `PdfWord`, `PdfPigWordSplitter`, `PdfTableHelpers` (column ranges, row clustering, total scanning).

## Common XLSX parsing approach

XLSX formats use **ClosedXML** (`new XLWorkbook(path)` — cached formula values by default). Use `cell.GetFormattedString()` everywhere to match Python/openpyxl's stringy cell values; do not use the typed `XLCellValue` which returns numbers/blanks. The shared technique:

- Open the workbook, pick the relevant sheet (usually the only one, or the one named like the quote number).
- Locate the **header row** by scanning the sheet for a cell whose value matches a known anchor label (e.g. `Product Code`). Do not assume a fixed row number — quote metadata above the table varies.
- Capture the header row's column numbers and map each known label (`Product Code`, `Product Description`, …) to its column.
- Iterate data rows below the header. Stop at the first wholly-empty row, or at a recognisable footer (e.g. a cell containing `TOTAL $...`).
- Locate the quote total by scanning for a cell whose value starts with `TOTAL ` followed by a currency string, or by finding a row labelled `TOTAL` and reading the adjacent value cell.
- Currency strings use `$` and thousands separators (e.g. `$2,275.00`), not `USD ` — `DecimalCleaner.Parse` strips `$`, `,`, `USD`, and whitespace.

Shared helpers live in `src/BidParser.Parsing/Xlsx/`: `WorkbookReader`, `HeaderMap`.

## Software Only (PDF) — extraction algorithm

Header anchor: `"Product"` immediately followed (same y-band ±3pt, increasing x) by `"Code"`. Columns: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Net Unit Price`, `Quantity`.

Row classification:
- `Product Code` cell trims to `"Term-Months"` OR `Product` cell is `"Term in months"` → **Term-Months row** (KEEP as a line item with sentinel-zero pricing, and reset continuation grouping).
- `Product Code` matches `^[A-Z0-9-]+$` after trim → **anchor row** (new `LineItem`).
- `Product Code` empty AND `Product` non-empty → **continuation row** (append `Product` text to current anchor's description).
- Otherwise → ignore.

Per-field handling: flatten description by joining continuation snippets with single spaces and collapsing internal whitespace; strip `USD`/commas from prices → `Decimal`; `term` and `qty` → `int`.

Edge cases the tests must cover:
- 3–5 line description wrap per item.
- `Term-Months` row kept as a line item.
- `TOTAL:` wrapping onto a later page.
- Thousands separators in prices (`2,275.00`).

Expected output for `XQ-4076249.pdf` (already validated by hand — use these as the golden test values):

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

Same line items, same supplier (Nutanix), different envelope. Produces the same `LineItem` shape as Software Only (PDF) so a downstream consumer cannot tell which parser was used.

Sheet selection: in the sample there is a single sheet named after the quote (`XQ-4076249`). The parser should open the active sheet and not hard-code a name.

Section + header location (all anchor-based — no fixed rows or columns):
1. Scan the sheet for a cell whose value is the literal string `Quote Number`. This is the top-left cell of the line-item header row — the line-item grid's leftmost column is `Quote Number`, not `Product Code`.
2. That cell's row is the header row. Build a label-to-column-letter map by walking every cell in that row.
3. Required labels: `Product Code`, `Product Description`, `Term (Months)`, `List Price`, `Sale Price`, `Quantity`. The header row also contains other labels (`Quote Number`, `Quote Name`, `Line Name`, `Payment Terms`, `Total Discount (%)`, `Amount`, …) — ignore those for field extraction.
4. Iterate data rows below the header. Stop at the first wholly-empty row or the first cell whose value starts with `TOTAL ` (whichever comes first).

Row classification (data rows below the header):
- `Product Code` cell trims to `Term-Months` → **Term-Months row** (KEEP as a line item with sentinel-zero pricing).
- `Product Code` cell non-empty and not `Term-Months` → **anchor row** (new `LineItem`). One row per item — no continuation rows in this format because cells don't wrap.

Per-field handling:
- **Part Number** (`vpn`): trim source `Product Code`.
- **Description** (`description`): trim source `Product Description` (single cell, no flattening needed).
- **Term** (`term`): source `Term (Months)` is already numeric (e.g. `60`) → `int`.
- **List Price** (`msrp`): source `List Price` is a string like `$383.00` or `$2,275.00` — strip `$`, commas, whitespace → `Decimal`.
- **Sale Price** (`cost`): source `Sale Price` is a string like `$101.11` or `$600.60` — same cleaner → `Decimal`.
- **Quantity** (`qty`): source `Quantity` is numeric → `int`.

Total location: scan downward from the last data row for a cell whose value starts with the literal string `TOTAL ` (e.g. `TOTAL $1,625,358.51`). Strip the `TOTAL`, `$`, and commas → `Decimal`. A second `TOTAL` label often appears further down on a separate row with the amount in an adjacent column — treat this as a fallback only if the first form isn't found.

Edge cases the tests must cover:
- Header row position not fixed (metadata block above the table can grow across samples).
- `Term-Months` rows present and correctly kept.
- Currency strings prefixed with `$` (not `USD `), with thousands separators (`$2,275.00`).
- `Total Discount (%)` sits between `List Price` and `Sale Price` in the sample, so the label-to-column map must use header text — never assume adjacency or fixed column letters.

Expected output for `XQ-4076249.xlsx` (already validated by hand — golden values match the PDF version):

| Part Number      | Term | List Price | Sale Price | Quantity |
|------------------|------|------------|------------|----------|
| SW-NCM-STR-PR    | 60   | 383.00     | 101.11     | 2096     |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275.00    | 600.60     | 864      |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275.00    | 600.60     | 1232     |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCI-E-PRO-PR  | 60   | 3455.00    | 912.12     | 145      |
| Term-Months      | 60   | 0          | 0          | 60       |
| SW-NCM-E-STR-PR  | 60   | 583.00     | 153.91     | 145      |
| Term-Months      | 60   | 0          | 0          | 60       |

Computed total = quoted total = `$1,625,358.51`.

## Renewal (PDF) — extraction algorithm

Header anchor: word `"No"` in the top-left of the table header. Columns: `No`, `Product Code`, `Serial Number`, `Start Date`, `End Date`, `Term Adjusted List Unit Price`, `Total Discount`, `Net Unit Price`, `Qty`, `Total Net Price`. Note the header labels themselves span multiple visual lines in this layout (`Term`/`Adjusted`/`List Unit`/`Price` are stacked vertically) — the column-anchor logic must tolerate multi-line header text.

Row classification:
- `No` cell is a positive integer AND `Product Code` non-empty → **anchor row** (new `LineItem`).
- `No` empty AND any other column non-empty → **continuation row** (append text per column to the current anchor — the `Serial Number` column wraps so this is how the second token arrives).
- Otherwise → ignore.

Per-field handling:
- **Part Number** (`vpn`): trim `Product Code` cell.
- **Serial Number** (`serial_number`): the source `Serial Number` cell wraps across two lines (e.g. `24SW000351227,\nLIC-02472987`). Flatten by joining the fragments with **no separator** (the comma is at the end of the first line), then trim leading/trailing whitespace and collapse internal whitespace. The resulting value (e.g. `24SW000351227,LIC-02472987`) is stored as a single string. **We do not split it into separate serial/license fields** — the supplier's punctuation is preserved verbatim and downstream consumers handle the combined string.
- **Start Date / End Date** (`start_date`, `end_date`): source is `MM/DD/YYYY`; parse with `DateOnly.ParseExact("MM/dd/yyyy")` and store as a `DateOnly`. The frontend formats as `DD/MM/YYYY` for display.
- **List Price** (`msrp`): strip `USD` and commas from the source `Term Adjusted List Unit Price` column → `Decimal`.
- **Sale Price** (`cost`): strip `USD` and commas from the source `Net Unit Price` column → `Decimal`.
- **Quantity** (`qty`): source `Qty` column → `int`.

The Renewal layout has no Term-Months sub-header to skip. The `Total Net Price` column wraps onto the next line because the column is narrow, but we don't extract that field — the validation comes from the `TOTAL:` line.

**Platform-column variant** (`XQ-4029825`): an optional `Platform` column sits between `No` and `Product Code`. The parser detects it from the header band (searches for the word `"Platform"`) and adds it to the column-range map only when present — backward-compatible with the base samples. When a row carries a non-empty Platform value (itself potentially wrapping across two lines, joined without separator), `LineItem.Description` is set to `"Platform: {value}"` (e.g. `"Platform: NX-8035N-G8-HY"`); rows with no Platform value leave `Description` null. Product Code also wraps in this variant (e.g. `RSW-NCI-` / `ULT-PR`); fragments are joined without a separator via `JoinUnspaced`, same as Serial Number and Platform.

**USD-prefix fusion:** before column bucketing, `NutanixRenewalPdfParser.FuseCurrencyTokens` walks the word stream and pairs each `"USD"` token with its nearby numeric amount (forward window of 6 words; spatial tolerance `Top ∈ [USD.Top − 3.5, USD.Top + 15.0]`, same page). The pair is collapsed into one synthetic `PdfWord` anchored at the *amount's* coordinates, so wider or wrapped amounts (e.g. `USD` left-aligned on one line, `1,121.00` right-aligned on the line below) always land in the correct price column. Without this, the `USD` prefix and the numeric token can straddle the column boundary and `DecimalCleaner.Parse` throws on a bare `'USD'`.

Edge cases the tests must cover:
- `Serial Number` cell wrapping across two lines (the comma is at the end of the first line, so the no-separator join is correct).
- `Net Unit Price` and `Qty` running together visually with no space (e.g. `USD 54.41 160`) — column x-ranges disambiguate.
- `TOTAL: USD ...` itself wraps onto two lines (`TOTAL:` and `USD` on one line, the amount on the next).
- The serial cell could in principle contain only a serial with no embedded license/comma — the no-separator join still produces a valid single-string value; no special handling needed.
- Wrapped-currency layout (`XQ-4166696.pdf`): `USD` sits on a different visual line from its amount. `FuseCurrencyTokens` joins them so column assignment uses the amount's x position.

Expected output for `XQ-4128926.pdf` (already validated by hand — golden values):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 24SW000351227,LIC-02472987  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |
| RSW-NCI-ULT-PR  | 24SW000351236,LIC-02472996  | 2026-07-13  | 2027-07-12  | 575        | 371.83     | 32       |
| RSW-NCI-ULT-PR  | 24SW000351221,LIC-02472983  | 2026-07-13  | 2027-07-12  | 575        | 429.11     | 72       |
| RSW-NCM-STR-PR  | 24SW000351228,LIC-02472985  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |

Computed total = quoted total = `USD 60,205.68`.

Expected output for `XQ-4166696.pdf` (wrapped-currency sample, hand-validated golden values):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 25SW000430057,LIC-02537784  | 2026-06-17  | 2028-12-01  | 189        | 54.64      | 80       |
| RSW-NCI-PRO-PR  | 25SW000430055,LIC-02537786  | 2026-06-17  | 2028-12-01  | 1121       | 661.61     | 80       |
| RSW-NCM-STR-PR  | 25SW000430056,LIC-02537783  | 2026-10-28  | 2028-12-01  | 161        | 40.20      | 400      |
| RSW-NCI-PRO-PR  | 25SW000430054,LIC-02537785  | 2026-10-28  | 2028-12-01  | 955        | 755.64     | 400      |

Computed total = quoted total = `USD 375,636.00`.

Expected output for `XQ-4029825.pdf` (Platform-column variant, hand-validated golden values):

| Part Number     | Description               | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|---------------------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCI-ULT-PR  | _(empty)_                 | 25SW000437991,LIC-02543011  | 2026-08-16  | 2029-12-31  | 1943       | 354.77     | 448      |
| RSW-NCI-ULT-PR  | _(empty)_                 | 25SW000437992,LIC-02543012  | 2026-08-16  | 2029-12-31  | 1943       | 601.52     | 192      |
| RSW-NCI-PRO-PR  | _(empty)_                 | 22SW000262928,LIC-01461229  | 2026-11-03  | 2029-12-31  | 1440       | 889.43     | 128      |
| RS-HW-PRD-MY    | Platform: NX-8035N-G8-HY | 22SH3G410326                | 2026-11-03  | 2029-07-31  | 2676.24    | 1957.37    | 1        |
| RS-HW-PRD-MY    | Platform: NX-8035N-G8-HY | 22SH3G410327                | 2026-11-03  | 2029-07-31  | 2676.24    | 1957.37    | 1        |

Computed total = quoted total = `USD 392,190.58`.

## Hardware Only (PDF) — extraction algorithm

This PDF bundles multiple stacked quote sections (Quote A, Quote B, Quote C, Quote D) in a single file. **We only parse Quote D** — the reseller-facing breakdown. Quote C in the same PDF is a separate budgetary breakdown of pure components; ignore it.

Section + header location (all anchor-based — no fixed pages or positions):
1. Scan all words across pages for the literal sequence `Quote D For distributor to quote to the reseller only`. That word position is the Quote D banner.
2. From the banner's y position, scan downward (continuing across pages) for the line-item header row — the word `Product` immediately followed (same y-band ±3pt, increasing x) by `Code`.
3. From the header row, derive column x-ranges for the seven labels: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Total Discount`, `Net Unit Price`, `Quantity`, `Total Net Price`. Each column's range is `[its leftmost x, next header's leftmost x)`; rightmost extends to page width.
4. Collect body rows below the header until the literal `TOTAL:` marker — this both terminates Quote D and carries its quoted total.

Row classification:
- `Product Code` cell non-empty after trim → **anchor row** (new `LineItem`). Both SKU-shaped strings (`NX-1175S-G10-6517P-CM`) and plain labels (`Support-Term`, `Platform Integration`) qualify.
- `Product Code` empty AND at least one other column non-empty → **continuation row**. Append text per column to the corresponding field on the current anchor — both `Product Code` and `Product` wrap independently in this format.
- Otherwise → ignore.

**Every classified line item is kept** — no filler rows are skipped. In particular the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) is a real line item here. This matches the rule for Software Only formats where `Term-Months` rows are also kept.

Per-field handling:
- **Part Number** (`vpn`): concatenate the anchor + continuation `Product Code` snippets with **no separator** (e.g. `NX-1175S-G10-` + `6517P-CM` → `NX-1175S-G10-6517P-CM`). Trim whitespace at the end. Keep non-SKU labels verbatim.
- **Description** (`description`): join anchor + continuation `Product` snippets with single spaces; collapse internal whitespace.
- **Term** (`term`): per-row nullable. `int` when populated (e.g. `60`); null when the source `Term (Months)` cell is empty.
- **List Price** (`msrp`): strip `USD` and commas from source `List Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null** (bundled-component rows).
- **Sale Price** (`cost`): strip `USD` and commas from source `Net Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null** (same rule).
- **Quantity** (`qty`): `int`. On the `Support-Term` row this cell holds the term value (`60`) rather than a count — keep as-is.

Edge cases the tests must cover:
- Multi-quote PDF: ensure rows from Quote A, B, or C don't appear in Quote D's output. A negative-assertion test (a part number unique to Quote C that must not appear) is the cheapest guard.
- Part Number wrap: several SKUs wrap (`NX-1175S-G10-`/`6517P-CM`, `C-NVM-7.68TB-`/`AB1A-CM`, `C-PWR-4FC13C14A-`/`CM`). The no-separator join is critical.
- Description wrap: 1–4 lines per item.
- Bundled-component rows: Product Code + Description + Quantity populated, but Term / List / Discount / Net Unit Price cells empty → emit `0` for both prices, null for term.
- `Support-Term` is **not** a sub-header — keep it.
- The `TOTAL:` line may sit on the same row as `USD <amount>` (Quote D in the sample) or wrap to a separate row (Software Only PDF) — handle both.

Expected output for `XQ-4108785.pdf` Quote D (already validated by hand — golden values):

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

XLSX counterpart of Hardware Only (PDF). Both extract from **Quote D** of the same engagement and produce the same line items and total (`$22,491.87`); the parsers differ only in how they read the source. The workbook contains multiple stacked quote sections (Quote A, B, C, D). Only Quote D is in scope.

Section + header location (all anchor-based — no fixed positions):
1. Scan the sheet for a cell whose value is `Quote D For distributor to quote to the reseller only`. That cell's row is the Quote D banner.
2. From the banner row, scan downward for the next cell whose value is `Product Code`. That cell's row is the line-item header row.
3. Build a label→column-letter map from that header row. **Do not assume column letters carry over from Quote C** — in the sample, Quote C has `Product Code` in column H, Quote D has it in column E.
4. Stop at the first cell below the header whose value starts with `TOTAL ` — that cell ends the section and carries the quoted total.

Row classification: every non-empty row between the header row and the `TOTAL ` row is a line item. **No filler rows are skipped in this format** — in particular the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) *is* a kept line item here. This matches Software Only where the analogous `Term-Months` row is also kept.

Per-field handling:
- **Part Number** (`vpn`): trim source `Product Code`. Some rows have non-SKU labels (`Support-Term`, `Platform Integration`) — keep them verbatim.
- **Description** (`description`): trim source `Product Description`. Single cell, no wrap.
- **Term** (`term`): integer in the source when populated; **may be empty per row** (typical for hardware components). Emit as null/empty when empty, not 0. Do not invent values.
- **List Price** (`msrp`): string like `$25,021.99` — strip `$`, commas, whitespace → `Decimal`. **When the cell is empty (bundled-component rows that carry no own price), emit `Decimal("0")` — not null.**
- **Sale Price** (`cost`): same cleaner as List Price; same `0` rule for empty cells.
- **Quantity** (`qty`): numeric in the source → `int`. Note: on the `Support-Term` row the `Quantity` cell carries the term value (e.g. `60`) rather than a count — keep what the cell says.

Edge cases the tests must cover:
- Multi-quote XLSX: ensure rows from Quote A, B, or C don't appear in Quote D's output. A negative-assertion test (a part number unique to Quote B that must not appear) is the cheapest guard.
- Column-letter drift inside the same workbook: Quote C and Quote D have different `Product Code` columns. The header-map must be rebuilt per section.
- Bundled-component rows: Product Code + Description + Quantity populated, but List Price and Sale Price cells empty → emit `0` for both prices.
- `Term (Months)` empty on hardware rows but populated on the support row → field nullability is per-row, not per-format.
- `Support-Term` row's `Quantity` cell holds the term value (`60`), not a count — keep as-is.

Expected output for `XQ-4108785.xlsx` Quote D (already validated by hand — golden values):

| Part Number             | Term | List Price | Sale Price | Quantity |
|-------------------------|------|------------|------------|----------|
| NX-1175S-G10-6517P-CM   | —    | 25021.99   | 20017.57   | 1        |
| C-MEM-32GB-6400-CM      | —    | 0.00       | 0.00       | 4        |
| C-HDD-12TB-ETBA-CM      | —    | 0.00       | 0.00       | 2        |
| C-NVM-7.68TB-AB1A-CM    | —    | 0.00       | 0.00       | 2        |
| C-HBA-3816-1N-C-CM      | —    | 0.00       | 0.00       | 1        |
| C-NIC-25G4E1-CM         | —    | 0.00       | 0.00       | 1        |
| C-PWR-4FC13C14A-CM      | —    | 0.00       | 0.00       | 2        |
| S-HW-PRD                | 60   | 4019.99    | 2411.99    | 1        |
| Support-Term            | 60   | 0.00       | 0.00       | 60       |
| C-TPM-2.0-U-C-CM        | —    | 77.89      | 62.31      | 1        |
| Platform Integration    | 0    | 4003.51    | 0.00       | 1        |

Computed total = quoted total = `$22,491.87` (the zero-priced rows contribute `$0` and don't disturb the sum).

## MVP scope & guardrails

Locked-in product decisions. Anything outside these is deferred — flag scope drift to the user before building.

- **Single vendor**: Nutanix only. The architecture is registry-driven so Dell/Lenovo drop in later, but no other vendor ships in MVP.
- **Single-file upload** per parse. The "DRAG MULTIPLE FILES TO BATCH PARSE" hint is a future-state affordance.
- **Auto-download flow**, no review screen. User clicks *Upload & parse* → dropzone morphs into a progress panel → the `_parsed.xlsx` downloads automatically. Validation runs server-side; a match surfaces as a green success toast; a mismatch surfaces as a **blocking modal** (`ValidationWarningModal`) that the user must acknowledge (OK button) before the file is downloaded — the download does not begin until after acknowledgement.
- **Per-user remembered vendor, FX rate & margin** — last values used by each user are persisted on their account and pre-fill on next login.
- **Env-var admin bootstrap** — `ADMIN_USERNAME` / `ADMIN_PASSWORD` seed the first admin on a fresh DB (defaults `admin` / `changeme`, created with `must_change_password=True`). Env vars are ignored once any user row exists.
- **Stack**: ASP.NET Core 10 backend + React/Vite/TypeScript frontend, packaged as a single Docker image.

Out of scope for MVP: multi-file batch upload, CSV vendor formats, vendors other than Nutanix, review/approve gate, multi-tenancy/org boundaries, email notifications, SSO, audit log beyond ParseJob history.

## Authentication & authorisation

- **Passwords**: bcrypt cost factor 12. Password rules enforced on `/auth/change-password` only — bootstrap and admin reset both write the literal `changeme` and rely on `must_change_password` to force compliance on next login. Rules: ≥ 8 chars, at least one uppercase, one digit, one symbol.
- **Sessions**: ASP.NET Core Data Protection cookies (`bidparser_session`), HttpOnly, SameSite=Lax, hard 12-hour expiry from login — no sliding refresh. The `Secure` flag is set only when the request resolves to HTTPS after `ForwardedHeadersMiddleware` processing (so local HTTP dev still works; production behind NPM gets `Secure=True` via `X-Forwarded-Proto`). `SESSION_SECRET` is the app-name discriminator, not the signing key — see Operational config for details.
- **CSRF**: every non-GET endpoint requires `X-Requested-With: BidParser` set by `frontend/src/api/client.ts`. Combined with SameSite=Lax, this is sufficient for an internal app.
- **Rate limiting on `/auth/*`**: 5 attempts per minute, two independent buckets — per remote IP across `/auth/login` + `/auth/change-password`, AND per submitted username on `/auth/login` (applied pre-auth so attackers can't enumerate one username from many IPs). Either bucket tripping returns `429` with `Retry-After` and a generic body. In-memory leaky bucket; does not survive restart.
- **`must_change_password` gate**: when the current user has the flag set, the backend returns `403 password_change_required` for every endpoint except `/auth/*` and `/me`. The frontend redirects to `/change-password` and `App.tsx` route guards keep the user there.
- **Authorization policies are per-endpoint, not path-globbed.** A path-globbed middleware would let locked users update `/me/settings`. The three policies are:
  - `LoggedIn` — valid session cookie; `must_change_password` is **ignored**. Used by `/auth/logout`, `/auth/change-password`, `GET /me`.
  - `ActiveUser` — `LoggedIn` **and** `must_change_password=false`. Used everywhere else under `/api` except admin routes.
  - `Admin` — `ActiveUser` + `role == "admin"`.
- **Last-admin guard**: `PATCH /api/users/{id}` and `DELETE /api/users/{id}` return `409 Conflict` if the operation would leave zero admins, or if it targets the calling admin themselves.
- **Password recovery**: no self-service. Admin `PATCH /api/users/{id}` with `{reset_password: true}` writes `changeme` and sets `must_change_password=True`.

## API surface (all under `/api`)

| Method | Path | Auth | Notes |
|---|---|---|---|
| `POST` | `/auth/login` | none | Body `{username, password}`. Rate-limited. Returns the user object and sets the session cookie. |
| `POST` | `/auth/logout` | user | Clears the current session cookie only. Other devices stay logged in until their own 12h expires. |
| `POST` | `/auth/change-password` | user | Body `{old_password, new_password}`. Enforces password rules. Clears `must_change_password`. |
| `GET`  | `/me` | user | Returns the current user shape. |
| `PATCH` | `/me/settings` | user | Body `{default_vendor?, fx_rate?, margin?}`. Updates per-user remembered defaults. |
| `GET` | `/parsers` | user | Returns the registry — each entry includes `slug`, `display_name`, `vendor`, `accepted_mime`, `crm_template`. Drives the vendor → file-type cascade. |
| `POST` | `/parse` | user | Multipart: `file`, `vendor`, `parser_slug`, `fx_rate`, `margin`. Max 10 MB enforced both sides. See response headers below. |
| `GET` | `/history` | user | `?limit=&offset=&q=` — user-scoped. `q` is a case-insensitive substring filter on `source_filename`. `when` is a server-computed relative time string ("just now", "5m ago", "Yesterday", "3 days ago", then absolute date). |
| `GET` | `/history/{id}/source` | user | Streams the stored original. 404 if foreign user or expired. |
| `GET` | `/history/{id}/output` | user | Streams the parsed `*_parsed.xlsx`. Same gating. |
| `GET` | `/users` | admin | Admin-only user CRUD. `POST` requires `{username, name, role}`; password is set to `changeme` + `must_change_password=True`. `PATCH` accepts `{username?, name?, role?, reset_password?}`. |
| `GET` | `/metrics/summary` | admin | `?from=YYYY-MM-DD&to=YYYY-MM-DD&vendor=&userId=&parserSlug=`. Default window is last 30 days, server-local TZ inclusive. Returns `{range, kpis, by_user, by_vendor, by_parser, time_series}`. `mismatch_rate` is a 4-dp string (`"0"` on empty range). `time_series` buckets by local calendar day via SQLite `date(created_at, 'localtime')` — depends on container `TZ`. |
| `GET` | `/metrics/export` | admin | Same query params as `/summary`. Streams a ClosedXML workbook (`Utilisation` sheet) as `utilisation_<from>_<to>.xlsx`. Dates are real Excel dates, money numeric, `Totals Match` bool. Ordered `CreatedAt DESC`. |
| `GET` | `/monitoring/failures` | admin | `?limit=&offset=` (limit clamped 1–100, default 25). Newest-first. Items include `category` (snake_case: `magic_byte_mismatch` / `parser_error` / `unhandled_exception` / `validation_mismatch`), `parser_display_name` (looked up from `IParserRegistry`), inline `error_detail`, `source_available` (live `File.Exists` check), and nullable `computed_total` / `quoted_total` strings (populated for `validation_mismatch` entries only). |
| `GET` | `/monitoring/failures/{id}/source` | admin | Streams the retained source file. 404 when the row is missing OR the file is gone (e.g. retention race) — does not leak which case. |

**`/parse` response headers** on success: `X-Validation: match | mismatch`, `X-Currency`, `X-Computed-Total`, `X-Quoted-Total`. `X-Currency` carries the parser's `QuoteMetadata.Currency` (`USD` for Nutanix, `AUD` for HP) so the frontend labels computed/quoted totals in the correct currency in both the success toast and the `ValidationWarningModal` — never hard-code `USD` in the UI. The frontend reads these: a match triggers a green success toast and immediate download; a mismatch triggers a blocking `ValidationWarningModal` overlay (amber, with an OK button that auto-focuses) — the download does not begin until the user acknowledges the modal. **`X-Quoted-Total` empty-header semantic**: when `QuotedTotal == null`, the header is **present with an empty string value**, not omitted. The SPA distinguishes "header missing" (parser ran but didn't emit a total) from "header present, value empty" (no quoted total to compare against). ASP.NET Core strips empty-string headers by default — preserve the empty value with `new StringValues(new string[] { "" })`.

**`/parse` failure mode**: when the parser raises (PDF unreadable, header anchor missing, TOTAL missing, invalid file type, etc.), the backend returns `422` with `{detail: {stage, hint, message}}`. The uploaded source is **discarded** — no `ParseJob` row is recorded and no original is retained on disk. The frontend renders the error inline in the dropzone, preserving the form so the user can pick a different file.

## Response conventions

**Three error body shapes** (one per kind of failure — the SPA branches on the shape, so new endpoints must use the matching record from `src/BidParser.Api/Contracts/`):

- `ApiError { Detail: string }` → `{"detail":"<message>"}` — most errors (auth 401, 404s, 409 conflicts, rate-limit 429, "Unknown vendor." 400, CSRF 403, etc.).
- `PasswordValidationError { Detail: string[] }` → `{"detail":["msg1","msg2",…]}` — **only** `POST /auth/change-password` when password rules fail. Each string is one rule violation. The SPA joins them with a space. Do **not** stringify into a single detail.
- `ParseErrorResponse { Detail: { Stage, Hint, Message } }` → `{"detail":{"stage":"…","hint":"…","message":"…"}}` — **only** `POST /parse` 422 (parser failure).

Success responses that have no useful body use `OkResponse { Ok: true }` → `{"ok":true}`. New endpoints must use these typed records — no anonymous `new { detail = "…" }` objects.

**Decimal serialisation** — money/rates always serialise as strings with a fixed scale so the SPA never has to re-format. Apply via `[JsonConverter]` attributes on DTO properties (per-field — same `decimal` type, different scales). Converters live in `src/BidParser.Api/Serialization/`.

| Field(s) | Scale | Format | Notes |
|---|---|---|---|
| `fx_rate` | 4 dp | `"0.7400"` | EF `HasPrecision(12, 4)`; round with `decimal.Round(v, 4, MidpointRounding.AwayFromZero)` before write. |
| `margin` | 2 dp | `"7.50"` | EF `HasPrecision(12, 2)`. |
| `computed_total`, `quoted_total`, `X-Computed-Total`, `X-Quoted-Total` | 2 dp | `"1625358.51"` | `ToString("F2", InvariantCulture)`. |

**JSON casing**: `JsonNamingPolicy.SnakeCaseLower` globally — C# `Detail` → JSON `"detail"`, `MustChangePassword` → `"must_change_password"`. New DTOs follow this without per-property `[JsonPropertyName]` attributes.

## CRM template mapping

All five Nutanix parsers declare `CrmTemplate = "Foreign Uplift"` and `AvailableTemplates = ["Foreign Uplift"]`. `ForeignUpliftWriter.WriteForeignUplift` produces their output workbook.

The HP Bid (XLSX) parser declares `CrmTemplate = "No Calculation"` and `AvailableTemplates = ["No Calculation", "Uplift"]`. The user selects the template from a dropdown in the UI at parse time. `AnzGenericWriter.Write(items, path, sheetName, includeMargin, margin, vendorName)` produces HP output for both templates; the only difference is that `Uplift` populates the Margin column (K) while `No Calculation` leaves it blank. `Bundle Detail` rows carry `cost = 0` (the component price is dropped because the `Bundle` parent holds the deal total); `AnzGenericWriter` writes the `0.0001` sentinel in the Cost column (I) for any zero cost, since the downstream import rejects a literal `0`. Nutanix's sentinel lives on the Foreign columns (T/U); HP Bid's is on the local Cost column.

The HP OneConfig (XLSX) parser declares `CrmTemplate = "% Off RRP with Uplift"` (single template — no dropdown). `PercentOffWithUpliftWriter.Write(items, path, margin, imPercent, vendorName)` produces the output. The parent Config row writes its real `Total Price` on column H (MSRP); all children write the `0.0001` sentinel on H. Column I (Cost) is intentionally blank. Column K (Margin) and column X (IM%) are written for every row and are required parse parameters. IM% is persisted as `User.ImPercent` alongside `Margin` and `FxRate`, serialised as `im_percent` in JSON responses.

`ParseService.ParseAsync` accepts an optional `crmTemplate` parameter. When omitted or empty, it defaults to `parser.CrmTemplate`. The effective template is validated against `parser.AvailableTemplates` and the matching writer branch is dispatched. `/api/parsers` exposes `available_templates` (snake_case array) alongside `crm_template` so the frontend can render either a static callout (single-template parsers) or a dropdown (multi-template parsers).

## Operational config & deployment

`docs/DEPLOYMENT.md` is the operator-facing runbook (env-var table, NPM proxy config, `/data` volume layout, first-login walkthrough). When changing the deployment story, update that file and reflect any agent-relevant changes here.

Agent-relevant deployment facts:

- **Single container**, multi-stage Dockerfile: `node:20-alpine` builds the SPA → `mcr.microsoft.com/dotnet/sdk:10.0` builds and publishes the API → `mcr.microsoft.com/dotnet/aspnet:10.0` runtime serves on `:3447`. The SPA is copied into `wwwroot/`; `UseStaticFiles` + `MapFallbackToFile("index.html")` handles SPA routing.
- **Schema migrations** run inside `MigratorHostedService` at startup (`Database.MigrateAsync()`). No entrypoint script needed. `BootstrapAdminHostedService` seeds the admin row when zero users exist.
- **Publishes `3447:3447`**, intended to sit behind nginx-proxy-manager (or equivalent) for TLS termination. The reverse proxy must set `client_max_body_size` to at least `MAX_UPLOAD_MB` (default 10) — NPM's default is 1 MB and silently rejects larger uploads.
- **`Secure` cookie flag** is set only when `X-Forwarded-Proto=https` reaches the app after `ForwardedHeadersMiddleware` processing, so local HTTP dev still works and production behind HTTPS still gets `Secure=True`.
- **Persistent state** lives entirely under `/data` (`db.sqlite` + `dp-keys/` + `files/originals/` + `files/outputs/`). **`/data/dp-keys` must persist** — it holds the Data Protection keyring; losing it logs everyone out. `${DATA_DIR:-bidparser-data}:/data` defaults to a Docker named volume; `DATA_DIR` in the operator's `.env` swaps it for a bind mount.
- **`SESSION_SECRET`** is the Data Protection app-name discriminator, **not** a cryptographic key. The keyring in `/data/dp-keys` is the actual signing material. Rotating `SESSION_SECRET` scopes new cookies away from old ones (effectively logs everyone out). Deleting `/data/dp-keys` invalidates the keyring.
- **Key env-var defaults**: `ADMIN_USERNAME=admin`, `ADMIN_PASSWORD=changeme`, `MAX_UPLOAD_MB=10`, `RATE_LIMIT_AUTH_PER_MIN=5`, `RETENTION_DAYS=90`, `SESSION_LIFETIME_HOURS=12`, `TZ=Australia/Sydney` (controls server-local time used for `/admin/metrics` daily buckets; `tzdata` ships with the `mcr.microsoft.com/dotnet/aspnet:10.0` image). `SESSION_SECRET` has a dev default (`dev-only-change-me`) and must be overridden in production. Full table lives in `docs/DEPLOYMENT.md`.
- GitHub Actions CI/CD (`.github/workflows/build.yml`) — every push and PR runs the `test` job (`dotnet test BidParser.sln`). The `build-and-push` job that produces the `linux/amd64` Docker image only runs for `v*` tag pushes, so pushing main + tagging a release no longer double-builds the same SHA. Each tagged release publishes three image tags to `ghcr.io`: `<semver>` (e.g. `0.4.0`), `sha-<short-sha>`, and `latest`.
- **UI version string** in the footer (`frontend/src/components/Footer.tsx`) comes from `import.meta.env.VITE_APP_VERSION`, injected at frontend build time via the `APP_VERSION` Docker build-arg. The CI workflow resolves `APP_VERSION` to the tag's SemVer (e.g. `v0.2.0` → `0.2.0`, rendered as `v0.2.0`) on tagged releases, and to `dev-<short-sha>` on branch/PR builds (rendered as `dev-<short-sha>` without the `v` prefix). No file edits required to bump the version — pushing a new `v*` tag is sufficient.

## Release versioning

Releases follow **Semantic Versioning 2.0.0**:

- Use tags in the form `vMAJOR.MINOR.PATCH` (for example `v0.1.0` or `v1.0.0`).
- Increment `MAJOR` for incompatible API/config/deployment changes, `MINOR` for backwards-compatible functionality, and `PATCH` for backwards-compatible fixes.
- Use `0.y.z` while the app is still in home-network/internal testing and the deployment/API contract may change.
- Pre-release identifiers are allowed when useful (`v1.0.0-rc.1`, `v0.2.0-alpha.1`) and must sort according to SemVer 2.0.0 rules.
- Every GitHub Release should point at a matching pushed SemVer tag; pushing that tag triggers the Docker image build for the same version.

## Extensibility — adding a parser format

To add a new format (new Nutanix file type, or a Dell/Lenovo quote):

1. **One parser class**: `src/BidParser.Parsing/<Vendor>/<Slug>/Vendor<Slug>Parser.cs` implementing `IParser` — declare `Slug`, `DisplayName`, `Vendor`, `AcceptedMime`, `CrmTemplate`, `Parse(string path)`. Optional `Detect(string path)` defaults to `0.0`. Reference `Vendors.X`, `CrmTemplates.X`, and `ParserSlugs.X` from `BidParser.Domain.Constants` — do not inline the string literals. For a new vendor or CRM template, add the constant first.
   - `AvailableTemplates` has a default interface implementation of `[CrmTemplate]` (single-element). Override it only when the parser supports more than one output template (e.g. HP overrides to `[CrmTemplates.NoCalculation, CrmTemplates.Uplift]`). When a parser overrides `AvailableTemplates`, the frontend automatically renders a template dropdown; single-template parsers continue to show the static callout.
2. **One registry entry**: append an instance to the `Parsers` list in `src/BidParser.Parsing/Registry/ParserRegistry.cs`. This is the only registration point; no assembly scanning.
3. **One fixture + golden output**: drop the sample under `samples/inputs/`, hand-validate, commit the expected `*_parsed.xlsx` golden under `samples/outputs/`, and add a test case to `tests/BidParser.Parsing.Tests/`.
4. **Optionally a new spec markdown** (`docs/<vendor>_<format>.md`) mirroring the existing five Nutanix specs, plus a one-line link from this file.
5. **If the format needs a new CRM template**, implement a new writer in `src/BidParser.Output/` and wire it in `ParseService`. Every Nutanix file type reuses `ForeignUpliftWriter`; HP uses `AnzGenericWriter` for both its templates. Add the writer dispatch `case` in `ParseService.ParseAsync` under the template switch.
6. **If the format has no quoted total** (e.g. HP), construct `ValidationResult` directly with `Matches = true` and `Difference = 0` rather than calling `ParseValidation.Validate(items, quotedTotal)` — that helper returns `Matches = false` when `quotedTotal is null`, which would trigger the frontend's mismatch-warning modal on every parse.

The developer **does not touch**: API routes, frontend components (dropdowns auto-populate from `/api/parsers`), Docker config, validation logic, auth, history, or any other parser. The parser surface area is the entire change for a new format — preserve that property.

## Working with the user

- The user wants to iterate one supplier format at a time. Confirm extraction accuracy in chat (render a table) **before** writing scaffolding code for a new format.
- Output field mapping for `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` is locked in `docs/output_mapping.md`. Don't re-derive cell positions from the template directly — read the spec.
- `XQ-4108785.pdf` and `XQ-4108785.xlsx` are both multi-quote files (sections Quote A, B, C, D). Both parsers extract **Quote D only** (`nutanix_hardware_only_pdf.md` and `nutanix_hardware_only_xlsx.md`) — the reseller-facing breakdown. Quote C inside these files is a separate budgetary breakdown of pure components and is **not** parsed by either format. Quotes A and B are noise.

## Commands

- `dotnet test BidParser.sln` — full test suite (173 tests: 101 parsing + 72 API integration).
- `dotnet run --project src/BidParser.Api` — local backend dev server (`http://localhost:5000`).
- `cd frontend && npm run dev` — Vite frontend dev server, proxying `/api` to `http://127.0.0.1:5000`.
- `cd frontend && npm run build` — TypeScript + production frontend build.
- `docker compose up -d` — run the production container (after `cp .env.example .env` and setting `SESSION_SECRET`).
- `docker compose build` — local image build from the repo root `Dockerfile`.

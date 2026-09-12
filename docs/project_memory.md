# BidParser — Detailed Project Reference

This file holds the verbose reference material:
per-format extraction algorithms, golden expected outputs, the full API surface,
response conventions, auth/deployment detail, and the sample→format mapping. The
lean `CLAUDE.md` links here. Search this file when you need format specifics or
endpoint contracts.

## Project artefacts (detailed)

- `src/BidParser.Api/` — ASP.NET Core 10 Minimal API. Endpoints for `/auth/*`, `/me`, admin `/users`, admin `/admin/dell-settings`, `/parsers`, `/parse-ui-config`, `/parse`, `/history`, admin `/metrics/*`, admin `/monitoring/*`, `/admin/runtime-config`, and health check. Runtime configuration is validated server-side and its HTML guidance is sanitized before storage. Hosts the React SPA from `wwwroot/` in production.
- `src/BidParser.Application/` — host-neutral orchestration shared by API/Infrastructure and Desktop. `ParserCatalog` projects concrete and Auto descriptors; `SourceFormatInspector` owns extension/MIME and magic-byte rules; `QuoteParseService` resolves concrete/Auto selection, wrong-format suggestions, and parser invocation; `WorkbookWriteService` validates template/split options and performs canonical staged XLSX/ZIP output. References Domain and Output only — no EF Core, ASP.NET, authentication, storage retention, or database concerns.
- `src/BidParser.Domain/` — `LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`, `IParser`, `IParserRegistry`. `Constants/` centralises vendors, CRM templates, output naming styles, and parser slugs, including Datalogic, Epson, and Strike and their `*_quote_pdf` slugs — never hand-roll these strings in new code. `Vendors.LenovoIsg`/`Vendors.LenovoIdg` are the dropdown vendor groupings; `Vendors.LenovoOutput` ("Lenovo") is the name every Lenovo parser's `OutputVendorName` writes to Col B of the CRM workbook regardless of ISG/IDG (see the vendor-split section below). `IParser.AcceptedMimes` is the authoritative accepted MIME set and defaults to `[AcceptedMime]`; multi-container parsers override it while retaining `AcceptedMime` as the primary/legacy-compatible value. `IParser` also exposes `OutputVendorName` (default `=> Vendor`), `AvailableTemplates` (default `[CrmTemplate]`), `SolutionSplitLabel` (default `"Solution ID"`), `OutputNameStyle` (default `OutputNameStyle.BidScoped`), and capability traits including `SupportsSolutionIdSplit` and `SupportsOnCost` (both default `false`). `LineItem` has optional `Comments` and `SolutionId` fields; the latter is populated on every Lenovo LBP-E and LBP-I ISG parent and child, and HP Services line (not LBP-I IDG, which has no Solution IDs).
- `src/BidParser.Infrastructure/` — `AppDbContext` (EF Core + SQL Server, `AddDbContextPool`), `User`/`ParseJob`/`ParseMetric`/`FailedParseJob` entities plus singleton `DellApiSettings`, SQL Server migrations, `FileStorage`, `ParseService` (validates uploads by extension AND magic bytes — `%PDF` / `PK\x03\x04` / JSON `{` or `[` after BOM/space; on post-save failure keeps the source for admin download and invokes `FailedParseJobRecorder`), Dell token/quote clients and encrypted settings service, `FailedParseJobRecorder` (fresh `AppDbContext` scope so the request-scoped context's pending user-default mutations are discarded), `RetentionService` (project-then-`ExecuteDeleteAsync`; purges both `ParseJob` and `FailedParseJob` rows plus their files). `ParseJob.SplitBySolutionId` records whether its single `OutputPath` is a ZIP rather than XLSX, allowing parse/history/monitoring downloads to return the correct filename and content type. `User.Role` is the `UserRole` enum (`Admin`, `User`), stored lowercase via `HasConversion`. `FailedParseJob.Category` is the `FailureCategory` enum stored the same way — lowercase with no separator (`magicbytemismatch`) — while the wire spelling is camelCase (`magicByteMismatch` / `parserError` / `unhandledException`) via the SQL-translatable conditional mapping in `MonitoringEndpoints.BuildUnifiedQuery`. The two are deliberately independent: changing the wire value touches no stored rows. Entity timestamps are stamped exclusively by `AppDbContext.StampTimestamps()` — no `= DateTime.UtcNow` initializers on entities.
- `src/BidParser.Parsing/` — Cleaning helpers, PDF word collection via PdfPig (`PdfWordCollector`, `PdfTableHelpers`), XLSX helpers via ClosedXML (`WorkbookReader`, `HeaderMap`), ExcelDataReader for legacy OLE `.xls` (Lenovo LBP-E and Cisco CCW) and OpenXML `.xlsx` (Lenovo LBP-E), HTML-disguised `.xls` via HtmlAgilityPack (Zebra Price Concession XLS), JSON reader via `System.Text.Json` (Dell CTO & APOS Quote API responses), and 22 concrete parsers in the explicit `ParserRegistry`. The PDF set includes the Datalogic, Epson, and Strike quote parsers, which share body-content-band column recovery and wrapped logical-row helpers.
- `src/BidParser.Output/` — `CrmWriter` (the single writer for every CRM template), `CrmTemplateProfile` (which columns each template populates — the only per-template difference), shared `TemplateLayout` (internal, 27-column header array, plus the two zero-price constants — single source of truth), `OutputNaming`. **The two zeros:** CRM does not accept a literal `0` as a price, so the output has two deliberate zeros meaning opposite things (full rule in `docs/output_mapping.md` → "Zero pricing: the two zeros"). `TemplateLayout.ZeroPriceSentinel` (`0.0001`) means *"this line really is free"* — CRM rounds it back to `0` on import — and is the default for any zero-dollar price: on Foreign Cost/MSRP columns (T/U) for `Foreign Uplift`; on the local Cost column (I) — where every HP `Bundle Detail` lands — and on MSRP column (H) when the parser populates it (HPE `BundleDetails` and any zero `ListPrcEst`; HP/Lenovo leave MSRP null so col H stays blank) for `No Calculation`/`Uplift`; and on MSRP column (H) for `% Off RRP with Uplift`, where children always emit the sentinel and the parent writes its real `Total Price`. `TemplateLayout.NoBidPrice` (literal `0`) means *"this line carries no bid price"* — CRM rejects it and falls back to the line's standard price in SAP — and is the narrow exception, today only Zebra `Cancelled = Y` rows (cols H/I). Both are write-time only; change a constant in one place to retune.
- `src/BidParser.Desktop/` — .NET 10 WPF host targeting `net10.0-windows`. It composes Application, Domain, Parsing, and Output directly with no Infrastructure/API reference, database, auth, or localhost server. The MVVM workflow excludes Dell, processes the selected quote in place, retains only an in-memory `ParsedQuote`, and saves explicitly through the shared staged writer. It embeds Inter/icon assets and validated schema-v1 fallback guidance/defaults; remote refresh and update checks are independent, bounded, silent on failure, and never cached. Full contract: `docs/desktop.md`.
- `ParseMetric` — append-only utilisation ledger (`src/BidParser.Infrastructure/Entities/ParseMetric.cs`). Snapshots user/vendor/parser/totals at parse time. Survives 90-day retention and user deletes via nullable FKs (`parse_metrics → parse_jobs` is `ON DELETE SET NULL`; `parse_metrics → users` is `ClientSetNull` — EF nulls it in memory on user delete, and `DeleteUserAsync` calls `ExecuteUpdateAsync` first to ensure untracked rows are also cleared). Written transactionally with `ParseJob` for successful parses only. Powers `/admin/metrics`. Time-series buckets are grouped in SQL after `AT TIME ZONE` conversion; container `TZ` controls which local calendar day/month each parse lands on.
- **`ImportType`** (`Auto` / `Manual`, `src/BidParser.Infrastructure/Entities/ImportType.cs`) records how the format was selected for a parse. `ParseService` sets it from whether the incoming slug was an `AutoDetectTypes` sentinel, and it is persisted as the nullable `importType` column on `ParseJob`, `ParseMetric`, **and** `FailedParseJob` (stored lowercase via `HasConversion`). Nullable because rows created before the `AddImportType` migration carry no value — do not treat null as `Manual`.
- **User deletion also deletes files**: `DeleteUserAsync` cascade-removes the user's `ParseJob` rows, but the DB cascade doesn't touch disk. The handler snapshots each job's `SourcePath`/`OutputPath` **before** the commit, then `FileStorage.TryDelete`s them **after** it succeeds — otherwise the files would be orphaned in `UPLOAD_DIR` forever (retention discovers files via the rows that just vanished). Interaction: a `validationMismatch` `FailedParseJob` shares the `ParseJob`'s source file; its `UserId` is nulled (not cascaded) and the row survives until retention — monitoring renders the now-deleted source as "Input purged" via `File.Exists`.
- `FailedParseJob` — records parse failures and successful-but-mismatched parses for admin review. `FailureCategory` enum: `MagicByteMismatch`, `ParserError`, `UnhandledException`, `ValidationMismatch`. Exception categories written from `ParseService.ParseAsync`'s catch block via `FailedParseJobRecorder.RecordAsync` — but a `ParseValidationException` (user-input 400: unknown/unsupported CRM template, missing IM%) is caught **before** the generic handler and rethrown after deleting both stored files, so it records **no** `FailedParseJob` and leaves no upload (it's a client error, not monitoring-worthy). `ValidationMismatch` entries written in the **success path** (after `ParseJob` commit) via `RecordMismatchAsync` when `!Validation.Matches`; they reference the same source file as the `ParseJob` (no copy), populate `ComputedTotal`/`QuotedTotal`, set `ErrorDetail` to a human-readable totals summary. Recorder uses a fresh `AppDbContext` scope; mismatch recording is best-effort (exceptions caught and logged via `ILogger<ParseService>` with parse-job id, user id, parser slug). `SaveUploadAsync` runs **outside** the recorder's `try` so pre-save failures produce no row. Lifecycle: row and referenced file purged together at `RETENTION_DAYS`; for `ValidationMismatch` the source file is shared with `ParseJob` — double-delete attempts during retention are silently ignored. Powers `/admin/monitoring` alongside `ParseJob`: the `GET /monitoring/runs` query **excludes** `ValidationMismatch` rows so each mismatch surfaces once via its `ParseJob` (which carries the output file); the exception categories are the only `FailedParseJob` rows shown there.
- **Runtime configuration** — `runtime_configs` stores independently editable `guidanceMessages` and `vendorDefaults` documents. Bootstrap inserts the initial report-type wording and Zebra `onCostPct: 2.85` only when keys are missing. `RuntimeConfigurationService` validates registry-derived references, canonicalizes values, sanitizes guidance HTML, and does not cache reads. An invalid stored document remains visible as raw JSON plus `validationError` on the Admin surface so it can be repaired; the public surface degrades to empty maps. `/api/parse-ui-config` supplies fixed-scale string defaults plus guidance by concrete parser slug; Auto parses use the resolved slug and unmapped slugs render no guidance.
- `tests/BidParser.Parsing.Tests/` — 661 parser/application/output tests covering cleaning helpers, every registered parser against retained inputs, Solution-ID grouping/renumbering, format detection, shared PDF geometry, application selection/write services, and template writer cell-by-cell equivalence against `samples/outputs/`.
- `tests/BidParser.Api.Tests/` — 189 xUnit + `WebApplicationFactory` integration tests covering migration/bootstrap, auth, users, parse/history/monitoring flows, retention and metrics, runtime configuration, OpenXML output, cancellation, cleanup, and parser capability exposure. Shared `CustomTestFixture`/`TestRegistry`/`TestParser` live in `TestInfrastructure.cs`.
- `tests/BidParser.Desktop.Configuration.Tests/` — 118 cross-platform tests covering schema-v1 fallback/validation, safe guidance markup, bounded HTTP behavior, independent failures, numeric-field validation, and SemVer update decisions. Together the three projects make `dotnet test BidParser.sln` a 968-test suite.
- `frontend/` — React 19 / Vite / TypeScript app. Visual design: slate-50 background, white cards with `border-slate-200` + `shadow-sm`, `#0077d4` accent, Inter typography, uppercase tracked labels. Shared utility classes in `src/styles.css` (`.label`, `.field`, `.button`, `.button-primary`, `.button-danger`, `.icon-button`, `.card`, `.toast`). Contains API client, auth context, data-router shell, login + forced password change screens, sticky `AppHeader`, and an admin-only `AdminMenu` for Users, Dell API, Metrics, Monitoring, and Runtime Configuration. The data router exists so Runtime Configuration can use React Router's stable `useBlocker` API for unsaved in-app navigation. The dashboard discovers parsers from `/api/parsers`, loads numeric defaults and sanitized guidance independently from `/api/parse-ui-config`, resets configured numeric states only when the vendor changes, and shows guidance by resolved concrete slug in the single post-parse `ParseResultModal`. Runtime Configuration provides independent, accessibly named JSON editors, live registry-derived references, copyable examples, validation/recovery errors, timestamps, and unsaved-navigation protection. `MetricsDashboard` and `MonitoringPage` share URL-driven date controls including All; both exports stream XLSX workbooks with bounded memory. Components in `src/components/metrics/`, `src/components/monitoring/`, shared `Footer`. `recharts ^2` is the only chart dependency. **Branding:** `public/logo.png` is the 512×512 master and the derived favicon/manifest assets are served from the site root.

### Implementation notes

- Parser slugs, package names, spec docs are vendor-prefixed. Use `nutanix_software_only_pdf`, `nutanix_software_only_xlsx`, `nutanix_renewal_pdf`, `nutanix_renewal_xlsx`, `nutanix_hardware_only_pdf`, `nutanix_hardware_only_xlsx`; never reintroduce vendorless slugs.
- Frontend proxies `/api` to `http://127.0.0.1:5000` by default. If port 5000 is occupied: `VITE_API_PROXY_TARGET=http://127.0.0.1:<port>`.
- In Docker, `DB_CONNECTION_STRING` is assembled by `docker-compose.yml` from `MSSQL_SA_PASSWORD` and `MSSQL_DB` and passed to the app container; `UPLOAD_DIR` defaults to `/data/files`. The app container mounts `/data` (named volume `bidparser-data` or `DATA_DIR` bind mount). SQL Server data lives in a separate `bidparser-mssql-data` volume mounted at `/var/opt/mssql` in the `mssql` container.
- `RetentionBackgroundService` (**cleanup-first**, then 24h cadence) calls `RetentionService.CleanupOldParseJobsAsync` to delete expired `ParseJob` rows + source+output files, and expired `FailedParseJob` rows + source files, after `RETENTION_DAYS`. Runs once at startup (after `MigratorHostedService`, which registers earlier so migrations complete first) then every 24h — a sleep-first loop would never fire if the container restarts more often than daily (e.g. every `v*` deploy). `ParseMetric` rows retained indefinitely — `parse_job_id` nulled by SQL Server `ON DELETE SET NULL` when the parent `ParseJob` is purged.
- `ChangePasswordPage` relies on the `App.tsx` route guard (every other route redirects to `/change-password` when `mustChangePassword=true`). The app uses a data router so the Runtime Configuration editor can block in-app navigation with React Router's stable `useBlocker` while either JSON document is dirty.
- The vendor-specific settings block renders only when **both** a vendor and a file type are selected.
- `FileTypeSelect.tsx` renders a `Sample File` link beneath it once a parser is chosen, pointing at a static copy under `/samples/<name>`. Mapping in the `SAMPLE_FILES` record at the top of `FileTypeSelect.tsx`; every parser slug needs an entry with a matching file committed under `frontend/public/samples/`. When adding a parser, add both the `SAMPLE_FILES` entry and the file.
- UI label vocabulary differs from field names: `margin` renders as **`Uplift`**; HP OneConfig `imPercent` renders as **`Discount Off MSRP`**. API request fields and DB columns are unchanged — rename is presentation-only. Numeric values are never persisted per user; all four states may be prefilled from administrator-managed vendor defaults on initial vendor resolution and each vendor change, with absent values blank. Manual edits survive parser/template changes within the same vendor. `onCostPct` is rendered and submitted only when the selected parser advertises `SupportsOnCost`; currently both Zebra parsers and the Datalogic, Epson, and Strike PDF parsers do so.
- `User.Name` is a nullable display name surfaced in `AccountChip` and `SettingsPage`. Admin creates require it; bootstrap admin starts with `Name="Administrator"`.
- Golden fixtures in `samples/outputs/` retain the source-oriented `<basename>_<FileToken>.xlsx` naming used by parser tests (e.g. `XQ-9100002_ForeignUplift.xlsx`, `Deals_Sample_01_HPI_NoCalculation.xlsx`). User downloads use bid metadata as documented in `docs/output_mapping.md`. PDF and XLSX parsers for the same quote compare against the same golden. See `docs/sample_fixture_sanitisation.md` for the fixture-data handling record.
- Hardware parser tests include a negative assertion: NX-1175S-G10-6517P-CM has Quote D's cost (USD 20,017.57), not Quote C's (USD 5,903.72).

### Portable desktop host

- **Catalog policy** — alphabetical vendors, concrete formats in registry order, and no Dell entry.
  Auto is selected where available; single-format vendors select their concrete parser. Effective
  numeric and split controls are the intersection of parser traits and `CrmTemplateCapabilities`.
- **Local data boundary** — one selected source path, parsed in place; one in-memory result; explicit
  staged XLSX/ZIP save. No upload copy, EF context, login, history, monitoring, metric, registry
  setting, scheduled task, or config cache. Process-only state is the last save directory and update
  dismissal.
- **Remote schema** — embedded and public documents are versioned roots:
  `{ "schemaVersion": 1, "guidanceMessages": [...] }` and
  `{ "schemaVersion": 1, "vendorDefaults": [...] }`. Exactly version 1 is accepted. Unknown
  references/properties are ignored; duplicate known assignments or any malformed/unsafe known
  content reject that whole document. Guidance maps a tiny no-attribute HTML subset directly to
  presentation models; it is never executed or loaded as XAML.
- **Endpoints/transport** — constants live in `DesktopEndpoints`: the two raw GitHub configuration
  paths and `repos/regalen/BidParser/releases/latest`. Three concurrent startup requests
  each have a five-second timeout; redirects, retries, credentials, caching, and disruptive dialogs
  are absent. Config is capped at 256 KiB and release metadata at 128 KiB. Each successful result is
  applied independently on the WPF dispatcher.
- **Update semantics** — `NuGet.Versioning` compares the installed informational version with the
  latest stable release tag after stripping one `v`; build metadata is ignored. Only a newer stable
  release shows the session-dismissible bar. The release page is reconstructed from a fixed base;
  no payload URL, binary download, installer, elevation, or replacement is trusted/performed.
- **Packaging** — the checked-in `win-x64` profile is self-contained, single-file, native-library
  self-extracting, untrimmed, not ReadyToRun, and embeds symbols. Windows CI requires exactly one
  `BidParser.exe`, verifies Product/File version resources, smoke-launches it, and publishes its
  SHA-256. Full behavior, trust boundary, troubleshooting link, and Windows acceptance matrix are
  owned by `docs/desktop.md`.

## Sample → format mapping

The three checked-in Dell JSON fixtures are sanitised representative Dell Quote API payloads used as stubbed responses in parser and API integration tests.

They also pin the *response contract*, because Dell's published spec does not fully cover it. Dell's OpenAPI documents the response schema only for **v1** (`Quote` → `Item` → `Sku`); v2/v3/v4 declare `200: Success` with no schema. The live payload carries fields absent from every published version — `items[].listPriceIncludingShipping`, `salesPriceIncludingShipping`, `unitListPriceIncludingShipping`, `unitSalesPriceIncludingShipping`, `openBasketId`, `items[].itemId`, and `items[].skus[].serviceTags` — two of which are the only price source for the CTO parser, and one of which carries serial numbers. Because Dell serves an unspecified default version when `Accepts-version` is absent, `DellApiSettings.ApiVersion` pins it to `4.0`. Two related notes: `quoteType` (`DSA Quote`, `DSAPARTNER`, `Premier E-Quote` across the fixtures) identifies the origination channel and **does not** discriminate CTO from APOS, so `dell_auto`'s heuristic `Detect()` remains the routing mechanism; and `isRebateEligible`, which the CTO parser binds, appears in no fixture and no published spec version — it is exercised only by inline JSON literals in `DellCtoJsonParserTests`.

| Sample file        | Format                 | Notes                                  |
|--------------------|------------------------|----------------------------------------|
| `XQ-9100002.pdf`   | Software Only (PDF)    | Compact 6-column layout                |
| `XQ-9100002.xlsx`  | Software Only (XLSX)   |                                        |
| `XQ-9100003.pdf`   | Hardware Only (PDF)    |                                        |
| `XQ-9100003.xlsx`  | Hardware Only (XLSX)   |                                        |
| `XQ-9100004.pdf`   | Renewal (PDF)          |                                        |
| `XQ-9100007.pdf`   | Renewal (PDF)          | Wrapped currency amount (USD on separate line)  |
| `XQ-9100001.pdf`   | Renewal (PDF)          | Platform-column variant: extra `Platform` column, wrapped Product Code, hardware rows emit `Description = "Platform: {value}"` |
| `XQ-9100010.xlsx`  | Renewal (XLSX)         | XLSX envelope of Renewal; `Product Description` + `Platform` combined into Description; blank row before `TOTAL` |
| `XQ-9100005.pdf`   | Software Only (PDF)    | Extended 9-column layout, 1 line item  |
| `XQ-9100006.pdf`   | Software Only (PDF)    | Extended 9-column layout, wrapped SKUs |
| `XQ-9100009-….pdf` | Hardware Only (PDF)    | Multi-page Quote D (pages 4–5/7); page-footer falls inside Term column; quoted total `USD 247,510.94` |
| `XQ-9100008-….pdf` | Hardware Only (PDF)    | Multi-page Quote D (pages 7–10/12); wrapped `USD <amount>` for `NX-8150-G10-6728P-CM` net unit price; quoted total `USD 3,601,962.18` |
| `Deals_Sample_01_HPI.xlsx` | HP Bid (XLSX) | 479 items: Part Number × 3, Bundle × 14, Bundle Detail × 462; includes `#`-concatenated Option Code example |
| `Deals_Sample_02_HPI.xlsx` | HP Bid (XLSX) | 5 Part Number rows only; Part-Number-only file |
| `99000001.xlsx`                  | HP OneConfig (XLSX) | 1 Config + 30 components; AUD 6,042.77 Total Price |
| `CH9000000001.xlsx`              | HP Services (XLSX)  | 13 items: 1 SAID (1073 3019 2253), priced service row UJ561AC; AUD 11,928.95 |
| `CH9000000002.xlsx`              | HP Services (XLSX)  | 17 items: 7 SAIDs, blank warranty end date row A71DPPT; AUD 6,432.00 |
| `HPE_Deal_9500000001_v2.xlsx`    | HPE Bid (XLSX)      | 66 items: Bundle × 3, BundleDetails × 63; anchor `"LineType"`; computed total 131,713.36 |
| `HPE_Deal_9500000002_v1.xlsx`    | HPE Bid (XLSX)      | 4 Part Number rows only; qty from `Quantity` (×5); computed total 30,215.00 |
| `translate_quote_98000001_v25_all.xlsx` | HP Global Bid (XLSX) | 24 items; deal 98000001 v.25; qty always 1; AUD computed total 35,233.34 |
| `BRDAS019000004V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | Parts-only: 3 parents, no Solution IDs or CONFIGURATION DETAILS; AUD 77,545.95 |
| `BRDAS019000005V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | Parts-only: 1 parent; AUD 38,896.08 |
| `BRDAS019000001V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | 66 items: 1 solution (SID row qty 20 → parent cost 640,046.00 ÷ 20), 65 children; AUD 640,046.00 |
| `BRDAS019000003V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | 150 items: 2 solutions (SIDX02Q2PL/PM), parent costs from solution total ÷ line qty; AUD 393,231.78 |
| `BRDAS019000002V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | 406 items: 5 solutions over 13 pages; mid-grid Grand Total/header repeats, cross-page description, hanging-hyphen wrap; AUD 3,787,260.72 |
| `BRDAS019000007V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | 84 items: 1 solution (SIDX02YUF5); `Components` header centred 11pt right of its values — the merged No./Components cell regression; AUD 95,588.19 |
| `BRDAS019000006V1.pdf` | Lenovo LBP-I ISG Quote (PDF) | 96 items: 2 solutions; same header drift at 8pt, the 0.23pt near-miss case; AUD 154,445.77 |
| `BRPAS019100001V1.pdf` | Lenovo LBP-I IDG Quote (PDF) | 80 items: 13 laptop lines, 1 configured (67 components); AUD 1,455,094.20 |
| `BRPAS019100002V1.pdf` | Lenovo LBP-I IDG Quote (PDF) | 146 items: 3 desktop/monitor lines, 1 configured (143 components incl. 6 label-less `SPEC` rows); AUD 41,060.30 |
| `BRPAS019100003V1.pdf` | Lenovo LBP-I IDG Quote (PDF) | 116 items: 1 workstation line, configured (115 components, 7 printed `Qty 0`); carries **no `MTM` recap table**, so the terms heading bounds the grid; AUD 90,796.00 |
| `BRDAD019200001.xls` | Lenovo LBP-E ISG Quote (XLSX) | 62 items: 2 configuration parents, 60 children; AUD 103,542.60 |
| `Bid_Platform_Bid_Request_Sample_03.xls` | Lenovo LBP-E ISG Quote (XLSX) | 188 items: 4 configuration parents with distinct Solution IDs, 184 children; AUD 423,640.27 |
| `Bid_Platform_Bid_Request_Sample_01.xls` | Lenovo LBP-E ISG Quote (XLSX) | 153 items: 3 configuration parents, 10 standalone parents, 140 children; AUD 81,882.90 |
| `Bid_Platform_Bid_Request_Sample_02.xls` | Lenovo LBP-E ISG Quote (XLSX) | 50 items: 1 configuration parent, 4 standalone parents, 45 children; AUD 25,430.84 |
| `Bid_Platform_Bid_Request_Sample_04.xlsx` | Lenovo LBP-E ISG Quote (XLSX) | 39 items: 1 configuration parent with Solution ID SIDTEST0001, 38 children; AUD 71,380.91 |
| `Bid_Platform_Bid_Request_Sample_05.xlsx` | Lenovo LBP-E ISG Quote (XLSX) | 254 items: 4 config parents + 30 standalone parents, 220 children; AUD 377,368.03 |
| `Bid_Platform_Bid_Request_Sample_06.xlsx` | Lenovo LBP-E ISG Quote (XLSX) | 156 items: 4 configuration parents (38 children each); AUD 183,932.90 |
| `Bid_Platform_Bid_Request_Sample_07.xlsx` | Lenovo LBP-E ISG Quote (XLSX) | 1 item: standalone parts quote (no configuration/Solution ID); AUD 74,378.04 |
| `Bid_Platform_Bid_Request_Sample_08.xlsx` | Lenovo LBP-E ISG Quote (XLSX) | 113 items: 2 config parents (55 children each) + 1 standalone deployment parent; AUD 354,578.02 |
| `Zebra_PC_97000001_V2.0.pdf` | Zebra PCR (PDF)              | 3 active items; AUD; page-break description split on ZD4AH22 |
| `Zebra_PC_97000001.xls` | Zebra PCR (XLS)              | HTML-disguised XLS; 3 active items; matches PDF extraction |
| `Zebra_PC_97000002_V2.0.pdf` | Zebra PCR (PDF)              | 5 items, all active |
| `Zebra_PC_97000002.xls` | Zebra PCR (XLS)              | 5 items, all active |
| `Zebra_PC_97000003_V1.0.pdf` | Zebra PCR (PDF)              | 8 items; cross-page description split; AUD |
| `Zebra_PC_97000003.xls` | Zebra PCR (XLS)              | 8 items |
| `Zebra_PC_97000004_V1.0.pdf` | Zebra PCR (PDF)              | 13 items; fused-header layout; PDF golden workbook |
| `Dell_CTO_Sample.json` | Dell CTO (API)        | 45 items: 2 parents, 43 children; AUD 45,108.52 |
| `Dell_Peripherals_Sample.json` | Dell CTO peripherals/spares (API) | 29 items: all 23 parents + 6 children; AUD 3,501.04 |
| `Dell_APOS_Sample.json` | Dell APOS (API)      | 2 flat sku items; sorted by service tag; AUD 5,021.82 |
| `Quote_9400000001.xls` | Cisco CCW Quote (XLS)  | Legacy .xls binary; 102 items: 12 parents, 90 children; AUD 716,644.04 |
| `Datalogic_PE930003.pdf` | Datalogic Quote (PDF) | 8 items; primary golden fixture; AUD 60,595.50 |
| `Datalogic_PE930001.pdf` | Datalogic Quote (PDF) | 3 items; wrapped part numbers; Min Shipment Size 10; AUD 209,397.00 |
| `Datalogic_PE930002.pdf` | Datalogic Quote (PDF) | 1 item; wrapped part number; AUD 2,625.45 |
| `Epson_96000002.pdf` | Epson Quote (PDF) | 2 items; quote-level quantity 450; primary golden fixture; no quoted total |
| `Epson_96000001.pdf` | Epson Quote (PDF) | 1 item; quote-level quantity 200; no quoted total |
| `Epson_96000003.pdf` | Epson Quote (PDF) | 1 item; quantity 2,500 and split price token; no quoted total |
| `Strike_Quote_9202.pdf` | Strike Quote (PDF) | 4 items; wrapped product codes; primary golden fixture; AUD 63,585.00 |
| `Strike_Quote_9201.pdf` | Strike Quote (PDF) | 19 items across multiple pages; product codes containing spaces; AUD 61,064.57 |
| `Strike_Quote_9203.pdf` | Strike Quote (PDF) | 2 items; wrapped unit-price decimal; AUD 28,820.70 |

## ParseResult contract

```
ParseResult
├── Metadata        QuoteMetadata (QuoteNumber, BidNumber?, BidRevision?, Supplier, Currency, QuotedTotal, SourceFilename, ParserSlug)
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
  HP Services (XLSX):          Description, SerialNumber, StartDate, EndDate, LineSequence, SolutionId
  Lenovo LBP-E ISG (XLS/XLSX): Description, LineSequence, Comments, SolutionId
  Lenovo LBP-I ISG (PDF):     Description, LineSequence, Comments, SolutionId
  Lenovo LBP-I IDG (PDF):      Description, LineSequence
  Zebra PCR (PDF+XLS):        Description, Msrp, MinQty, LineSequence, Comments, IsCancelled
  Datalogic Quote (PDF):      Description, Msrp, MinQty, LineSequence
  Epson Quote (PDF):          Description, Msrp, MinQty, LineSequence
  Strike Quote (PDF):         Description, Msrp, MinQty, LineSequence
  Common debug:                Raw (Dictionary<string,string> of original column text)

`CrmWriter` writes `SerialNumber` to `Serial Number` (column M) and `Comments` to
`Comments` (column R) independently, so a line can carry both values without collision.

IsCancelled (bool, default false): set by Zebra parser for Cancelled=Y rows. The writer
puts TemplateLayout.NoBidPrice (a literal 0, never the zero-price sentinel) in col H/I so
CRM rejects it and pulls the line's standard price from SAP, leaves col W blank, sets
Qty=1, writes Comments="Cancelled (Standard Price)". See "the two zeros" in
docs/output_mapping.md. All other parsers leave this false; it is inert for them.

Across formats: List Price / List Unit Price / MSRP / Term Adjusted List Unit Price
→ Msrp; Sale Price / Net Unit Price / Cost Price → Cost.
```

Dates stored internally as `DateOnly` (serialised ISO `YYYY-MM-DD`); display formatting (`DD/MM/YYYY`) is a frontend concern.

Format detection is a **soft hint, not routing**: `IParser.Detect()` returns 0.0–1.0 confidence. It is **not** used to silently reroute a manual selection. Its two consumers are the **wrong-file-type** suggestion flow (when a manual selection fails) and the per-vendor **Auto** option (`AutoDetectTypes`, preselected default for Nutanix, Lenovo ISG, Zebra, and Dell — Lenovo IDG has no Auto entry).

## Common PDF parsing approach

All PDF formats use **UglyToad.PdfPig 0.1.16** (word-level bounding boxes) via `PdfWordCollector`. PdfPig uses bottom-left origin; Y is flipped in `PdfWord` construction so downstream code uses top-left origin. `pdfplumber` is not a runtime dependency; only a checked-in reference snapshot generated with it is used by the characterisation tests. `PdfWord.Top` and `Bottom` retain their tight-box meaning for parser-specific windows. On a page where at least half of non-whitespace tight word boxes have collapsed, ordinary horizontal words also carry a baseline-derived `LineY`; `RowsBetween` groups on that stable placement coordinate and otherwise uses the tight-box midpoint. `PageUsesBaselineGeometry` carries the collector's exact page-level choice through splitting and other word transformations, and the short-glyph repair skips those same pages rather than recomputing the collapse ratio. Each collapsed-page word also carries baseline-derived letter X extents (tight extents otherwise), so `PdfTableHelpers.RowsBetween` can cut a word PdfPig merged across a cell boundary at a real letter gap. A complete edge `Page N of M` counter causes its whole resolved visual line to be removed before row bucketing and splitting, including PdfPig whitespace tokens and same-line branding such as `Strike Group Australia`. The filter reuses the table row's 3.5pt line tolerance (the measured Strike name/counter difference is 0.38pt) rather than deleting every word in a wider horizontal Y strip. See [troubleshooting](troubleshooting.md#parsing) for the Type 3 collapsed-box rationale and measured threshold margin.

- Collect `PdfWord` records from all pages, preserving page index.
- Find header row by its first-cell anchor word(s) (`"Product"`+`"Code"` for Software Only, `"No"` for Renewal).
- Derive column x-ranges from header word x0s — each range is `[its x0, next header's x0)`; rightmost extends to page width.
- Collect body words below the header y across pages until a `"TOTAL:"` token.
- Cluster words into rows by baseline (tight-box midpoint fallback; tolerance 3.5pt). Bucket each row's words into columns by `x0`; join intra-bucket words with single spaces.
- Locate quoted total by scanning after the last body row for `TOTAL:` + `USD` + amount, tolerating page-break wrap.

Shared helpers in `src/BidParser.Parsing/Pdf/`: `PdfWordCollector`, `PdfWord` (+ `PdfLetter`), `PdfTableHelpers`, `RowBlocks`.

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

Per-field: join Product Code fragments and normalize `vpn` with `ToUpperInvariant()` for SAP SKU resolution (source casing remains in `Raw`); flatten description by joining continuation snippets with single spaces, collapse internal whitespace; strip `USD`/commas from prices → `Decimal`; `term`/`qty` → `int`.

Edge cases: 3–5 line description wrap; `Term-Months` row kept; `TOTAL:` wrapping onto a later page; thousands separators (`2,275.00`).

Expected output for `XQ-9100002.pdf` (golden):

| Part Number      | Term | List Price | Sale Price | Quantity |
|------------------|------|------------|------------|----------|
| SW-NCM-STR-PR    | 60   | 383        | 101.11     | 2096     |
| TERM-MONTHS      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 864      |
| TERM-MONTHS      | 60   | 0          | 0          | 60       |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 1232     |
| TERM-MONTHS      | 60   | 0          | 0          | 60       |
| SW-NCI-E-PRO-PR  | 60   | 3455       | 912.12     | 145      |
| TERM-MONTHS      | 60   | 0          | 0          | 60       |
| SW-NCM-E-STR-PR  | 60   | 583        | 153.91     | 145      |
| TERM-MONTHS      | 60   | 0          | 0          | 60       |

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

Per-field: `vpn` = trim `Product Code` then `ToUpperInvariant()` for SAP SKU resolution (source casing remains in `Raw`); `description` = trim `Product Description`; `term` = `Term (Months)` numeric → `int`; `msrp` = `List Price` ($-string) → `Decimal`; `cost` = `Sale Price` → `Decimal`; `qty` = `Quantity` → `int`.

Total: scan down from the last data row for a cell starting with `TOTAL ` (e.g. `TOTAL $1,625,358.51`), strip `TOTAL`/`$`/commas → `Decimal`. A second `TOTAL` label further down is a fallback only.

Edge cases: header position not fixed; `Term-Months` rows kept; `$`-prefixed currency with thousands separators; `Total Discount (%)` sits between `List Price` and `Sale Price` so the map must use header text — never assume adjacency or fixed columns.

Expected output for `XQ-9100002.xlsx` (golden values match the PDF version, total `$1,625,358.51`).

## Renewal (PDF) — extraction algorithm

Header anchor: word `"No"` top-left of the table header. Columns: `No`, `Product Code`, `Serial Number`, `Start Date`, `End Date`, `Term Adjusted List Unit Price`, `Total Discount`, `Net Unit Price`, `Qty`, `Total Net Price`. Header labels span multiple visual lines (`Term`/`Adjusted`/`List Unit`/`Price` stacked) — the column-anchor logic must tolerate multi-line header text.

Row classification:
- `No` is a positive integer AND `Product Code` non-empty → **anchor row**.
- `No` empty AND any other column non-empty → **continuation row** (append per column — `Serial Number` wraps so the second token arrives this way).
- Otherwise → ignore.

Per-field:
- `vpn`: join and trim `Product Code`, then `ToUpperInvariant()` for SAP SKU resolution; source casing remains in `Raw`.
- `serial_number`: source cell wraps across two lines (e.g. `24SW000351227,\nLIC-02472987`). Join fragments with **no separator** (comma is at end of first line), trim, collapse internal whitespace → single string (`24SW000351227,LIC-02472987`). Do **not** split into serial/license fields.
- `start_date`/`end_date`: source `MM/DD/YYYY`; `DateOnly.ParseExact("MM/dd/yyyy")`. Frontend displays `DD/MM/YYYY`.
- `msrp`: strip `USD`/commas from `Term Adjusted List Unit Price` → `Decimal`.
- `cost`: strip `USD`/commas from `Net Unit Price` → `Decimal`.
- `qty`: `Qty` → `int`.

No Term-Months sub-header. `Total Net Price` wraps but isn't extracted (validation comes from `TOTAL:`).

**Platform-column variant** (`XQ-9100001`): an optional `Platform` column sits between `No` and `Product Code`. Detect from the header band (search `"Platform"`); add to column-range map only when present. When a row carries a non-empty Platform value (itself potentially wrapping, joined without separator), `LineItem.Description` = `"Platform: {value}"` (e.g. `"Platform: NX-8035N-G8-HY"`); rows with no Platform value leave `Description` null. Product Code also wraps (`RSW-NCI-`/`ULT-PR`); fragments joined without separator via `JoinUnspaced`.

**USD-prefix fusion:** before bucketing, `NutanixRenewalPdfParser.FuseCurrencyTokens` walks the word stream and pairs each `"USD"` with its nearby numeric amount (forward window 6 words; spatial tolerance `Top ∈ [USD.Top − 3.5, USD.Top + 15.0]`, same page). The pair collapses into one synthetic `PdfWord` anchored at the *amount's* coordinates, so wrapped amounts land in the correct price column. Without this, `USD` and the numeric straddle the boundary and `DecimalCleaner.Parse` throws on a bare `'USD'`.

Edge cases: serial wrap; `Net Unit Price`/`Qty` running together (`USD 54.41 160`) disambiguated by x-ranges; `TOTAL: USD ...` wrapping; serial with no embedded license; wrapped-currency layout (`XQ-9100007.pdf`).

Expected output for `XQ-9100004.pdf` (golden):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 24SW000351227,LIC-02472987  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |
| RSW-NCI-ULT-PR  | 24SW000351236,LIC-02472996  | 2026-07-13  | 2027-07-12  | 575        | 371.83     | 32       |
| RSW-NCI-ULT-PR  | 24SW000351221,LIC-02472983  | 2026-07-13  | 2027-07-12  | 575        | 429.11     | 72       |
| RSW-NCM-STR-PR  | 24SW000351228,LIC-02472985  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |

Computed total = quoted total = `USD 60,205.68`.

Expected output for `XQ-9100007.pdf` (wrapped-currency):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 25SW000430057,LIC-02537784  | 2026-06-17  | 2028-12-01  | 189        | 54.64      | 80       |
| RSW-NCI-PRO-PR  | 25SW000430055,LIC-02537786  | 2026-06-17  | 2028-12-01  | 1121       | 661.61     | 80       |
| RSW-NCM-STR-PR  | 25SW000430056,LIC-02537783  | 2026-10-28  | 2028-12-01  | 161        | 40.20      | 400      |
| RSW-NCI-PRO-PR  | 25SW000430054,LIC-02537785  | 2026-10-28  | 2028-12-01  | 955        | 755.64     | 400      |

Computed total = quoted total = `USD 375,636.00`.

Expected output for `XQ-9100001.pdf` (Platform-column variant):

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
- **Part Number** ← trimmed `Product Code`, normalized with `ToUpperInvariant()` for SAP SKU resolution; source casing remains in `Raw`.
- **Description** ← `Product Description`, with `Platform` appended as `… (Platform: {value})` when the platform cell is non-empty (software-subscription rows leave Platform blank → bare description). This differs from Renewal (PDF), whose source has no description column and emits only `Platform: {value}`.
- **Serial Number** ← `Serial Number`; internal whitespace stripped so the embedded license joins as `26SW000487027,LIC-02574676` (matching the PDF convention).
- **Start/End Date** ← `Start Date` / `End Date` (native date cell, else `MM/dd/yyyy` with the usual fallbacks).
- **MSRP** ← `Term Adjusted List Unit Price`; **Cost** ← `Net Unit Price` (both `$`-prefixed, parsed with `defaultZero`). No `Term` column → `Term` left null.
- **Quantity** ← `Quantity`.

Expected output for `XQ-9100010.xlsx` (golden):

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
- `vpn`: concatenate anchor + continuation `Product Code` snippets with **no separator** (`NX-1175S-G10-` + `6517P-CM`), trim trailing whitespace, then `ToUpperInvariant()` for SAP SKU resolution. Plain source labels therefore emit as `SUPPORT-TERM` and `PLATFORM INTEGRATION`; source casing remains in `Raw`.
- `description`: join anchor + continuation `Product` snippets with single spaces; collapse whitespace.
- `term`: per-row nullable. `int` when populated; null when empty.
- `msrp`: strip `USD`/commas from `List Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null.**
- `cost`: strip `USD`/commas from `Net Unit Price` → `Decimal`. **Empty cell → `Decimal("0")`, not null.**
- `qty`: `int`. On `Support-Term` this cell holds the term value (`60`) — keep as-is.

Edge cases: rows from Quote A/B/C must not appear (negative-assertion test); part number wrap; description wrap 1–4 lines; bundled-component rows (price cells empty → `0`); `Support-Term` kept; `TOTAL:` may be on the same row as `USD <amount>` or wrap.

Expected output for `XQ-9100003.pdf` Quote D (golden):

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
| SUPPORT-TERM             | 60   | 0.00       | 0.00       | 60       |
| C-TPM-2.0-U-C-CM         | —    | 77.89      | 62.31      | 1        |
| PLATFORM INTEGRATION     | 0    | 4003.51    | 0.00       | 1        |

Computed total = quoted total = `USD 22,491.87`.

## Hardware Only (XLSX) — extraction algorithm

XLSX counterpart of Hardware Only (PDF). Both extract Quote D and produce the same line items and total (`$22,491.87`). Workbook has stacked Quote A/B/C/D; only Quote D in scope.

Section + header location (anchor-based):
1. Scan for a cell whose value is `Quote D For distributor to quote to the reseller only` — that row is the banner.
2. From the banner row, scan down for the next cell `Product Code` — that row is the header.
3. Build a label→column-letter map. **Don't assume columns carry over from Quote C** — Quote C has `Product Code` in column H, Quote D in column E.
4. Stop at the first cell below the header starting with `TOTAL ` — carries the quoted total.

Row classification: every non-empty row between header and `TOTAL ` is a line item. **No filler rows skipped** — the `Support-Term` row is kept.

Per-field: `vpn` = trim `Product Code`, then `ToUpperInvariant()` for SAP SKU resolution (plain source labels emit as `SUPPORT-TERM` / `PLATFORM INTEGRATION`; source casing remains in `Raw`); `description` = trim `Product Description`; `term` = int when populated, null when empty (do not invent); `msrp`/`cost` = $-string → `Decimal`, **empty → `Decimal("0")`**; `qty` = `int` (Support-Term holds term value `60`).

Edge cases: Quote A/B/C rows must not appear; column-letter drift inside the workbook; bundled-component rows → `0`; per-row term nullability; Support-Term qty holds `60`.

Expected output for `XQ-9100003.xlsx` Quote D matches the PDF version, total `$22,491.87`.

## API surface (all under `/api`)

| Method | Path | Auth | Notes |
|---|---|---|---|
| `POST` | `/auth/login` | none | Body `{username, password}`. Rate-limited. Returns the user object and sets the session cookie. |
| `POST` | `/auth/logout` | user | Clears the current session cookie only. |
| `POST` | `/auth/change-password` | user | Body `{oldPassword, newPassword}`. Enforces password rules. Clears `mustChangePassword`. Re-issues the session cookie (new password-hash stamp) so the acting session survives while all other sessions for the user are revoked. |
| `GET`  | `/me` | user | Returns the current user shape. |
| `PATCH` | `/me/settings` | user | Body `{defaultVendor?, fxRate?, margin?, imPercent?}`. `defaultVendor` is checked against the `KnownVendors` allowlist in `MeEndpoints`, which covers every vendor a parser is registered under (Nutanix, HP, HPE, Lenovo ISG, Lenovo IDG, Zebra, Dell, Cisco, Datalogic, Epson, Strike) — keep it in step with `Vendors.*` when adding a vendor, or this endpoint will reject a `defaultVendor` that `ParseService` happily persists on a successful parse. Decimals accept string form and must be ≥ 0. Endpoint accepts numerics (tests/tooling) but the SPA only mutates `defaultVendor` — implicitly, inside `ParseService` on each successful parse (there is no client `updateSettings` caller). |
| `GET` | `/parsers` | user | Returns the registry — each concrete entry has `slug`, `displayName`, `vendor`, legacy-compatible primary `acceptedMime`, authoritative `acceptedMimes`, `crmTemplate`, `availableTemplates`, `supportsSolutionIdSplit`, `solutionSplitLabel`, and `supportsOnCost`. Synthesized Auto entries expose the distinct union of their concrete parsers' `acceptedMimes`, intersect capabilities across their concrete parsers, and leave `acceptedMime` empty. |
| `GET` | `/parse-ui-config` | active user | Returns runtime `vendorDefaults` (nullable fixed-scale strings: `fxRate` 4 dp, percentages 2 dp) and sanitized `guidanceByParserSlug`; a configuration read failure degrades to empty maps so parsing remains available. |
| `GET` | `/admin/runtime-config` | admin | Returns both editable documents, timestamps, nullable per-document `validationError`, concrete parser references, vendor references, numeric-field metadata, and the HTML allowlist. Invalid stored data is returned raw with its error so the editor remains the recovery path. |
| `PUT` | `/admin/runtime-config/guidance-messages` | admin + CSRF | Body `{json}`; validates/sanitizes and saves only guidance. |
| `PUT` | `/admin/runtime-config/vendor-defaults` | admin + CSRF | Body `{json}`; validates/canonicalizes and saves only defaults. |
| `POST` | `/parse` | user | Multipart with exactly one source: uploaded `file`, or Dell `quoteId` (`13 digits.version`). Also accepts `vendor`, `parserSlug`, `fxRate`, `margin` and optional `imPercent`, `onCostPct`, `splitBySolutionId`. The split flag is accepted only when the resolved parser declares `SupportsSolutionIdSplit`; otherwise 400. When true, the response and persisted `ParseJob.OutputPath` are a ZIP containing one renumbered workbook per Solution ID. The Dell path fetches the raw Quote API JSON, stores it as the source, and resolves `dell_auto` to CTO or APOS before using the normal parse/persistence/output pipeline. `vendor=Dell` with a `parserSlug` other than `dell_auto` is rejected with `400 "Dell only supports automatic file-type detection."` before any fetch/parse happens — Dell has no manual file-type selection at the API layer, independent of the SPA's disabled dropdown. All numerics parse with `NumberStyles.Number` (no currency symbols/exponents) and must be ≥ 0, matching `/me/settings`. Uploads remain capped at 10 MB both sides. See response headers below. |
| `GET` | `/history` | user | `?limit=&offset=&q=` — user-scoped. Each row carries nullable historical `bidNumber` / `bidRevision`, the retained `sourceFilename`, and `crmTemplate` (the template written at parse time, persisted on `ParseJob.CrmTemplate`). `q` is a case-insensitive substring filter on the displayed `{bidNumber} v{bidRevision}` value or `sourceFilename`; it remains safe for historical rows with null bid metadata. `when` is a server-computed relative-time string. |
| `GET` | `/history/{id}/source` | user | Streams the stored original. 404 if foreign user or expired. |
| `GET` | `/history/{id}/output` | user | Streams the parsed output filename (obeying the parser's `OutputNameStyle`: `<BidNumber>_<BidRevision>_<FileToken>.xlsx` for `BidScoped`, `<BidNumber>_<FileToken>.xlsx` for `SolutionScoped`), or the corresponding ZIP when `ParseJob.SplitBySolutionId` is true; falls back to the legacy source-basename name when either bid field is absent. Same gating. |
| `GET` | `/users` | admin | User CRUD. `POST` requires `{username, name, role}`; sets a random one-time temp password + `mustChangePassword=True` and returns `{user, tempPassword}`. `PATCH` accepts `{username?, name?, role?, resetPassword?}` and returns `{user, tempPassword}` (`tempPassword` non-null only when `resetPassword=true`). |
| `GET` | `/admin/dell-settings` | admin | Returns Dell token/quote endpoint settings, `apiVersion` (the `Accepts-version` header value, default `4.0`), and `clientSecretConfigured`; never returns the client secret. |
| `PUT` | `/admin/dell-settings` | admin | Replaces Dell settings. `clientSecret` is optional; omitted/blank preserves the stored encrypted value. Requires the CSRF header. |
| `POST` | `/admin/dell-settings/test` | admin | Invalidates the cached token and verifies the stored Dell credentials against the token endpoint. Requires the CSRF header. |
| `GET` | `/metrics/summary` | admin | `?range=all&from=YYYY-MM-DD&to=YYYY-MM-DD&vendor=&userId=&parserSlug=`. Default window last 30 days, server-local TZ inclusive. Returns `{range, timeSeriesGranularity, kpis, byUser, byVendor, byParser, timeSeries}`. `mismatchRate` is 4-dp string (`"0"` on empty range). `timeSeries` buckets by local calendar day (or month if `range=all`) via SQL in the API layer. |
| `GET` | `/metrics/export` | admin | Same params as `/summary`. Streams all matched ledger rows through a dedicated non-retrying DbContext and forward-only OpenXML writer into a delete-on-close temporary file, rolling over after Excel's row limit. Returns `utilisation_<from>_<to>.xlsx` (or `utilisation_all.xlsx`). Real server-local Excel dates, money numeric, `Totals Match` bool. Ordered `CreatedAt DESC`, then id descending. |
| `GET` | `/monitoring/runs` | admin | Unified runs log. `?range=all&status=&vendor=&userId=&parserSlug=&from=YYYY-MM-DD&to=YYYY-MM-DD&limit=&offset=&importType=` (limit 1–100, default 25). Newest-first. Merges successful/mismatched `ParseJob` rows (`kind:"job"`, status `success`/`validationMismatch`, `outputAvailable` true) with genuine `FailedParseJob` failures (`kind:"failure"`, status = category, `outputAvailable` false) using a unified database-side query projection via `Concat()`. **Excludes `FailedParseJob` rows where `Category == ValidationMismatch`** so each mismatch appears once (sourced from its `ParseJob`, which has the output file). `status` ∈ {`success`, `validationMismatch`, `magicByteMismatch`, `parserError`, `unhandledException`}. `importType` ∈ {`auto`, `manual`}. `parserDisplayName` from `IParserRegistry`; `sourceAvailable`/`outputAvailable` are live `File.Exists`. Pagination and filtering execute directly against the unified query on the database. |
| `GET` | `/monitoring/runs/export` | admin | XLSX export of the unified runs log. Same filters as `/monitoring/runs` (`range`, `status`, `vendor`, `userId`, `parserSlug`, `from`, `to`, `importType`) but completely ignores pagination (`page`, `limit`, `offset`). Streams all matched rows via OpenXML `SAX` mode for constant memory, bypassing EF buffering via a dedicated non-retrying DbContext and using temporary files mapped directly to stream output. Strings are formula-escaped (`InlineString`), `created_at` is a real server-local Excel date matching the metrics export, and worksheet rollover occurs automatically at 1,048,576 rows with exact repeated headers. Filenames match boundary selection (`parser_runs_YYYY-MM-DD_YYYY-MM-DD.xlsx` or `parser_runs_all.xlsx`). |
| `GET` | `/monitoring/jobs/{id}/source` | admin | Streams the retained `ParseJob` source file (no user-ownership check, unlike `/history`). 404 when row or file is gone. |
| `GET` | `/monitoring/jobs/{id}/output` | admin | Streams the `ParseJob` output using its bid number/revision filename (obeying `OutputNameStyle`), or the Solution-ID ZIP when `ParseJob.SplitBySolutionId` is true; falls back to the legacy source-basename name when either bid field is absent. 404 when row or file is gone. |
| `GET` | `/monitoring/failures/{id}/source` | admin | Streams the retained `FailedParseJob` source file (failure-row input). 404 when the row is missing OR the file is gone — doesn't leak which. |

**`/parse` response headers** on success: `X-Parser-Slug` (resolved parser slug), `X-Validation: match | mismatch`, `X-Currency`, `X-Computed-Total`, `X-Quoted-Total`, `X-Split-Count` (only for a Solution-ID ZIP; count of workbook entries, including `1`), `X-Cancelled-Lines` (only when cancelled lines exist; format `line:VPN;line:VPN;…`), and `X-Rebate-Ineligible: true` when a Dell CTO quote has a rebate-ineligible parent/base SKU. `X-Currency` carries `QuoteMetadata.Currency` (`USD` Nutanix, `AUD` HP) — never hard-code `USD` in the UI. The SPA always shows the single `ParseResultModal` (no auto-download): match → success state; mismatch, cancelled lines, and rebate-ineligible Dell CTO items → folded warnings; download is gated behind the modal's Download button. Guidance comes from `/api/parse-ui-config` keyed by the resolved concrete slug, never a parse header. **`X-Quoted-Total` empty-header semantic:** when `QuotedTotal == null` the header is **present with an empty string value**, not omitted (the SPA distinguishes "header missing" from "header present, empty"). ASP.NET strips empty-string headers by default — preserve with `new StringValues(new string[] { "" })`.

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
| `fxRate` | 4 dp | `"0.7400"` | EF `HasPrecision(12, 4)`; round with `decimal.Round(v, 4, MidpointRounding.AwayFromZero)`. |
| `margin` | 2 dp | `"7.50"` | EF `HasPrecision(12, 2)`. |
| `computedTotal`, `quotedTotal`, `X-Computed-Total`, `X-Quoted-Total` | 2 dp | `"1625358.51"` | `ToString("F2", InvariantCulture)`. |

**JSON casing:** the ASP.NET Core Web defaults — camelCase. There is **no global `PropertyNamingPolicy`**: `Detail` → `"detail"`, `MustChangePassword` → `"mustChangePassword"`. New DTOs follow this without per-property `[JsonPropertyName]`; reach for that attribute only to satisfy an external protocol, on the integration DTO that owns the mapping (see `DellAuthTokenProvider.TokenResponse` pinning OAuth's `access_token` / `expires_in`). Request bodies, multipart form fields, query parameters and BidParser's own status/error values are camelCase alike. **SQL columns remain snake_case** — see `AppDbContext`.

## Bid metadata and recent uploads

`QuoteMetadata` keeps its legacy `QuoteNumber` and adds `BidNumber` / `BidRevision`. Both are
`required string?` — required so every parser must state its intent, nullable because **bid
extraction is best-effort and never throws**.

**Why it degrades instead of failing.** The bid fields select the preferred download filename but
never reach the CRM workbook, line items, or validation. Failing a parse over an unreadable header
would cost the user a correct workbook merely to protect its label, while absence safely falls back
to the prior source-basename filename (and shows an em dash in Recent Uploads). Never reintroduce a
throw here — in particular not stage `detect`, which `ParseService` would reclassify as a
wrong-file-type selection and use to delete the upload.

**`BidMetadataCleaner` rules.** Revisions drop one optional leading `v`/`v.` and are never parsed
numerically (Zebra's `2.0` must stay `2.0`). The number is the load-bearing half: without it both
values come back null. A number with an unreadable revision keeps the number and falls back to
`DefaultRevision` — every revision anchor is newer and less proven than its number anchor, so a bid
number with an assumed revision beats no bid number. `CleanRevisionless` is the explicit opt-in for
formats documented as carrying no source revision.

**Anchor robustness.** `WorkbookReader.ValueRightOf` (the HP/HPE/Global Bid metadata-block reader)
matches labels case-insensitively, unlike `FindCell` — these are prose labels, not table headers
driving column mapping. Lenovo LBP-I's `V<revision>` group stays optional; it was written that way
before bid metadata existed, which is evidence such files occur.

**Observing the degradation.** Because a broken anchor is silent by design, `ParseService` logs a
warning naming the file and parser slug when `BidNumber` comes back null. That line, plus
`bid_number IS NULL` on `parse_jobs` rows created after deployment, are the signals that a vendor
changed a header and a format's anchor needs revisiting.

Every successful `ParseJob` persists whatever was extracted; the nullable columns also preserve
historical jobs without source-filename guesses. Recent Uploads displays `Vendor | Bid Number |
File Type | CRM Template | When | Files`; the bid cell is `{bidNumber} v{bidRevision}` or `—`.
The original filename remains retained and downloadable, and history search covers either it or
the formatted bid value.

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
  `ParseError(stage: "fileType", message, message)`.
- **Naming (hybrid).** `DetectSuggestedType` delegates to the shared `FormatDetection.Resolve`
  (Domain): `Detect()` over same-vendor siblings whose `AcceptedMimes` contains the actual upload MIME
  (excluding it), top score, named when `≥ 0.7` (`FormatDetection.MinConfidence` — the same
  threshold the Auto flow uses). Message: *"The file is not recognised as
  {selected} and appears to be a {suggested}. Select the correct file type and try again."*;
  when nothing is confident: *"The file is not recognised as {selected}. Check the selected
  file type and try again."*
- **Candidate set / coverage.** Only vendors with ≥2 formats accepting the upload MIME can ever name a
  sibling: **Nutanix PDF** (Software/Renewal/Hardware), **Nutanix XLSX** (Software/Renewal/
  Hardware), **HP XLSX** (Bid/Global Bid/OneConfig). Lenovo and Zebra each have two formats
  with distinct MIMEs → always the generic message, but both formats of each carry a
  `Detect()` signature so the vendor can offer Auto.
- **`Detect()` signatures** (each `try/catch → score`, `0.0` on failure):
  - *Nutanix XLSX:* Hardware = `Quote D` banner present; Renewal = `Quote Number` grid with
    `Net Unit Price` + `Serial Number` + `Term Adjusted List Unit Price`; Software = same grid
    with `Term (Months)` + `List Price` + `Sale Price`, no banner, no renewal columns.
  - *Nutanix PDF* (over whitespace-normalised `WordStreamText`): Hardware = `…distributor to
    quote to the reseller only` banner; Renewal = a `Serial` column word, no banner; Software =
    contains `Nutanix`, no banner, no `Serial`.
  - *HP XLSX:* Bid = `Line Type` header; Global Bid = `Product number` on the `Product
    numbers` sheet; OneConfig = `Config ID` header.
  - *Zebra PDF:* `Price Concession Items` word sequence (+ currency token).
  - *Zebra XLS:* ≥2 HTML tables + a header row with `Part No.` and `Unit Special Price` (+ `Price Concession` title text).
  - *Datalogic PDF:* Datalogic identity plus the `ID # / Part Number` header sequence.
  - *Epson PDF:* `Contract` plus the `Epson Product Code` header sequence.
  - *Strike PDF:* `QUOTE REFERENCE` plus the `Product Code / … / QTY` header grid.
- **Frontend.** `DashboardPage.fileTypeErrorMessage` matches `stage === "fileType"` and shows
  `FileTypeErrorModal` (message-only; the dropdown is left unchanged).
- **Tests.** `WrongFileTypeDetectionTests` and `FormatDetectionTests` (parsing project, runnable) assert the cross-detection matrix and shared resolver; `WrongFileTypeTests` and `AutoDetectParseTests` (API project, needs Docker) assert the 422 `fileType` response, Auto resolution, header output, persistence, and cleanup.

## Auto file-type detection

Supported vendors (Nutanix, Lenovo ISG, Zebra, and Dell via `AutoDetectTypes`) expose an **"Auto (detect format)"** entry in the file-type dropdown, preselected by default when that vendor is selected. Lenovo IDG has no Auto entry — a single-format vendor, matching HP/HPE/Cisco precedent (see the vendor-split section below). For Nutanix, Lenovo, and Zebra this is only a default — the user can still pick a specific format. **Dell is the exception: Auto is the only selectable entry.** `ParseSettingsCard` filters Dell's dropdown down to the Auto entry and disables it (greyed out, no manual override), and `ParseEndpoints` enforces the same rule server-side — any `/api/parse` POST with `vendor=Dell` and `parserSlug != dell_auto` is rejected with `400 "Dell only supports automatic file-type detection."`, regardless of whether it came through the SPA or a direct API call.

- **Sentinel slugs & resolution.** `ParserSlugs.NutanixAuto` (`nutanix_auto`), `ParserSlugs.LenovoAuto` (`lenovo_auto`), `ParserSlugs.ZebraAuto` (`zebra_auto`), and `ParserSlugs.DellAuto` (`dell_auto`). `/api/parsers` synthesizes an entry with `displayName = "Auto (detect format)"` and compatible `availableTemplates` ahead of the vendor's first parser. Guidance is never keyed by a sentinel slug.
- **Backend flow (`ParseService`).** When `parserSlug` is an Auto slug (`nutanix_auto` / `lenovo_auto` / `zebra_auto` / `dell_auto`):
  - Derives MIME from the source extension (`.pdf` → `application/pdf`, `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `.xls` → `application/vnd.ms-excel`, `.json` → `application/json`). For Dell, `ParseEndpoints` first validates `quoteId`, fetches the raw JSON, and supplies it as `<quoteId>.json`; Dell API failures return 422 with stage `dellApi` before anything is persisted.
  - Saves file, validates magic bytes.
  - Calls `FormatDetection.Resolve(registry.Parsers, vendor, acceptedMime, sourcePath)` (threshold `MinConfidence = 0.7`).
  - If no candidate scores ≥ 0.7, throws `ParseError("fileType", NoMatchMessage)` (records nothing, deletes upload).
  - On match, runs `parser.Parse()`, persists `ParseJob` & `ParseMetric` with the **resolved** parser slug, and emits `X-Parser-Slug: <resolved_slug>`.
- **Frontend SPA.** `DashboardPage` preselects Auto for Nutanix, Lenovo, Zebra, and Dell. Nutanix, Lenovo, and Zebra supply an uploaded file; Dell supplies a quote ID whose fetched JSON response is detected as CTO or APOS. For Dell, `ParseSettingsCard` additionally disables the file-type dropdown and filters it to only the Auto entry — the user has no way to force `dell_cto_json`/`dell_apos_json` manually. On parse success, the SPA reads `X-Parser-Slug` from `api.parse()`, looks up the resolved parser for report-type guidance, and displays `Detected file type: <DisplayName>` in `ParseResultModal`.
- **Extending to another vendor** = one entry in `AutoDetectTypes.All`, with three constraints: the vendor's Auto entry may only advertise CRM templates common to **all** of that vendor's parsers (the HP trio differs, so an HP Auto entry would advertise none and let each parser's `CrmTemplate` default apply); every format of that vendor must have a `Detect()` override (HPE doesn't — default `0.0` would make Auto never match); and a capability such as `SupportsSolutionIdSplit` or `SupportsOnCost` may be true on the synthesized Auto entry only when **every** parser of the vendor supports it. `ParseService` always re-validates capabilities against the resolved concrete parser.

## Lenovo ISG / Lenovo IDG vendor split and `OutputVendorName`

Lenovo issues two unrelated bid templates: **Infrastructure Solutions Group (ISG)** quotes
(server/storage, built around Solution IDs — LBP-E XLS/XLSX and LBP-I PDF) and
**Intelligent Devices Group (IDG)** quotes (laptops/monitors/docks, no Solution IDs —
LBP-I IDG PDF). They cannot share a `Detect()`/format-suggestion pool (different anchors,
different structure), so the dropdown vendor is split into `Vendors.LenovoIsg` ("Lenovo
ISG") and `Vendors.LenovoIdg` ("Lenovo IDG") — this keeps the Auto entry and the
split-by-Solution-ID capability coherent per vendor, since every Lenovo ISG parser
supports both and the IDG parser supports neither.

**CRM knows one vendor, "LENOVO".** The ISG/IDG split is a BidParser-side grouping only —
Col B (Vendor Name) of every Lenovo output workbook, ISG or IDG, must keep reading `LENOVO`
verbatim (`CrmWriter.cs`), or CRM receives a vendor name it doesn't recognise and five
committed ISG golden workbooks change. `IParser.OutputVendorName` (default `=> Vendor`)
exists for exactly this: all three Lenovo parsers override it to `Vendors.LenovoOutput =
"Lenovo"`, and `ParseService`'s `CrmWriterOptions.VendorName` reads `parser
.OutputVendorName.ToUpperInvariant()` instead of `vendor.ToUpperInvariant()` — a
presentation trait the parser declares and the writer obeys, per the "writers must never
special-case a vendor or slug" rule. For every non-Lenovo parser this is identical to the
old `vendor.ToUpperInvariant()` value, so no other golden output changed.

**`users.default_vendor` back-fill.** Existing users persisted `default_vendor = 'Lenovo'`
(the old single vendor string) before the split; migration `AddLenovoVendorSplit` runs
`UPDATE users SET default_vendor = 'Lenovo ISG' WHERE default_vendor = 'Lenovo'` (and the
inverse in `Down`), so no user is left with a `defaultVendor` that no longer matches any
dropdown entry. Historical `parse_jobs.vendor` / `parse_metrics` rows are deliberately
**not** rewritten — they are an append-only record of the vendor string in effect at parse
time, not a live foreign key.

**Known consequence, accepted:** the wrong-file-type suggestion flow groups siblings by
vendor + MIME (see **Wrong file-type detection**), so an IDG quote uploaded under
`Lenovo ISG → LBP-I ISG Quote (PDF)` reports a recognition failure without naming LBP-I IDG
as the correct type — cross-format separation is still asserted by
`WrongFileTypeDetectionTests`, just not surfaced as an in-app suggestion across the vendor
boundary.

## CRM template mapping

All six Nutanix parsers declare `CrmTemplate = "Foreign Uplift"` and `AvailableTemplates = ["Foreign Uplift"]`; `CrmWriter.Write` produces their workbook.

HP Bid (XLSX): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`. User selects template via dropdown. `CrmWriter.Write(items, path, crmTemplate, options)` produces both; the `Uplift` profile populates Margin column (K), `No Calculation` leaves it blank. `Bundle Detail` rows carry `cost = 0` (the `Bundle` parent holds the deal total); `CrmWriter` writes the `0.0001` sentinel in Cost column (I) for any zero cost.

HP OneConfig (XLSX): `CrmTemplate = "% Off RRP with Uplift"` (single template). `CrmWriter.Write(items, path, CrmTemplates.PercentOffWithUplift, options)`. The parent Config row writes its real `Total Price` on column H (MSRP); children write the `0.0001` sentinel on H. Column I (Cost) blank. Columns K (Margin) and X (IM%) written for every row, required parse params. IM% persisted as `User.ImPercent`, serialised `imPercent`.

Dell CTO and APOS: `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation"]`. The Quote API response is written through `CrmWriter` without FX or margin inputs. Both parsers validate `Σ(cost × qty)` against the JSON payload's top-level `salesPrice` field via `ParseValidation.Validate(items, quote.SalesPrice)` — APOS previously validated against `Σ(items[].unitSalesPrice)` (a per-item echo, not the real quote total); it was aligned to CTO's approach so both parsers check the same field.

Lenovo LBP-E ISG Quote (XLSX): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`, `SupportsSolutionIdSplit = true`. The single stable slug `lenovo_lbpe_isg_xls` accepts both legacy OLE `.xls` and OpenXML `.xlsx`; price columns use either the legacy `Adjusted Buy Price` family or the modern `Estimated Price` family and are mapped positionally left-to-right. Configuration parents carry the workbook subtotal and `Solution ID: <SID>` comment; every parent and child carries the structured `SolutionId`; included components are zero-cost children written as the `0.0001` sentinel. Positive rows after a configuration closes arithmetically become standalone parents. When `splitBySolutionId=true`, `SolutionOutputSplitter` groups in first-appearance order and restarts parent/child sequencing in each workbook; the single retained output is a ZIP. Validation remains over the original whole quote, not per group.

Lenovo LBP-I ISG Quote (PDF): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`, `SupportsSolutionIdSplit = true`. One parent per Solution ID — the first numbered product-grid line under each `SID…` row, with cost = solution total ÷ parent qty; every other numbered line and every CONFIGURATION DETAILS component is a zero-cost child (the `0.0001` sentinel at write time) carrying the same structured `SolutionId`. Parts-only quotes (no SID rows) emit per-line priced parents with null Solution IDs, so a split yields one `NoSolutionID` workbook. Splitting and validation behave exactly as for LBP-E above.

Lenovo LBP-I IDG Quote (PDF): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`, `SupportsSolutionIdSplit = false` — this format has no Solution IDs. One parent per product-grid line, in document order; every CONFIGURATION DETAILS component under that line is a zero-cost child (`0.0001` sentinel at write time), or the placeholder VPN `"SPEC"` when its source label is blank. Col B (Vendor Name) still reads `LENOVO` via `OutputVendorName` — see the vendor-split section below.

Datalogic, Epson, and Strike Quote (PDF): `CrmTemplate = "No Calculation"`, `AvailableTemplates = ["No Calculation", "Uplift"]`, and `SupportsOnCost = true`. They use the common local-currency writer; On Cost is optional in both profiles and Uplift additionally writes margin. Datalogic maps source list price to MSRP. Epson and Strike explicitly return MSRP `0`, which becomes the genuine-zero `0.0001` sentinel only when the workbook is written.

`ParseService.ParseAsync` accepts an optional `crmTemplate`. When omitted/empty it defaults to `parser.CrmTemplate`, is validated against `parser.AvailableTemplates`, and the matching writer branch is dispatched. `/api/parsers` exposes `availableTemplates` so the frontend renders a static callout (single-template) or a dropdown (multi-template).

## MVP scope & guardrails

Locked-in product decisions. Anything outside is deferred — flag scope drift before building.

- **Vendors ship one format at a time**, registry-driven. Nutanix, HP, HPE, Lenovo (ISG and IDG), Zebra, Dell, Cisco, Datalogic, Epson, and Strike parsers currently ship; new vendors/formats are added as a parser class + fixture + registry entry (see "Adding a parser format" in AGENTS.md).
- **Single-file upload** per parse. "DRAG MULTIPLE FILES TO BATCH PARSE" is a future-state affordance.
- **Result-popup flow**, no review/edit screen. Upload & parse → progress panel → blocking `ParseResultModal` with an explicit Download button (no auto-download), sanitized administrator-managed guidance for the resolved concrete parser (hidden when unmapped), and Close. Validation server-side; warnings (mismatch totals and/or cancelled lines) are folded into the same popup. The currency hard-failure still uses `CurrencyErrorModal`.
- **Per-user remembered vendor** — only the last-used vendor persists on each User row (`ParseService` sets `user.DefaultVendor = vendor` on success). Numeric inputs are **never** persisted back. The dashboard applies administrator-managed `vendorDefaults` on initial vendor resolution and each later vendor change, using blanks for missing fields; manual edits then remain until the vendor changes. `onCostPct` is capability-driven and submitted only for parsers with `SupportsOnCost`. The User table still has nullable `fxRate`/`margin`/`im` columns and `/me/settings` still accepts them (tests/tooling), but the SPA neither reads nor writes them.
- **Env-var admin bootstrap** — `ADMIN_USERNAME`/`ADMIN_PASSWORD` seed the first admin on a fresh DB (defaults `admin`/`changeme`, `mustChangePassword=True`). Ignored once any user row exists.
- **Stack**: ASP.NET Core 10 + React/Vite/TypeScript, single Docker image.

Out of scope: multi-file batch upload, CSV formats, review/approve gate, multi-tenancy, email, SSO, audit log beyond ParseJob history.

## Authentication & authorisation

- **Passwords**: bcrypt cost 12. Rules enforced on `/auth/change-password` only — the env-var admin bootstrap still uses `ADMIN_PASSWORD` (`changeme` default) + `mustChangePassword`, but admin **create/reset generate a random one-time temp password** (`RandomNumberGenerator`, 10 hex chars) returned once in the response (`UserWithTempPassword { user, tempPassword }`) for the admin to hand over; there is no fixed shared credential. Rules: ≥ 8 chars, ≥ 1 uppercase, ≥ 1 digit, ≥ 1 symbol.
- **Sessions**: Data Protection cookies (`bidparser_session`), HttpOnly, SameSite=Lax, hard 12-hour expiry from login (no sliding refresh). `Secure` flag set only when the request resolves to HTTPS after `ForwardedHeadersMiddleware` (local HTTP dev works; production behind NPM gets `Secure=True` via `X-Forwarded-Proto`). `SESSION_SECRET` is the app-name discriminator, not the signing key. **Token creation/parsing is centralised in `SessionTokenService`** (single `SessionPayload { UserId, IssuedAt, Stamp }` record); the payload **binds the session to a fingerprint of the password hash** (`Stamp = PasswordHash[^8..]`). `HandleAuthenticateAsync` rejects a token whose stamp no longer matches the stored hash, so any password change or admin reset **revokes every existing session** for that user with no server-side store. `ChangePasswordAsync` re-issues the acting session's cookie so it survives its own change.
- **Login timing**: unknown usernames still spend one bcrypt verification (against a fixed dummy hash) so response timing doesn't reveal which usernames exist.
- **CSRF**: every non-GET endpoint requires `X-Requested-With: BidParser` (set by `frontend/src/api/client.ts`). With SameSite=Lax, sufficient for an internal app.
- **Rate limiting on `/auth/*`**: 5/min, two buckets — per remote IP across `/auth/login`+`/auth/change-password`, AND per submitted username on `/auth/login` (pre-auth). Either tripping → `429` + `Retry-After` + generic body. In-memory leaky bucket; doesn't survive restart.
- **`mustChangePassword` gate**: when set, the backend returns `403 password_change_required` for every endpoint except `/auth/*` and `/me`. Frontend redirects to `/change-password`; `App.tsx` route guards keep the user there.
- **Authorization policies are per-endpoint, not path-globbed** (a path-glob would let locked users hit `/me/settings`):
  - `LoggedIn` — valid session cookie; `mustChangePassword` **ignored**. Used by `/auth/logout`, `/auth/change-password`, `GET /me`.
  - `ActiveUser` — `LoggedIn` AND `mustChangePassword=false`. Everywhere else under `/api` except admin.
  - `Admin` — `ActiveUser` + `role == "admin"`.
- **Last-admin guard**: `PATCH`/`DELETE /api/users/{id}` return `409` if the op would leave zero admins, or if it targets the calling admin.
- **Password recovery**: no self-service. Admin `PATCH /api/users/{id}` with `{resetPassword: true}` generates a random one-time temp password (returned once as `tempPassword`) + `mustChangePassword=True`, and revokes the target's existing sessions (password-hash stamp change).

## Operational config & deployment

`docs/DEPLOYMENT.md` is the operator runbook (env-var table, NPM proxy config, `/data` layout, first-login walkthrough). Keep it in sync when the deployment story changes.

- **Single container**, multi-stage Dockerfile: `node:22-alpine` builds the SPA → `dotnet/sdk:10.0` builds/publishes the API → `dotnet/aspnet:10.0` runtime serves on `:3447`. SPA copied into `wwwroot/`; `UseStaticFiles` + `MapFallbackToFile("index.html")` for SPA routing.
- **Schema migrations** run in `MigratorHostedService` at startup (`Database.MigrateAsync()`). `BootstrapAdminHostedService` seeds the admin row when zero users exist.
- **Publishes `3447:3447`**, behind nginx-proxy-manager for TLS. The reverse proxy must set `client_max_body_size` ≥ `MAX_UPLOAD_MB` (default 10) — NPM defaults to 1 MB.
- **`Secure` cookie** set only when `X-Forwarded-Proto=https` reaches the app after `ForwardedHeadersMiddleware`.
- **Persistent state**: app container `/data` holds `dp-keys/`, `files/originals/`, and `files/outputs/` (named volume `bidparser-data` or `DATA_DIR` bind mount). SQL Server data lives in the separate `bidparser-mssql-data` volume at `/var/opt/mssql` in the `mssql` container. **`/data/dp-keys` must persist** — it holds the Data Protection keyring; losing it logs everyone out.
- **`SESSION_SECRET`** is the Data Protection app-name discriminator, **not** a key. The keyring in `/data/dp-keys` is the actual signing material. Rotating `SESSION_SECRET` scopes new cookies away from old (logs everyone out). Deleting `/data/dp-keys` invalidates the keyring.
- **Env-var defaults**: `ADMIN_USERNAME=admin`, `ADMIN_PASSWORD=changeme`, `MAX_UPLOAD_MB=10`, `RATE_LIMIT_AUTH_PER_MIN=5`, `RETENTION_DAYS=90`, `SESSION_LIFETIME_HOURS=12`, `TZ=Australia/Sydney` (controls server-local time for `/admin/metrics` daily buckets; `tzdata` ships with the aspnet image). `SESSION_SECRET` has a dev default (`dev-only-change-me`) and must be overridden in production.
- **GitHub Actions CI/CD** (`.github/workflows/build.yml`): application-changing PRs run the full
  frontend/.NET `validate` job on GitHub-hosted `ubuntu-latest` and a separate desktop build/config
  test on `windows-latest`. Main pushes publish nothing. A tag runs `prepare-publish`, which rejects
  malformed release tags or tags outside `main`, then Linux `build-and-push` and Windows
  `desktop-build` in parallel. `release` downloads the verified executable, verifies and publishes
  its checksum, and creates one same-repository GitHub Release from a subject-only changelist. The
  repository `GITHUB_TOKEN` has the scoped permissions required for package and release publishing;
  no external publishing credential is used. There is no self-hosted-runner or Podman dependency;
  private-repository jobs consume GitHub-hosted Actions minutes. Feature pushes without a PR run
  nothing; tag pushes are never path-filtered.
- **Shared version** — `BidParserVersion` in `Directory.Build.props` is the build-time source for
  API/Desktop Version and exact InformationalVersion; Assembly/File versions use the SemVer core.
  CI also passes the same value to frontend `VITE_APP_VERSION`, Docker, and the release name. A tag
  `v1.0.0` becomes `1.0.0`. Source-SHA suffixing is disabled so desktop update comparison remains
  parseable. No file edit is needed to release.

## Release versioning (SemVer 2.0.0)

- Tags `vMAJOR.MINOR.PATCH`, beginning with `v1.0.0`.
- `MAJOR` for incompatible API/config/deployment changes, `MINOR` for backwards-compatible features, `PATCH` for backwards-compatible fixes.
- Pre-release identifiers are allowed (for example, `v1.0.0-rc.1` or `v1.1.0-alpha.1`) and are
  sorted per SemVer 2.0.0.
- Every GitHub Release points at a matching pushed tag; pushing a validated tag triggers both the
  Docker build and portable Windows build, then publishes `BidParser.exe` and its checksum to the
  same-repository release.
- **Attribution**: tag messages, release titles, and release bodies name no AI/bot contributor — see the canonical **Attribution policy** in `AGENTS.md`. `softprops/action-gh-release` generates notes from squashed PR titles and the static body block in `build.yml`, so neither the workflow nor the release surfaces tooling attribution unless a PR title introduces it.

## Spec docs index

- `docs/nutanix_software_only_pdf.md` — Software Only (PDF), Nutanix subscription quotes.
- `docs/nutanix_software_only_xlsx.md` — Software Only (XLSX), same data as the PDF, workbook envelope.
- `docs/nutanix_renewal_pdf.md` — Renewal (PDF), subscription renewals with serial/license numbers.
- `docs/nutanix_renewal_xlsx.md` — Renewal (XLSX), workbook envelope of the renewal format.
- `docs/nutanix_hardware_only_pdf.md` — Hardware Only (PDF), multi-quote PDF; parse Quote D only.
- `docs/nutanix_hardware_only_xlsx.md` — Hardware Only (XLSX), multi-quote workbook; parse Quote D only.
- `docs/hp_bid_xlsx.md` — HP Bid (XLSX). Deal-export workbooks with Part Number, Bundle, and Bundle Detail rows; no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/hp_global_bid_xlsx.md` — HP Global Bid (XLSX). `Product numbers` sheet; header anchor `"Product number"`; AUD-only (`Converted net price [AUD]` column required, else `ParseError("currency")`); qty always 1; comments encode remaining qty (`"{remaining} Remaining"`; `Full term (Months)` not parsed); no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/hp_oneconfig_xlsx.md` — HP OneConfig (XLSX). Single Config per file, parent VPN from `Config ID`, MSRP from `Total Price`, 30 children zeroed. Output `% Off RRP with Uplift`; requires `margin` + `imPercent`.
- `docs/hpe_bid_xlsx.md` — HPE Bid (XLSX). HPE deal-export workbooks; header anchor `"LineType"` (one word); `Part Number`/`Bundle`/`BundleDetails` rows; vpn from `ProductNumber`/`BundleID`/`ComponentID` (OptionCode ignored); msrp from `ListPrcEst`, cost from `Offering`, qty from `Quantity` (not Min Order Qty), comments `"Max Qty: {MaxDealQty}"`; BundleDetails msrp/cost → `0.0001` sentinel; no quoted total; available templates `No Calculation` and `Uplift`.
- `docs/lenovo_lbpe_isg_xls.md` — Lenovo LBP-E ISG Quote (XLSX). Dual-container parser (legacy OLE `.xls` and OpenXML `.xlsx`) read via ExcelDataReader; configuration parents take cost from `Subtotal`, children are zero-cost, arithmetic block closure promotes separately billable options to standalone parents, and Solution IDs populate parent comments.
- `docs/lenovo_lbpi_isg_pdf.md` — Lenovo LBP-I ISG Quote (PDF). PRODUCT AND SERVICE DETAILS grid with unnumbered `SID…` rows pricing the numbered lines beneath them, joined to CONFIGURATION DETAILS by line number; one parent per Solution ID (cost = solution total ÷ parent qty), everything else zero-cost children; parts-only quotes have per-line prices. Handles mid-grid Grand Total/header repeats, cross-page wrapped descriptions, and hanging lone hyphens.
- `docs/lenovo_lbpi_idg_pdf.md` — Lenovo LBP-I IDG Quote (PDF). A different Lenovo bid template (laptops/monitors/docks, no Solution IDs) from a separate `Vendors.LenovoIdg` vendor. One priced parent per product-grid line, joined to CONFIGURATION DETAILS by line number for zero-cost children (`"SPEC"` placeholder when label-less). Part Number/Description and Line Item#/Components are genuinely separate ruled columns recovered via `PdfTableHelpers.CentredColumnRanges`'s centred-header recurrence, not a merged-cell split.
- `docs/dell_cto_json.md` — Dell CTO (Quote API JSON). Parent/child SKU tree, child-omission rules, `unit*PriceIncludingShipping` pricing, rebate-eligibility comment.
- `docs/dell_apos_json.md` — Dell APOS (Quote API JSON). Flat SKU list sorted by service tag, native contract dates and device serials.
- `docs/cisco_ccw_quote_xls.md` — Cisco CCW Quote (legacy OLE `.xls`). Three-level Cisco line numbers flattened to the repository's two-level parent/child sequence.
- `docs/zebra_price_concession.md` — Zebra PCR (Price Concession) (PDF + XLS). HTML-disguised XLS (HtmlAgilityPack); PDF derives its column grid from header anchors plus repeated body edges, keeps per-letter geometry to split only at resolved boundaries, and assigns wrapped descriptions by the largest same-page row gap. Field map: Part No.→vpn, Min. Qty→qty *and* min_qty, Max. Qty→comments ("Max Qty: {n}"), List Price→msrp (col H), Unit Special Price→cost (col I). Cancelled=Y rows: literal 0 (not the sentinel) in col H/I, blank col W, qty=1, Comments="Cancelled (Standard Price)", warning modal. On Cost % written to col Z when provided. Available templates: No Calculation and Uplift.
- `docs/datalogic_quote_pdf.md` — Datalogic Price Exception PDF. AUD-only; content-band table recovery; wrapped part numbers; list price, target unit price, minimum shipment size, and Grand Total validation.
- `docs/epson_quote_pdf.md` — Epson project-price PDF. Quote-level quantity, content-band table recovery, no quoted total, and Model retained in raw source data.
- `docs/strike_quote_pdf.md` — Strike quote PDF. Multi-page table continuation, space-aware wrapped product codes, printed-Value validation, and delivery-exclusive quoted total.
- `docs/output_mapping.md` — how parsed `LineItem` fields map into `ANZ-GENERIC_*.xlsx`, output filename convention, column visibility across all 8 CRM upload templates, and locked output rules.
- `docs/desktop.md` — portable WPF host behavior, local-data boundary, remote schema/trust model, update semantics, packaging/release prerequisites, and Windows acceptance runbook.
- `docs/architecture.md` — human-facing architecture guide: project responsibilities, parse flow, persistence, auth, and where to start on a given change. The orientation document; this file is the deep reference behind it.
- `docs/troubleshooting.md` — diagnosing common build, container, database, auth, and parse failures.
- `docs/DEPLOYMENT.md` — operator runbook: env-var table, volumes, reverse proxy, upgrades, CI/CD triggers.
- `docs/agent_tooling.md` — MCP tooling reference for agents: the Roslyn and Playwright tool inventories, when to prefer each over text search, and the E2E-test criteria. Rules live in `AGENTS.md`.

The design language the SPA implements (slate-50 background, white cards with `border-slate-200` +
`shadow-sm`, `#0077d4` accent, `#0063FF` brand blue, Inter typography, uppercase tracked labels) is
captured in `frontend/src/styles.css` and the components themselves. The original HTML design
prototypes have been removed — the implemented components are the reference.

### Output templates

- `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` — Nutanix output. Mapping in `docs/output_mapping.md`.
- `samples/template/ANZ-GENERIC_NoCalculation.xlsx` — HP, no Margin column populated. `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_Uplift.xlsx` — HP, Margin column populated. `docs/output_mapping.md` HP section.
- `samples/template/ANZ-GENERIC_PercentOffWithUplift.xlsx` — HP OneConfig (Margin col K + IM% col X populated; MSRP on H, Cost col I blank). `docs/hp_oneconfig_xlsx.md`.

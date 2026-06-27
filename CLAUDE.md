# CLAUDE.md

Canonical project reference for Claude Code in this repository. This file is kept
lean: build/test commands, core architecture principles, state-tracking rules, and
code style. **Detailed reference** — per-format extraction algorithms, golden
expected outputs, the full API surface, response conventions, auth/deployment
detail, and the sample→format mapping — lives in **`docs/project_memory.md`**.
Read that file (and the per-format specs in `docs/`) when you need specifics.

## What is being built

Two products in one repo, sharing the same parser core:

- **Web app** — ASP.NET Core 10 + SQL Server + React 19/Vite/TypeScript, deployed
  as a single Docker image via `docker-compose` behind nginx-proxy-manager. Users
  log in, upload a supplier quote (PDF or XLSX), and download a standardised
  `*_parsed.xlsx` from a result popup.
- **BidParserLite** — a portable Windows desktop variant: a single self-contained
  `.exe`, no installer, no database, no admin rights required. Reuses the same
  `Domain`/`Parsing`/`Output` projects via `BidParserLite.sln`. Ships as a
  GitHub Release asset on every `v*` tag alongside the Docker image.

Work is **iterative, one supplier format at a time** — the architecture is a
pluggable `IParser` registry so a new format is one parser class + fixtures + one
registry entry, and both products pick it up automatically.

Backend was re-platformed from Python/FastAPI to ASP.NET Core 10. All tests pass:
`dotnet test BidParser.sln` (256: 178 parsing + 78 API integration). API tests
need a running Docker daemon (SQL Server testcontainer).

## Project layout

**Two solutions share three projects:**

| Project | `BidParser.sln` | `BidParserLite.sln` |
|---|:---:|:---:|
| `src/BidParser.Domain/` | ✓ | ✓ |
| `src/BidParser.Parsing/` | ✓ | ✓ |
| `src/BidParser.Output/` | ✓ | ✓ |
| `src/BidParser.Core/` | — | ✓ |
| `src/BidParser.Wpf/` | — | ✓ |
| `src/BidParser.Api/` | ✓ | — |
| `src/BidParser.Infrastructure/` | ✓ | — |
| `tests/BidParser.Parsing.Tests/` | ✓ | ✓ |
| `tests/BidParser.Api.Tests/` | ✓ | — |

- `src/BidParser.Api/` — Minimal API. Endpoints `/auth/*`, `/me`, `/users`, `/parsers`, `/parse`, `/history`, `/metrics/*`, `/monitoring/*`, health. Cookie auth, CSRF, rate limiters, `GlobalExceptionHandler`, `SecurityHeadersMiddleware`, locked-down `ForwardedHeaders`. Typed response records in `Contracts/`, decimal converters in `Serialization/`. Hosts the SPA from `wwwroot/`.
- `src/BidParser.Domain/` — `LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`, `IParser`, `IParserRegistry`. `Constants/` centralises `Vendors.*`, `CrmTemplates.*`, `ParserSlugs.*`, and `ReportTypes.*` (the hardcoded slug → report-type map, see below).
- `src/BidParser.Infrastructure/` — `AppDbContext` (EF Core + SQL Server, `AddDbContextPool`), entities (`User`, `ParseJob`, `ParseMetric`, `FailedParseJob`), migrations, `FileStorage`, `ParseService`, `FailedParseJobRecorder`, `RetentionService`.
- `src/BidParser.Parsing/` — PDF helpers via PdfPig (`Pdf/`), XLSX helpers via ClosedXML (`Xlsx/`), legacy `.xls` via ExcelDataReader, six Nutanix + three HP + one HPE + two Lenovo + two Zebra parsers, explicit `Registry/ParserRegistry.cs`.
- `src/BidParser.Output/` — `ForeignUpliftWriter`, `AnzGenericWriter`, `PercentOffWithUpliftWriter`, shared `TemplateLayout`, `OutputNaming`.
- `src/BidParser.Core/` (**desktop only**) — `ParseRunner`: the pure parse→write orchestration lifted from `ParseService` with all DB/auth/User code removed. Returns `ParseOutcome { Validation, Currency, CancelledLines, OutputPath }`. Also defines `CancelledLine` and `ParseValidationException`.
- `src/BidParser.Wpf/` (**desktop only**, `net10.0-windows`) — WPF shell. `MainViewModel` ports the web SPA's state: vendor/file-type/template pickers and the **vendor-driven** conditional-field matrix from `ParseSettingsCard.tsx` + the settings blocks (FX Rate, Uplift, Discount Off MSRP, On Cost %), plus `canSubmit` enablement and wrong-file-type / currency / generic error branching from `DashboardPage.tsx`, with success / warning result panels (each showing the hardcoded report type). A **Reset** button (`ResetCommand`) restores the launch defaults (file, numeric inputs, result/error, and the vendor→file-type→template selection). A footer shows the app version (`AppVersionDisplay`, from the assembly's `InformationalVersion` — stamped by the release `-p:Version=`, `0.0.0-dev` locally), a **GitHub** link (`OpenReleasesCommand` opens the releases page in the browser), and a dynamic-year `Ingram Micro` copyright. On Convert a Windows **Save As** dialog (View-side `SaveFilePrompt` delegate) lets the user name/place the output, pre-filled with the `OutputNaming` default beside the input; cancelling aborts before any parse. Brand assets in `Assets/` (`app.ico` is the exe `<ApplicationIcon>` + `Window.Icon`; `logo.png` is the in-window header logo) — both bundled as `<Resource>`. No DI, no config files, no persistent storage. (Full field matrix in `docs/project_memory.md`.)
- `tests/BidParser.Parsing.Tests/`, `tests/BidParser.Api.Tests/` — xUnit; the latter uses `WebApplicationFactory` (shared infra in `TestInfrastructure.cs`).
- `frontend/` — React 19/Vite/TypeScript SPA, `recharts ^2`. ProductLens visual design; shared utility classes in `src/styles.css`; CRM template names in `src/constants.ts`. Brand assets in `public/` (`logo.png` 512×512 master; `favicon.ico`, `apple-touch-icon.png`, `icon-192.png`, `site.webmanifest` derived from it) — the logo renders in `AppHeader`/`LoginPage` and the favicons are linked from `index.html` (theme colour `#0063FF`).
- `samples/inputs/`, `samples/outputs/` (golden `<basename>_parsed.xlsx`), `samples/template/` (output templates).
- `docs/` — per-format extraction specs, `output_mapping.md`, `DEPLOYMENT.md`, `design/`, and `project_memory.md` (detailed reference).

## Core architecture principles

- **Anchor-based extraction is mandatory.** Every parser locates sections, headers, totals, and column positions by searching for anchor strings — never hard-code row numbers, column letters, or fixed offsets. The same workbook can contain multiple quote sections where rows shift and column letters differ between sections (Quote C uses column H for `Product Code`; Quote D in the same file uses column E). Hard-coded positions break on real quotes.
- **Single parser contract.** Every parser returns `ParseResult { Metadata, LineItems, Validation }`. `LineItem` requires only `VPN`, `Cost`, `Qty`; the rest are nullable, populated per format. See `docs/project_memory.md` for the full contract.
- **Validation is identical across formats**: `computed_total = Σ(cost × qty)` compared to `quoted_total` with `0.01` tolerance. For formats with **no quoted total** (e.g. HP), construct `ValidationResult` directly with `Matches = true`, `Difference = 0` — do **not** call `ParseValidation.Validate(items, null)`, which returns `Matches = false` and trips the frontend's mismatch modal on every parse.
- **Format detection is a soft hint, not routing.** `Detect()` returns 0.0–1.0. It is **not** used to route or pre-fill the dropdown (the user always picks the file type). Its one consumer is the **wrong-file-type** flow: when the selected parser fails recognition, `ParseService` runs `Detect()` across the *same-vendor, same-MIME* siblings to name the likely-correct type (see "Wrong file-type handling" below). Parsers without a `Detect()` override default to `0.0` and simply never get suggested.
- **Wrong file-type handling.** A recognition failure — `ParseError(stage: "detect")`, raised when the table anchor or a required column (`HeaderMap.Require`) is missing — is treated as a *wrong file-type selection*, **not** a recorded failure. `ParseService` catches it before the generic handler: it suggests the correct type via sibling `Detect()` (confidence ≥ 0.7), deletes the stored upload, writes **nothing** (no `FailedParseJob`/`ParseJob`/`ParseMetric`), and rethrows `ParseError(stage: "file_type")` with a composed message (named when confident, generic otherwise). Failures *after* the table is located (e.g. `stage: "currency"`, `"extract"`, magic-byte `"upload"`) remain genuine recorded failures. The SPA shows `FileTypeErrorModal` for `stage: "file_type"`.
- **Registry is the single extension point** — `src/BidParser.Parsing/Registry/ParserRegistry.cs` holds an explicit `IReadOnlyList<IParser>`. No assembly scanning; registration order is visible. Each parser lives in its own subfolder.
- **Dates** are `DateOnly` internally (serialised ISO `YYYY-MM-DD`); `DD/MM/YYYY` display is a frontend concern.
- **`XQ-4108785` (PDF + XLSX) are multi-quote files** (Quote A/B/C/D). Both parsers extract **Quote D only**. Quote C is a separate budgetary breakdown; A/B are noise.

## State-tracking & data rules

- **Entity timestamps** are stamped exclusively by `AppDbContext.StampTimestamps()` — never add `= DateTime.UtcNow` initializers on entities.
- **Zero-dollar sentinel**: writers emit `TemplateLayout.ZeroPriceSentinel = 0.0001m` for any zero-dollar price (downstream import rejects literal `0`). Single source of truth — change the constant to retune. Placement differs per writer (see `docs/project_memory.md`).
- **Numeric inputs are never persisted** back to the User row from the parse flow. Only the last-used vendor persists (`ParseService` sets `user.DefaultVendor` on success). The dashboard re-prompts FX rate / margin / IM% every parse; the three numeric inputs always start empty on page load.
- **`ParseMetric`** is an append-only ledger written transactionally with `ParseJob` on success; retained indefinitely (FK nulled by `ON DELETE SET NULL` when the `ParseJob` is purged at `RETENTION_DAYS`).
- **`FailedParseJob`** records exception failures (catch block) and `ValidationMismatch` entries (success path, best-effort, fresh `AppDbContext` scope). Category serialises snake_case.
- **Admin monitoring is a unified runs view.** `GET /api/monitoring/runs` (admin) merges successful/mismatched `ParseJob` rows (`kind: "job"`, status `success` / `validation_mismatch`, both input+output downloadable) with genuine `FailedParseJob` failures (`kind: "failure"`, status = category, input only). A validation mismatch exists in **both** tables; the `ParseJob` is the source of truth, so the runs query **excludes** `FailedParseJob` rows where `Category == ValidationMismatch` to surface each mismatch exactly once (with its output file). Filters: `status`, `vendor`, `userId`, `parserSlug`, `from`/`to`; each table is read to `offset+limit` then merged/sorted/paged in memory. Downloads: `/monitoring/jobs/{id}/source|output` (jobs) and `/monitoring/failures/{id}/source` (failures, input only). The SPA renders this at `/admin/monitoring`.
- **Magic-byte upload validation**: `%PDF` / `PK\x03\x04`, checked alongside extension in `ParseService`.

## Canonical naming (locked vocabulary)

Display headers (Title Case, UI + chat tables) and field names (snake_case, model/JSON/tests) are decoupled but one-to-one. **Old names (`part_number`, `cost_price`, `term_months`, `quantity`) must not appear in new code or docs.**

| Concept | Display header | Field name | Notes |
|---|---|---|---|
| Part number | `Part Number` | `vpn` | Vendor Part Number |
| Description | `Description` | `description` | |
| Term in months | `Term` | `term` | |
| List / catalogue price | `List Price` | `msrp` | |
| Customer price | `Sale Price` | `cost` | |
| Quantity | `Quantity` | `qty` | |
| Serial number (incl. embedded license) | `Serial Number` | `serial_number` | |
| Subscription start | `Start Date` | `start_date` | |
| Subscription end | `End Date` | `end_date` | |
| Minimum order quantity | `Min Order Qty` | `min_qty` | HP only; null for Nutanix |
| Output line sequence | `Item` | `line_sequence` | HP only; col A of ANZ-GENERIC templates |
| Output comments | `Comments` | `comments` | col R of ANZ-GENERIC templates; set by parsers that need it (e.g. Global Bid writes `"{remaining} Remaining"`; HP Bid writes `"Max Qty: {Max Deal Qty}"` on Part Number/Bundle lines); null = blank |

Parsers detect *source* labels (`"Net Unit Price"`, `"List Unit Price"`, …) as extraction anchors; the source label is captured in `raw[source_label]`, the cleaned value written to the canonical field.

**UI label vocabulary differs from field names** (presentation-only; API fields and DB columns unchanged): `margin` renders as **`Uplift`**; HP OneConfig `im_percent` renders as **`Discount Off MSRP`**.

## Code style

- **Use the centralised constants** (`Vendors.*`, `CrmTemplates.*`, `ParserSlugs.*`, `ReportTypes.*` from `BidParser.Domain.Constants`) — never inline these string literals. Add the constant first for a new vendor/template/slug.
- **Report type is a hardcoded slug → report-type map**, not DB config. `ReportTypes.For(slug)` (in `BidParser.Domain.Constants`) is the single source of truth shared by both products: the web surfaces it via `/api/parsers` (`report_type` field, shown in `ParseResultModal`), and the desktop reads it directly (shown in the result panel). Unmapped slugs render no guidance. There is no admin UI or endpoint for it.
- **Typed response records only** — use the records in `Contracts/` (`ApiError`, `PasswordValidationError`, `ParseErrorResponse`, `OkResponse`, …). No anonymous `new { detail = "…" }` objects. Three error body shapes; the SPA branches on shape (see `docs/project_memory.md`).
- **JSON casing**: `JsonNamingPolicy.SnakeCaseLower` globally — no per-property `[JsonPropertyName]`.
- **Decimal serialisation**: money/rates serialise as fixed-scale strings via per-field `[JsonConverter]` (`fx_rate` 4 dp, `margin`/totals 2 dp).
- **ClosedXML**: use `cell.GetFormattedString()`, not the typed `XLCellValue`.
- Primary constructors on services; full `CancellationToken` propagation; structured logging with no secrets.
- Vendor-prefixed slugs/specs (`nutanix_software_only_pdf`, …) — never reintroduce vendorless slugs.

## Commands

- `dotnet test BidParser.sln` — full suite (256 tests; API tests need Docker).
- `dotnet build BidParser.Core` — verify the shared orchestrator compiles (no Docker needed).
- `dotnet run --project src/BidParser.Api` — backend dev server (`http://localhost:5000`).
- `cd frontend && npm run dev` — Vite dev server, proxies `/api` to `http://127.0.0.1:5000` (`VITE_API_PROXY_TARGET=…` to point elsewhere).
- `cd frontend && npm run build` — TypeScript + production frontend build.
- `docker compose up -d` — production container (after `cp .env.example .env`, set `SESSION_SECRET`).
- `docker compose build` — local image build from the repo-root `Dockerfile`.
- **Windows only** — `dotnet build BidParserLite.sln` / `dotnet test BidParserLite.sln` / `dotnet publish src/BidParser.Wpf -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` (produces `BidParserLite.exe`).

## Adding a parser format

1. **One parser class** in `src/BidParser.Parsing/<Vendor>/<Slug>/` implementing `IParser` (`Slug`, `DisplayName`, `Vendor`, `AcceptedMime`, `CrmTemplate`, `Parse`; optional `Detect` defaults `0.0`). Reference `Vendors.*`/`CrmTemplates.*`/`ParserSlugs.*`. Override `AvailableTemplates` only for multi-template parsers (the frontend then auto-renders a dropdown). **If the vendor has ≥2 formats sharing a MIME, add a `Detect()` signature** (anchor/header check, `try/catch → score`) here *and* to the siblings so the wrong-file-type flow can name the correct type — and a cross-detection test case in `WrongFileTypeDetectionTests` (each format high on its own fixture, `< 0.7` on siblings). Lenovo/Zebra have a single format per MIME, so they need no `Detect()`.
2. **One registry entry** appended to `ParserRegistry.cs`.
3. **A `ReportTypes` map entry** (keyed by the new slug) in `src/BidParser.Domain/Constants/ReportTypes.cs` if the format needs report-type guidance in the result popup; omit it to show none. Both products read it.
4. **One fixture + golden** under `samples/inputs/` and `samples/outputs/`, plus a test case. Add a `SAMPLE_FILES` entry + file under `frontend/public/samples/` (`FileTypeSelect.tsx`).
5. **Optionally a spec** `docs/<vendor>_<format>.md`, linked from `docs/project_memory.md`.
6. **New CRM template** → new writer in `src/BidParser.Output/`, dispatch `case` in both `ParseService.ParseAsync` (web) and `ParseRunner.Run` (desktop).
7. **Both solutions pick up the new parser automatically** — rebuild `BidParserLite.sln` on Windows to verify.

**Parser-specific error modals**: throw `ParseError("currency", hint, message)` (or another stage name) from the parser; the API returns HTTP 422 with `{ detail: { stage, hint, message } }`. To show a dedicated modal for a specific stage, add a `isCurrencyError`-style helper in `DashboardPage.tsx` and a matching modal component — the existing `CurrencyErrorModal` (AUD validation) is the pattern to follow. **Reserved stages:** `"detect"` is the wrong-file-type signal (recognition failure — see "Wrong file-type handling"); it is reclassified to `"file_type"` by `ParseService` and rendered by `FileTypeErrorModal`. Use `"detect"` only for "this file isn't my format" failures, never for genuine extraction errors.

Do **not** touch API routes, frontend components (dropdowns auto-populate from `/api/parsers`), Docker, validation, auth, or history — the parser surface is the entire change. Preserve that property.

## Working with the user

- Iterate one supplier format at a time. **Confirm extraction accuracy in chat (render a table) before writing scaffolding** for a new format.
- Output field mapping is locked in `docs/output_mapping.md` — read it; don't re-derive cell positions from the template.
- Flag scope drift against the MVP guardrails (single-file upload, result-popup download, no review/edit gate — full list in `docs/project_memory.md`) before building anything outside them.

## Release versioning

SemVer 2.0.0 tags `vMAJOR.MINOR.PATCH`; `0.y.z` while in internal testing. Pushing a `v*` tag triggers three CI jobs: tests → Docker image to `ghcr.io` **and** a portable `BidParserLite-X.Y.Z-win-x64.exe` published as a GitHub Release asset. Details in `docs/project_memory.md`.

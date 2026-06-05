# CLAUDE.md

Canonical project reference for Claude Code in this repository. This file is kept
lean: build/test commands, core architecture principles, state-tracking rules, and
code style. **Detailed reference** — per-format extraction algorithms, golden
expected outputs, the full API surface, response conventions, auth/deployment
detail, and the sample→format mapping — lives in **`docs/project_memory.md`**.
Read that file (and the per-format specs in `docs/`) when you need specifics.

## What is being built

An internal web app for sales operations. Users upload a supplier quote (PDF or
XLSX in supplier-specific layouts); the app extracts and validates line items and
presents a result popup (success/warnings, a Download button, and the
admin-configured report type to use) from which the user downloads a standardised
`*_parsed.xlsx`. Stack: ASP.NET Core 10 backend +
React 19/Vite/TypeScript frontend, deployed as a single Docker image via
`docker-compose` behind nginx-proxy-manager. Work is **iterative, one supplier
format at a time** — the architecture is a pluggable `IParser` registry so a new
format is one parser class + fixtures + one registry entry.

Backend was re-platformed from Python/FastAPI to ASP.NET Core 10. All tests pass:
`dotnet test BidParser.sln` (237: 161 parsing + 76 API integration). API tests
need a running Docker daemon (SQL Server testcontainer).

## Project layout

- `src/BidParser.Api/` — Minimal API. Endpoints `/auth/*`, `/me`, `/users`, `/parsers`, `/report-types` (admin), `/parse`, `/history`, `/metrics/*`, `/monitoring/*`, health. Cookie auth, CSRF, rate limiters, `GlobalExceptionHandler`, `SecurityHeadersMiddleware`, locked-down `ForwardedHeaders`. Typed response records in `Contracts/`, decimal converters in `Serialization/`. Hosts the SPA from `wwwroot/`.
- `src/BidParser.Domain/` — `LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`, `IParser`, `IParserRegistry`. `Constants/` centralises `Vendors.*`, `CrmTemplates.*`, `ParserSlugs.*`.
- `src/BidParser.Infrastructure/` — `AppDbContext` (EF Core + SQL Server, `AddDbContextPool`), entities (`User`, `ParseJob`, `ParseMetric`, `FailedParseJob`, `ReportTypeConfig`), migrations, `FileStorage`, `ParseService`, `FailedParseJobRecorder`, `RetentionService`.
- `src/BidParser.Parsing/` — PDF helpers via PdfPig (`Pdf/`), XLSX helpers via ClosedXML (`Xlsx/`), legacy `.xls` via ExcelDataReader, six Nutanix + three HP + two Lenovo parsers, explicit `Registry/ParserRegistry.cs`.
- `src/BidParser.Output/` — `ForeignUpliftWriter`, `AnzGenericWriter`, `PercentOffWithUpliftWriter`, shared `TemplateLayout`, `OutputNaming`.
- `tests/BidParser.Parsing.Tests/`, `tests/BidParser.Api.Tests/` — xUnit; the latter uses `WebApplicationFactory` (shared infra in `TestInfrastructure.cs`).
- `frontend/` — React 19/Vite/TypeScript SPA, `recharts ^2`. ProductLens visual design; shared utility classes in `src/styles.css`; CRM template names in `src/constants.ts`.
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
| Output comments | `Comments` | `comments` | col R of ANZ-GENERIC templates; set by parsers that need it (e.g. Global Bid writes `"{term} Months \| {remaining} Remaining"`); null = blank |

Parsers detect *source* labels (`"Net Unit Price"`, `"List Unit Price"`, …) as extraction anchors; the source label is captured in `raw[source_label]`, the cleaned value written to the canonical field.

**UI label vocabulary differs from field names** (presentation-only; API fields and DB columns unchanged): `margin` renders as **`Uplift`**; HP OneConfig `im_percent` renders as **`Discount Off MSRP`**.

## Code style

- **Use the centralised constants** (`Vendors.*`, `CrmTemplates.*`, `ParserSlugs.*` from `BidParser.Domain.Constants`) — never inline these string literals. Add the constant first for a new vendor/template/slug.
- **Typed response records only** — use the records in `Contracts/` (`ApiError`, `PasswordValidationError`, `ParseErrorResponse`, `OkResponse`, …). No anonymous `new { detail = "…" }` objects. Three error body shapes; the SPA branches on shape (see `docs/project_memory.md`).
- **JSON casing**: `JsonNamingPolicy.SnakeCaseLower` globally — no per-property `[JsonPropertyName]`.
- **Decimal serialisation**: money/rates serialise as fixed-scale strings via per-field `[JsonConverter]` (`fx_rate` 4 dp, `margin`/totals 2 dp).
- **ClosedXML**: use `cell.GetFormattedString()`, not the typed `XLCellValue`.
- Primary constructors on services; full `CancellationToken` propagation; structured logging with no secrets.
- Vendor-prefixed slugs/specs (`nutanix_software_only_pdf`, …) — never reintroduce vendorless slugs.

## Commands

- `dotnet test BidParser.sln` — full suite (237 tests).
- `dotnet run --project src/BidParser.Api` — backend dev server (`http://localhost:5000`).
- `cd frontend && npm run dev` — Vite dev server, proxies `/api` to `http://127.0.0.1:5000` (`VITE_API_PROXY_TARGET=…` to point elsewhere).
- `cd frontend && npm run build` — TypeScript + production frontend build.
- `docker compose up -d` — production container (after `cp .env.example .env`, set `SESSION_SECRET`).
- `docker compose build` — local image build from the repo-root `Dockerfile`.

## Adding a parser format

1. **One parser class** in `src/BidParser.Parsing/<Vendor>/<Slug>/` implementing `IParser` (`Slug`, `DisplayName`, `Vendor`, `AcceptedMime`, `CrmTemplate`, `Parse`; optional `Detect` defaults `0.0`). Reference `Vendors.*`/`CrmTemplates.*`/`ParserSlugs.*`. Override `AvailableTemplates` only for multi-template parsers (the frontend then auto-renders a dropdown). **If the vendor has ≥2 formats sharing a MIME, add a `Detect()` signature** (anchor/header check, `try/catch → score`) here *and* to the siblings so the wrong-file-type flow can name the correct type — and a cross-detection test case in `WrongFileTypeDetectionTests` (each format high on its own fixture, `< 0.7` on siblings). Lenovo/Zebra have a single format per MIME, so they need no `Detect()`.
2. **One registry entry** appended to `ParserRegistry.cs`.
3. **One fixture + golden** under `samples/inputs/` and `samples/outputs/`, plus a test case. Add a `SAMPLE_FILES` entry + file under `frontend/public/samples/` (`FileTypeSelect.tsx`).
4. **Optionally a spec** `docs/<vendor>_<format>.md`, linked from `docs/project_memory.md`.
5. **New CRM template** → new writer in `src/BidParser.Output/`, dispatch `case` in `ParseService.ParseAsync`.

**Parser-specific error modals**: throw `ParseError("currency", hint, message)` (or another stage name) from the parser; the API returns HTTP 422 with `{ detail: { stage, hint, message } }`. To show a dedicated modal for a specific stage, add a `isCurrencyError`-style helper in `DashboardPage.tsx` and a matching modal component — the existing `CurrencyErrorModal` (AUD validation) is the pattern to follow. **Reserved stages:** `"detect"` is the wrong-file-type signal (recognition failure — see "Wrong file-type handling"); it is reclassified to `"file_type"` by `ParseService` and rendered by `FileTypeErrorModal`. Use `"detect"` only for "this file isn't my format" failures, never for genuine extraction errors.

Do **not** touch API routes, frontend components (dropdowns auto-populate from `/api/parsers`), Docker, validation, auth, or history — the parser surface is the entire change. Preserve that property.

## Working with the user

- Iterate one supplier format at a time. **Confirm extraction accuracy in chat (render a table) before writing scaffolding** for a new format.
- Output field mapping is locked in `docs/output_mapping.md` — read it; don't re-derive cell positions from the template.
- Flag scope drift against the MVP guardrails (single-file upload, result-popup download, no review/edit gate — full list in `docs/project_memory.md`) before building anything outside them.

## Release versioning

SemVer 2.0.0 tags `vMAJOR.MINOR.PATCH`; `0.y.z` while in internal testing. Pushing a `v*` tag triggers the Docker build and publishes to `ghcr.io`. Details in `docs/project_memory.md`.

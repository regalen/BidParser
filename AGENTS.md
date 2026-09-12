# AGENTS.md

Canonical working agreement for any AI coding agent in this repository, whichever tool it runs
under. This file is kept lean: the invariants that are expensive to rediscover and costly to break,
plus build/test commands and code style.

It is not the place to learn what the project is. Read these first, in this order, and do not
duplicate their content back into this file:

- **`README.md`** — what BidParser does, supported formats, getting started.
- **`docs/architecture.md`** — components, parse flow, persistence, auth, where to start on a change.
- **`docs/project_memory.md`** — deep reference: per-format extraction algorithms, golden expected
  outputs, the full API surface, response conventions, the sample→format mapping.
- **`docs/desktop.md`** — portable Windows behavior, trust boundary, packaging, release setup, and
  manual acceptance.
- **`docs/<vendor>_<format>.md`** — the spec for a specific format you are touching.
- **`CONTRIBUTING.md`** — branching, PRs, releases, local Docker validation.

## Sample data, fixtures and commercially sensitive information

This is a public repository. **Raw customer files must never be committed**, including as a
temporary intermediate that would be removed later. Git retains prior objects, so a later cleanup
commit does not make an earlier disclosure safe.

Before any PDF, XLS, XLSX, CSV, JSON, HTML, screenshot, golden output, template, browser-download
sample, or material derived from a real commercial transaction is copied into the working tree:

1. Inspect both its filename and its contents; renaming a file is not sanitisation.
2. Replace customer, distributor and reseller names; individual contact names; email addresses;
   addresses; phone numbers; account, quote, bid, contract, serial, opportunity, deal-registration
   and other internal identifiers with explicitly synthetic values. Use `example.invalid` for
   placeholder email addresses.
3. Replace customer-specific pricing, purchase pricing, non-public discounts, and commercially
   sensitive metadata unless the value has been deliberately reviewed and documented as
   publication-safe.
4. Prefer fully synthetic fixtures whenever technically feasible. Where a real-world structure is
   necessary, transform it into an explicitly publication-safe fixture before it enters the tree.
5. Preserve the structural anchors, row/column positions, document format, extraction-relevant
   values, and test coverage needed by the parser; then run the relevant parser/output tests.
6. Apply the same review to golden outputs, document metadata, screenshots, logs, comments and test
   names. Keep each `frontend/public/samples/` file byte-identical to its root input fixture.
7. Never commit an unsanitised file with the intention of cleaning it in a later commit. If there
   is any uncertainty, do not commit it.

Public supplier product names, standard legal text, and public supplier-facing information may be
retained only where required for fixture structure or coverage and documented in
`docs/sample_fixture_sanitisation.md`. They are never a reason to retain identifiable customer,
reseller, employee, or non-public commercial data.

## Repository navigation

Layout and component responsibilities are in `docs/architecture.md`; do not re-derive them by
sweeping the tree. The orientation that matters for making a change:

- **`src/BidParser.Domain`** is the contract layer and depends on nothing. Changing anything here
  (`IParser`, `LineItem`, a `Constants` member) fans out across every parser and writer — establish
  the real call set first.
- **`src/BidParser.Parsing/Registry/ParserRegistry.cs`** is the explicit list of registered parsers
  and the single extension point. There is no assembly scanning.
- **`src/BidParser.Output`** holds one writer for all CRM templates. Per-template behaviour belongs
  in `CrmTemplateProfile`, per-format behaviour in a trait on the parser — never a vendor check in
  the writer.
- **`samples/inputs/`** are fixtures; **`samples/outputs/`** are golden workbooks that parser tests
  compare against cell by cell. Treat a golden diff as a finding, not a chore.
- **Fixture sanitisation is mandatory.** Follow the public-data rules above before adding or
  replacing any input, golden, template, or browser-download sample.
- **`frontend/`** never hardcodes the parser list; dropdowns populate from `/api/parsers`.
- **`src/BidParser.Application`** is the host-neutral parse/write seam. Desktop must never reference
  Infrastructure/API; web-only persistence, auth, Dell acquisition, history, and metrics stay above it.

Work is **iterative, one supplier format at a time**. A new format should be one parser class plus
fixtures plus one registry entry, with API routes, frontend components, Docker, auth, validation,
and history untouched. Preserve that property.

## Desktop invariants

- Desktop v1 is local/offline and excludes Dell. It consumes the shared registry/Application/
  Parsing/Output stack; do not add manual Dell JSON, a localhost server, database, authentication,
  retained quote copy, history, registry persistence, or a second parser/writer implementation.
- Embedded schema-v1 guidance/defaults make startup usable without network. Public configuration
  can affect safe wording and prefills only; it must never control capabilities, parsing, output,
  filenames, endpoints, or executable behavior. Keep independent failure/fallback and hard size,
  timeout, redirect, markup, duplicate, precision, and schema validation.
- Release output is exactly one unsigned, self-contained Windows x64 `BidParser.exe`. Publish on
  Windows with the checked-in profile; Linux cross-builds are compile checks, not native resource
  acceptance. A validated `v*` tag on `regalen/BidParser` is the single SemVer source for Docker
  and desktop artifacts; it creates the same-repository GitHub Release with `BidParser.exe` and its
  SHA-256 checksum. Main pushes never publish artifacts. Use the scoped repository `GITHUB_TOKEN`;
  do not introduce external release publishing or separate release tokens.

## Core architecture principles

- **Anchor-based extraction is mandatory.** Every parser locates sections, headers, totals, and column positions by searching for anchor strings — never hard-code row numbers, column letters, or fixed offsets. The same workbook can contain multiple quote sections where rows shift and column letters differ between sections (Quote C uses column H for `Product Code`; Quote D in the same file uses column E). Hard-coded positions break on real quotes.
- **Single parser contract.** Every parser returns `ParseResult { Metadata, LineItems, Validation }`. `LineItem` requires only `VPN`, `Cost`, `Qty`; the rest are nullable, populated per format. See `docs/project_memory.md` for the full contract.
- **Validation is identical across formats**: `computed_total = Σ(cost × qty)` compared to `quoted_total` with `0.01` tolerance. For formats with **no quoted total** (e.g. HP), construct `ValidationResult` directly with `Matches = true`, `Difference = 0` — do **not** call `ParseValidation.Validate(items, null)`, which returns `Matches = false` and trips the frontend's mismatch modal on every parse.
- **Format detection is a soft hint, not routing.** `Detect()` returns 0.0–1.0 and is **never** used to silently reroute a manual selection. Its only consumers are the wrong-file-type suggestion flow and the per-vendor **Auto** option (`AutoDetectTypes` — preselected for Nutanix, Lenovo ISG, and Zebra; the *only* option for Dell). Lenovo IDG has no Auto entry (single format). Parsers without a `Detect()` override default to `0.0` and are never suggested or auto-detected. Scoring detail: `docs/project_memory.md` §Auto file-type detection.
- **Wrong file-type handling.** A recognition failure — `ParseError(stage: "detect")`, raised when the table anchor or a required column (`HeaderMap.Require`) is missing — is a *wrong file-type selection*, **not** a recorded failure. `ParseService` suggests the correct type via sibling `Detect()`, deletes the upload, writes **nothing** (no `FailedParseJob`/`ParseJob`/`ParseMetric`), and rethrows `stage: "fileType"` for `FileTypeErrorModal`. Failures *after* the table is located (`"currency"`, `"extract"`, magic-byte `"upload"`) remain genuine recorded failures. Mechanism: `docs/project_memory.md` §Wrong file-type detection.
- **Registry is the single extension point** — `src/BidParser.Parsing/Registry/ParserRegistry.cs` holds an explicit `IReadOnlyList<IParser>`. No assembly scanning; registration order is visible. (Synthesized Auto entries emitted by `/api/parsers` via `AutoDetectTypes` are the single exception.) Each parser lives in its own subfolder.
- **Dell quotes use the Quote API, not manual JSON upload**, and have **no manual file-type selection.** The dashboard takes a Dell quote ID; the backend fetches the JSON and `dell_auto`'s `Detect()` alone decides CTO vs APOS — `ParseSettingsCard` locks the dropdown and `ParseEndpoints` independently rejects any `vendor=Dell` request whose `parserSlug` isn't `dell_auto` (`400`). Both Dell parsers are **No Calculation only** and validate `Σ(cost × qty)` against the payload's top-level `salesPrice` (not a per-item echo). The full-tree `ParseOptions.IncludeSubComponentDetail` machinery is deliberately retained as test-only and must not be removed. See `docs/dell_cto_json.md` / `docs/dell_apos_json.md`.
- **Dates** are `DateOnly` internally (serialised ISO `YYYY-MM-DD`); `DD/MM/YYYY` display is a frontend concern.
- **`XQ-4108785` (PDF + XLSX) are multi-quote files** (Quote A/B/C/D). Both parsers extract **Quote D only**. Quote C is a separate budgetary breakdown; A/B are noise.

## State-tracking & data rules

- **Entity timestamps** are stamped exclusively by `AppDbContext.StampTimestamps()` — never add `= DateTime.UtcNow` initializers on entities.
- **The two zero prices.** CRM does not accept a literal `0` as a price. That one fact gives the output **two deliberate ways to write zero**, and they mean opposite things — choosing wrong silently changes what the customer is quoted. Both constants live in `TemplateLayout`; never write a bare `0m` to a price column.
  - **`ZeroPriceSentinel = 0.0001m` — "this line really is free."** The default for a genuine zero-dollar price. CRM would reject a literal `0`, so writers emit the sentinel and CRM rounds it back to `0.00` on import. Used for every dropped-component line whose price sits on its parent (HP `Bundle Detail`, HPE `BundleDetails`, Lenovo/Cisco/Dell CTO children, HP OneConfig children). Apply it via `TemplateLayout.NonZeroPrice(value)`, never by hand.
  - **`NoBidPrice = 0m` — "this line carries no bid price."** A literal `0`, written on purpose *because* CRM rejects it: CRM then falls back to the line's standard price held in SAP. For lines that belong on the quote but that we are deliberately not bid-pricing — today only Zebra `Cancelled = Y` rows. Never a stand-in for a genuine $0.00.
  - Both are write-time only: the in-memory `LineItem.Cost` / `LineItem.Msrp` keep their parsed values and validation totals are unaffected. Single source of truth — change a constant to retune. Placement differs per writer (see `docs/project_memory.md`).
- **Numeric inputs are never persisted** back to the User row from the parse flow. Only the last-used vendor persists (`ParseService` sets `user.DefaultVendor` on success). The dashboard may prefill all four numeric inputs from administrator-managed `runtime_configs.vendorDefaults` on initial vendor resolution and each later vendor change; absent values stay blank, and manual edits survive parser/template changes within the same vendor. `onCostPct` is rendered, submitted, and forwarded only when the selected parser declares `SupportsOnCost`.
- **`ParseMetric`** is an append-only ledger written transactionally with `ParseJob` on success; retained indefinitely (FK nulled by `ON DELETE SET NULL` when the `ParseJob` is purged at `RETENTION_DAYS`).
- **`FailedParseJob`** records exception failures (catch block) and `ValidationMismatch` entries (success path, best-effort, fresh `AppDbContext` scope). Category has **two deliberate spellings**: camelCase on the wire (`magicByteMismatch`, via the SQL-translatable mapping in `MonitoringEndpoints.BuildUnifiedQuery`) and lowercase with no separator in SQL (`magicbytemismatch`, via the `AppDbContext` value conversion). Changing the wire spelling therefore never touches stored rows.
- **`DellApiSettings`** is a singleton row (`dell_api_settings`, `id = 1`) containing the Dell endpoints and credentials. Its client secret is protected with ASP.NET Core Data Protection, is write-only through the admin API/UI, and must be re-entered after `SESSION_SECRET` rotation or Data Protection key loss. `ApiVersion` (default `4.0`) is sent as Dell's `Accepts-version` request header — **it must stay pinned**: with the header absent Dell serves an unspecified default version, and both Dell parsers depend on post-v1 response fields (`items[].unit*PriceIncludingShipping`, `items[].skus[].serviceTags`) that would silently bind to `0`/null.
- **Admin monitoring is a unified runs view.** `GET /api/monitoring/runs` merges `ParseJob` rows with genuine `FailedParseJob` failures. A validation mismatch exists in **both** tables; the `ParseJob` is the source of truth, so the query **excludes** `FailedParseJob` rows where `Category == ValidationMismatch`, surfacing each mismatch exactly once with its output file. Filters, paging, and download routes: `docs/project_memory.md` §API surface.
- **Magic-byte upload validation**: `%PDF` / `PK\x03\x04`, checked alongside extension in `ParseService`.

## Canonical naming (locked vocabulary)

Display headers (Title Case, UI + chat tables) and `LineItem` properties are decoupled but one-to-one. **Old names (`part_number`, `cost_price`, `term_months`, `quantity`) must not appear in new code or docs.**

`LineItem` is an internal domain model — it is never serialised to the API, so these names are C#
identifiers, not a wire contract. The lower-case tokens (`vpn`, `min_qty`, …) still used throughout
`docs/output_mapping.md` and the per-format specs are shorthand for the same concepts; treat the
`LineItem` column below as authoritative when writing code.

| Concept | Display header | `LineItem` property | Notes |
|---|---|---|---|
| Part number | `Part Number` | `Vpn` | Vendor Part Number |
| Description | `Description` | `Description` | |
| Term in months | `Term` | `Term` | |
| List / catalogue price | `List Price` | `Msrp` | |
| Customer price | `Sale Price` | `Cost` | |
| Quantity | `Quantity` | `Qty` | |
| Serial number (incl. embedded license) | `Serial Number` | `SerialNumber` | col M of ANZ-GENERIC templates |
| Subscription start | `Start Date` | `StartDate` | |
| Subscription end | `End Date` | `EndDate` | |
| Minimum order quantity | `Min Order Qty` | `MinQty` | HP only; null for Nutanix |
| Configurator solution identifier | `Solution ID` | `SolutionId` | Lenovo LBP-E/LBP-I ISG only; set on parents and children |
| Output line sequence | `Item` | `LineSequence` | Format-specific; col A of ANZ-GENERIC templates |
| Output comments | `Comments` | `Comments` | col R of ANZ-GENERIC templates; set by parsers that need it (e.g. Global Bid writes `"{remaining} Remaining"`; HP Bid writes `"Max Qty: {Max Deal Qty}"` on Part Number/Bundle lines); null = blank |

Parsers detect *source* labels (`"Net Unit Price"`, `"List Unit Price"`, …) as extraction anchors; the source cell is captured in `LineItem.Raw`, **keyed by the source document's column heading verbatim** (`raw["Unit Net Price"]`, `raw["Part No."]`), and the cleaned value written to the canonical property.

**UI label vocabulary differs from these names** (presentation-only; API fields and DB columns unchanged): `margin` renders as **`Uplift`**; HP OneConfig `imPercent` renders as **`Discount Off MSRP`**; HP Services split renders as **`SAID`** (Service Agreement ID).

## Code style

- **Use the centralised constants** (`Vendors.*`, `CrmTemplates.*`, `ParserSlugs.*` from `BidParser.Domain.Constants`) — never inline these string literals. Add the constant first for a new vendor/template/slug.
- **Result guidance is runtime configuration.** `runtime_configs.guidanceMessages` maps only concrete registered parser slugs to server-sanitized HTML. `/api/parse-ui-config` is the client source; Auto parses use the resolved concrete `X-Parser-Slug`, never an Auto slug. Bootstrap seeds guidance only when missing and never overwrites administrator edits.
- **Typed response records only** — use the records in `Contracts/` (`ApiError`, `PasswordValidationError`, `ParseErrorResponse`, `OkResponse`, …). No anonymous `new { detail = "…" }` objects. Three error body shapes; the SPA branches on shape (see `docs/project_memory.md`).
- **JSON casing**: the ASP.NET Core Web defaults — **camelCase**. There is deliberately **no global `PropertyNamingPolicy`**: PascalCase DTO properties camelCase themselves, so new DTOs need no `[JsonPropertyName]`. Reach for `[JsonPropertyName]` **only** to satisfy an external protocol, and put it on the integration DTO that owns the mapping (see `DellAuthTokenProvider.TokenResponse`, which pins OAuth's `access_token` / `expires_in`) — never re-introduce an application-wide policy to serve one upstream API. Request bodies, multipart form fields, query parameters and BidParser's own status/error values are all camelCase; **SQL columns stay snake_case** (see `AppDbContext`).
- **Decimal serialisation**: money/rates serialise as fixed-scale strings via per-field `[JsonConverter]` (`fxRate` 4 dp, `margin`/totals 2 dp).
- **ClosedXML**: use `cell.GetFormattedString()`, not the typed `XLCellValue`.
- Primary constructors on services; full `CancellationToken` propagation; structured logging with no secrets.
- Vendor-prefixed slugs/specs (`nutanix_software_only_pdf`, …) — never reintroduce vendorless slugs. Slugs are **deliberately snake_case** and are the one identifier class exempt from the camelCase rule above: they are persisted on `parse_jobs`/`parse_metrics`/`failed_parse_jobs` for every historical row, so renaming one is a data migration, not a style change. `ParserSlugs.*` identifiers are PascalCase and must match their own value (`DellAposJson = "dell_apos_json"`).

## Agent tooling (MCP)

Two MCP servers are configured for every agent in `.mcp.json`: **`roslyn`** (semantic C#/.NET
analysis over `BidParser.sln`) and **`playwright`** (a live browser). Use whichever answers the
question fastest — they replace guesswork, not the build. Neither is mandatory. **Full tool
inventory and workflows: `docs/agent_tooling.md`.**

- **Roslyn is the default for anything semantic in C#** — symbols, references, implementations,
  callers, type hierarchies, and the blast radius of a change. Prefer it over text search whenever
  the question is about *meaning* rather than *characters*; text search misses overrides and
  interface dispatch. Establish the real call set before refactoring anything shared (`IParser`,
  `LineItem`, `TemplateLayout`, `CrmWriter`, `Constants.*`), and prefer solution-aware rewrites
  (`rename_symbol`, `change_signature`) over hand edits.
- **grep/ripgrep and glob stay right** for everything Roslyn does not model: frontend code, config,
  CI workflows, Docker, docs, sample filenames — and string-level questions in C# too (a literal, a
  slug, a log message). Grep to find the entry point, then switch to Roslyn to reason about it.
- **Use Playwright MCP when a change affects frontend behaviour or a user-visible workflow** —
  render, navigate, upload, walk the parse flow end to end, reproduce a defect, confirm a fix. It is
  a one-off manual-testing instrument; delete any `.playwright-mcp/` directory it leaves behind.
- **MCP browser sessions are not tests.** `@playwright/test` is the separate, committed E2E harness.
  Its current four mocked-API checks cover parser-capability-driven On Cost and runtime-configuration
  interactions; they are run locally with `npx playwright test` and have no CI job. Add another
  committed spec only when it buys durable regression protection; not for every frontend change,
  and never brittle ones merely because Playwright is available.
- **Tooling never replaces building and testing.** Run the relevant builds and tests from
  **Commands** after changes. Roslyn diagnostics are a fast pre-check and a browser pass is manual
  evidence — neither substitutes for a green suite or CI.

## Commands

- `dotnet test BidParser.sln` — full suite (968 tests; API tests need a container runtime).
- `dotnet test tests/BidParser.Parsing.Tests/BidParser.Parsing.Tests.csproj` — fast parser/output suite (661 tests; no container runtime).
- `dotnet test tests/BidParser.Api.Tests/BidParser.Api.Tests.csproj` — API integration suite (189 tests; needs a container runtime; takes several minutes).
- `dotnet test tests/BidParser.Desktop.Configuration.Tests/BidParser.Desktop.Configuration.Tests.csproj` — desktop remote-config/update suite (118 tests; no container runtime, runs on Linux).
- `dotnet build src/BidParser.Desktop/BidParser.Desktop.csproj --configuration Release` — cross-platform desktop compile check; launch/native metadata verification still requires Windows.
- `dotnet run --project src/BidParser.Api` — backend dev server (`http://localhost:5000`; needs a reachable SQL Server — `docker compose up -d mssql` starts just the database).
- `cd frontend && npm run dev` — Vite dev server, proxies `/api` to `http://127.0.0.1:5000` (`VITE_API_PROXY_TARGET=…` to point elsewhere).
- `cd frontend && npm run build` — TypeScript + production frontend build. Type errors only surface here, not in `npm run dev`.
- `docker compose build` then `docker compose up -d` — local stack (after `cp .env.example .env`). Build explicitly; see `CONTRIBUTING.md`.

**Test expectations.** Run the suite that covers what you changed, and the frontend build for
frontend changes. The parsing suite is fast and covers every parser and the writer — there is no
excuse for skipping it on a parser change. API tests need a Docker-compatible container runtime
(SQL Server testcontainer): a Docker daemon, or any socket exposed via `DOCKER_HOST` (rootless
Podman works). If no runtime is available, say so rather than reporting the suite as green.

Never edit a golden workbook in `samples/outputs/` to make a failing test pass without first
explaining what changed in the output and why it is correct.

## Environment assumptions

Only .NET 10, Node 22, and a container runtime are project requirements. Roslyn and Playwright MCP
are optional developer conveniences, not build dependencies. Do not introduce a dependency on a
particular machine, host name, LAN address, editor, or AI tooling subscription, and do not document
one as required.

Secrets belong in `.env` (gitignored) — never in committed files, documentation examples, test
fixtures, or log output. `.env.example` carries placeholders only. If you find what looks like a
real credential in the repository, report it rather than quoting its value.

Generated artefacts are not hand-edited: EF migrations (`dotnet ef migrations add`), build output
(`frontend/dist`, `wwwroot/`), and `package-lock.json`.

## Adding a parser format

1. **One parser class** in `src/BidParser.Parsing/<Vendor>/<Slug>/` implementing `IParser` (`Slug`, `DisplayName`, `Vendor`, `AcceptedMime`, `CrmTemplate`, `Parse`; optional `Detect` defaults `0.0`). `AcceptedMime` is the primary/legacy-compatible value; `AcceptedMimes` is the authoritative accepted set and defaults to `[AcceptedMime]`, so override it when one parser accepts multiple containers. Reference `Vendors.*`/`CrmTemplates.*`/`ParserSlugs.*`. Override `AvailableTemplates` only for multi-template parsers (the frontend then auto-renders a dropdown). Override `TermRendersAsComment` (defaults `false`) if the format expresses the term as a `"{n} Months"` comment rather than the numeric Warranty/Duration column. Override `SupportsSolutionIdSplit` (defaults `false`) only when the parser populates `LineItem.SolutionId` on every emitted line, allowing the generic output pipeline to offer one workbook per Solution ID/SAID; override `SolutionSplitLabel` (defaults `"Solution ID"`) to custom-label the split dimension in the UI (e.g. `"SAID"` for HP Services). Override `OutputNameStyle` (defaults `BidScoped`) with `SolutionScoped` when split entries and whole-quote files should omit the revision token. **Writers must never special-case a vendor or slug** — format-specific presentation and delivery traits are declared on the parser and generic code obeys. **If a vendor has multiple formats that accept the same upload MIME, add a `Detect()` signature** (anchor/header check, `try/catch → score`) here *and* to the siblings so the wrong-file-type flow can name the correct type — and a cross-detection test case in `WrongFileTypeDetectionTests` (each format high on its own fixture, `< 0.7` on siblings). Lenovo LBP-E accepts XLS and XLSX while LBP-I accepts PDF; Zebra's formats accept distinct MIMEs. All four carry a `Detect()` signature so those vendors can offer Auto. `Detect()` also powers the **Auto** dropdown entry for vendors listed in `AutoDetectTypes` — a vendor's Auto entry may only advertise CRM templates common to **all** of that vendor's parsers, so check the intersection before adding a vendor to `AutoDetectTypes.All`.
2. **Bid metadata on the returned `QuoteMetadata`** — locate the format's bid number (and revision, if the source carries one) by anchor and pass them through `BidMetadataCleaner.Clean`, or `CleanRevisionless` when the format has no revision. Extraction is **best-effort: never throw for missing bid metadata.** The fields are display-and-search only, so an unreadable header must degrade to null rather than cost the user a workbook. Detail: `docs/project_memory.md` §Bid metadata and recent uploads.
3. **One registry entry** appended to `ParserRegistry.cs`.
4. **Runtime guidance** — add a concrete parser slug to the administrator-managed guidance document only when it needs a result-popup message; do not add hardcoded UI guidance.
5. **One fixture + golden** under `samples/inputs/` and `samples/outputs/`, plus a test case. Add a `sampleFiles` entry + file under `frontend/public/samples/` (`FileTypeSelect.tsx`).
6. **Optionally a spec** `docs/<vendor>_<format>.md`, linked from `docs/project_memory.md`.
7. **New CRM template** → new `CrmTemplateProfile` entry in `src/BidParser.Output/CrmTemplateProfile.cs`.

**Parser-specific error modals**: throw `ParseError("currency", hint, message)` (or another stage name) from the parser; the API returns HTTP 422 with `{ detail: { stage, hint, message } }`. To show a dedicated modal for a specific stage, add a `isCurrencyError`-style helper in `DashboardPage.tsx` and a matching modal component — the existing `CurrencyErrorModal` (AUD validation) is the pattern to follow. **Reserved stages:** `"detect"` is the wrong-file-type signal (recognition failure — see "Wrong file-type handling"); it is reclassified to `"fileType"` by `ParseService` and rendered by `FileTypeErrorModal`. `"dellApi"` is reserved for Dell Quote API configuration, authentication, lookup, rate-limit, timeout, and upstream failures and is rendered by `DellApiErrorModal`. Use `"detect"` only for "this file isn't my format" failures, never for genuine extraction errors.

Do **not** touch API routes, frontend components (dropdowns auto-populate from `/api/parsers`), Docker, validation, auth, or history — the parser surface is the entire change. Preserve that property.

## Documentation maintenance

Documentation is part of the change, not a follow-up. Each file has one job — update the one that
owns the fact rather than restating it elsewhere, and link between them instead of duplicating.

| Change | Update |
|---|---|
| New or altered supplier format | `docs/<vendor>_<format>.md`, the README format table, the spec index and sample→format map in `docs/project_memory.md` |
| Output columns or filenames | `docs/output_mapping.md` |
| Endpoint, response shape, or entity | `docs/project_memory.md`; `docs/architecture.md` only if a responsibility moved |
| Environment variable, volume, or CI trigger | `docs/DEPLOYMENT.md`, `.env.example`, and `CONTRIBUTING.md`'s CI table |
| A failure mode a developer would hit | `docs/troubleshooting.md` |
| Desktop workflow, remote schema, packaging, or release prerequisite | `docs/desktop.md`; update `docs/DEPLOYMENT.md`/`CONTRIBUTING.md` too when CI changes |

Counts, versions, and command output quoted in documentation must be verified, not estimated — run
the command. When you find documentation that contradicts the code, the code is the source of
truth: fix the documentation rather than preserving it, and say so in your summary.

## Working with the user

- Iterate one supplier format at a time. **Confirm extraction accuracy in chat (render a table) before writing scaffolding** for a new format.
- Output field mapping is locked in `docs/output_mapping.md` — read it; don't re-derive cell positions from the template.
- Flag scope drift against the MVP guardrails (single-file upload, result-popup download, no review/edit gate — full list in `docs/project_memory.md`) before building anything outside them.

## Attribution policy (canonical — applies to every agent and every automation)

This is the single source of truth on authorship for **all** coding agents (Claude Code, Codex,
Gemini, GitHub Copilot, …) and for any script or workflow that writes commits, PRs, releases, or
docs. Tool-specific instruction files defer to this section rather than restating it.

- **The only identity that may ever be attributed** — as author, co-author, committer, contributor,
  reviewer, release author, or any similar role — is `regalen <regalen@outlook.com>`.
- **Never attribute any AI agent, bot, model, assistant, person, or organisation** (Claude,
  Anthropic, Codex, OpenAI, Gemini, Google, GitHub Copilot, …) **anywhere**: git author/committer
  metadata, `Co-authored-by:`/`Signed-off-by:` trailers, commit messages, PR titles and
  descriptions, merge messages, release notes, changelogs, tags, generated documentation, code
  comments, issue descriptions, contributor lists, acknowledgements, or any other automation
  output. No "Generated with …", "AI-assisted", or equivalent phrasing — not even because tooling
  produced the content.
- **Never infer or invent a contributor identity** from GitHub accounts, local usernames, machine
  names, existing commit metadata, or tooling defaults. Where one is required, use `regalen` only.
- **Verify git identity before committing.** Never touch global config unless explicitly
  instructed, and never rewrite existing history solely to change attribution unless asked.
  ```bash
  git config user.name    # must print: regalen
  git config user.email   # must print: regalen@outlook.com
  # if missing or wrong, set it for this repository only:
  git config user.name "regalen" && git config user.email "regalen@outlook.com"
  ```
- **PRs and releases describe the change, never the tool used to make it.** GitHub generates
  release notes from squashed PR titles, so a clean PR title is what keeps releases free of tool
  attribution.

## Release versioning

SemVer 2.0.0 tags `vMAJOR.MINOR.PATCH` (`0.y.z` while in internal testing), cut from `main` only. Pushing a `v*` tag builds the Docker image (`<version>` + `latest`) to `ghcr.io`, then creates a GitHub Release. Full validation runs on the PR merge candidate before `main`; do not tag an unvalidated commit. Full policy: `docs/project_memory.md` §Release versioning.

## Branching & pull requests

`CONTRIBUTING.md` holds the full workflow — branch model, branch naming, PR steps, releases, CI triggers, and the local Docker validation runbook. The rules that must never be broken:

- **Never commit or push directly to `main`.** Branch off `main` as kebab-case `<type>/<short-slug>` (`feat/`, `fix/`, `docs/`, `chore/`, `refactor/`, `test/`, `perf/`), one branch per unit of work; squash-merge once CI is green and delete the branch. No review approval required (solo maintainer).
- **Branch names carry no tooling marker.** Exactly two segments, `<type>/<short-slug>` — never an assistant/model/vendor name and never a generated hash suffix, whatever the worktree tooling created. If you start a session on a name like `claude/<slug>-<hash>`, rename it with `git branch -m feat/<short-slug>` **before doing any work**; if it has already been pushed, leave it and say so. Same principle as the Attribution policy below: the tool never appears in the record.
- **CI validates PRs, not every feature-branch push.** Before pushing application changes, run the full local suite and frontend build. Opening or updating a PR to `main` runs the same checks against the synthetic merge commit; pushes to `main` and `v*` tags build the deployable Docker image without repeating the suite.
- **The PR title is the squashed commit subject** — write it as a Conventional Commit changelog line (e.g. `feat: split Lenovo output by Solution ID`). It feeds the generated release notes.
- **Attribution** — commits, PRs, merges, tags, and releases follow the [Attribution policy](#attribution-policy-canonical--applies-to-every-agent-and-every-automation) above: `regalen <regalen@outlook.com>` only, never an AI/bot identity or trailer.
- **Local Docker validation** — when the user asks to "build and let me test", "run it locally", "spin it up", or similar, run the `CONTRIBUTING.md` workflow; `dotnet test` alone is not acceptance validation. Always `docker compose build` explicitly — the `bidparser` service declares both `build:` and `image:`, so a bare `up -d` silently reuses a stale image.
- **Never destroy local state without confirmation.** `docker compose down` is the normal teardown (volumes preserved). `docker compose down -v`, `docker system prune`, `docker volume prune`, and `docker image prune -a` all require explicit user confirmation. Leave no validation artifacts in the repo root — use `.ai/inbox/` if something must be kept.

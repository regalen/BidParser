# AGENTS.md

This file is the canonical project reference for AI coding agents working in this repository.

## Project Status

All five MVP phases are complete and the codebase has been audited against the plan. The current artefacts are:

- `backend/` — parser package plus FastAPI app surface. Contains the parser contract, registry, shared PDF/XLSX helpers, five Nutanix parsers, the Foreign Uplift template writer, SQLite/SQLAlchemy models, Alembic initial migration, storage, parse orchestration, daily retention background task, auth/session/rate-limit dependencies, `/auth/*`, `/me`, admin `/users`, `/parsers`, `/parse`, and history/download endpoints. SPA static file serving is wired for production (serves the built frontend from `/app/static/` when the directory exists). Pytest covers parser extraction, Quote D isolation (negative assertion against Quote C pricing), cell-by-cell workbook output equivalence, auth, user admin, parse API roundtrip, history/downloads, and rate limiting.
- `frontend/` — React/Vite/TypeScript app. Visual design language matches ProductLens (slate-50 page background, white cards with `border-slate-200` + `shadow-sm`, `#0077d4` accent, Inter typography, uppercase tracked labels). Shared utility classes live in `src/styles.css` (`.label`, `.field`, `.button`, `.button-primary`, `.button-danger`, `.icon-button`, `.card`, `.toast`). Contains the API client, auth context, route shell, login + forced password change screens (each rendered on the ProductLens slate-50 background with a centered card, accent-blue icon tile, and the shared `Footer`; the change-password screen has a live rule checklist for length/uppercase/digit/symbol/match), sticky white `AppHeader` with an `AccountChip` (display name + `@username` + ghost-red logout) and admin Settings link, V4 side-panel dashboard, Nutanix parser selection from `/api/parsers`, FX/margin inputs from `/api/me`, single-file dropzone/progress state, auto-download on parse, validation toasts, dynamic recent-uploads pagination with download actions, auth-expiry redirects, and admin user settings as a card grid. Components are extracted into individual files per the plan's repo layout, with a shared `Footer` used across all routes.

- `samples/inputs/` — real supplier quote files. Three PDFs (`XQ-4076249.pdf`, `XQ-4108785.pdf`, `XQ-4128926.pdf`) and two XLSXs (`XQ-4076249.xlsx`, `XQ-4108785.xlsx`). All five are supplier-issued inputs the parser must handle. `XQ-4108785.pdf` and `XQ-4108785.xlsx` are the *same engagement* delivered in two envelopes; both parsers extract from **Quote D** (reseller-facing breakdown) within those files and produce matching totals (`USD 22,491.87`).
- `samples/outputs/` — golden `XQ-*_parsed.xlsx` fixtures, one per quote number (not per input file — PDF and XLSX variants of the same quote share one golden file since they produce identical output). Used by the template-writer regression tests; regenerated whenever an output rule changes. Naming follows the spec: `<basename>_parsed.xlsx` (e.g. `XQ-4076249_parsed.xlsx`).
- `backend/tests/fixtures/` — symlinks to the five sample inputs in `samples/inputs/`.
- `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` — the standardised internal template that parsed line items are written into. Field mapping is locked in `docs/output_mapping.md`; do not interpret cell positions from this file directly.
- `docs/nutanix_software_only_pdf.md` — human-written extraction spec for the "Software Only (PDF)" format (Nutanix subscription quotes).
- `docs/nutanix_software_only_xlsx.md` — human-written extraction spec for the "Software Only (XLSX)" format (same data as Software Only PDF, delivered as a workbook).
- `docs/nutanix_renewal_pdf.md` — human-written extraction spec for the "Renewal (PDF)" format (Nutanix subscription renewals with serial/license numbers).
- `docs/nutanix_hardware_only_pdf.md` — human-written extraction spec for the "Hardware Only (PDF)" format (Nutanix multi-quote PDF; we only parse Quote D, the reseller-facing breakdown).
- `docs/nutanix_hardware_only_xlsx.md` — human-written extraction spec for the "Hardware Only (XLSX)" format (Nutanix multi-quote workbook; we only parse Quote D, the reseller-facing breakdown — same sub-quote as the PDF version).
- `docs/output_mapping.md` — defines how parsed `LineItem` fields are written into the `ANZ-GENERIC_ForeignUplift.xlsx` template, the output filename convention, and the locked output rules (e.g. MSRP column H stays empty; `serial_number` lands in Comments not Serial Number; term written only when `>= 1`). Read this before generating any `*_parsed.xlsx` file.
- `docs/PLAN.md` — the full MVP implementation plan. Read this before writing any code — it covers repo layout, parser interface, API surface, auth, Docker setup, GitHub Actions, and testing strategy.
- `docs/design/` — Claude Design handoff bundle for the V4 side-panel UI. Read `docs/design/README.md` before implementing frontend work.

Implementation notes:

- Parser slugs, package names, and spec docs are vendor-prefixed for future supplier expansion. Use `nutanix_software_only_pdf`, `nutanix_software_only_xlsx`, `nutanix_renewal_pdf`, `nutanix_hardware_only_pdf`, and `nutanix_hardware_only_xlsx`; do not reintroduce vendorless parser slugs.
- The frontend is locally runnable with Vite and proxies `/api` to the backend at `http://127.0.0.1:8000` by default. If port 8000 is occupied, run Vite with `VITE_API_PROXY_TARGET=http://127.0.0.1:<port>` to point at an alternate backend.
- In Docker, `DATABASE_URL` and `UPLOAD_DIR` are set explicitly to `/data/db.sqlite` and `/data/files` (the `/data` volume). The `_root()` fallback in `config.py` is only used in local development.
- `main.py` lifespan starts a daily `_retention_loop()` background task that calls `cleanup_old_parse_jobs()` to delete expired ParseJob rows and their files after `RETENTION_DAYS`.
- `ChangePasswordPage` relies on the `App.tsx` route guard (every other route redirects to `/change-password` when `must_change_password=True`) to keep the user on the page until they pick a new password — no `useBlocker` is used (it requires a data router and breaks under the declarative `BrowserRouter`).
- The vendor-specific settings block (Nutanix `EXCHANGE RATE` + `MARGIN` inputs and the emerald `CRM IMPORT TEMPLATE` callout) renders in the dashboard only when **both** a vendor and a file type are selected. Until then, only the two cascading selects appear above the Upload & parse button.
- `User.name` is a nullable display name surfaced in the UI (`AccountChip` headline, `SettingsPage` user cards) and on future reporting. Admin-issued creates require it; the bootstrap admin starts with `name="Administrator"`. Existing rows were backfilled to `name = username` by the `0002_user_name` migration.
- Golden fixture files in `samples/outputs/` are named `<basename>_parsed.xlsx` (one per quote number, not per input file). PDF and XLSX parsers for the same quote both compare against the same golden file.
- Hardware parser tests include a negative assertion checking that NX-1175S-G10-6517P-CM has Quote D's cost (USD 20,017.57), not Quote C's (USD 5,903.72).

Current implementation checkpoint:

- Phase 1 is complete.
- Phase 2 is complete.
- Phase 3 is complete.
- Phase 4 is complete.
- Phase 5 is complete.
- Post-phase audit completed: retention task wired, golden fixture naming fixed, negative assertion tests for Quote D isolation added, frontend components extracted into separate files per plan.
- Post-MVP UI iteration (2026-05-15): frontend retheme to ProductLens design language (slate palette, shared `Footer`, new login/change-password chrome with live rule checklist, sticky white header, settings-as-card-grid), conditional rendering of the vendor-specific settings block, dropped `useBlocker`, and added `User.name` (model column + alembic migration `0002_user_name` + schemas + admin CRUD + UI surfaces).
- Verification commands:
  - `cd backend && .venv/bin/python -m pytest -q`
  - `cd frontend && npm run build`
- Last known result: backend `17 passed`; frontend production build succeeded.
- All MVP phases are complete. GitHub Actions CI/CD publishing is deferred until explicitly requested.

Sample → format mapping:

| Sample file        | Format                 |
|--------------------|------------------------|
| `XQ-4076249.pdf`   | Software Only (PDF)    |
| `XQ-4076249.xlsx`  | Software Only (XLSX)   |
| `XQ-4108785.pdf`   | Hardware Only (PDF)    |
| `XQ-4108785.xlsx`  | Hardware Only (XLSX)   |
| `XQ-4128926.pdf`   | Renewal (PDF)          |

## What is being built

An internal web app for sales operations. Users upload a supplier quote (PDF or XLSX in various supplier-specific layouts), the app extracts and validates the line items, and the user reviews them before a (future) export step writes the standardised XLSX. Stack: Python/FastAPI backend + React/Vite/TypeScript frontend, deployed as Docker via `docker-compose` on an internal server.

Work is **iterative, one format at a time**. Formats spec'd so far: "Software Only (PDF)", "Software Only (XLSX)", "Renewal (PDF)", "Hardware Only (PDF)", and "Hardware Only (XLSX)". The architecture is built around a pluggable `BaseParser` registry so adding the next format means dropping in one parser module, one set of fixtures, and one registry entry — nothing else.

**Anchor-based extraction is mandatory.** Every parser must locate sections, headers, totals, and column positions by searching for anchor strings in the source document. Never hard-code row numbers, column letters, or fixed offsets. The same workbook can contain multiple quote sections where row positions shift across samples (more line items above push everything down) and column letters differ between sections (Quote C uses column H for `Product Code`; Quote D in the same file uses column E). Hard-coded positions will break across real-world quotes.

## Architecture (planned)

Single contract that every parser returns and the frontend consumes:

```
ParseResult
├── metadata        QuoteMetadata (quote_number, supplier, currency, quoted_total, source_filename, parser_slug)
├── line_items      list[LineItem]
└── validation     ValidationResult (computed_total, quoted_total, matches, difference, warnings)

LineItem — superset of fields across formats. Only `vpn`, `cost`, `qty` are
required; the rest are Optional and populated per format:
  Required: vpn, cost, qty
  Software Only (PDF + XLSX):  description, term, msrp
  Renewal:                     msrp, serial_number, start_date, end_date
  Hardware Only (PDF + XLSX):  description, term, msrp    # term is per-row nullable
  Common debug:                raw (dict[str, str] of original column text)

Across formats: List Price / List Unit Price / MSRP / Term Adjusted List Unit Price
→ `msrp`; Sale Price / Net Unit Price / Cost Price → `cost`. Software Only PDF and
XLSX produce the same shape; Hardware Only PDF and XLSX likewise produce the same
shape (both extract from Quote D and yield 11 rows totalling `$22,491.87`).
```

Dates are stored internally as ISO `YYYY-MM-DD` (Python `date`); display formatting (`DD/MM/YYYY` per the Renewal spec) is a frontend concern, not a model concern.

### Canonical naming (display headers and field names)

User-locked vocabulary. Display headers (Title Case, used in the UI table and any chat-rendered tables) and internal field names (snake_case, used by the Pydantic model, JSON API, and tests) are decoupled but one-to-one:

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

Parsers still detect *source* labels in the document (`"Net Unit Price"`, `"List Unit Price"`, `"Sale Price"`, `"MSRP"`, etc.) when locating columns — those are extraction-time anchors, not internal field names. The source label is captured in `raw[source_label]` for debugging; the cleaned value is written to the canonical field. **Old names (`part_number`, `cost_price`, `term_months`, `quantity`) must not appear in new code or docs.**

Key extension point: `backend/app/parsers/registry.py` holds an explicit `PARSER_REGISTRY: list[type[BaseParser]]`. Auto-discovery is deliberately avoided — registration order is visible. Each parser lives in its own subpackage (`backend/app/parsers/<slug>/`).

Format detection is a **soft hint, not a routing decision**: `BaseParser.detect()` returns a 0.0–1.0 confidence and the UI pre-fills the dropdown when confidence > 0.7, but the user always confirms before parsing runs. Silent mis-routing is worse than one extra click.

Validation logic is identical across formats: `computed_total = Σ(cost × qty)` compared to `quoted_total` with `Decimal("0.01")` tolerance. The UI gates the (stub) Approve button on a match unless the user explicitly overrides.

## Common PDF parsing approach

All PDF formats use `pdfplumber` (pure-Python, word-level bounding boxes, no Poppler needed in the container). The shared technique:

- Open the PDF and collect words from all pages with their page index preserved.
- Find the header row by locating its first-cell anchor word(s) (e.g. `"Product"` + `"Code"` for Software Only, `"No"` for Renewal).
- Derive column x-ranges from the header word x0s — each column's range is `[its x0, next header's x0)`; rightmost extends to page width.
- Collect body words below the header y across pages until a `"TOTAL:"` token.
- Cluster words into rows by `top` (tolerance ~3pt). Bucket each row's words into columns by `x0`; join intra-bucket words with single spaces.
- Locate the quoted total by scanning after the last body row for `TOTAL:` + `USD` + amount, tolerating page-break wrap.

Do **not** rely on `pdfplumber.extract_tables()` — these PDFs have no ruling lines.

Shared helpers live in `backend/app/parsers/pdf_utils.py`: word collection, header anchor detection, column-range derivation, row clustering, currency/number parsing.

## Common XLSX parsing approach

XLSX formats use `openpyxl` with `data_only=True` so formula cells return their cached values (not the formula strings). The shared technique:

- Open the workbook, pick the relevant sheet (usually the only one, or the one named like the quote number).
- Locate the **header row** by scanning the sheet for a cell whose value matches a known anchor label (e.g. `Product Code`). Do not assume a fixed row number — quote metadata above the table varies.
- Capture the header row's column letters and map each known label (`Product Code`, `Product Description`, …) to its column.
- Iterate data rows below the header. Stop at the first wholly-empty row, or at a recognisable footer (e.g. a cell containing `TOTAL $...`).
- Locate the quote total by scanning for a cell whose value starts with `TOTAL ` followed by a currency string, or by finding a row labelled `TOTAL` and reading the adjacent value cell.
- Currency strings use `$` and thousands separators (e.g. `$2,275.00`), not `USD ` — the cleaner strips `$`, `,`, and whitespace before parsing as `Decimal`.

Shared helpers can live in `backend/app/parsers/xlsx_utils.py`: sheet selection, header-row search, header-label-to-column mapping, currency parsing.

## Software Only (PDF) — extraction algorithm

Header anchor: `"Product"` immediately followed (same y-band ±3pt, increasing x) by `"Code"`. Columns: `Product Code`, `Product`, `Term (Months)`, `List Unit Price`, `Net Unit Price`, `Quantity`.

Row classification:
- `Product Code` cell trims to `"Term-Months"` OR `Product` cell is `"Term in months"` → **skip** (sub-header that repeats per page).
- `Product Code` matches `^[A-Z0-9-]+$` after trim → **anchor row** (new `LineItem`).
- `Product Code` empty AND `Product` non-empty → **continuation row** (append `Product` text to current anchor's description).
- Otherwise → ignore.

Per-field handling: flatten description by joining continuation snippets with single spaces and collapsing internal whitespace; strip `USD`/commas from prices → `Decimal`; `term` and `qty` → `int`.

Edge cases the tests must cover:
- 3–5 line description wrap per item.
- `Term-Months` sub-header repeating on each page.
- `TOTAL:` wrapping onto a later page.
- Thousands separators in prices (`2,275.00`).

Expected output for `XQ-4076249.pdf` (already validated by hand — use these as the golden test values):

| Part Number      | Term | List Price | Sale Price | Quantity |
|------------------|------|------------|------------|----------|
| SW-NCM-STR-PR    | 60   | 383        | 101.11     | 2096     |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 864      |
| SW-NCI-PRO-PR    | 60   | 2275       | 600.60     | 1232     |
| SW-NCI-E-PRO-PR  | 60   | 3455       | 912.12     | 145      |
| SW-NCM-E-STR-PR  | 60   | 583        | 153.91     | 145      |

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
- `Product Code` cell trims to `Term-Months` → **skip** (filler row identical in purpose to the PDF sub-header; appears after every real line item).
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
- `Term-Months` filler rows present and correctly skipped.
- Currency strings prefixed with `$` (not `USD `), with thousands separators (`$2,275.00`).
- `Total Discount (%)` sits between `List Price` and `Sale Price` in the sample, so the label-to-column map must use header text — never assume adjacency or fixed column letters.

Expected output for `XQ-4076249.xlsx` (already validated by hand — golden values match the PDF version):

| Part Number      | Term | List Price | Sale Price | Quantity |
|------------------|------|------------|------------|----------|
| SW-NCM-STR-PR    | 60   | 383.00     | 101.11     | 2096     |
| SW-NCI-PRO-PR    | 60   | 2275.00    | 600.60     | 864      |
| SW-NCI-PRO-PR    | 60   | 2275.00    | 600.60     | 1232     |
| SW-NCI-E-PRO-PR  | 60   | 3455.00    | 912.12     | 145      |
| SW-NCM-E-STR-PR  | 60   | 583.00     | 153.91     | 145      |

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
- **Start Date / End Date** (`start_date`, `end_date`): source is `MM/DD/YYYY`; parse with `datetime.strptime("%m/%d/%Y")` and store as a `date`. The frontend formats as `DD/MM/YYYY` for display.
- **List Price** (`msrp`): strip `USD` and commas from the source `Term Adjusted List Unit Price` column → `Decimal`.
- **Sale Price** (`cost`): strip `USD` and commas from the source `Net Unit Price` column → `Decimal`.
- **Quantity** (`qty`): source `Qty` column → `int`.

The Renewal layout has no Term-Months sub-header to skip and no description wrap. The `Total Net Price` column wraps onto the next line because the column is narrow, but we don't extract that field — the validation comes from the `TOTAL:` line.

Edge cases the tests must cover:
- `Serial Number` cell wrapping across two lines (the comma is at the end of the first line, so the no-separator join is correct).
- `Net Unit Price` and `Qty` running together visually with no space (e.g. `USD 54.41 160`) — column x-ranges disambiguate.
- `TOTAL: USD ...` itself wraps onto two lines (`TOTAL:` and `USD` on one line, the amount on the next).
- The serial cell could in principle contain only a serial with no embedded license/comma — the no-separator join still produces a valid single-string value; no special handling needed.

Expected output for `XQ-4128926.pdf` (already validated by hand — golden values):

| Part Number     | Serial Number               | Start Date  | End Date    | List Price | Sale Price | Quantity |
|-----------------|-----------------------------|-------------|-------------|------------|------------|----------|
| RSW-NCM-STR-PR  | 24SW000351227,LIC-02472987  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |
| RSW-NCI-ULT-PR  | 24SW000351236,LIC-02472996  | 2026-07-13  | 2027-07-12  | 575        | 371.83     | 32       |
| RSW-NCI-ULT-PR  | 24SW000351221,LIC-02472983  | 2026-07-13  | 2027-07-12  | 575        | 429.11     | 72       |
| RSW-NCM-STR-PR  | 24SW000351228,LIC-02472985  | 2026-07-13  | 2027-07-12  | 77         | 54.41      | 160      |

Computed total = quoted total = `USD 60,205.68`.

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

**Every classified line item is kept** — no filler rows are skipped. In particular the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) is a real line item here. This is the *opposite* rule to Software Only PDF, where the analogous `Term-Months` row is filler and skipped.

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
- `Support-Term` is **not** a sub-header — keep it. Do not apply the Software-Only `Term-Months` skip rule.
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

Row classification: every non-empty row between the header row and the `TOTAL ` row is a line item. **No filler rows are skipped in this format** — in particular the `Support-Term` row (Product Code `Support-Term`, Description `Support Term in Months`) *is* a kept line item here. Compare with Software Only where the analogous `Term-Months` row is skipped; the difference is per-format and the user has confirmed it.

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

## Working with the user

- The user wants to iterate one supplier format at a time. Confirm extraction accuracy in chat (render a table) **before** writing scaffolding code for a new format.
- Output field mapping for `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` is locked in `docs/output_mapping.md`. Don't re-derive cell positions from the template directly — read the spec.
- `XQ-4108785.pdf` and `XQ-4108785.xlsx` are both multi-quote files (sections Quote A, B, C, D). Both parsers extract **Quote D only** (`nutanix_hardware_only_pdf.md` and `nutanix_hardware_only_xlsx.md`) — the reseller-facing breakdown. Quote C inside these files is a separate budgetary breakdown of pure components and is **not** parsed by either format. Quotes A and B are noise.

## Commands

- `cd backend && .venv/bin/python -m pytest -q` — backend parser/template/API suite.
- `cd backend && .venv/bin/uvicorn app.main:app --reload` — local backend API dev server.
- `cd frontend && npm run dev` — Vite frontend dev server, proxying `/api` to `http://127.0.0.1:8000`.
- `cd frontend && npm run build` — TypeScript + production frontend build.
- `docker compose up -d` — run the production container (after `cp .env.example .env` and setting `SESSION_SECRET`).
- `docker compose build` — local image build from the repo root `Dockerfile`.

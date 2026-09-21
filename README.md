# BidParser

BidParser turns a supplier quote (PDF, XLSX, XLSM, XLS, or a Dell Quote API response) into a
standardised CRM upload workbook — `*_NoCalculation.xlsx`, `*_ForeignUplift.xlsx`, and so on —
ready for import into the downstream quoting/CRM system. It ships as an authenticated web app and
as a portable Windows desktop host for local, offline parsing.

## Public releases and desktop privacy

The clean public release line begins at `v1.0.0`. A validated `v*` tag on `main` publishes the
matching Docker image to GHCR and a GitHub Release containing the portable `BidParser.exe` and its
SHA-256 checksum. Ordinary pushes to `main` publish neither artifact.

Desktop parsing keeps the selected source file on the local machine: it is parsed in place, held
only in memory while the app runs, and written only to the destination the user chooses. The app
optionally reads public guidance/default JSON and release metadata; it never uploads the source
document, quote contents, credentials, or a usage history.

**Why it exists.** Suppliers each publish quotes in their own layout, and every one of them has to
be retyped into the same CRM import template by hand. BidParser does that transcription
mechanically, and checks its own arithmetic against the quoted total before handing the file back.

The architecture is a pluggable `IParser` registry: each supplier format is one parser class plus
fixtures plus one registry entry, and the app surfaces it automatically in the upload dropdown.
Extraction is **anchor-based** — parsers locate sections, headers, totals, and columns by searching
for anchor strings, never by hard-coded row/column positions.

- **Backend** — ASP.NET Core 10 Minimal API, SQL Server via EF Core, shipped as a single Docker
  image behind a reverse proxy for TLS.
- **Frontend** — React 19 / Vite / TypeScript SPA, served from the API's `wwwroot/` in production.
- **Desktop** — .NET 10 WPF application for Windows x64, published as one self-contained
  `BidParser.exe` with no installer, database, login, or separately installed runtime.
- **Shared application layer** — host-neutral selection, format validation, auto-detection, output
  validation, naming, and workbook/ZIP generation used by both web and desktop.
- **Parser core** — PdfPig (PDF), ClosedXML (XLSX and XLSM), ExcelDataReader (legacy OLE `.xls` and
  OpenXML `.xlsx`), HtmlAgilityPack (HTML-disguised `.xls`), `System.Text.Json` (Dell).

## Documentation map

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | System architecture, request/data flow, component responsibilities, where to start on a change |
| [docs/desktop.md](docs/desktop.md) | Portable desktop behavior, remote configuration, packaging, release prerequisites, and Windows acceptance |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Operator runbook: environment variables, volumes, reverse proxy, upgrades |
| [docs/troubleshooting.md](docs/troubleshooting.md) | Diagnosing common build, container, database, auth, and parse failures |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branching, pull requests, releases, local Docker validation |
| [docs/output_mapping.md](docs/output_mapping.md) | How parsed fields map into the CRM workbook columns |
| [docs/project_memory.md](docs/project_memory.md) | Deep reference: per-format extraction algorithms, full API surface, auth detail |
| [AGENTS.md](AGENTS.md) | Working agreements and invariants for AI coding agents |
| `docs/<vendor>_<format>.md` | Per-format extraction specs (one per supported quote format) |

---

## Supported vendors & quote formats

Each format is a registered parser with a stable **slug**. The order below matches the registry
([`ParserRegistry.cs`](src/BidParser.Parsing/Registry/ParserRegistry.cs)), which is also the
dropdown order. The **Output template** column is the CRM template the parser writes to; where a
parser exposes more than one, the user picks it at parse time. Existing seeded result guidance uses
the report-type wording shown below; administrators can update it at runtime. A dash means no
guidance is seeded for that format.

| Vendor | Quote format | Accepted input | Slug | Output template | Report type |
|---|---|---|---|---|---|
| **Nutanix** | Software Only (PDF) | PDF | `nutanix_software_only_pdf` | Foreign Uplift | Standard |
| **Nutanix** | Software Only (XLSX) | XLSX | `nutanix_software_only_xlsx` | Foreign Uplift | Standard |
| **Nutanix** | Renewal (PDF) | PDF | `nutanix_renewal_pdf` | Foreign Uplift | Start End Date |
| **Nutanix** | Renewal (XLSX) | XLSX | `nutanix_renewal_xlsx` | Foreign Uplift | Start End Date |
| **Nutanix** | Hardware Only (PDF) | PDF | `nutanix_hardware_only_pdf` | Foreign Uplift | Standard |
| **Nutanix** | Hardware Only (XLSX) | XLSX | `nutanix_hardware_only_xlsx` | Foreign Uplift | Standard |
| **HP** | HP Bid (XLSX) | XLSX | `hp_bid_xlsx` | No Calculation / Uplift | Hardware SOH |
| **HP** | Global Bid (XLSX) | XLSX | `hp_global_bid_xlsx` | No Calculation / Uplift | Hardware SOH |
| **HP** | OneConfig (XLSX) | XLSX | `hp_oneconfig_xlsx` | % Off RRP with Uplift | Standard |
| **HP** | Services (XLSX) | XLSX | `hp_services_xlsx` | No Calculation / Uplift | Start End Date |
| **HPE** | HPE Bid (XLSX) | XLSX | `hpe_bid_xlsx` | No Calculation / Uplift | Hardware SOH |
| **Lenovo ISG** | LBP-E ISG Quote (XLSX) | XLS (OLE) **and** XLSX | `lenovo_lbpe_isg_xls` | No Calculation / Uplift | Standard |
| **Lenovo ISG** | LBP-I ISG Quote (PDF) | PDF | `lenovo_lbpi_isg_pdf` | No Calculation / Uplift | Standard |
| **Lenovo IDG** | LBP-I IDG Quote (PDF) | PDF | `lenovo_lbpi_idg_pdf` | No Calculation / Uplift | Standard |
| **Zebra** | PCR (PDF) | PDF | `zebra_pcr_pdf` | No Calculation / Uplift | Hardware SOH |
| **Zebra** | PCR (XLS) | XLS (HTML-disguised) | `zebra_pcr_xls` | No Calculation / Uplift | Hardware SOH |
| **Dell** | CTO (Quote API) | JSON (fetched) | `dell_cto_json` | No Calculation | Hardware SOH |
| **Dell** | APOS (Quote API) | JSON (fetched) | `dell_apos_json` | No Calculation | Start End Date |
| **Cisco** | CCW Quote (XLS) | XLS (OLE) | `cisco_ccw_quote_xls` | No Calculation | Hardware SOH Disc % |
| **Datalogic** | Quote (PDF) | PDF | `datalogic_quote_pdf` | No Calculation / Uplift | Standard |
| **Epson** | Quote (PDF) | PDF | `epson_quote_pdf` | No Calculation / Uplift | Standard |
| **Strike** | Quote (PDF) | PDF | `strike_quote_pdf` | No Calculation / Uplift | Standard |
| **Trellix** | Quote (PDF) | PDF | `trellix_quote_pdf` | No Calculation / Uplift | — |
| **Trellix** | Quote (XLSM) | XLSM | `trellix_quote_xlsm` | No Calculation / Uplift | — |

Notes on the table:

- **Trellix result guidance** can be configured through administrator settings; neither parser has
  seeded report-type message.

- **Lenovo is two vendors in the dropdown.** Lenovo's Infrastructure Solutions Group (ISG, servers
  and storage, organised around Solution IDs) and Intelligent Devices Group (IDG, laptops and
  monitors, no Solution IDs) publish structurally unrelated bid templates, so they are separate
  vendor groupings. Every Lenovo output workbook still writes `LENOVO` as the vendor name, because
  the CRM knows only one Lenovo.
- **Dell has no file upload.** The dashboard takes a Dell quote ID; the backend fetches the JSON
  from Dell's Quote API and decides CTO versus APOS itself. Dell is the one vendor where automatic
  detection is the only option, enforced both in the UI and at the API layer. Dell is therefore
  web-only in desktop v1; the portable app does not expose manual JSON upload.
- **Auto (detect format)** is an extra dropdown entry for Nutanix, Lenovo ISG, Zebra, and Dell. It
  is preselected for those vendors, and for all but Dell the user can still choose a specific
  format.
- **Multi-template formats** (No Calculation / Uplift) render a template dropdown. The only
  difference between the two is whether the Margin (Uplift) column is written; the layout is
  otherwise identical.
- **Lenovo ISG Solution ID split.** Both Lenovo ISG formats can optionally return one renumbered
  workbook per Solution ID, delivered as a single ZIP. The ZIP is what history and monitoring
  retain.

---

## Output templates

Every parse produces a clean copy of one of the standardised `ANZ-GENERIC_*.xlsx` templates in
[`samples/template/`](samples/template/), named per the convention in
[docs/output_mapping.md](docs/output_mapping.md). All of them share a 27-column A→AA layout; they
differ only in which columns carry pricing.

Eight template workbooks are checked in, but **only four are wired up** — a CRM template is
selectable only when it has a matching profile in
[`CrmTemplateProfile.cs`](src/BidParser.Output/CrmTemplateProfile.cs). The remaining four workbooks
are reference copies of CRM's other upload layouts, kept for when a future format needs one.

| Template | Sheet | Used by |
|---|---|---|
| **Foreign Uplift**<br>`ANZ-GENERIC_ForeignUplift.xlsx` | `Foreign Uplift` | All six Nutanix formats |
| **No Calculation**<br>`ANZ-GENERIC_NoCalculation.xlsx` | `No Calculation` | HP Bid, HP Global Bid, HP Services, HPE Bid, all three Lenovo formats, both Zebra formats, both Dell formats, Cisco CCW, Datalogic, Epson, Strike, Trellix |
| **Uplift**<br>`ANZ-GENERIC_Uplift.xlsx` | `Uplift` | HP Bid, HP Global Bid, HP Services, HPE Bid, all three Lenovo formats, both Zebra formats, Datalogic, Epson, Strike, and Trellix; additionally writes the Margin column. |
| **% Off RRP with Uplift**<br>`ANZ-GENERIC_PercentOffWithUplift.xlsx` | `% Off RRP with Uplift` | HP OneConfig only |

A single writer, [`CrmWriter`](src/BidParser.Output/CrmWriter.cs), produces all four. It never
special-cases a vendor or slug: the per-template column behaviour lives in `CrmTemplateProfile`, and
per-format presentation traits are declared as properties on the parser.

**How they differ:**

- **Foreign Uplift** — the only template with foreign-currency columns (Foreign Currency / Cost /
  MSRP / FX Rate). MSRP lands in column U, cost in column T. Used exclusively by Nutanix, whose
  quotes are USD-denominated.
- **No Calculation / Uplift** — local-currency (AUD) layout. Cost lands in column I, MSRP in column
  H where the source provides one. `Uplift` additionally writes the Margin column (K); `No
  Calculation` leaves it blank. Same physical layout, selected per parse.
- **% Off RRP with Uplift** — pricing lands on the MSRP column (H); Cost (I) is intentionally
  blank. Both Margin (K) and IM% (X) are required.

Every template ends with an end-loop sentinel row: column B = `*` (the loop marker the downstream
import requires) and a "DO NOT DELETE THIS LINE" warning in column D.

Because the downstream importer rejects a literal `0` as a price, zero is written two different
ways on purpose: a genuinely free line gets the sentinel `0.0001` (rounded back to `0.00` on
import), while a line we deliberately do not bid-price gets a literal `0` so the importer falls
back to its standard price from SAP. See "Zero pricing: the two zeros" in
[docs/output_mapping.md](docs/output_mapping.md) — getting this backwards silently changes what the
customer is quoted.

---

## Getting started

**Prerequisites**

- .NET 10 SDK
- Node 22
- A Docker-compatible container runtime — needed for the API integration tests (which start a SQL
  Server testcontainer) and to run the production image. Rootless Podman works; point `DOCKER_HOST`
  at its socket.

**Run the two dev servers**

```bash
dotnet run --project src/BidParser.Api
```

```bash
cd frontend && npm install && npm run dev
```

The backend listens on `http://localhost:5000`. The Vite dev server proxies `/api` to
`http://127.0.0.1:5000`; override with `VITE_API_PROXY_TARGET` if port 5000 is taken.

Running the API on its own still needs a reachable SQL Server — set `DB_CONNECTION_STRING`, or
start just the database from the compose file with `docker compose up -d mssql`.

**Run the tests**

```bash
dotnet test tests/BidParser.Parsing.Tests/BidParser.Parsing.Tests.csproj
```

The parser/application/output suite is the fast one (697 tests) and needs no container runtime.
The full suite, `dotnet test BidParser.sln`, additionally runs the 190 API integration tests
against a SQL Server testcontainer and 118 cross-platform desktop configuration/update tests
(1005 tests total).

**Build the desktop host**

Linux can compile the Windows target for validation:

```bash
dotnet build src/BidParser.Desktop/BidParser.Desktop.csproj --configuration Release
```

The release executable is produced on a Windows runner from the checked-in `win-x64` publish
profile. Download it from
[`regalen/BidParser/releases/latest`](https://github.com/regalen/BidParser/releases/latest).
Desktop v1 supports every registered non-Dell format; Dell remains a web-only Quote API workflow.
See [docs/desktop.md](docs/desktop.md) for the complete contract and release runbook.

**Run the whole stack in containers**

```bash
cp .env.example .env
```

Then edit `.env` — at minimum replace `SESSION_SECRET` (`openssl rand -hex 32`) and
`MSSQL_SA_PASSWORD` — and build explicitly before starting:

```bash
docker compose build && docker compose up -d
```

The app is served on `http://localhost:3447`. On a fresh database it creates the schema and seeds
an admin user from `ADMIN_USERNAME` / `ADMIN_PASSWORD` (`admin` / `changeme` by default), which
must change its password on first login.

Build explicitly rather than relying on `docker compose up -d` alone: the `bidparser` service
declares both a `build:` context and a published `image:` name, so a bare `up` will happily reuse a
stale or pulled image.

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full operator runbook and
[docs/troubleshooting.md](docs/troubleshooting.md) when something does not come up.

---

## Adding a new format

A new supplier format is a self-contained change — one parser class, one registry line, one fixture
plus golden output, and optionally a report-type mapping. API routes, frontend dropdowns, Docker,
auth, and validation are untouched, because the dropdowns populate from `/api/parsers` and the
writer is driven by parser-declared traits rather than vendor checks. Preserving that property is
the point of the design.

The full checklist is in [AGENTS.md](AGENTS.md#adding-a-parser-format); the surrounding architecture
is described in [docs/architecture.md](docs/architecture.md).

# Architecture

How BidParser is put together, how a parse flows through it, and where to start when you need to
change something. For per-format extraction algorithms and the exhaustive endpoint contracts, see
[project_memory.md](project_memory.md); for the CRM column mapping see
[output_mapping.md](output_mapping.md).

## The shape of the system

BidParser has two hosts over one parse/output core. The web host is one ASP.NET Core deployment
serving the API and React SPA with SQL Server and retained files. The portable Windows host runs
the same parser and workbook services locally, without Infrastructure, a database, authentication,
retained quote data, or a local HTTP server.

```
Browser (React SPA)                       Windows desktop (WPF)
      │ same-origin cookie session                    │ local file
      ▼                                               ▼
ASP.NET Core 10 API ── Infrastructure       Application orchestration
      │                 │ EF/files                    │
      └────────┬────────┘                              │
               └──────────────┬───────────────────────┘
                              ▼
                  Application + parser registry
                              │
                  Parsing + Domain + Output
                              │
                    CRM workbook / split ZIP
```

In production the SPA is not separately hosted: the Docker build compiles it to static assets and
copies them into the API's `wwwroot/`, and the API serves them with `UseStaticFiles()` plus
`MapFallbackToFile("index.html")` for client-side routing. Frontend and backend therefore share an
origin, which is why cookie authentication and a simple CSRF header are sufficient.

## Projects and responsibilities

`BidParser.sln` contains seven source projects and three test projects. Dependencies point inward
toward `Domain`, which references nothing.

| Project | Responsibility |
|---|---|
| `src/BidParser.Domain` | The contracts and vocabulary. `IParser`, `IParserRegistry`, `LineItem`, `QuoteMetadata`, `ParseResult`, `ValidationResult`, `ParseError`, shared `FormatDetection`, and the `Constants/` that centralise vendor names, CRM template names, parser slugs, runtime-config keys, and auto-detect entries. No I/O, no dependencies. |
| `src/BidParser.Application` | Host-neutral orchestration shared by web and desktop: registry projection, extension/MIME and magic-byte inspection, concrete/Auto resolution, wrong-format suggestions, template capability validation, canonical naming, and staged XLSX/ZIP generation. Depends on Domain and Output, with no EF Core or ASP.NET dependency. |
| `src/BidParser.Parsing` | Every parser, plus the container-format helpers they share: `Pdf/` (PdfPig word geometry), `Xlsx/` (ClosedXML reading and header mapping), `Cleaning/` (value normalisation). Holds `Registry/ParserRegistry.cs`, the explicit list of registered parsers. |
| `src/BidParser.Output` | Turning `LineItem`s into a CRM workbook. `CrmWriter` is the single writer; `CrmTemplateProfile` describes what each template populates; `TemplateLayout` owns the 27-column layout and the two zero-price constants; `OutputNaming` and `SolutionOutputSplitter` handle filenames and the Solution ID split. |
| `src/BidParser.Infrastructure` | Persistence and orchestration. `Persistence/AppDbContext`, `Entities/`, EF Core `Migrations/`, `Storage/FileStorage`, `Dell/` (Quote API client, token provider, encrypted settings), and `Services/` — `ParseService`, `FailedParseJobRecorder`, `RetentionService`, `RuntimeConfigurationService`. |
| `src/BidParser.Api` | HTTP surface. `Program.cs` wires everything; `Endpoints/` holds one file per route group; `Contracts/` the typed response records; `Auth/`, `Middleware/`, `Serialization/`, `Hosting/` (startup hosted services). |
| `src/BidParser.Desktop` | Portable WPF host. Composes Application with the concrete registry, local dialogs, MVVM workflow, theme/accessibility resources, embedded fallback configuration, remote refresh, and update notification. Does not reference Infrastructure or API. |
| `tests/BidParser.Parsing.Tests` | Parser and writer tests against committed fixtures and golden workbooks. No container runtime required. |
| `tests/BidParser.Api.Tests` | Integration tests over `WebApplicationFactory` against a real SQL Server testcontainer. |
| `tests/BidParser.Desktop.Configuration.Tests` | Cross-platform schema, safe-markup, transport, fallback, and SemVer/update tests linked to the desktop configuration code. No Windows runtime or container required. |

`frontend/` is the SPA source, built by Vite and consumed only as static output by the API.

## The parser contract

Everything vendor-specific lives behind one interface, `IParser`
([`src/BidParser.Domain/Abstractions/IParser.cs`](../src/BidParser.Domain/Abstractions/IParser.cs)).
A parser declares its identity (`Slug`, `DisplayName`, `Vendor`), what it accepts (`AcceptedMime`
and the authoritative `AcceptedMimes`), what it writes (`CrmTemplate`, `AvailableTemplates`),
optional UI/output capabilities such as `SupportsOnCost` and `SupportsSolutionIdSplit`, and
returns a `ParseResult { Metadata, LineItems, Validation }`.

Two design rules matter more than the rest:

**Anchor-based extraction is mandatory.** Parsers locate sections, headers, totals, and columns by
searching for anchor strings — never by hard-coded row numbers or column letters. This is not
stylistic. A single Nutanix workbook contains several stacked quote sections in which the same
logical column sits at a different letter (`Product Code` is column H in one section, column E in
the next), and header blocks vary in height between quotes from the same vendor. Fixed positions
work on the fixture and fail on the next real quote.

**Writers never special-case a vendor or slug.** Where a format needs different presentation, the
parser declares a trait and generic code obeys it. `TermRendersAsComment` decides whether the term
lands in the numeric Warranty column or as a `"{n} Months"` comment; `SupportsSolutionIdSplit`
decides whether the split-output option is offered; `OutputVendorName` decides what goes in the
workbook's Vendor Name column, which is how both Lenovo vendor groupings still write `LENOVO`. Add
a trait rather than a conditional.

### Parser selection

Selection is explicit, not inferred. The user picks a vendor and then a file type, both populated
from `GET /api/parsers`, and that choice routes the parse.

`Detect()` returns a 0.0–1.0 confidence score and is deliberately **never** used to silently
reroute a manual selection. It has exactly two consumers:

- **Auto (detect format)** — a synthesised dropdown entry for vendors listed in `AutoDetectTypes`
  (Nutanix, Lenovo ISG, Zebra, Dell). When the user parses with an Auto slug, `FormatDetection`
  scores the vendor's parsers whose accepted MIME types match the upload and picks the best above a
  0.7 threshold. The resolved concrete slug is what gets persisted and returned in the
  `X-Parser-Slug` response header. A vendor's Auto entry may only advertise CRM templates common to
  all of that vendor's parsers.
- **Wrong file-type suggestion** — when a manually selected parser fails at recognition, sibling
  parsers are scored to name the type the user probably meant.

Parsers without a `Detect()` override score `0.0` and are therefore never auto-detected or
suggested — which is fine for single-format vendors, and is why adding a second format to a vendor
means adding `Detect()` to *both* formats, not just the new one.

### Recognition failure is not a parse failure

This distinction drives a lot of behaviour and is easy to break. A parser that cannot find its
table anchor or a required column throws `ParseError(stage: "detect")`. `ParseService` treats that
as *the user picked the wrong file type*: it deletes the upload, writes nothing at all — no
`ParseJob`, no `ParseMetric`, no `FailedParseJob` — and rethrows with `stage: "fileType"` so the
SPA can show the file-type modal with a suggestion.

Any failure *after* the table has been located (`"currency"`, `"extract"`, magic-byte `"upload"`)
is a genuine failure and is recorded for admin review. Use `"detect"` only for "this file is not my
format".

## How a parse flows

1. **Upload.** The SPA posts multipart to `POST /api/parse` with the file, the chosen vendor and
   parser slug, the CRM template, and whatever numeric inputs the template requires (FX rate,
   margin, IM%, on-cost %). For Dell there is no file — the SPA sends a quote ID.
2. **Dell fetch (Dell only).** `ParseEndpoints` validates the quote ID, then `DellQuoteClient`
   fetches the JSON from Dell's Quote API using stored credentials, and that response becomes the
   stored source. Failures here return 422 with stage `dellApi` before anything is persisted.
3. **Store and validate the container.** `ParseService` saves the upload via `FileStorage` and
   checks magic bytes (`%PDF`, `PK\x03\x04`, or a leading `{`/`[` for JSON) alongside the
   extension, so a renamed file is rejected rather than mis-parsed.
4. **Resolve the parser.** A concrete slug is used directly; an Auto slug goes through
   `FormatDetection`.
5. **Parse.** The parser returns `ParseResult`. Validation is uniform across formats:
   `computed_total = Σ(cost × qty)` compared to the quoted total with a `0.01` tolerance. Formats
   whose source carries no quoted total (HP, HPE, Zebra) construct a passing `ValidationResult`
   directly rather than validating against null.
6. **Write the workbook.** `ParseService` builds `CrmWriterOptions` — vendor name, margin, FX rate,
   currency, IM%, on-cost, term placement — and calls `CrmWriter`, which copies the template and
   fills it per the `CrmTemplateProfile`. When a Solution ID split is requested,
   `SolutionOutputSplitter` produces one renumbered workbook per Solution ID and the retained output
   is a single ZIP.
7. **Persist.** On success a `ParseJob` row and a `ParseMetric` row are written in one transaction.
   A validation mismatch additionally records a `FailedParseJob` of category `ValidationMismatch`
   on a best-effort basis.
8. **Respond.** The workbook is returned along with headers the SPA reads — `X-Parser-Slug`,
   `X-Validation`, `X-Currency`, `X-Computed-Total`, `X-Quoted-Total`, and situational ones like
   `X-Split-Count` and `X-Cancelled-Lines`. The SPA always shows a result modal with an explicit
   Download button; there is no auto-download and no review/edit screen.

### How a desktop parse flows

1. **Select locally.** The WPF view model projects the shared parser catalog, excludes Dell, and
   derives template/numeric/split controls from parser and writer capabilities.
2. **Inspect the source.** The desktop view model enforces the shared 10 MiB local-host ceiling;
   `QuoteParseService` validates the extension, MIME expectation, and magic bytes without copying
   the file.
3. **Resolve and parse.** Concrete selections stay explicit; Auto uses the same registry detection
   threshold. The parser runs off the UI thread and returns the same `ParseResult` used by web.
4. **Review.** The parsed quote remains in memory while the persistent result region shows success,
   mismatch/parser warnings, or sanitized failure detail. No history or metric is written.
5. **Save explicitly.** `WorkbookWriteService` validates options and uses the shared writer,
   naming, and Solution ID/SAID splitter. It stages in the chosen directory and atomically moves a
   completed XLSX or ZIP into place after conflict handling.

The web `ParseService` still owns upload retention, Dell acquisition, EF persistence, monitoring,
metrics, and user-default mutation; those concerns do not move into Application or Desktop. Full
desktop behavior and its remote trust boundary are in [desktop.md](desktop.md).

## Persistence

EF Core against SQL Server, registered with `AddDbContextPool`. Schema changes are EF migrations in
`src/BidParser.Infrastructure/Migrations/`, applied automatically at startup by
`MigratorHostedService` — there is no manual migration step in deployment.

| Entity | Purpose |
|---|---|
| `User` | Credentials (bcrypt, cost 12), role, display name, and the remembered last-used vendor. |
| `ParseJob` | One successful parse: source and output paths, vendor, parser slug, CRM template, bid metadata, and whether the output is a Solution ID ZIP. |
| `ParseMetric` | Append-only utilisation ledger, written transactionally with `ParseJob`. Retained indefinitely; its foreign keys are nulled rather than cascaded when a job or user is deleted, so history survives. |
| `FailedParseJob` | Failures worth admin review, plus validation mismatches. |
| `DellApiSettings` | Singleton row holding Dell Quote API endpoints and credentials. The client secret is encrypted with ASP.NET Core Data Protection and is write-only through the API. |

`ImportType` (`Auto` / `Manual`) is recorded on `ParseJob`, `ParseMetric`, and `FailedParseJob` so
reporting can distinguish auto-detected parses from explicitly chosen ones.

Two conventions are load-bearing:

- **Timestamps are stamped centrally** by `AppDbContext.StampTimestamps()`. Do not add
  `= DateTime.UtcNow` initialisers to entities; you will get two different notions of "now".
- **Deleting rows must also delete files.** The database cascade does not reach disk. `DeleteUserAsync`
  snapshots the affected file paths before committing and deletes them after, or the files are
  orphaned in the upload directory forever.

`RetentionBackgroundService` runs once at startup and then every 24 hours, deleting `ParseJob` and
`FailedParseJob` rows older than `RETENTION_DAYS` along with their files. It runs cleanup first
rather than sleeping first, because a container that restarts more often than daily would otherwise
never reach the cleanup.

## Authentication and security

Session cookies backed by ASP.NET Core Data Protection, issued by a custom
`SessionCookieAuthHandler`. Sessions have a hard expiry from login with no sliding refresh.

The mechanism worth understanding: the session payload embeds a fingerprint of the user's password
hash. Authentication rejects any token whose fingerprint no longer matches the stored hash, so a
password change or an admin reset revokes every existing session for that user without any
server-side session store. The acting session is re-issued during a password change so it survives
its own change.

Authorisation is applied per endpoint via three policies rather than by path globbing — a path glob
would let a user who must change their password still reach `/me/settings`:

- `LoggedIn` — valid session; ignores the must-change-password flag. Used by logout, change
  password, and `GET /me`.
- `ActiveUser` — logged in and not pending a password change. Everything else non-admin.
- `Admin` — `ActiveUser` plus the admin role.

Also in place: bcrypt verification against a dummy hash for unknown usernames so login timing does
not reveal which accounts exist; a `X-Requested-With: BidParser` header required on every non-GET
request as CSRF protection (sufficient given SameSite=Lax and a same-origin SPA); rate limiting on
auth endpoints per IP and per submitted username; a security-headers middleware including a
content security policy; and a locked-down forwarded-headers configuration that requires an
explicit trusted proxy list in production.

There is no self-service password recovery. An admin resets a password, which issues a one-time
random temporary password returned once in the API response.

## Frontend

React 19 with Vite and TypeScript, React Router for routing, Tailwind for styling, recharts for the
admin metrics charts, and lucide-react for icons.

The frontend holds no hardcoded parser list. Vendor and file-type dropdowns, the CRM template
selector, the On Cost input, and the Solution ID split option are driven by `GET /api/parsers`.
Administrator-managed numeric defaults and sanitized result guidance come from
`GET /api/parse-ui-config`, keyed by exact
vendor names and resolved concrete parser slugs respectively. This keeps parser discovery
registry-driven while allowing operational wording/default changes without a redeploy.

Requests go through a single API client that sets the CSRF header and handles auth expiry
redirects. Error handling branches on the *shape* of the error body, of which there are exactly
three: a plain `{detail: string}`, a `{detail: string[]}` used only by password validation, and a
`{detail: {stage, hint, message}}` used only by parse failures. New endpoints must reuse the
matching record from `Contracts/` rather than returning an anonymous object, or the SPA will not
know how to render the error.

## Configuration

Infrastructure configuration is read from environment variables at startup; there is no UI for
secrets, storage, retention, or network settings. `docker-compose.yml` assembles the database
connection string from the SQL Server password and database name, and passes the rest through from
`.env`. The full table of variables, defaults, and meanings is in [DEPLOYMENT.md](DEPLOYMENT.md).

Operational parse UI configuration is separate: `runtime_configs` stores administrator-managed
vendor numeric defaults and sanitized result guidance. The Runtime Configuration admin page edits
the two documents independently, and API reads are uncached so saved changes apply without restart.
If a stored document no longer passes current validation, the Admin API returns its raw JSON plus
the validation error so that page remains the recovery path; the parse-facing API still degrades to
empty maps.

Desktop configuration is a separate distribution/trust model, not a client for `runtime_configs`.
Schema-v1 guidance and vendor defaults are embedded for offline startup, then independently replaced
in memory by validated public documents when available. They are never cached or persisted and can
control wording/prefills only. The update check is a third independent request. See
[desktop.md](desktop.md#remote-configuration-and-trust-boundary).

Two are easy to get wrong:

- **`SESSION_SECRET` is not a signing key.** It is the Data Protection application discriminator.
  The actual key material is the keyring in `/data/dp-keys`, which must persist across restarts.
  Rotating the secret logs everyone out and makes the stored Dell client secret undecryptable until
  an admin re-enters it.
- **`TZ` changes reporting.** The admin metrics time series buckets by local calendar day, so the
  container timezone determines which day a parse lands on.

## Where to start

| If you are changing… | Start at |
|---|---|
| Adding a supplier format | The checklist in [AGENTS.md](../AGENTS.md#adding-a-parser-format), then an existing parser in the same container format as a template |
| How a field lands in the workbook | [output_mapping.md](output_mapping.md), then `CrmTemplateProfile` — not `CrmWriter`, which should stay format-agnostic |
| Adding a CRM template | `CrmTemplates` constants, then a `CrmTemplateProfile` entry; the writer should need no change |
| An API route or response shape | `src/BidParser.Api/Endpoints/`, and the matching record in `Contracts/` |
| The database schema | An entity under `Entities/`, then `dotnet ef migrations add` — the schema is migration-driven, never hand-edited |
| Auth or session behaviour | `src/BidParser.Api/Auth/`, especially the session token service |
| Dropdowns or the parse form | `frontend/src/components/`; check whether the data should come from `/api/parsers` instead of being hardcoded |
| Desktop workflow, configuration, or packaging | [desktop.md](desktop.md), then `src/BidParser.Desktop`; shared parse/write behavior belongs in Application |
| Deployment, volumes, or env vars | [DEPLOYMENT.md](DEPLOYMENT.md) and `docker-compose.yml` |

Files you should not hand-edit: anything under `Migrations/` (generate them with `dotnet ef`), the
golden workbooks in `samples/outputs/` (regenerate them deliberately and review the diff — they are
the parser test oracle), and `package-lock.json`. Build output — `frontend/dist` and the API's
`wwwroot/` — is generated and is not committed to the repository.

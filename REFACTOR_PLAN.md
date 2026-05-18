# Refactor to React + .NET — Plan

## Context

Company standard is **React (or Angular) on the frontend and .NET on the backend**. The current app:

- **Frontend** — React 19 + Vite + TypeScript. **Already aligned**; reused unchanged.
- **Backend** — Python 3.12 + FastAPI + SQLAlchemy + Alembic + pdfplumber + openpyxl + SQLite. **Out of policy**; needs to be re-platformed onto ASP.NET Core.

Goal: re-platform the backend onto **ASP.NET Core 10 (LTS) + EF Core + SQLite + UglyToad.PdfPig + ClosedXML + BCrypt.Net-Next**, preserving the parser contract, the 18 passing tests' behaviours, the five golden `*_parsed.xlsx` fixtures, the public API contract the SPA depends on, and the single-Docker-image deployment shape (`3447:3447` behind NPM, `/data` volume).

**Pre-production**: the app has never been deployed with real users. There is no production database, no historical `parse_jobs` rows that matter, and no users whose passwords must be preserved. The local `data/db.sqlite` is disposable. **This refactor is a fresh install — no data migration, no schema fingerprinting, no bcrypt-compatibility verification, no cookie-format migration story.** The cutover is: delete the old container's volume (or just the old `db.sqlite`), start the .NET container, the `BootstrapAdminHostedService` seeds the admin from env vars, done.

User decisions:

- Frontend stays React 19 (no Angular rewrite).
- DB stays SQLite.
- PDF: **UglyToad.PdfPig** (MIT, exposes word/letter bounding boxes — pdfplumber's direct analogue).
- Migration: **full rewrite in .NET, parser-by-parser PRs.**
- .NET target: **.NET 10 LTS** (released Nov 2025, supported until Nov 2028). Avoids inheriting an immediate 8→10 upgrade after the port lands.
- **No data migration**: cutover wipes `/data/db.sqlite` and starts fresh.

## Target .NET solution layout

```
/BidParser.sln
/src/
  BidParser.Api/                       Program.cs (UseStaticFiles + MapFallbackToFile("index.html")), appsettings.*, endpoints, auth, hosting, options
    Endpoints/{Auth,Me,Users,Parse,Parsers,History}Endpoints.cs
    Auth/{SessionCookieAuthHandler,RateLimiter,PasswordPolicy,RequireCsrfHeader,BootstrapAdminHostedService,UnifiedErrorHandler}.cs
    Hosting/{RetentionBackgroundService,MigratorHostedService}.cs
    Options/AppOptions.cs
  BidParser.Domain/                    LineItem, QuoteMetadata, ValidationResult, ParseResult, ParseError, IParser, IParserRegistry
  BidParser.Infrastructure/            AppDbContext, entities (User, ParseJob), FileStorage, ParseService, RetentionService, EF migrations
  BidParser.Parsing/                   Cleaning, Pdf (PdfWordCollector w/ PdfPig + PdfPigWordSplitter), Xlsx (ClosedXML wrappers), Nutanix/* (5 parsers), Registry
  BidParser.Output/                    ForeignUpliftWriter (ClosedXML), OutputNaming
/tests/
  BidParser.Parsing.Tests/             xUnit + FluentAssertions; one file per parser + WorkbookComparer + characterisation test
  BidParser.Api.Tests/                 WebApplicationFactory integration: auth flow, users CRUD, parse roundtrip, history, rate limit
/frontend/                              unchanged
/samples/                               unchanged (golden fixtures stay the test oracle)
/docs/                                  unchanged until PR 13
/Dockerfile                             rewritten in PR 1
```

Dev: SQLite + uploads at `./data/`; SPA proxied to .NET host at `:5000`.
Docker: `/data/db.sqlite` + `/data/files/originals|outputs` + `/data/dp-keys` (Data Protection keyring); SPA copied into `/app/wwwroot/`.

## API parity matrix (the React SPA must keep working — JSON field names, decimal scales, status codes, and named response headers must be contract-equivalent)

| FastAPI route | .NET endpoint (file/handler) | Auth policy | Notes |
|---|---|---|---|
| `POST /api/auth/login` | `AuthEndpoints.Login` | Anonymous | `{username,password}` → `{user}` + Set-Cookie. Rate-limited per IP and per username (5/min). |
| `POST /api/auth/logout` | `AuthEndpoints.Logout` | LoggedIn | Clears the current cookie only. |
| `POST /api/auth/change-password` | `AuthEndpoints.ChangePassword` | LoggedIn | Enforces ≥8 / upper / digit / symbol; clears `must_change_password`. |
| `GET /api/me` | `MeEndpoints.GetMe` | LoggedIn | Allowed even when `must_change_password=true`. |
| `PATCH /api/me/settings` | `MeEndpoints.UpdateSettings` | **ActiveUser** | Blocked with `403 password_change_required` when locked. |
| `GET /api/parsers` | `ParsersEndpoints.List` | ActiveUser | Driven by `ParserRegistry`. |
| `POST /api/parse` | `ParseEndpoints.Parse` | ActiveUser | Multipart; returns file stream + `X-Validation`, `X-Computed-Total`, `X-Quoted-Total` (**empty value** when null, not omitted — SPA reads via `headers.get`), `Content-Disposition: attachment; filename="<stem>_parsed.xlsx"`. On success, **also writes** `user.default_vendor = vendor; user.fx_rate = rounded; user.margin = rounded` to the User row before commit (drives "remember last-used settings" pre-fill). See full error matrix below. |
| `GET /api/history?limit&offset&q` | `HistoryEndpoints.List` | ActiveUser | User-scoped; `q` case-insensitive substring on `source_filename` (whitespace-only `q` treated as no filter); `when` is a server-computed relative string — exact format (port verbatim): `"just now"` (< 60s), `"5m ago"` (60s–1h), `"3h ago"` (1h–24h), `"Yesterday"` (24–48h), `"3 days ago"` (2–6 days), `DD/MM/YYYY` (≥ 7 days). |
| `GET /api/history/{id}/source\|output` | `HistoryEndpoints.Download*` | ActiveUser | 404 if foreign user or expired. |
| `GET\|POST\|PATCH\|DELETE /api/users[/{id}]` | `UsersEndpoints` | Admin | Admin-only; **case-insensitive** username uniqueness on create + update (`func.lower(...)` equivalent). Create/reset write literal password `"changeme"` with `must_change_password=true`. Last-admin guard has two messages: `409 {detail:"Cannot remove the last admin."}` and `409 {detail:"Admins cannot delete themselves."}`. **Self-delete check fires before the user-id lookup** (so `DELETE /users/{ownId}` with a non-existent id still returns 409, not 404). |

**Auth policies** (per-endpoint, not path-globbed — mirrors the Python `Depends(current_user)` vs `Depends(require_active_user)` vs `Depends(require_admin)` split exactly):

- `Anonymous` — no auth required.
- `LoggedIn` — valid session cookie; `must_change_password` is **ignored**. Used for `/auth/logout`, `/auth/change-password`, `GET /me`.
- `ActiveUser` — valid session cookie **and** `must_change_password=false`. Used everywhere else under `/api` except admin routes.
- `Admin` — `ActiveUser` + `role == "admin"`.

Implementation: register four `AuthorizationPolicy` builders in `Program.cs` (`AddAuthorization(o => o.AddPolicy("ActiveUser", p => p.RequireAuthenticatedUser().RequireAssertion(ctx => !ctx.User.HasClaim("must_change_password", "true"))))`, etc.) and decorate each endpoint with `.RequireAuthorization("ActiveUser")` / `"Admin"`. A path-based filter is explicitly avoided — it would let locked users update `PATCH /me/settings`.

**`POST /api/parse` error matrix** (port `parse_service.py:61–117` verbatim; covered by `BidParser.Api.Tests/ParseErrorMatrixTests.cs`):

| Condition | Status | Body |
|---|---|---|
| `parser_slug` not in registry | 400 | `{detail: "Unknown parser."}` |
| `parser_slug` exists but `parser.vendor != vendor` | 400 | `{detail: "Parser does not match vendor."}` |
| File extension valid (`.pdf`/`.xlsx`) but doesn't match parser's `accepted_mime` | 400 | `{detail: "File extension does not match selected parser."}` |
| Upload size > `MAX_UPLOAD_MB` | 413 | `{detail: "File is too large."}` |
| Extension not in `{.pdf, .xlsx}` | 415 | `{detail: "Only PDF and XLSX files are supported."}` |
| `ParseError` raised by parser | 422 | `{detail: {stage, hint, message}}` |
| Any other exception during parse/write | 422 | `{detail: {stage: "parse", hint: "Could not parse this file.", message: str(exc)}}` |

In all error cases the source file is deleted from disk and **no ParseJob row is recorded** — the .NET orchestrator must match this (delete `source_path` and `output_path` before raising). The frontend renders errors inline in the dropzone (form preserved); none of these gate-block subsequent uploads.

**Serialisation**:

- `JsonNamingPolicy.SnakeCaseLower` + `JsonStringEnumConverter` (snake_case).
- **Decimal scale preservation** — Pydantic emits `Decimal("0.7400").__str__() == "0.7400"`, `Decimal("7.50") == "7.50"`, `Decimal("1625358.51") == "1625358.51"`. .NET `decimal` carries scale in the same way (96-bit value + scale byte), so the simplest correct approach is **(a) make sure values arrive at the JSON layer with the right scale**, then **(b) format with a per-field converter that pads if needed**:
  - `fx_rate` — quantize to 4 dp on every write path (EF Core column `HasPrecision(12,4)` + explicit `decimal.Round(v, 4, MidpointRounding.AwayFromZero)` on form-binding); converter `JsonStringDecimalConverter(scale: 4)` formats `ToString("F4", InvariantCulture)`.
  - `margin` — scale 2 (`F2`).
  - `computed_total`, `quoted_total`, `X-Computed-Total`, `X-Quoted-Total` headers — scale 2 (`F2`).
  - Apply via `[JsonConverter(typeof(...))]` attributes on the DTO properties (not a global converter — the same `decimal` type has different scales depending on field).
- Default `ProblemDetails` formatter **off** — `builder.Services.AddProblemDetails()` is not called. Two layers of normalisation, in this order, with `FrameworkErrorNormalisationTests` as the **source of truth** (if the tests pass, the mechanism is fine — don't fight the framework over which API catches what):
  1. **`IExceptionHandler` (`UnifiedErrorHandler`)** catches anything that escapes endpoint code as a thrown exception (`BadHttpRequestException`, `JsonException`, manually-thrown `HttpResponseException` analogues) and rewrites the response to `{detail: <string>}`.
  2. **`NormalizeErrorBodyMiddleware`** runs late in the pipeline as a fallback for framework-generated 4xx responses that are *written directly* without raising (Minimal API binding can emit 400/415 bodies before any handler sees them). Uses `Response.OnStarting` to inspect any `4xx` with `Content-Type: application/problem+json` and rewrite the body to `{detail: <title|default>}`. Cheap; only activates when the first layer missed something.
  3. If both layers leave a specific edge case unfixable, the contract test stays red and the fix is to add the case to the middleware — never to relax the test.

- **`detail` shape — array vs string vs object:**
  - `{detail: "string"}` — most endpoints (login 401, vendor mismatch 400, "Unknown vendor." 400, 404s, rate limit 429, last-admin guards 409).
  - `{detail: ["msg1", "msg2", ...]}` — **only** `POST /auth/change-password` when password rules fail. Each string is one rule violation (`"Password must be at least 8 characters."`, etc.). The SPA at `ChangePasswordPage.tsx:38–39` joins them with a space if it sees an array. `UnifiedErrorHandler` and the change-password endpoint must preserve the array — do **not** stringify it into a single `detail`.
  - `{detail: {stage, hint, message}}` — **only** `POST /parse` 422 (parser failure). Three string fields.

- **Upload size limits — set at three layers, all to `MAX_UPLOAD_MB`** (otherwise a 10 MB upload trips Kestrel's framework body before the endpoint or `UnifiedErrorHandler` ever runs):
  - `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = appOptions.MaxUploadBytes)`.
  - `services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = appOptions.MaxUploadBytes; o.MultipartHeadersLengthLimit = 32 * 1024; })`.
  - `[RequestSizeLimit(...)]` (or `.WithMetadata(new RequestSizeLimitAttribute(...))`) on the `/parse` endpoint specifically — defence in depth.
  - The endpoint also streams the upload through `FileStorage.SaveUploadAsync` which enforces the same limit and matches the existing Python `save_upload` (1 MB chunks, raises on overflow, deletes the partial file).

- **`/api/parse` endpoint style — default Minimal API, controller acceptable fallback**: every other endpoint is a Minimal API for consistency. `/api/parse` is the most framework-sensitive endpoint (multipart binding + streamed file write + per-form-field decimals + custom response headers + precise error body shapes). Implement as Minimal API first; if `[FromForm]` binding for `IFormFile + string + decimal + decimal + string` fights us on error message shape, fall back to an MVC controller (`[ApiController] public class ParseController : ControllerBase`). **Contract equivalence outranks endpoint-style consistency** — if the controller is what passes the tests cleanly, use the controller.

## Auth / session port

- **Cookie** (`bidparser_session`): payload `{user_id, issued_at}` protected via ASP.NET Core **Data Protection**: `services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/data/dp-keys")).SetApplicationName(appOptions.SessionSecret)`. 12-hour hard expiry, no sliding refresh, HttpOnly, SameSite=Lax, `Secure` only when `Request.IsHttps` after `ForwardedHeadersMiddleware`.
- **What `SESSION_SECRET` actually does in .NET (important — different from the Python role):** in the Python app, `SESSION_SECRET` is the HMAC signing key — the cryptographic material that authenticates the cookie. In the .NET port, the cryptographic material is the **Data Protection keyring** at `/data/dp-keys` (AES-GCM keys auto-generated on first start, rotated by the framework). `SetApplicationName(SESSION_SECRET)` is a **scope discriminator**, not a signing key — two apps with the same keyring but different application names can't read each other's cookies. Operator consequences:
  - **Rotating `SESSION_SECRET` does not, by itself, re-encrypt or invalidate cookies in the same way the Python rotation did.** It scopes them: any cookie issued under the old `SESSION_SECRET` becomes unreadable, so in practice rotating it *does* log everyone out — but the mechanism is "application name no longer matches", not "signing key changed".
  - **Deleting `/data/dp-keys` is what invalidates the keyring.** Operators who need a hard session reset should delete that directory (or wipe `/data` entirely).
  - **`/data/dp-keys` must persist across container restarts** — otherwise the keyring regenerates and every restart logs everyone out.
  - Operator runbook in `docs/DEPLOYMENT.md` (updated in PR 13) must spell this out so on-call doesn't assume the Python semantics still apply.
- **Custom `SessionCookieAuthHandler : AuthenticationHandler<...>`** registered at scheme `"SessionCookie"`. Builds `ClaimsPrincipal` with `NameIdentifier=user_id`, `Role`, and a `must_change_password` claim (string `"true"`/`"false"`).
- **CSRF**: `IEndpointFilter RequireCsrfHeader` rejects any non-GET without `X-Requested-With: BidParser` (frontend already sends this; **even `POST /api/auth/login` requires it** — confirmed in `routes_auth.py:19`).
- **`must_change_password` 403 gate**: implemented as the `ActiveUser` authorization policy (per-endpoint), **not** a path-globbed middleware. Locked users keep access to `POST /api/auth/logout`, `POST /api/auth/change-password`, and `GET /api/me`; every other endpoint returns `403 {detail:"password_change_required"}`. This matches the Python `Depends(require_active_user)` set exactly — verified against `routes_me.py` (`GET /me` uses `current_user`, `PATCH /me/settings` uses `require_active_user`).
- **Username matching is case-insensitive** for login lookup and create/update uniqueness. **Implemented via SQLite `COLLATE NOCASE` on the `username` column** + a unique index on the same column:
  - EF Fluent config: `entity.Property(u => u.Username).HasColumnType("TEXT COLLATE NOCASE"); entity.HasIndex(u => u.Username).IsUnique();`
  - SQLite's `NOCASE` collation makes both the unique constraint AND every `WHERE username = '...'` predicate case-insensitive at the storage layer.
  - Login lookup becomes a plain `db.Users.FirstOrDefaultAsync(u => u.Username == providedUsername)` — no `ToLower()` calls scattered through the code.
  - **Closes the application-check race** an `if (db.Users.Any(...)) throw 409` pattern would have between the check and the INSERT — the unique index now rejects duplicates at the DB even under concurrent admin user-create calls.
  - Username is stored **verbatim** (case preserved as the admin typed it); only comparison/uniqueness is case-insensitive. Rate-limit bucket keys still lowercase explicitly (`username:{key.ToLowerInvariant()}`) to match the Python literal-string bucket.
  - If we ever move off SQLite (not planned), this collation needs an explicit port (PostgreSQL: `citext` extension or `LOWER(username)` unique index; SQL Server: `COLLATE Latin1_General_CI_AI` or similar).
- **Last-admin guard**: re-implemented in `UsersEndpoints.Update/Delete`. Two distinct `409 Conflict` messages (`"Cannot remove the last admin."`, `"Admins cannot delete themselves."`). Self-delete check fires **before** the user-lookup query.
- **Rate limiter**: in-memory leaky bucket (singleton `RateLimiter` with `ConcurrentDictionary` keyed `ip:...` and `username:...`). **Do not** use `Microsoft.AspNetCore.RateLimiting` — the existing bucket semantics, `{detail: "Too many attempts. Please try again later."}` error body, and computed `Retry-After` header must be contract-compatible with the Python implementation. Same two-bucket rule on `/auth/login` (IP + username, **username key is lowercased** before bucketing — Python does `username_key = payload.username.strip().lower()`), one-bucket on `/auth/change-password` (IP). `Retry-After` value = `max(1, 60 - (now - oldest_entry_in_bucket))` seconds. Fallback IP key when no `X-Forwarded-For` and no `Connection.RemoteIpAddress` is the literal `"unknown"` (matches Python).
- **Password hashing**: BCrypt.Net-Next at cost 12. Fresh install — no legacy hashes to read.

## EF Core data model + migrations

Entities mirror current SQLAlchemy models (snake_case table/column names via Fluent config; fresh install — no compatibility constraint with the alembic-managed schema, but we keep names aligned so the docs/spec stays meaningful):

- `User { Id, Username (unique, case-insensitive comparisons), Name? (1-255), PasswordHash, Role ("admin"|"user"), MustChangePassword, DefaultVendor? (1-64), FxRate? (12,4), Margin? (12,2), CreatedAt, UpdatedAt, ParseJobs }` — `UpdatedAt` is **internal only**, not exposed on `UserPublic`.
- `ParseJob { Id, UserId, Vendor, ParserSlug, SourceFilename (display name, path-stripped), SourcePath, OutputPath, FxRate (12,4), Margin (12,2), ComputedTotal (14,2), QuotedTotal? (14,2), TotalsMatch, CreatedAt, User }`.
- **Relationship**: `User.HasMany(u => u.ParseJobs).WithOne(j => j.User).HasForeignKey(j => j.UserId).OnDelete(DeleteBehavior.Cascade)`. Matches SQLAlchemy's `cascade="all, delete-orphan"` — deleting a User deletes their ParseJob rows (but **not** the files on disk; the retention loop catches those eventually, per the existing Python behaviour).

**Migration strategy — fresh install only** (no alembic→EF compatibility logic; the existing `db.sqlite` is wiped at cutover, per the pre-production note above):

1. Generate one `00000000000001_InitialCreate` EF migration matching the desired final schema (functionally equivalent to alembic head `0004_remove_fx_rate_peg`).
2. `MigratorHostedService` on startup simply calls `Database.MigrateAsync()`. On a fresh DB this creates everything; on a DB that already has `__EFMigrationsHistory` it no-ops or applies subsequent migrations. **No alembic detection, no history seeding, no fingerprint checks.**
3. `BootstrapAdminHostedService` reads `ADMIN_USERNAME` / `ADMIN_PASSWORD` and seeds the admin row when zero users exist. Hardcoded literals: `name = "Administrator"`, `role = "admin"`, `must_change_password = true`. Re-runs on every startup but no-ops if any user row already exists (matches `bootstrap_admin` in `main.py:69-83`).

**Migration test** (PR 2):

- Fresh empty DB → `MigrateAsync` creates schema + admin row appears + admin can log in with seeded credentials and is forced into the password-change flow.

No "production DB copy" test; no "DB at older alembic version" test. The plan does not contemplate either scenario.

## Parser port plan

**`IParser` interface** mirrors `BaseParser`: `Slug`, `DisplayName`, `Vendor`, `AcceptedMime`, `CrmTemplate`, `ParseResult Parse(string)`, `double Detect(string) => 0`. **`Detect` is a future-compat shim only** — neither the current `/api/parsers` endpoint nor the SPA calls it, and no test asserts its return value. Default implementation returns `0.0`; concrete parsers may override but are not required to. If a `/api/detect` endpoint is added later, it will need its own test plan. **`ParserRegistry.Parsers`** is an explicit ordered `IReadOnlyList<IParser>` — same five entries, same order as `PARSER_REGISTRY` — **no MEF, no assembly scanning** (the AGENTS.md guidance explicitly rejects auto-discovery).

**Cleaning helpers** (1-1 port of `cleaning.py`): `TextCleaner.Clean / JoinSpaced / JoinUnspaced`, `DecimalCleaner.Parse / ParseInt / ParseOptionalInt`, `DateCleaner.ParseMmDdYyyy`. All `InvariantCulture`.

**PDF (pdfplumber → PdfPig)**:

| pdfplumber | PdfPig |
|---|---|
| `pdfplumber.open(path)` | `PdfDocument.Open(path)` |
| `page.extract_words(x_tolerance=1, y_tolerance=3)` | `page.GetWords(NearestNeighbourWordExtractor.Instance)` — **singleton, not `new`** (extractors expose `.Instance`). Then run `PdfPigWordSplitter` to re-split any tokens PdfPig glued together (uses per-letter `Word.Letters[i].GlyphRectangle`) |
| `word["x0"/"x1"/"top"/"bottom"]` | `Word.BoundingBox.Left/Right`; **flip Y** — PdfPig is bottom-left origin: `Top = page.Height - bbox.Top`; encapsulate once in the `PdfWord` record so the rest of the algorithm is unchanged |
| `page.width` | `page.Width` |

Header anchor detection, column-range derivation, row clustering (`y_tolerance≈3.5pt`), `TOTAL:` termination, multi-page word ordering — **straight LINQ port of the Python algorithms, no library calls**.

**XLSX (openpyxl → ClosedXML)**:

| openpyxl | ClosedXML |
|---|---|
| `load_workbook(path, data_only=True)` | `new XLWorkbook(path)` (cached formula values by default) |
| `wb.active` | `wb.Worksheets.First()` |
| `cell.value`, `cell.row`, `cell.column` | `cell.Value` (XLCellValue struct — use `cell.GetFormattedString()` to match Python's stringy comparisons), `cell.Address.RowNumber/ColumnNumber` |
| `ws.max_row` / `ws.max_column` | `ws.LastRowUsed().RowNumber()` / `ws.LastColumnUsed().ColumnNumber()` |
| `ws.iter_rows(min_row=h+1)` | `for (int r = h + 1; r <= last; r++)` |

Use `cell.GetFormattedString()` everywhere — Python sees `"$2,275.00"`/`"60"`/`""`; ClosedXML's typed `XLCellValue` would otherwise return numbers/blanks and break the cleaners.

**Five parsers — one PR each, line-for-line ports.** Each implements `IParser.Parse → ParseResult` and is verified against its golden fixture in `samples/outputs/`. Acceptance per parser:

- `LineItems` projected to `(Vpn, Term, Msrp, Cost, Qty)` (plus `SerialNumber/StartDate/EndDate` for Renewal) equals the tuple list in `backend/tests/test_parsers.py`.
- `Metadata.QuotedTotal`, `Validation.ComputedTotal`, `Matches=true`.
- Hardware parsers: exactly 11 line items; lead row cost `20017.57m` (not `5903.72m` — Quote C negative-assertion).

## Template writer port

`ForeignUpliftWriter.WriteForeignUplift(items, outputPath, margin, fxRate, vendorName, currency)`.

**Confirmed by reading `backend/app/output/template_writer.py`**: the Python writer creates a fresh workbook (`Workbook()`) — it does **not** load `ANZ-GENERIC_ForeignUplift.xlsx`. The template is human spec, not code input. **C# does the same**: `new XLWorkbook(); wb.AddWorksheet("Foreign Uplift")`, then header row at row 2 (column 12 has the optional label at row 1), data rows from row 3, footer row with `*` in column B and `END_LOOP_WARNING` in column D.

Critical to match openpyxl byte-for-byte on the cell-by-cell golden test:

- **Don't address empty cells.** ClosedXML emits inline-string cells when touched; openpyxl skips them entirely. Guard with `if (item.Description is not null) ws.Cell(r,5).Value = item.Description;` (same for `SerialNumber`, etc.).
- **Integer vs float cell type**: openpyxl writes `383` as Python `int`, `101.11` as `float`. Helper:
  ```csharp
  static XLCellValue ExcelNumber(decimal v) =>
    v == decimal.Truncate(v) ? (long)v : (double)v;
  ```
- **Date number format**: `"DD/MM/YYYY"` on columns 16 and 17 only.
- **Sheet name**: `"Foreign Uplift"` exactly.
- No `.Style` touched anywhere else — golden was produced with no styles.

## Background tasks + config

`RetentionBackgroundService : BackgroundService` — 24h cadence, sleep-first, scoped `RetentionService.CleanupOldParseJobsAsync(RetentionDays, ct)` deletes expired `ParseJob` rows and their files (best-effort, ignore missing). Mirrors `_retention_loop()`.

`AppOptions` bound from config; existing env-var names mapped 1-1 (`DATABASE_URL`, `UPLOAD_DIR`, `SESSION_SECRET`, `SESSION_LIFETIME_HOURS`, `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `RETENTION_DAYS`, `RATE_LIMIT_AUTH_PER_MIN`, `MAX_UPLOAD_MB`, `PORT`, `FORWARDED_ALLOW_IPS`). Operator runbook keeps working.

SQLite reliability: `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;` set via a `DbConnection.Open` interception in `Program.cs`.

## Test plan

`BidParser.Parsing.Tests` (xUnit + FluentAssertions):

- One `[Theory]` per parser over the 4 golden cases in `CASES` (Software PDF/XLSX, Renewal PDF, Hardware PDF/XLSX).
- `[Fact]` `HardwareParserExcludesQuoteCRows` for both Hardware parsers.
- `[Theory]` `TemplateWriterMatchesGoldenWorkbookCells` using `WorkbookComparer.AssertEqual(actualPath, samples/outputs/<stem>_parsed.xlsx)` — iterates every used cell, asserts `Value` equality (normalised to `decimal|string|DateTime|null`) and `Style.NumberFormat.Format` equality.
- **Characterisation test** (Phase 5, gate for Phases 6-10): export pdfplumber word coordinates for the three sample PDFs once into a checked-in JSON snapshot, then a `[Fact]` asserts PdfPig's coordinates (after y-flip and `PdfPigWordSplitter`) match within 1pt envelope. **De-risks the entire PDF port early.**

`BidParser.Api.Tests` using `WebApplicationFactory<Program>`:

- Port every `test_phase2_api.py` case: auth flow, forced password change gate, admin user CRUD + last-admin guards, parse roundtrip + per-user settings, history filter + user scoping, downloads, rate limit (5/min per IP and per username across distinct `X-Forwarded-For`).
- DB reset between tests: `db.Database.EnsureDeletedAsync(); db.Database.MigrateAsync(); await bootstrap.RunAsync();`.
- Strict contract equivalence on the parts the SPA actually reads: JSON field names + types + decimal string scales (`fx_rate == "0.7400"`, `margin == "7.50"`), named response headers (`X-Computed-Total: 1625358.51`, `X-Validation: match|mismatch`), `Content-Disposition` filename. Arbitrary error-message text from framework defaults (e.g. `BadHttpRequestException` messages) is **not** asserted character-for-character — only that the wrapping `{detail: <string>}` shape is correct.

**Additional contract tests** (gaps the original plan missed; flagged in code review):

- `ParseErrorMatrixTests` — one `[Theory]` row per condition in the `/parse` error matrix above (unknown parser, vendor mismatch, extension mismatch, oversize, unsupported extension, ParseError-raising fixture, generic-exception fixture). Each asserts exact status code and `{detail}` body shape.
- `MustChangePasswordPolicyTests` — for a locked user, assert: `GET /me` returns 200, `POST /auth/change-password` returns 200, `POST /auth/logout` returns 200, but `PATCH /me/settings` / `GET /parsers` / `POST /parse` / `GET /history` / any `/users*` all return `403 {detail:"password_change_required"}`.
- `FrameworkErrorNormalisationTests` — assert that malformed JSON (`POST /auth/login` body `not-json`), missing required login fields (empty body), missing parse multipart fields (no `file`), invalid form decimal (`fx_rate=abc`), and oversized multipart (>`MAX_UPLOAD_MB`) all return `{detail: <string>}`, **not** RFC7807 `{type,title,status,traceId}`.
- `ChangePasswordErrorShapeTest` — `POST /auth/change-password` with a 4-character no-uppercase no-digit no-symbol password returns `400 {detail: ["Password must be at least 8 characters.", "Password must include an uppercase letter.", "Password must include a digit.", "Password must include a symbol."]}`. **Detail is an array**, not a string — assert array type explicitly. Verifies the single-endpoint exception to the `{detail: string}` rule.
- `DecimalScaleTests` — assert that `GET /me` after a parse with `fx_rate=0.74m` returns `"fx_rate":"0.7400"` (string, four dp); `margin=7.5m` returns `"margin":"7.50"`.
- `QuotedTotalHeaderEmptyTest` — when a parser produces a `ParseResult` with `QuotedTotal == null`, the `/parse` response includes `X-Quoted-Total:` (header **present**, value **empty string**), not the header being omitted. SPA reads this via `headers.get('X-Quoted-Total')`; missing header would return `null` and trigger "unknown total" UI state instead of "no quoted total" UI state. **Must run through `WebApplicationFactory` end-to-end** (full HttpClient → Kestrel-equivalent test host → response.Headers), not by inspecting an in-memory `HttpResponse` object — empty-header semantics differ across hosting layers (Kestrel, TestServer, reverse proxies all handle empty headers slightly differently) and the parity test must exercise the layer closest to production. If this test proves persistently flaky on the hosting layer, the resolution is to change the API contract (e.g. omit the header entirely and have the SPA treat missing as null-equivalent) rather than fight the platform.
- `ParseUpdatesUserDefaultsTest` — after a successful `POST /parse` with `vendor=Nutanix, fx_rate=0.6543, margin=8.75`, a subsequent `GET /me` returns `default_vendor="Nutanix"`, `fx_rate="0.6543"`, `margin="8.75"`. Verifies the implicit per-parse user-defaults write that drives the SPA's pre-fill behaviour.
- `CaseInsensitiveUsernameTests` — `POST /auth/login` with `{username: "ADMIN"}` succeeds against a `username="admin"` row; `POST /users` with `{username: "Admin"}` when `admin` exists returns `409 {detail:"Username already exists."}`; `PATCH /users/{id}` to rename to a case-variant of another existing username returns the same 409.
- `LastAdminGuardMessagesTest` — distinguish the two 409s: demoting the last admin via `PATCH` returns `"Cannot remove the last admin."`; `DELETE /users/{self_id}` returns `"Admins cannot delete themselves."` (and self-delete with a non-existent id still returns 409, not 404 — self-check fires first).
- `HistoryRelativeWhenFormatTest` — exact string assertions on the `when` field for jobs aged 30s ("just now"), 5m ("5m ago"), 3h ("3h ago"), 30h ("Yesterday"), 3d ("3 days ago"), 14d (DD/MM/YYYY).
- `HistoryDownloadRoundtripTest` — after a full `POST /parse` roundtrip, the resulting `parse_jobs` row's `source_path` and `output_path` resolve under the configured `UPLOAD_DIR` and both `GET /api/history/{id}/source` and `GET /api/history/{id}/output` stream non-empty files. (Pre-production means there are no legacy rows with foreign absolute paths to worry about — this test only needs to confirm round-trip storage works in a single run.)
- `DefaultVendorValidationTest` — `PATCH /me/settings` with `{default_vendor: "Cisco"}` returns `400 {detail: "Unknown vendor."}`; with `{default_vendor: "Nutanix"}` returns 200 and the row is updated.
- `FilenameSanitizationTest` — uploading a `file.FileName = "../../etc/passwd.pdf"` results in a stored `SourceFilename = "passwd.pdf"` (path components stripped) and the `/parse` write lands under `UPLOAD_DIR/originals/`, not elsewhere.

## Dockerfile + CI

Three-stage Dockerfile: Node 20 builds SPA → `mcr.microsoft.com/dotnet/sdk:10.0` builds + publishes → `mcr.microsoft.com/dotnet/aspnet:10.0` runtime hosts on `:3447`, serves `wwwroot/`, mounts `/data`. Entry: `dotnet BidParser.Api.dll` (migration runs in `MigratorHostedService` at startup — no entrypoint script).

GitHub Actions — restructure the existing single-job `build.yml` into **two gated jobs**:

1. `test` (runs on every PR + every push):
   - `actions/setup-dotnet@v4` with `dotnet-version: 10.0.x`.
   - `actions/setup-node@v4` with `node-version: 20`.
   - `dotnet restore` → `dotnet test BidParser.sln --no-restore`.
   - `cd frontend && npm ci && npm run build` (verifies SPA still builds).
2. `build-and-push` (only on `push` to `main` or `v*` tags):
   - `needs: [test]` — **publish is gated on tests passing**. The current workflow has no test step and would publish a broken `latest` image during the incremental port if left as-is.
   - Same buildx logic as today; same GHCR tags (`latest`, `sha-<short>`, SemVer on `v*`).

## Implementation phases

**Progress status**:
- Phase 1 complete: .NET 10 skeleton, health endpoint, Docker/CI rewrite, central package management, and health-check test added. Verification: `dotnet restore`, `dotnet build --configuration Release --no-restore`, `dotnet test --configuration Release --no-build`, and frontend `npm run build` passed. Local Docker build intentionally not run.
- Phase 2 complete: EF Core entities/schema, initial migration, SQLite WAL interceptor, startup migrator, env-backed app options, bootstrap admin service, and migration/bootstrap integration test added. Verification: `dotnet restore`, `dotnet build --configuration Release --no-restore`, and `dotnet test --configuration Release --no-build` passed. Local Docker build intentionally not run.
- Phase 3 complete: Data Protection session cookies, auth policies, CSRF filter, login/logout/change-password, `/me`, `/me/settings`, rate limiter, decimal JSON converters, protected route placeholders for policy tests, and auth contract tests added. Verification: `dotnet restore`, `dotnet build --configuration Release --no-restore`, and `dotnet test --configuration Release --no-build` passed. Local Docker build intentionally not run.
- Phase 4 complete: admin `/api/users` list/create/update/delete endpoints, last-admin/self-delete guards, case-insensitive username conflict handling, password reset, and user-admin tests added. Verification: `dotnet restore`, `dotnet build --configuration Release --no-restore`, and `dotnet test --configuration Release --no-build` passed. Local Docker build intentionally not run.
- Phase 5 complete: domain parser contracts/models, cleaning helpers, PDF word collection/table helpers with pdfplumber-compatible coordinates, XLSX worksheet/header helpers, empty explicit parser registry, parsing test project, and checked-in pdfplumber snapshot characterisation gate added. Verification: `dotnet restore`, `dotnet build --configuration Release --no-restore`, and `dotnet test --configuration Release --no-build` passed. Local Docker build intentionally not run.
- Phase 6 complete: `NutanixSoftwareOnlyPdfParser` ported, registered in the explicit parser registry, and line-item/total equivalence test added for `samples/inputs/XQ-4076249.pdf`. Verification: `dotnet restore BidParser.sln`, `dotnet build BidParser.sln --configuration Release --no-restore`, and `dotnet test BidParser.sln --configuration Release --no-build` passed. Local Docker build intentionally not run.

**Why phased.** The whole port in one session would exhaust an agent's context budget mid-flight, leaving a half-finished branch nobody can safely resume. Each phase below is sized for **one git branch, one PR, one focused session, one merge to main**. The next session starts from a known-green `main`, not from a WIP working tree.

**Per-phase contract.** Every phase has:
- a **branch name** (`port/NN-slug`),
- **predecessors** that must be merged to `main` before this phase starts,
- a **scope** of what's in and (where useful) what's deferred,
- an **acceptance gate** — the explicit set of `dotnet test` cases + manual smoke that must be green before merge,
- an **approximate file count** so the reader can sanity-check session sizing,
- a **size tag**: **S** (small, ~3-5 files, one-and-done), **M** (medium, one full session), **L** (large, watch the context budget — these have split suggestions).

**Stop signals.**
- Stop at the phase boundary even if you have context left. Resume in a fresh session from a green `main`.
- If you hit ~70% context use mid-phase on an **L** phase, commit WIP on the branch, push, and split at the suggested seam — finish the remainder in the next session.
- **Never** start a new phase in the same session as the previous one's merge. The CI signal on `main` is the gate.

**Cross-phase invariants** (must hold after every merge, not just at the end):
- The .NET app builds (`dotnet build` clean).
- All tests merged so far pass (`dotnet test` green).
- The Docker image still builds and starts (`docker compose up -d` succeeds).
- The Python backend (`backend/`) stays untouched until Phase 14 — both backends compile, only the .NET one ships.

### Phase 1 — Foundation (`port/01-dotnet-skeleton`) [M]

**Goal**: prove the .NET 10 dependency stack works end-to-end. No application logic.

**Predecessors**: none.

**Scope**:
- `BidParser.sln` + 4 projects (`BidParser.Api`, `BidParser.Domain`, `BidParser.Infrastructure`, `BidParser.Parsing`) targeting `net10.0`. (`BidParser.Output` + tests projects added in later phases.)
- `Program.cs` minimal: `UseStaticFiles()` + `MapFallbackToFile("index.html")` + `MapGet("/api/healthz", () => Results.Ok())`.
- `Directory.Packages.props` referencing every NuGet package later phases need so PR 1 catches stack drift: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `PdfPig`, `ClosedXML`, `BCrypt.Net-Next`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.AspNetCore.Mvc.Testing`.
- New 3-stage Dockerfile (Node 20 SPA build → `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`). The existing Python Dockerfile stays on disk for now (Phase 14 deletes it).
- `.github/workflows/build.yml` restructured into `test` + `build-and-push` jobs with `needs: [test]`.
- `docker-compose.yml` points at the new image.

**Acceptance**:
- `dotnet restore` + `dotnet build` clean.
- `docker compose build && docker compose up -d` — container starts, `GET /` returns SPA `index.html`, `GET /api/healthz` returns 200.
- CI `test` and `build-and-push` jobs green on the PR.
- **If any NuGet package fails to restore on `net10.0`, stop and decide**: bump library version, file upstream, or downgrade plan to .NET 9 LTS. Cheap decision now, expensive mid-port.

**Files**: ~12.

### Phase 2 — Data layer (`port/02-ef-core`) [M]

**Goal**: schema and bootstrap admin work end-to-end. No auth yet.

**Predecessors**: Phase 1.

**Scope**:
- `BidParser.Infrastructure/Persistence/AppDbContext.cs` + `Entities/{User,ParseJob}.cs` with Fluent config: snake_case columns, **`COLLATE NOCASE` on `username`** with a unique index, decimal precision per the entity spec, `OnDelete(DeleteBehavior.Cascade)` on `User → ParseJob`.
- `Migrations/00000000000001_InitialCreate.cs`.
- `Hosting/MigratorHostedService.cs` — single `Database.MigrateAsync()` call + a `DbConnectionInterceptor.ConnectionOpenedAsync` that executes `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;`.
- `Auth/BootstrapAdminHostedService.cs` — seeds the admin row when zero users exist, with literal `name="Administrator"`, `role="admin"`, `must_change_password=true`.
- `Options/AppOptions.cs` bound from configuration (every env var in the operator runbook).

**Acceptance**:
- `BidParser.Api.Tests/MigrationTests.cs`: fresh DB → `MigrateAsync` creates schema + admin row appears + WAL pragma applied.
- Manual: `docker compose up -d` against a fresh `/data` volume → log line confirms admin seeded; `users` table contains the seeded row.

**Files**: ~10.

### Phase 3 — Auth core (`port/03-auth`) [L]

**Goal**: cookie sessions + login + logout + change-password + me + me/settings + rate limiting + framework-error normalisation all working against the DB.

**Predecessors**: Phase 2.

**Token-budget watch — split candidate**. This is the broadest phase. If approaching ~70% context use:
- **3a**: cookie auth infra (`SessionCookieAuthHandler`, Data Protection wiring, four `AuthorizationPolicy` builders, `RequireCsrfHeader`, `UnifiedErrorHandler` + `NormalizeErrorBodyMiddleware`) + `/auth/login` + `/auth/logout` + `GET /me`.
- **3b**: `/auth/change-password` + `/me/settings` + `RateLimiter` + `JsonStringDecimalConverter` attributes + the framework-error / decimal-scale / must-change-password tests.

**Scope (full phase, or 3a+3b combined)**:
- `Auth/SessionCookieAuthHandler.cs`, `RateLimiter.cs`, `PasswordPolicy.cs`, `RequireCsrfHeader.cs`, `UnifiedErrorHandler.cs`, `NormalizeErrorBodyMiddleware.cs`.
- Data Protection wiring: `PersistKeysToFileSystem("/data/dp-keys")` + `SetApplicationName(SESSION_SECRET)`.
- `ForwardedHeadersMiddleware` registered first in the pipeline; `KnownProxies`/`KnownNetworks` configured so `X-Forwarded-For` is honoured.
- `Endpoints/AuthEndpoints.cs` + `Endpoints/MeEndpoints.cs`.
- `JsonStringDecimalConverter` (scale-4 + scale-2 variants for `decimal` and `decimal?`) + `[JsonConverter]` attributes on `UserPublic` DTO.
- Four `AuthorizationPolicy` builders: `Anonymous`, `LoggedIn`, `ActiveUser`, `Admin`. Decorate each endpoint accordingly.

**Acceptance**:
- `LoginFlowTests`, `ChangePasswordErrorShapeTest` (asserts `detail` is an array), `MustChangePasswordPolicyTests`, `DecimalScaleTests`, `FrameworkErrorNormalisationTests`, `CaseInsensitiveUsernameTests` (login branch), rate-limit tests (per-IP, per-username, cross-IP via `X-Forwarded-For`) — all green.
- Manual SPA smoke (Vite dev → log in as admin → forced password change flow works → `/me/settings` updates persist on reload).

**Files**: ~25 (the split exists because of this).

### Phase 4 — Users admin (`port/04-users-admin`) [S]

**Goal**: admin CRUD for users.

**Predecessors**: Phase 3.

**Scope**:
- `Endpoints/UsersEndpoints.cs` (list / create / patch / delete).
- Last-admin guard (two distinct messages); self-delete check fires before user lookup; case-insensitive uniqueness already enforced by `COLLATE NOCASE`.
- Password reset writes literal `"changeme"` + `must_change_password=true`.

**Acceptance**:
- `UsersAdminCrudTests`, `LastAdminGuardMessagesTest`, `CaseInsensitiveUsernameTests` (create + update branches), self-delete-of-non-existent-id-returns-409 case — all green.
- Manual: SPA settings page can create / rename / change role / reset password / delete users.

**Files**: ~4.

### Phase 5 — Parsing infrastructure (`port/05-parsing-infra`) [L]

**Goal**: domain models + parser-side library wrappers + the **characterisation test that gates every later parser phase**. No actual parsers yet.

**Predecessors**: Phase 1 (no auth or DB dependency — parser tree is self-contained).

**Token-budget watch — split candidate**. Lots of geometry + ClosedXML plumbing + checked-in pdfplumber coordinate snapshot. If approaching budget:
- **5a**: Domain models + cleaning helpers + cleaning unit tests.
- **5b**: PDF wrappers (`PdfWord`, `PdfWordCollector` using `NearestNeighbourWordExtractor.Instance` + Y-flip + `PdfPigWordSplitter`) + header / row-cluster helpers + characterisation test snapshot + assertion.
- **5c**: XLSX wrappers (`WorkbookReader`, `HeaderMap`).

**Scope (full phase)**:
- `BidParser.Domain/Models/` (`LineItem`, `QuoteMetadata`, `ValidationResult`, `ParseResult`, `ParseError`).
- `BidParser.Domain/Abstractions/` (`IParser`, `IParserRegistry`).
- `BidParser.Parsing/Cleaning/` (`TextCleaner`, `DecimalCleaner`, `DateCleaner`) + unit tests.
- `BidParser.Parsing/Pdf/` (`PdfWord`, `PdfWordCollector`, `PdfPigWordSplitter`, header finders, row extractor).
- `BidParser.Parsing/Xlsx/` (`WorkbookReader`, `HeaderMap`).
- `BidParser.Parsing/Registry/ParserRegistry.cs` — empty `IReadOnlyList<IParser>` for now; populated in Phases 6-10.
- **Characterisation gate**: one-off pdfplumber word-coordinate snapshot of the 3 sample PDFs (checked into the test project as JSON, generated by a tiny script in `backend/`), plus `[Fact] PdfPigCoordinatesMatchPdfplumberSnapshot` asserting ≤1pt envelope agreement after Y-flip + splitter.

**Acceptance**:
- Cleaning unit tests green.
- **Characterisation test green — this is the hard gate. If it fails, no parser phase ships until the splitter / Y-flip / coordinate translation is corrected.**

**Files**: ~18.

### Phases 6–10 — Five parsers, one per phase [S each]

Each phase implements one parser, registers it in `ParserRegistry.Parsers`, and proves LineItem extraction matches the Python golden values from `backend/tests/test_parsers.py`. **Workbook-equivalence tests are deferred to Phase 11.**

**Predecessors (all five)**: Phase 5.

| Phase | Branch | Parser | Extra acceptance |
|---|---|---|---|
| 6 | `port/06-software-pdf` | `NutanixSoftwareOnlyPdfParser` | LineItem tuples match Python; `Term-Months` filler row skipped |
| 7 | `port/07-software-xlsx` | `NutanixSoftwareOnlyXlsxParser` | Same line items as Phase 6 via XLSX path; `Term-Months` filler skipped |
| 8 | `port/08-renewal-pdf` | `NutanixRenewalPdfParser` | Exercises `PdfPigWordSplitter` on stacked header; serial-number wrap join (no separator); `MM/DD/YYYY → DateOnly` |
| 9 | `port/09-hardware-pdf` | `NutanixHardwareOnlyPdfParser` | Exactly 11 line items; lead row cost `20017.57m`; **Quote C negative-assertion** test |
| 10 | `port/10-hardware-xlsx` | `NutanixHardwareOnlyXlsxParser` | Same 11 items as Phase 9 via XLSX path; same Quote C negative-assertion |

**Scope per phase**: one `BidParser.Parsing/Nutanix/Nutanix<X>Parser.cs` + one `tests/BidParser.Parsing.Tests/Nutanix<X>ParserTests.cs` + a one-line addition to `ParserRegistry.Parsers`.

**Acceptance per phase**: the parser's `Theory` rows + (Phases 9-10 only) the Quote C negative-assertion `Fact` — all green.

**Files per phase**: ~3.

### Phase 11 — Template writer (`port/11-template-writer`) [M]

**Goal**: `ForeignUpliftWriter` produces output that matches `samples/outputs/XQ-*_parsed.xlsx` cell-by-cell across all five fixtures.

**Predecessors**: Phases 6-10 (all five parsers merged).

**Scope**:
- `BidParser.Output/` project added to the solution.
- `ForeignUpliftWriter.cs` (don't touch empty cells; `ExcelNumber` int-vs-double helper; `"DD/MM/YYYY"` number format on date cells only; sheet name `"Foreign Uplift"`).
- `OutputNaming.cs` — `output_filename(source)` → `{stem}_parsed.xlsx`.
- `WorkbookComparer.AssertEqual(actualPath, expectedPath)` helper in the test project.
- `[Theory] TemplateWriterMatchesGoldenWorkbookCells` — five cases (one per parser × golden pair).

**Acceptance**: all 5 cell-equivalence tests green.

**Files**: ~5.

### Phase 12 — Parse pipeline part 1: orchestration (`port/12a-parse-orchestration`) [L]

**Goal**: end-to-end `POST /api/parse` works via the SPA — upload a PDF/XLSX, download `*_parsed.xlsx`. `/api/parsers` also live.

**Predecessors**: Phase 11.

**Token-budget watch — split**: this used to be one PR with history + retention. Now split into 12 (this) and 13 (history + retention) because the orchestration + tests + multipart wiring + manual smoke is already a full session.

**Scope**:
- `BidParser.Infrastructure/Storage/FileStorage.cs`: UUID-based path computation (`{uuid:N}{ext}`), streaming upload with size enforcement (1 MB chunks, partial-file cleanup on overflow), idempotent delete. Filename sanitization via `Path.GetFileName(file.FileName ?? "quote")`.
- `BidParser.Infrastructure/Services/ParseService.cs`: orchestrates resolve-parser → validate-extension → save-upload → parse → write-template → persist ParseJob + **autoupdate User.DefaultVendor/FxRate/Margin**. All error paths delete `source_path` + `output_path` before raising.
- `Endpoints/ParseEndpoints.cs` + `Endpoints/ParsersEndpoints.cs`.
- Kestrel `Limits.MaxRequestBodySize` + `FormOptions.MultipartBodyLengthLimit` + endpoint `[RequestSizeLimit]` all set to `MAX_UPLOAD_MB`. **If Minimal API form binding fights the contract, fall back to a `ParseController : ControllerBase`** — contract equivalence outranks endpoint-style consistency.
- `X-Validation` / `X-Computed-Total` / `X-Quoted-Total` headers (empty string when null, **via `StringValues.Empty`** not omission).
- `_resolve_parser` + `_validate_upload_type` ported with their exact 400/415 messages.

**Acceptance**:
- `ParseRoundtripTest`, all 7 cases of `ParseErrorMatrixTests`, `ParseUpdatesUserDefaultsTest`, `QuotedTotalHeaderEmptyTest` (via `WebApplicationFactory`), `FilenameSanitizationTest`, `DefaultVendorValidationTest` — all green.
- Manual SPA smoke: log in as admin, upload all 5 sample files in `samples/inputs/`, all 5 download successfully, each downloaded `*_parsed.xlsx` opens in Excel and matches its golden cell-by-cell.

**Files**: ~15.

### Phase 13 — Parse pipeline part 2: history & retention (`port/12b-history-retention`) [M]

**Goal**: history list + per-row downloads + automatic cleanup.

**Predecessors**: Phase 12.

**Scope**:
- `Endpoints/HistoryEndpoints.cs` (list with `q` filter, pagination, user scoping; source/output downloads with `Content-Disposition` per the parity matrix).
- `_relative_when` C# port — verbatim format strings (`"just now"`, `"5m ago"`, `"3h ago"`, `"Yesterday"`, `"3 days ago"`, `DD/MM/YYYY`).
- `BidParser.Infrastructure/Services/RetentionService.cs` + `Hosting/RetentionBackgroundService.cs` (24h cadence, sleep-first, scoped service).

**Acceptance**:
- `HistoryListTests` (user scoping, `q` filter, pagination), `HistoryRelativeWhenFormatTest` (all 6 format branches), `HistoryDownloadRoundtripTest`, cross-user 404 test, retention test asserting deletion of a synthetic old job + its files — all green.
- Manual SPA smoke: Recent Uploads section populates with the 5 uploads from Phase 12, search box filters correctly, source + output downloads work.

**Files**: ~6.

### Phase 14 — Cutover (`port/13-cutover`) [S]

**Goal**: delete the Python backend, update operator docs, final smoke against a production image with a fresh volume.

**Predecessors**: Phases 1-13 all merged.

**Scope**:
- Delete `backend/` tree, root `Dockerfile` Python-runtime stages, `backend/alembic.ini`, the Python `.venv` reference in `.gitignore` if present.
- Update `AGENTS.md`: replace Python paths with C# paths in the inventory; **parser extraction specs stay verbatim** (algorithm is language-agnostic).
- Update `docs/DEPLOYMENT.md`:
  - `SESSION_SECRET` is a Data Protection app-name discriminator, **not** a signing key.
  - `/data/dp-keys` must persist; delete this directory for a hard session reset.
  - Schema migration runs in `MigratorHostedService` at startup (replace the alembic note).
  - `/data` volume + `3447:3447` port unchanged.
- Update root `README` if it references Python anywhere.
- Update `frontend/vite.config.ts` proxy target if dev port changed (default stays `8000` or moves to `5000` — document either).

**Acceptance**:
- `dotnet test BidParser.sln` green (all phases' tests still pass).
- `docker compose up -d --build` against a fresh `/data` volume — admin bootstraps, SPA loads, all 5 sample files upload + parse + download cleanly.
- Restart the container — sessions and DB persist (Data Protection keyring + SQLite both on `/data`).

**Files**: many deletions + ~5 docs updates.

### Phase summary

| Phase | Status | Branch | Size | Approx files | Notes |
|---|---|---|---|---|---|
| 1 | Complete | port/01-dotnet-skeleton | M | 12 | Proves dependency stack on .NET 10; package ID adjusted to `PdfPig` |
| 2 | Complete | port/02-ef-core | M | 10 | EF Core + migration + bootstrap |
| 3 | Complete | port/03-auth | **L** | 25 | Split 3a/3b if budget tight |
| 4 | Complete | port/04-users-admin | S | 4 | |
| 5 | Complete | port/05-parsing-infra | **L** | 18 | Domain contracts, cleaning/PDF/XLSX helpers, and characterisation gate added |
| 6 | Pending | port/06-software-pdf | S | 3 | |
| 7 | Pending | port/07-software-xlsx | S | 3 | |
| 8 | Pending | port/08-renewal-pdf | S | 3 | |
| 9 | Pending | port/09-hardware-pdf | S | 3 | Quote C negative-assertion |
| 10 | Pending | port/10-hardware-xlsx | S | 3 | Quote C negative-assertion |
| 11 | Pending | port/11-template-writer | M | 5 | Phase 6-10 workbook tests run here |
| 12 | Pending | port/12a-parse-orchestration | **L** | 15 | Was one PR with Phase 13; split for budget |
| 13 | Pending | port/12b-history-retention | M | 6 | |
| 14 | Pending | port/13-cutover | S | docs + deletions | |

14 phases. Average phase ≈ 7-8 files. Three flagged **L** with explicit split seams. Hard dependencies form a chain; the only parallelism is Phase 5 against Phases 2-4 (parsing tree is independent of auth + DB), but for token-management simplicity, default to strict serial execution.

## Critical files to modify (target side)

New: `src/BidParser.Api/Program.cs`, `Endpoints/*.cs`, `Auth/*.cs`, `Hosting/*.cs`, `Options/AppOptions.cs`, `appsettings.json`; `src/BidParser.Domain/Models/*.cs` + `Abstractions/*.cs`; `src/BidParser.Infrastructure/Persistence/AppDbContext.cs` + `Entities/*.cs` + `Migrations/*.cs` + `Storage/FileStorage.cs` + `Services/{Parse,Retention}Service.cs`; `src/BidParser.Parsing/Cleaning/*.cs` + `Pdf/*.cs` + `Xlsx/*.cs` + `Nutanix/*.cs` + `Registry/ParserRegistry.cs`; `src/BidParser.Output/ForeignUpliftWriter.cs`; `tests/BidParser.Parsing.Tests/*.cs`; `tests/BidParser.Api.Tests/*.cs`; new `Dockerfile`; updated `.github/workflows/build.yml`.

Modified: `docker-compose.yml`, `frontend/vite.config.ts` (dev proxy target if port changes), `AGENTS.md`, `docs/DEPLOYMENT.md`, root `README` (if any).

Deleted at Phase 14: entire `backend/` tree, root `Dockerfile` Python-runtime stages.

Reused unchanged: `frontend/` (the React SPA — entire point of this refactor is that it stays put), `samples/inputs/`, `samples/outputs/`, `samples/template/`, `docs/output_mapping.md` + the five `nutanix_*` extraction specs.

## Risks (highest-impact first)

1. **PdfPig word grouping vs pdfplumber** — especially Renewal's stacked `Term/Adjusted/List Unit/Price` header and Hardware's wrapping SKUs (`NX-1175S-G10-`/`6517P-CM`). Mitigation: `PdfPigWordSplitter` re-splits merged tokens via Letter rects; **Phase 5's characterisation test snapshot-compares to pdfplumber output before any parser ships**.
2. **PdfPig Y-axis flip** — bottom-left origin. Mitigation: encapsulate flip once in `PdfWord` constructor; characterisation test catches a missed flip immediately.
3. **Per-parse user-defaults autoupdate easy to miss** — the SPA's pre-fill of vendor/fx_rate/margin depends on every successful `POST /parse` writing the same values onto the User row. Hidden in `parse_service.py:90-92`. Skipping it produces a port that "works" but silently loses a UX feature. Covered by `ParseUpdatesUserDefaultsTest`.
4. **`detail` array on change-password vs object on /parse vs string elsewhere** — three different shapes for the same key; UnifiedErrorHandler must preserve them. Covered by `ChangePasswordErrorShapeTest` + `ParseErrorMatrixTests`.
5. **Upload size limits at three layers** — Kestrel `MaxRequestBodySize`, `FormOptions.MultipartBodyLengthLimit`, and the streaming check in `FileStorage`. Missing any of these means a 10 MB upload returns a Kestrel framework body (or worse, a connection reset) instead of the `413 {detail: "File is too large."}` the SPA expects. Defence-in-depth required.
6. **Must-change-password gate misapplied as path glob** — would let locked users update `PATCH /me/settings` (verified the Python code splits per-endpoint). Mitigation: implement as the `ActiveUser` authorization policy decorated per-endpoint, not as path-globbed middleware. Covered by `MustChangePasswordPolicyTests`.
7. **Decimal scale loss in JSON** — Pydantic emits `"0.7400"` and `"7.50"`; naive `decimal.ToString()` on an under-scaled value emits `"0.74"`/`"7.5"` and breaks the SPA's parsing of `fx_rate`/`margin`. Mitigation: per-field `JsonStringDecimalConverter(scale)` attribute (`F4` for fx_rate, `F2` for margin/totals) + EF column precision + form-bind rounding. Two converter variants needed: one for `decimal` (HistoryRow), one for `decimal?` (UserPublic). Covered by `DecimalScaleTests`.
8. **ASP.NET framework-default error responses** for model-binding/multipart-binding failures bypass endpoint handlers and emit RFC7807 `{type,title,status,traceId}`. The SPA reads `detail`; would surface as "undefined" errors in the UI. Mitigation: register `UnifiedErrorHandler : IExceptionHandler` and don't call `AddProblemDetails()`. Covered by `FrameworkErrorNormalisationTests`.
9. **PdfPig extractor API** — `NearestNeighbourWordExtractor` is a singleton (`Instance`); calling `new` won't compile. Trivial but easy to mis-type.
10. **`X-Quoted-Total` empty-value vs missing header** — ASP.NET Core's `HeaderDictionary` indexer with empty string omits the header by default; must use `StringValues.Empty` or set via `Append("X-Quoted-Total", string.Empty)`. SPA distinguishes "header missing" from "header present, value empty". Covered by `QuotedTotalHeaderEmptyTest`.
11. **Username case sensitivity drift** — Python lowercases for comparison and rate-limit bucketing but preserves case in storage. A port that compares case-sensitively would let `admin` and `Admin` coexist as separate users and break the bucket-by-username rate limit. Mitigated by SQLite `COLLATE NOCASE` on the `username` column (the unique index + every equality predicate become case-insensitive at the storage layer; no application checks to race).
12. **Cascade delete on `User.ParseJobs`** — without `OnDelete(DeleteBehavior.Cascade)` in EF Fluent config, `DELETE /users/{id}` returns a 500 SQLite FK violation. Disk files are intentionally not cleaned up here — retention loop catches them.
13. **ClosedXML empty-cell behaviour** vs openpyxl (touched cells emit `<c>` elements). Mitigation: never address null/empty cells in `ForeignUpliftWriter`.
14. **ClosedXML number type** — must write `383` as `long`, `101.11` as `double` to match openpyxl. `ExcelNumber` helper guards this.
15. **Data Protection keyring** must live on `/data/dp-keys` or container restarts log everyone out. Wired in Phase 3.
16. **Forwarded-headers parsing** for rate-limit IP keys — needs `ForwardedHeadersMiddleware` registered before auth/limiter, with `KnownProxies`/`KnownNetworks` configured (or `ForwardedHeadersOptions.KnownNetworks.Clear()` + `KnownProxies.Clear()` for tests to trust loopback). Otherwise `X-Forwarded-For` is silently dropped and the cross-IP rate-limit test passes by accident.
17. **SQLite contention** between parse uploads + retention — set `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;` via an `IDbConnectionInterceptor.ConnectionOpenedAsync` registered with `optionsBuilder.AddInterceptors(...)`. Retention is single-threaded.

## Verification (end-to-end)

After Phase 13 (and again after Phase 14):

- `dotnet test BidParser.sln` — all parser, template, API, auth, rate-limit, history, error-matrix, must-change-password, decimal-scale, and framework-error tests green.
- `dotnet run --project src/BidParser.Api` + `cd frontend && npm run dev` — browser at `http://localhost:5173`, log in as admin (after `must_change_password` flow), upload each of the 5 sample files in `samples/inputs/`, confirm downloaded `*_parsed.xlsx` opens cleanly in Excel and matches the corresponding `samples/outputs/*_parsed.xlsx` cell-for-cell.
- `docker compose up -d --build` **with a fresh `/data` volume** (this is the cutover — wipe any existing `db.sqlite` first; pre-production, nothing to preserve): admin bootstrap fires; SPA loads at `:3447`; same 5-file smoke; restart the container and confirm sessions and DB persist (Data Protection keyring + SQLite both on `/data`).
- Rate-limit smoke: 6 failed logins from one IP → 6th returns `429` with `Retry-After`; 5 wrong passwords on one username from 5 different `X-Forwarded-For` IPs → 6th returns `429` (per-username bucket).
- **Decimal-scale smoke**: log in, parse a file, hit `GET /me` and assert the response JSON contains `"fx_rate":"0.7400"` and `"margin":"7.50"` (string with trailing zeros).
- **Framework-error smoke**: `curl -X POST /api/auth/login -d 'not-json' -H 'Content-Type: application/json'` returns a body matching `{"detail": ...}`, not RFC7807.

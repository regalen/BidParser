# Admin Utilisation Dashboard & Failure Monitoring

Two admin-only features that share a common navigation shell:

1. **Utilisation Dashboard** (`/admin/metrics`) — counts and breakdowns of successful parses, backed by a persistent `ParseMetric` ledger that survives the 90-day source-file purge.
2. **Monitoring** (`/admin/monitoring`) — list of failed parser runs with the full exception detail and a link to download the original source file, retained alongside the file until the 90-day retention purge.

Both ride the same Users-admin shell at `/admin/*`, accessed via a dropdown attached to the existing header cog button.

---

## Implementation phases

The work is split into six sequential phases so each fits comfortably inside an implementing LLM's context window. Each phase is **independently buildable, testable, and shippable** — after each one, `dotnet test BidParser.sln` should pass (and `npm run build` for frontend phases). Each phase entry below names the detailed sections that apply, the files touched, the tests added, and a one-line definition of done — read those sections in full before starting.

### Dependency graph

```
Phase 1 ──▶ Phase 2 ──▶ Phase 3 ─┐
                                 │
Phase 4 ──▶ Phase 5 ──▶ Phase 6 ─┘
```

Phases 1–3 (Metrics) and Phases 4–6 (Monitoring) are two independent vertical slices. After Phase 3 lands the shared admin shell, the two slices can be implemented in parallel; Phase 6 depends on Phase 3's shell. If Monitoring must ship before Metrics, lift §0 (shell rework) into Phase 4 and renumber accordingly.

---

### Phase 1 — Metrics ledger foundation (backend only) [COMPLETED]

**Goal**: Persist a `ParseMetric` row alongside every successful `ParseJob`. Backfill from existing `parse_jobs` rows at migration time.

**Sections to read**: §1.1, §1.2

**Files**:
- NEW: `src/BidParser.Infrastructure/Entities/ParseMetric.cs`
- NEW: `src/BidParser.Infrastructure/Migrations/<ts>_AddParseMetricsLedger.cs` (with inline SQL backfill in `Up()`)
- MODIFY: `src/BidParser.Infrastructure/Persistence/AppDbContext.cs` (DbSet, `OnModelCreating`, `StampTimestamps`)
- MODIFY: `src/BidParser.Infrastructure/Services/ParseService.cs` (add `ParseMetric` write in the success path, same `SaveChangesAsync`)

**Tests** (subset of §4.1 → Metrics):
- NEW: `tests/BidParser.Api.Tests/MetricsLedgerTests.cs`
- NEW: `tests/BidParser.Api.Tests/MetricsBackfillTests.cs`

**Done when**: full test suite passes; the migration applies cleanly against a snapshot of the production DB shape; a fresh `POST /api/parse` produces a row in `parse_metrics`.

---

### Phase 2 — Metrics API [COMPLETED]

**Goal**: Expose `/api/metrics/summary` and `/api/metrics/export`.

**Sections to read**: §1.3

**Files**:
- NEW: `src/BidParser.Api/Endpoints/MetricsEndpoints.cs`
- MODIFY: `src/BidParser.Api/Program.cs` (call `MapMetricsEndpoints()`)

**Tests** (subset of §4.1 → Metrics):
- NEW: `tests/BidParser.Api.Tests/MetricsEndpointsTests.cs`
- NEW: `tests/BidParser.Api.Tests/MetricsExportTests.cs`

**Done when**: all metrics API tests pass; manual `curl` with admin cookie returns the expected JSON shape from `/summary` and a valid XLSX from `/export`; TZ-bucketing test passes under both `TZ=UTC` and `TZ=Australia/Sydney`.

---

### Phase 3 — Admin shell + Metrics dashboard (frontend) [COMPLETED]

**Goal**: Land the `/admin/*` route shell, the dropdown menu, and the Utilisation Dashboard UI.

**Sections to read**: §0, §1.4

**Files**:
- MODIFY: `frontend/package.json` (add `recharts ^2.x`)
- MODIFY: `frontend/src/App.tsx` (routes for `/admin/users` + `/admin/metrics`, `/settings` redirect, admin guard)
- NEW: `frontend/src/components/AdminMenu.tsx`
- MODIFY: `frontend/src/components/AppHeader.tsx` (swap cog button for `<AdminMenu />`)
- MOVE: `frontend/src/pages/SettingsPage.tsx` → `frontend/src/pages/admin/UsersPage.tsx` (no behaviour change)
- NEW: `frontend/src/pages/admin/MetricsDashboard.tsx`
- NEW: `frontend/src/components/metrics/{DateRangeControl,FilterChips,KpiStrip,UtilisationTimeChart,BreakdownCard}.tsx`

**Tests**: none new (no frontend test suite today). Manual verification per §4.2 → "Shared shell" and "Metrics".

**Done when**: `npm run build` succeeds; admin sees a dropdown on the cog with Users + Metrics; both routes work; `/settings` redirects to `/admin/users`; date-range presets, drilldowns, and XLSX export all behave per §1.4.

---

### Phase 4 — Monitoring ledger foundation (backend only) [COMPLETED]

**Goal**: Capture every post-save parse failure as a `FailedParseJob` row, retain its source file, and clean up both at retention.

**Sections to read**: §2.1, §2.2, §2.3, §2.4

**Files**:
- NEW: `src/BidParser.Infrastructure/Entities/FailedParseJob.cs`
- NEW: `src/BidParser.Infrastructure/Migrations/<ts>_AddFailedParseJobs.cs`
- NEW: `src/BidParser.Infrastructure/Services/FailedParseJobRecorder.cs`
- MODIFY: `src/BidParser.Infrastructure/Persistence/AppDbContext.cs` (DbSet, `OnModelCreating`, `StampTimestamps` for the new entity)
- MODIFY: `src/BidParser.Infrastructure/Services/ParseService.cs` (rework catch block per §2.3 — keep source, drop output, invoke recorder)
- MODIFY: `src/BidParser.Infrastructure/Services/RetentionService.cs` (purge `failed_parse_jobs` + their source files alongside `parse_jobs`)
- MODIFY: `src/BidParser.Api/Program.cs` (register `FailedParseJobRecorder` as scoped)

**Tests** (subset of §4.1 → Monitoring):
- NEW: `tests/BidParser.Api.Tests/FailureRecordingTests.cs`
- NEW: `tests/BidParser.Api.Tests/FailureRetentionTests.cs`

**Done when**: full test suite passes; manually uploading a deliberately broken file (e.g. PDF renamed `.xlsx`) leaves a row in `failed_parse_jobs` and its file on disk; user `default_vendor`/`fx_rate`/`margin` are *not* updated when a parse fails.

---

### Phase 5 — Monitoring API [COMPLETED]

**Goal**: Expose `/api/monitoring/failures` (list) and `/api/monitoring/failures/{id}/source` (download).

**Sections to read**: §2.5

**Files**:
- NEW: `src/BidParser.Api/Endpoints/MonitoringEndpoints.cs`
- MODIFY: `src/BidParser.Api/Program.cs` (call `MapMonitoringEndpoints()`)

**Tests** (subset of §4.1 → Monitoring):
- NEW: `tests/BidParser.Api.Tests/MonitoringEndpointsTests.cs`

**Done when**: all monitoring API tests pass; manual `curl` with admin cookie returns the expected JSON shape (including inline `error_detail` and `source_available`); the source-download endpoint streams the right file and 404s when the file is missing.

---

### Phase 6 — Monitoring page (frontend) + ops/docs [COMPLETED]

**Goal**: Add the Monitoring page, wire it into the admin dropdown, finalise ops config and docs.

**Sections to read**: §2.6, §3

**Files**:
- MODIFY: `frontend/src/App.tsx` (add `/admin/monitoring` route)
- MODIFY: `frontend/src/components/AdminMenu.tsx` (add Monitoring item)
- NEW: `frontend/src/pages/admin/MonitoringPage.tsx`
- NEW: `frontend/src/components/monitoring/{FailuresTable,FailureRowDetail,CategoryBadge}.tsx`
- MODIFY: `docker-compose.yml` (TZ env var)
- MODIFY: `.env.example` (commented TZ entry)
- MODIFY: `docs/DEPLOYMENT.md` (TZ row)
- MODIFY: `AGENTS.md` (two-bullet note on `ParseMetric` and `FailedParseJob`)

**Tests**: none new. Manual verification per §4.2 → "Monitoring".

**Done when**: `npm run build` succeeds; admin can reach Monitoring via the dropdown; a failing parse appears in the list with download + expandable trace + copy-trace button; rebuilding the container with `TZ` set in `.env` picks up the new timezone; AGENTS.md reflects both new entities.

---

## 0. Shared changes — navigation shell

The current admin entry point is a single cog button next to `AccountChip` that navigates straight to `/settings` (Users admin). With three admin destinations, the cog becomes a dropdown.

### Routing

| Existing | After |
|---|---|
| `/settings` | redirect 301 → `/admin/users` |
| (page component) `SettingsPage.tsx` | moved to `src/pages/admin/UsersPage.tsx` (no behavioural change) |
| — | `/admin/metrics` → `src/pages/admin/MetricsDashboard.tsx` (new) |
| — | `/admin/monitoring` → `src/pages/admin/MonitoringPage.tsx` (new) |

All three routes guarded by an admin route guard (extract once, reuse). The `/settings` redirect keeps any in-flight bookmarks/tabs working.

### Header dropdown

`src/components/AppHeader.tsx` + a new `src/components/AdminMenu.tsx`. The cog button (lucide `Settings`) becomes a dropdown trigger styled identically to today's `.icon-button`. On click, opens a Tailwind-styled menu with three items: **Users / Metrics / Monitoring**. Each item navigates to its `/admin/*` route.

Implementation: reuse the click-outside-to-close pattern from `AccountChip.tsx` (`useRef` + `mousedown` listener) so it's dependency-free. Keyboard-accessible: `Esc` closes, `↑/↓` move focus across items, `Enter` activates. ARIA: `role="menu"` on the panel, `role="menuitem"` per link.

`AccountChip` is *not* touched.

---

## 1. Feature A — Utilisation Dashboard

### 1.1 Objective

Track application usage across users, vendors, and file types (parser slugs) over time. Because `ParseJob` rows and their source/output files are purged at `RETENTION_DAYS` (default 90), an append-only ledger (`ParseMetric`) retains execution metadata indefinitely.

Design principles baked in:

- **Decoupled by snapshot, not by FK survivability.** `ParseMetric` snapshots the user's username/name at write time, so the row stays interpretable even if the `User` row is later removed. No `IsActive` flag added to `User`.
- **Survives both 90-day file purge and user removal.** FK columns to `parse_jobs` and `users` are nullable with `OnDelete(SetNull)`.
- **Only successful parses are recorded here.** Failures go to the Monitoring feature (§2).

### 1.2 Schema — `ParseMetric`

#### `src/BidParser.Infrastructure/Entities/ParseMetric.cs` [NEW]

```csharp
namespace BidParser.Infrastructure.Entities;

public sealed class ParseMetric
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public int? ParseJobId { get; set; }

    public required string UserUsername { get; set; }
    public string? UserName { get; set; }

    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }

    public required string SourceFilename { get; set; }
    public required string Currency { get; set; }
    public decimal? QuotedTotal { get; set; }
    public decimal ComputedTotal { get; set; }
    public bool TotalsMatch { get; set; }
    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public ParseJob? ParseJob { get; set; }
}
```

#### `AppDbContext.cs` [MODIFY]

- Add `DbSet<ParseMetric> ParseMetrics => Set<ParseMetric>();`
- Configure `parse_metrics`:
  - Nullable FKs: `HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.SetNull)`; same for `ParseJob`.
  - Indexes: `created_at`; `(vendor, created_at)`; `(parser_slug, created_at)`; `(user_id, created_at)`.
  - Money precisions match `ParseJob` (`computed_total`/`quoted_total` `(14,2)`, `fx_rate` `(12,4)`, `margin` `(12,2)`).
  - `source_filename` `TEXT COLLATE NOCASE`.
- Extend `StampTimestamps()` to set `CreatedAt = DateTime.UtcNow` on `Added` `ParseMetric` entries.

#### Migration `AddParseMetricsLedger` [NEW]

- EF Core migration creates `parse_metrics` and indexes.
- **Inline backfill** in `Up()` via `migrationBuilder.Sql(...)`:
  ```sql
  INSERT INTO parse_metrics (
      user_id, parse_job_id, user_username, user_name,
      vendor, parser_slug, source_filename, currency,
      quoted_total, computed_total, totals_match, fx_rate, margin, created_at
  )
  SELECT pj.user_id, pj.id, u.username, u.name,
         pj.vendor, pj.parser_slug, pj.source_filename, 'USD',
         pj.quoted_total, pj.computed_total, pj.totals_match, pj.fx_rate, pj.margin, pj.created_at
  FROM parse_jobs pj
  INNER JOIN users u ON u.id = pj.user_id;
  ```
  `'USD'` is the historical-default currency for backfilled rows (every current parser emits USD). New rows use the parser-reported `Currency`. Runs once via `MigratorHostedService` on first boot after deploy.

#### `ParseService.cs` [MODIFY]

In the success path (before `SaveChangesAsync`), construct and `Add` a `ParseMetric` alongside the `ParseJob`:

```csharp
var metric = new ParseMetric
{
    UserId = user.Id,
    UserUsername = user.Username,
    UserName = user.Name,
    Vendor = vendor,
    ParserSlug = parser.Slug,
    SourceFilename = displayFilename,
    Currency = result.Metadata.Currency,
    QuotedTotal = result.Validation.QuotedTotal,
    ComputedTotal = result.Validation.ComputedTotal,
    TotalsMatch = result.Validation.Matches,
    FxRate = fxRateRounded,
    Margin = marginRounded,
    ParseJob = job,                     // EF wires ParseJobId after insert
};
db.Add(metric);
```

Both inserts in the same `SaveChangesAsync` call → same EF transaction, atomic.

#### `RetentionService` [NO CHANGE for metrics]

`OnDelete(SetNull)` causes SQLite to null `parse_metrics.parse_job_id` automatically when the parse job is purged. Metric row stays. Verify the EF-generated migration emits `ON DELETE SET NULL` explicitly (required for SQLite).

### 1.3 API — Metrics endpoints

#### `src/BidParser.Api/Endpoints/MetricsEndpoints.cs` [NEW]

Group: `app.MapGroup("/api/metrics").RequireAuthorization(AuthPolicies.Admin);`

##### `GET /api/metrics/summary`

| Param | Type | Default | Notes |
|---|---|---|---|
| `from` | `YYYY-MM-DD` | 30 days before `to` | Inclusive, server-local TZ. |
| `to` | `YYYY-MM-DD` | today (server-local) | Inclusive. |
| `vendor` | string | — | Drilldown filter. |
| `userId` | int | — | Drilldown filter. |
| `parserSlug` | string | — | Drilldown filter. |

Response (snake_case per project convention):

```json
{
  "range": { "from": "2026-04-23", "to": "2026-05-23" },
  "kpis": {
    "total_parses": 1234,
    "active_users": 7,
    "active_vendors": 1,
    "mismatch_rate": "0.0234"
  },
  "by_user":   [{ "user_id": 3, "username": "jdoe", "name": "John Doe", "count": 412 }],
  "by_vendor": [{ "vendor": "Nutanix", "count": 1234 }],
  "by_parser": [{ "parser_slug": "nutanix_software_only_pdf", "display_name": "Software Only (PDF)", "count": 880 }],
  "time_series": [{ "date": "2026-04-23", "count": 18 }]
}
```

Implementation notes:

- All breakdowns and time-series respect any active drilldown filters → clicking "Nutanix" updates everything consistently.
- `mismatch_rate` = `count(totals_match=false) / count(*)`, 4 dp string. `"0"` on empty range.
- `by_user` groups by `user_id` when set; falls back to grouping by `user_username` when null (deleted users still aggregate correctly).
- `by_parser.display_name` resolved by joining against `IParserRegistry.Parsers` in code.
- **Time-series bucketing** is server-local; `CreatedAt` is stored UTC. Use SQLite's `date(created_at, 'localtime')` via `FromSqlInterpolated` for the time-series query only. Document the dependency on container `TZ` env var (§3.4).

##### `GET /api/metrics/export`

Same query params. `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition: attachment; filename="utilisation_<from>_<to>.xlsx"`.

- One worksheet (`Utilisation`). Header row: `Date`, `User`, `Username`, `Vendor`, `Parser`, `Source Filename`, `Currency`, `Quoted Total`, `Computed Total`, `Totals Match`, `FX Rate`, `Margin`.
- ClosedXML (already in `BidParser.Output`). Stream via `Results.File(stream, ...)` after writing to a `MemoryStream`.
- Dates written as real Excel dates, money as numbers, `Totals Match` as bool. Order by `CreatedAt DESC`.

### 1.4 Frontend — Metrics dashboard

`src/pages/admin/MetricsDashboard.tsx` + `src/components/metrics/*`:

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Header: title + DateRangeControl + [Export XLSX] button                  │
├────────────────────────────────────────────────────────────────────────────┤
│  Active-filter chips: [Vendor: Nutanix ×]  [User: John Doe ×]  …          │
├────────────────────────────────────────────────────────────────────────────┤
│  KPI strip: Total Parses | Active Users | Active Vendors | Mismatch %    │
├────────────────────────────────────────────────────────────────────────────┤
│  Time-series chart (recharts <BarChart>) — daily counts, full width      │
├──────────────────────┬──────────────────────┬──────────────────────────────┤
│  By User (rows)      │  By Vendor (rows)    │  By File Type (rows)        │
│  click → filter      │  click → filter      │  click → filter             │
└──────────────────────┴──────────────────────┴──────────────────────────────┘
```

Components: `DateRangeControl`, `FilterChips`, `KpiStrip`, `UtilisationTimeChart`, `BreakdownCard` (generic, used by user/vendor/parser cards).

- **State**: `useSearchParams` drives everything. `?from=&to=&vendor=&userId=&parserSlug=`. Refresh-safe, shareable.
- **Export button**: `<a download href={`/api/metrics/export?${searchParams}`}>` — browser-native download with active filters.
- **Date controls**: `Last 7 days`, `Last 30 days`, `This month`, `Last month`, calendar-month picker, custom range.
- **New dependency**: `recharts` (`^2.x`) in `frontend/package.json`.

---

## 2. Feature B — Failure Monitoring

### 2.1 Objective

Give admins a forensic view of parses that failed after the file was uploaded. Each entry shows who ran it, what they tried to parse, the full exception (as it appears in the docker terminal), and a link to download the original source file. Rows live only as long as the source file does — the same 90-day retention.

Scope of "failure" recorded — **all post-save failures**:

- Magic-byte mismatch (`ParseError` raised from `ValidateMagicBytesAsync`).
- Parser exception (`ParseError` from inside any parser — anchor missing, `TOTAL:` missing, etc.).
- Generic unhandled exception (anything else thrown between `SaveUploadAsync` and `SaveChangesAsync`).

Pre-save failures (415 wrong extension, 400 missing/invalid field, 413 oversized) are **not** recorded — no source file exists to download.

### 2.2 Schema — `FailedParseJob`

#### `src/BidParser.Infrastructure/Entities/FailedParseJob.cs` [NEW]

```csharp
namespace BidParser.Infrastructure.Entities;

public sealed class FailedParseJob
{
    public int Id { get; set; }

    // Nullable FK — user may be deleted later. Snapshot fields below preserve identity.
    public int? UserId { get; set; }
    public required string UserUsername { get; set; }
    public string? UserName { get; set; }

    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }

    public required string SourceFilename { get; set; }
    public required string SourcePath { get; set; }     // path under /data/files/originals/

    public required FailureCategory Category { get; set; }

    // Populated for ParseError only — null for unhandled exceptions.
    public string? Stage { get; set; }
    public string? Hint { get; set; }
    public string? Message { get; set; }

    // ex.ToString() — type, message, stack trace, inner exceptions. TEXT, uncapped.
    public required string ErrorDetail { get; set; }

    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}

public enum FailureCategory
{
    MagicByteMismatch,
    ParserError,
    UnhandledException,
}
```

#### `AppDbContext.cs` [MODIFY]

- Add `DbSet<FailedParseJob> FailedParseJobs => Set<FailedParseJob>();`
- Configure `failed_parse_jobs`:
  - Nullable user FK with `OnDelete(DeleteBehavior.SetNull)`.
  - `Category` stored as lowercase string via `HasConversion` (mirrors `User.Role` pattern).
  - Indexes: `created_at DESC`; `(user_id, created_at DESC)`; `(category, created_at DESC)` for future filtering.
  - `source_filename` `TEXT COLLATE NOCASE`.
  - `error_detail`: `HasColumnType("TEXT")`, no `HasMaxLength` (uncapped).
- Extend `StampTimestamps()` for `FailedParseJob` `Added` entries.

#### Migration `AddFailedParseJobs` [NEW]

- Creates `failed_parse_jobs` and indexes. No backfill (historical failures were discarded — no data to recover).

### 2.3 Recording the failure — `ParseService` changes

Today's `ParseService.ParseAsync` has a single `catch` that deletes both the source and the output, then re-throws. That changes to:

```csharp
await storage.SaveUploadAsync(fileStream, sourcePath, maxUploadBytes, ct);

try
{
    await ValidateMagicBytesAsync(sourcePath, parser.AcceptedMime, ct);
    var result = parser.Parse(sourcePath);

    // … writer + job + metric + SaveChangesAsync as before …
    return new ParseServiceResult(job, OutputNaming.OutputFilename(displayFilename), outputPath, result.Validation);
}
catch (Exception ex)
{
    storage.TryDelete(outputPath);       // partial/empty output, drop it
    // sourcePath KEPT — admins need it for download.

    await failureRecorder.RecordAsync(
        user, vendor, parserSlug, displayFilename, sourcePath,
        fxRateRounded, marginRounded, ex, ct);

    throw;
}
```

Key points:

- The pre-save validation throws (`ResolveParser`, `ValidateExtension`) stay outside this `try` — they happen before `SaveUploadAsync`, so the existing behaviour (no row, no file) is unchanged.
- The `try` covers magic-byte validation, the parser itself, the output writer, and the `SaveChangesAsync` for the success path. Any of those throwing produces a `FailedParseJob`.
- `failureRecorder` is a new injected service (see below) that uses a **fresh `AppDbContext`** for the failure write. The request-scoped context is mid-flow with pending change-tracker state (the user's `DefaultVendor`/`FxRate`/`Margin` updates from the success path were already mutated before the throw) — using a separate context avoids accidentally persisting those.
- The user-default mutations are deliberately *not* saved on failure: a failed parse shouldn't change the user's remembered vendor/FX/margin.

#### `src/BidParser.Infrastructure/Services/FailedParseJobRecorder.cs` [NEW]

```csharp
public sealed class FailedParseJobRecorder(IServiceScopeFactory scopeFactory)
{
    public async Task RecordAsync(
        User user, string vendor, string parserSlug,
        string displayFilename, string sourcePath,
        decimal fxRate, decimal margin,
        Exception ex, CancellationToken ct)
    {
        var category = ex switch
        {
            ParseError pe when pe.Stage == "upload" => FailureCategory.MagicByteMismatch,
            ParseError                              => FailureCategory.ParserError,
            _                                       => FailureCategory.UnhandledException,
        };

        var failure = new FailedParseJob
        {
            UserId         = user.Id,
            UserUsername   = user.Username,
            UserName       = user.Name,
            Vendor         = vendor,
            ParserSlug     = parserSlug,
            SourceFilename = displayFilename,
            SourcePath     = sourcePath,
            Category       = category,
            Stage          = (ex as ParseError)?.Stage,
            Hint           = (ex as ParseError)?.Hint,
            Message        = (ex as ParseError)?.Message,
            ErrorDetail    = ex.ToString(),
            FxRate         = fxRate,
            Margin         = margin,
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(failure);
        await db.SaveChangesAsync(ct);
    }
}
```

Register in `Program.cs`: `builder.Services.AddScoped<FailedParseJobRecorder>();` and inject it into `ParseService`'s constructor.

#### `GlobalExceptionHandler` [NO CHANGE]

The recorder writes from inside `ParseService.ParseAsync`, before the exception escapes to the handler. The handler keeps its current job: return a safe `ProblemDetails` 500 with no exception details leaked to the wire. Recording lives next to the work, not in the cross-cutting handler.

#### `ParseEndpoints.cs` [NO CHANGE]

The 422 / 500 response shapes are unchanged. The user-facing toast text doesn't reveal anything new — only what's already in `ParseError.Message`.

### 2.4 Retention — `RetentionService` [MODIFY]

Extend `CleanupOldParseJobsAsync` (or split into a `CleanupOldAsync` that calls two private methods):

```csharp
// new section, mirrors the parse_jobs cleanup
var oldFailures = await db.FailedParseJobs
    .Where(f => f.CreatedAt < cutoff)
    .Select(f => new { f.SourcePath })
    .ToListAsync(ct);

foreach (var f in oldFailures) storage.TryDelete(f.SourcePath);

await db.FailedParseJobs
    .Where(f => f.CreatedAt < cutoff)
    .ExecuteDeleteAsync(ct);
```

This satisfies the "row disappears when the file is purged" requirement — they go together in the same cleanup pass.

> Add an integration test: seed a `FailedParseJob` with `CreatedAt = now - 91 days`, run retention, assert both the row and the file are gone.

### 2.5 API — Monitoring endpoints

#### `src/BidParser.Api/Endpoints/MonitoringEndpoints.cs` [NEW]

Group: `app.MapGroup("/api/monitoring").RequireAuthorization(AuthPolicies.Admin);`

##### `GET /api/monitoring/failures`

Query params: `limit` (default 25, max 100), `offset` (default 0). Newest first.

Response:

```json
{
  "total": 142,
  "items": [
    {
      "id": 87,
      "created_at": "2026-05-22T14:03:11Z",
      "user_id": 3,
      "username": "jdoe",
      "name": "John Doe",
      "vendor": "Nutanix",
      "parser_slug": "nutanix_software_only_pdf",
      "parser_display_name": "Software Only (PDF)",
      "source_filename": "XQ-4188888.pdf",
      "category": "parser_error",
      "stage": "extract",
      "hint": "TOTAL line not found",
      "message": "TOTAL line not found",
      "error_detail": "BidParser.Domain.Models.ParseError: TOTAL line not found\n   at …",
      "source_available": true
    }
  ]
}
```

- `error_detail` is included inline in the list response. UI shows the structured `category`/`stage`/`hint`/`message` collapsed-by-default; expand reveals the full `error_detail` in a monospace block.
- `source_available` is `File.Exists(sourcePath)` — guards against orphaned rows if retention raced ahead of the row delete (should never happen, but cheap to surface).
- Pagination keeps payload bounded.
- `parser_display_name` resolved from `IParserRegistry` in code (slug → display name).

##### `GET /api/monitoring/failures/{id}/source`

Streams the source file. Same pattern as `/api/history/{id}/source` but admin-policy. Returns 404 if the file no longer exists on disk (e.g. retention race) — does not leak whether the row exists.

`Content-Disposition: attachment; filename="<original>"`. Content-type derived from extension (`application/pdf` / `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`).

### 2.6 Frontend — Monitoring page

`src/pages/admin/MonitoringPage.tsx`:

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Header: title "Failed parser runs" + pagination controls                 │
├────────────────────────────────────────────────────────────────────────────┤
│  Table (newest first):                                                    │
│   When         User         Vendor    File type        Filename     Actn  │
│   2m ago       John Doe     Nutanix   Software (PDF)   XQ-…pdf      [⬇][▾]│
│   ▾ (expanded)                                                            │
│     Category: parser_error    Stage: extract                              │
│     Hint: TOTAL line not found                                            │
│     Message: TOTAL line not found                                         │
│     ┌───────────────────────────────────────────────────────────────────┐ │
│     │ BidParser.Domain.Models.ParseError: TOTAL line not found          │ │
│     │    at BidParser.Parsing.Nutanix.SoftwareOnly… (PdfTableHelpers…)  │ │
│     │    …                                                              │ │
│     └───────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

Components (`src/components/monitoring/`):

- `FailuresTable.tsx` — rows with relative-time, user, vendor, parser display-name, filename, action cell. The action cell holds `[⬇ download]` (disabled if `source_available=false`) and `[▾ expand]`.
- `FailureRowDetail.tsx` — expanded panel with structured fields (category badge, stage, hint, message) and a `<pre>` block for `error_detail` (monospace, scrollable, copy-to-clipboard button).
- `CategoryBadge.tsx` — small coloured pill: `magic_byte_mismatch` (slate), `parser_error` (amber), `unhandled_exception` (red).

UX:

- Pagination via URL params (`?page=1`) for consistency with Metrics.
- Download is a native `<a download href={...}>` — no JS blob.
- "Copy trace" button on the expanded panel copies `error_detail` to the clipboard.
- Empty state: "No failed parses recorded. " (with the retention note as secondary text).

No new dependencies required. Tailwind + lucide icons already cover everything.

---

## 3. Operational config

### 3.1 `docker-compose.yml` [MODIFY]

Add `TZ` env var with a sensible default (overridable in `.env`):

```yaml
environment:
  - TZ=${TZ:-Australia/Sydney}
```

### 3.2 `.env.example` [MODIFY]

Add a commented `TZ` entry so operators see it alongside the other tunables:

```dotenv
# Container timezone — controls server-local time used for the Utilisation
# Dashboard's daily buckets. Defaults to Australia/Sydney if unset.
# TZ=Australia/Sydney
```

### 3.3 `docs/DEPLOYMENT.md` [MODIFY]

Add `TZ` row to the env-var table: "Controls server-local time used for the Utilisation Dashboard's daily buckets. Defaults to `Australia/Sydney`. Set to your operating timezone in `.env` if different." Note that `tzdata` is already in the `mcr.microsoft.com/dotnet/aspnet:10.0` image — no Dockerfile change.

### 3.4 `AGENTS.md` [MODIFY]

Add two short bullets under the implementation-notes block:

> `ParseMetric` is an append-only utilisation ledger (`src/BidParser.Infrastructure/Entities/ParseMetric.cs`). Snapshots user/vendor/parser/totals at parse time. Survives both 90-day retention and user deletes via nullable `OnDelete(SetNull)` FKs. Written transactionally with `ParseJob` for successful parses only. Powers `/admin/metrics`.

> `FailedParseJob` records post-save parse failures with the full exception (`ex.ToString()`) and retains the original source file for admin download. Lifecycle is tied to retention — both the row and its source file are purged together at `RETENTION_DAYS`. Written from `ParseService.ParseAsync`'s catch block via a dedicated `FailedParseJobRecorder` that uses a fresh `AppDbContext` scope. Powers `/admin/monitoring`.

---

## 4. Verification plan

### 4.1 Backend integration tests (`tests/BidParser.Api.Tests/`)

**Metrics**

- `MetricsLedgerTests`:
  - Successful `POST /api/parse` writes both `ParseJob` and `ParseMetric` in one transaction.
  - A 422 failure (e.g. magic-byte mismatch) writes **no** `ParseMetric` (it goes to `FailedParseJob` instead — see below).
  - Retention purge of `parse_jobs` nulls `ParseMetric.ParseJobId`, leaves the metric row intact.
  - `DELETE /api/users/{id}` nulls `ParseMetric.UserId`, leaves snapshot fields queryable.
- `MetricsEndpointsTests`:
  - `/summary` requires Admin (401/403 otherwise).
  - Aggregations correct under all filter combinations.
  - `mismatch_rate` correct on mixed match/mismatch fixtures; `"0"` on empty range.
  - Time-series bucketing respects `TZ` (run with `TZ=UTC` and `TZ=Australia/Sydney` over a UTC-midnight-crossing fixture).
- `MetricsExportTests`:
  - `/export` returns valid XLSX (reopen via ClosedXML, assert header + ≥1 row).
  - Filename contains date range; filters applied.
- `MetricsBackfillTests`:
  - Seed three pre-migration `ParseJob` rows; apply migration; assert three `ParseMetric` rows with matching values.

**Monitoring**

- `FailureRecordingTests`:
  - A test parser that throws `ParseError("extract", "...", "...")` → `FailedParseJob` row written with `Category=ParserError`, structured fields populated, source file retained on disk.
  - Magic-byte mismatch (upload a `.pdf` containing XLSX magic) → `Category=MagicByteMismatch`, source retained.
  - A test parser that throws `InvalidOperationException` → `Category=UnhandledException`, structured fields null, `ErrorDetail` contains the type name and stack.
  - Pre-save failures (415 wrong extension; 400 missing field) → **no** `FailedParseJob` row, no source on disk (existing behaviour).
  - On failure, the user's `DefaultVendor`/`FxRate`/`Margin` are **not** updated (the request-scoped context's pending changes are discarded).
- `MonitoringEndpointsTests`:
  - `GET /failures` requires Admin.
  - Pagination correct; newest-first ordering correct.
  - `parser_display_name` resolved from registry.
  - `source_available` flips to `false` when the underlying file is deleted manually mid-test.
  - `GET /failures/{id}/source` streams the file; 404 when file missing.
- `FailureRetentionTests`:
  - `FailedParseJob` older than `RETENTION_DAYS` is purged along with its source file in the same cleanup pass.

### 4.2 Frontend manual verification

**Shared shell**

- Admin clicks the header cog → dropdown shows Users / Metrics / Monitoring → each item navigates to `/admin/{users,metrics,monitoring}`.
- `/settings` URL still works (redirects to `/admin/users`).
- Non-admin user does not see the cog button.

**Metrics**

- Dashboard loads with last-30-days default, KPIs/breakdowns/time-series populated.
- Click a vendor row → URL gains `?vendor=Nutanix`, all sections re-scope, chip appears.
- "Export XLSX" downloads a file whose contents match the current view (open in Excel — dates native, totals numeric).
- Delete a test user → their historical parses still attributed to them in the dashboard.

**Monitoring**

- Upload a known-broken file (e.g. a renamed XLSX masquerading as `.pdf`) → expect a magic-byte failure → row appears in Monitoring with the source download working and the full trace visible on expand.
- Upload a file that triggers a parser exception (e.g. a PDF with no `TOTAL:` line) → row shows `parser_error` badge + Stage/Hint/Message + full stack.
- Trigger an unhandled exception (test-only parser that throws `InvalidOperationException`) → row shows `unhandled_exception` badge + full stack with no Stage/Hint.
- Download the source → file opens correctly.
- "Copy trace" copies the full `error_detail` to clipboard.
- Wait past `RETENTION_DAYS` (or invoke the retention service manually in test/dev) → row and file both gone.

---

## 5. Files touched (summary)

**New**

- `src/BidParser.Infrastructure/Entities/ParseMetric.cs`
- `src/BidParser.Infrastructure/Entities/FailedParseJob.cs`
- `src/BidParser.Infrastructure/Services/FailedParseJobRecorder.cs`
- `src/BidParser.Infrastructure/Migrations/<ts>_AddParseMetricsLedger.cs`
- `src/BidParser.Infrastructure/Migrations/<ts>_AddFailedParseJobs.cs`
- `src/BidParser.Api/Endpoints/MetricsEndpoints.cs`
- `src/BidParser.Api/Endpoints/MonitoringEndpoints.cs`
- `frontend/src/components/AdminMenu.tsx`
- `frontend/src/pages/admin/UsersPage.tsx` (moved from `SettingsPage.tsx`)
- `frontend/src/pages/admin/MetricsDashboard.tsx`
- `frontend/src/pages/admin/MonitoringPage.tsx`
- `frontend/src/components/metrics/{DateRangeControl,FilterChips,KpiStrip,UtilisationTimeChart,BreakdownCard}.tsx`
- `frontend/src/components/monitoring/{FailuresTable,FailureRowDetail,CategoryBadge}.tsx`
- `tests/BidParser.Api.Tests/MetricsLedgerTests.cs`
- `tests/BidParser.Api.Tests/MetricsEndpointsTests.cs`
- `tests/BidParser.Api.Tests/MetricsExportTests.cs`
- `tests/BidParser.Api.Tests/MetricsBackfillTests.cs`
- `tests/BidParser.Api.Tests/FailureRecordingTests.cs`
- `tests/BidParser.Api.Tests/MonitoringEndpointsTests.cs`
- `tests/BidParser.Api.Tests/FailureRetentionTests.cs`

**Modified**

- `src/BidParser.Infrastructure/Persistence/AppDbContext.cs` — two new DbSets, two new `OnModelCreating` blocks, two new branches in `StampTimestamps`.
- `src/BidParser.Infrastructure/Services/ParseService.cs` — write `ParseMetric` on success; reshape catch block to retain source + invoke recorder on failure.
- `src/BidParser.Infrastructure/Services/RetentionService.cs` — also purge `FailedParseJob` rows + their source files.
- `src/BidParser.Api/Program.cs` — `MapMetricsEndpoints()`, `MapMonitoringEndpoints()`, register `FailedParseJobRecorder`.
- `frontend/src/App.tsx` — `/admin/users`, `/admin/metrics`, `/admin/monitoring` routes + `/settings` redirect + admin guard reuse.
- `frontend/src/components/AppHeader.tsx` — swap the cog button for `<AdminMenu />`.
- `frontend/package.json` — add `recharts`.
- `docker-compose.yml` — `TZ` env var.
- `.env.example` — commented `TZ` entry.
- `docs/DEPLOYMENT.md` — document `TZ`.
- `AGENTS.md` — two-bullet note on `ParseMetric` and `FailedParseJob`.

**Unchanged** (deliberately)

- `User` entity — no `IsActive` flag; snapshot fields cover historical attribution.
- `GlobalExceptionHandler` — stays a pure HTTP-shaping concern; failure recording lives next to the work.
- `ParseEndpoints` — 422/500 response shapes unchanged, no new exception types leak to the wire.
- Auth/CSRF/rate-limit machinery.

---

## 6. Open follow-ups (out of scope for this PR)

- "Retry" action on the Monitoring screen (re-submit the stored source through the same parser without the user re-uploading).
- Failure-rate KPI on the Metrics dashboard (would need either a join on `failed_parse_jobs` in the summary query or moving failure counts into `ParseMetric`).
- Filtering on the Monitoring list (date range, vendor, user, category). Trivial to add later — schema already has the right indexes.
- Per-user export on Metrics (currently admin-only export of all users).
- Multi-vendor reality check: once a second vendor ships, AGENTS.md "Nutanix only" guardrail should be lifted and the by-vendor breakdown becomes genuinely useful.

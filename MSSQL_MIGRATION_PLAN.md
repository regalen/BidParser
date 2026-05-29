# Migration Plan: SQLite → SQL Server 2025 Enterprise Developer Edition

> **Deliverable note:** On approval, the first execution step writes this same content to
> **`MSSQL_MIGRATION_PLAN.md`** in the repo root (the file the original request asked for),
> then implementation proceeds.

## Context

BidParser is an internal ASP.NET Core 10 + React app at `0.y.z` internal testing.
Persistence is **EF Core 10 on SQLite** — a single file (`/data/db.sqlite`) in the app
container's `/data` volume, wired in `Program.cs` via `UseSqlite(appOptions.ToSqliteConnectionString())`.
The goal is to move persistence to **Microsoft SQL Server 2025 Enterprise Developer Edition**
running as its own container in the existing `docker-compose` stack, self-hosted on-prem.

**Confirmed decisions (from the user):**
1. **Fresh start, no data port** — empty SQL Server DB; admin re-bootstraps via
   `ADMIN_USERNAME`/`ADMIN_PASSWORD`; history/metrics start empty. **No ETL.**
2. **Reset migrations** — delete the 8 existing SQLite migrations + snapshot, regenerate a
   single `InitialCreate` against the SqlServer provider.
3. **Tests on real SQL Server via Testcontainers** (`Testcontainers.MsSql`).

Why non-trivial (all verified against the code): SQLite specifics are threaded through the
schema (`HasColumnType("TEXT COLLATE NOCASE")` on 4 columns, `HasColumnType("TEXT")` on
`error_detail`), the connection wiring + a WAL `PRAGMA` interceptor, one raw-SQL time-series
query using `date(created_at, 'localtime')`, the `AppOptions.DataDir` derivation (it parses
the SQLite connection string to locate the data dir where DataProtection keys live), and a
test suite hard-coupled to SQLite (`Microsoft.Data.Sqlite`, `sqlite_master`, `PRAGMA`,
`EXPLAIN QUERY PLAN`, `datetime('now')` seeds).

### Shape of the change

```mermaid
flowchart TD
    subgraph cfg["Config / infra (Phase 1)"]
        DC[docker-compose.yml<br/>+ mssql service + healthcheck] 
        DF[Dockerfile<br/>drop DATABASE_URL env]
        ENV[.env.example<br/>+ MSSQL vars]
    end
    subgraph deps["Deps + wiring (Phase 2)"]
        PKG[Directory.Packages.props<br/>Sqlite→SqlServer +Testcontainers.MsSql]
        AO[AppOptions.cs<br/>ConnectionString; DataDir from UploadDir]
        PROG[Program.cs<br/>UseSqlServer + EnableRetryOnFailure;<br/>drop interceptor]
        INT[delete SqlitePragmaConnectionInterceptor]
    end
    subgraph schema["Schema + SQL (Phase 3)"]
        CTX[AppDbContext.cs<br/>UseCollation; drop TEXT column types]
        MIG[delete 8 migrations + snapshot<br/>regen InitialCreate]
        MET[MetricsEndpoints.cs<br/>raw date(localtime) → C# TimeZoneInfo]
        HIST[HistoryEndpoints.cs<br/>escape '[' in LIKE]
    end
    subgraph tests["Tests (Phase 5)"]
        TF[MsSql container fixture +<br/>TestInfrastructure DB_CONNECTION_STRING]
        MT[rewrite MigrationTests]
        DEL[delete MetricsBackfillTests]
        INLINE[HealthTests/AuthFlowTests<br/>drop inline sqlite URLs]
    end
    PKG --> AO --> PROG --> CTX --> MIG --> MET --> HIST
    PROG -.-> INT
    DC --> PROG
    CTX --> TF --> MT
    MIG --> DEL
    TF --> INLINE
```

---

## 1. Current SQLite schema (source of truth: `AppDbContext.OnModelCreating`)

4 tables, all PK `id` autoincrement. Decimals declared via `HasPrecision(p,s)` (provider-agnostic,
SQLite emits `TEXT`). `DateTime` → `TEXT`, `bool` → `INTEGER`. Provider-specific bits live only in:
- `HasColumnType("TEXT COLLATE NOCASE")` — `users.username`, and `source_filename` on
  `parse_jobs`/`parse_metrics`/`failed_parse_jobs`.
- `HasColumnType("TEXT")` — `failed_parse_jobs.error_detail` (unbounded).

Indexes (preserved by the regenerated migration via the existing fluent config): unique
`ix_users_username`; `ix_parse_jobs_user_id` + `ix_parse_jobs_user_id_created_at` (created_at DESC);
4 `parse_metrics` indexes; 3 `failed_parse_jobs` indexes (created_at DESC). FK cascade behaviors:
`parse_jobs.user_id` CASCADE; all others SET NULL.

---

## 2. SQLite-specific constructs & SQL Server target (verified)

| # | Construct | Where | Action |
|---|---|---|---|
| 1 | autoincrement PK / `bool`→INTEGER / `decimal`→TEXT / `DateTime`→TEXT | all tables (migrations only) | EF emits `int IDENTITY`, `bit`, `decimal(p,s)`, `datetime2` automatically once the provider swaps. **No fluent change** — handled by regenerating the migration. |
| 2 | `HasColumnType("TEXT COLLATE NOCASE")` | 4 string columns | Replace with `.UseCollation("SQL_Latin1_General_CP1_CI_AS")` and **drop** the `HasColumnType`; `HasMaxLength(n)` then drives `nvarchar(n)`. **Fluent change in `AppDbContext`.** |
| 3 | `HasColumnType("TEXT")` on `error_detail` | `AppDbContext.cs:147` | **Drop** it → maps to `nvarchar(max)`. Fluent change. |
| 4 | `PRAGMA journal_mode=WAL; synchronous=NORMAL` | `SqlitePragmaConnectionInterceptor` + `Program.cs:38,42` | N/A to SQL Server. **Delete** interceptor + both registration lines. |
| 5 | Raw SQL `date(created_at, 'localtime')` time-series | `MetricsEndpoints.cs:91-104` | SQL Server has no `date()`/`'localtime'`. **Rewrite** — drop the `db.Database.SqlQuery<TimeSeriesRow>` block + `TimeSeriesRow` record; fetch `CreatedAt` from the already-built `query` (e.g. `await query.Select(m => m.CreatedAt).ToListAsync()`) and bucket by local date in C# using `TimeZoneInfo.Local` (the container's `TZ`). Identical under Testcontainers + prod; volume is tiny at internal scale. |
| 6 | `EF.Functions.Like(..., "\\")` escaping | `HistoryEndpoints.cs:45-48,163` | LIKE works on SqlServer, **but SQL Server also treats `[` as a wildcard.** Add `.Replace("[", "\\[")` to `EscapeLikePattern` (before/after the existing replaces — order doesn't matter since `[` isn't produced by them). |
| 7 | upsert / `INSERT OR REPLACE` / `STRFTIME` / JSON fns | — | **None exist.** App uses EF change tracking + single atomic `SaveChangesAsync`. Nothing to change. |
| 8 | raw `CREATE TABLE … AUTOINCREMENT` rebuild; ANSI backfill `INSERT…SELECT` | migrations `…SourceFilenameNoCase`, `…AddParseMetricsLedger` | Discarded by the migration reset; fresh-start needs no backfill. |

`RetentionService` uses `ExecuteDeleteAsync` (provider-agnostic) — no change. `HistoryTests`
ages a row with `ExecuteSqlRawAsync("UPDATE parse_jobs SET created_at = {0} …")` passing
`"yyyy-MM-dd HH:mm:ss.fffffff"` — SQL Server implicitly converts the string param to `datetime2`;
keep and verify.

---

## 3. Dependency changes

`Directory.Packages.props`:
- **Remove** `Microsoft.EntityFrameworkCore.Sqlite` (10.0.0).
- **Add** `Microsoft.EntityFrameworkCore.SqlServer` `10.0.0`.
- **Add** `Testcontainers.MsSql` (latest 4.x — pin a recent version; see §7 healthcheck note).

Project refs:
- `src/BidParser.Infrastructure/BidParser.Infrastructure.csproj`: Sqlite → SqlServer.
- `src/BidParser.Api/BidParser.Api.csproj`: Sqlite → SqlServer (keep `EntityFrameworkCore.Design`).
- `tests/BidParser.Api.Tests/BidParser.Api.Tests.csproj`: drop `Microsoft.EntityFrameworkCore.Sqlite`;
  add `Testcontainers.MsSql`. (`Microsoft.Data.Sqlite` was pulled transitively; the `using
  Microsoft.Data.Sqlite` in `MigrationTests.cs` must be removed when that test is rewritten.)

`Microsoft.Data.Sqlite` is also used directly in `AppOptions.cs:22,78` (connection-string-builder)
— removed in the §5 refactor.

---

## 4. `Program.cs` + `AppOptions` refactor

**`AppOptions.cs`** — currently `DataDir` (line 22) parses the SQLite `DataSource` to find where
DataProtection keys live, and `EnsureDirectories()` (73-84) creates that DB dir. With no SQLite
file, decouple:
- Replace `DatabaseUrl` / `ToSqliteConnectionString()` / `DefaultDatabaseUrl()` with a
  `ConnectionString` property read from env **`DB_CONNECTION_STRING`** (a full ADO.NET SqlServer
  connection string). Keep a local-dev default pointing at `localhost` if desired, or leave empty.
- `DataDir` ⇒ `Path.GetDirectoryName(Path.GetFullPath(UploadDir)) ?? DefaultDataDir()`
  (so `UPLOAD_DIR=/data/files` ⇒ `/data`, where `dp-keys` lives). `DataProtectionKeysDir` unchanged.
- `EnsureDirectories()` ⇒ create only `UploadDir` + `DataProtectionKeysDir`; drop the DB-dir block.
- Remove both `Microsoft.Data.Sqlite` usages.

**`Program.cs`** (lines 38-43):
```csharp
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(appOptions.ConnectionString,
        sql => sql.EnableRetryOnFailure()));
```
Delete `AddSingleton<SqlitePragmaConnectionInterceptor>()` (line 38) and the `AddInterceptors`
call (line 42). Then delete `SqlitePragmaConnectionInterceptor.cs`. `MigratorHostedService`
(`MigrateAsync` on startup) is unchanged — it creates the DB and applies `InitialCreate`;
`EnableRetryOnFailure` covers transient first-boot connection failures.

---

## 5. Schema fluent changes + migration reset (`AppDbContext.cs`)

1. The 4 NOCASE columns (lines 37, 72, 101, 132): remove `.HasColumnType("TEXT COLLATE NOCASE")`,
   add `.UseCollation("SQL_Latin1_General_CP1_CI_AS")`. Keep `HasMaxLength` + `IsRequired`.
2. `error_detail` (line 147): remove `.HasColumnType("TEXT")`. Keep `IsRequired`.
3. Delete all files in `src/BidParser.Infrastructure/Migrations/` (8 `.cs`/`.Designer.cs` +
   `AppDbContextModelSnapshot.cs`).
4. Regenerate:
   `dotnet ef migrations add InitialCreate -p src/BidParser.Infrastructure -s src/BidParser.Api`
   (requires a design-time `DB_CONNECTION_STRING` — any reachable SqlServer, or just a syntactically
   valid string since `migrations add` doesn't connect). Review emitted SQL for `IDENTITY`, `bit`,
   `decimal(p,s)`, `datetime2`, descending indexes, unique `ix_users_username`, CASCADE/SET NULL FKs,
   `nvarchar(max)` on `error_detail`, and CI collation on the 4 string columns.

---

## 6. Docker Compose architecture

Two services on the default compose network; app gated on a healthy DB. App **keeps the `/data`
volume** — now holding only uploaded files + DataProtection keys (the DB lives in
`bidparser-mssql-data`).

```yaml
services:
  mssql:
    image: mcr.microsoft.com/mssql/server:2025-latest
    container_name: bidparser-mssql
    environment:
      - ACCEPT_EULA=Y
      - MSSQL_PID=EnterpriseDeveloper
      - MSSQL_SA_PASSWORD=${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}
    volumes:
      - bidparser-mssql-data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' -b -o /dev/null || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s
    restart: unless-stopped

  bidparser:
    build: { context: . }
    image: ghcr.io/regalen/bidparser:latest
    container_name: bidparser
    depends_on:
      mssql: { condition: service_healthy }
    ports: ["3447:3447"]
    volumes:
      - ${DATA_DIR:-bidparser-data}:/data
    environment:
      - TZ=${TZ:-Australia/Sydney}
      - SESSION_SECRET=${SESSION_SECRET:?Set SESSION_SECRET in .env}
      - SESSION_LIFETIME_HOURS=${SESSION_LIFETIME_HOURS:-12}
      - ADMIN_USERNAME=${ADMIN_USERNAME:-admin}
      - ADMIN_PASSWORD=${ADMIN_PASSWORD:-changeme}
      - RETENTION_DAYS=${RETENTION_DAYS:-90}
      - RATE_LIMIT_AUTH_PER_MIN=${RATE_LIMIT_AUTH_PER_MIN:-5}
      - MAX_UPLOAD_MB=${MAX_UPLOAD_MB:-10}
      - FORWARDED_ALLOW_IPS=${FORWARDED_ALLOW_IPS:?Set FORWARDED_ALLOW_IPS in .env}
      - DB_CONNECTION_STRING=Server=mssql,1433;Database=${MSSQL_DB:-bidparser};User Id=sa;Password=${MSSQL_SA_PASSWORD:?};Encrypt=True;TrustServerCertificate=True
      - UPLOAD_DIR=/data/files
    restart: unless-stopped

volumes:
  bidparser-data:
  bidparser-mssql-data:
```

**`Dockerfile`**: remove `ENV DATABASE_URL=sqlite:////data/db.sqlite` (line 38); keep `VOLUME /data`
and `ENV UPLOAD_DIR=/data/files`. (`DB_CONNECTION_STRING` is supplied by compose, not baked in.)

**`.env.example`**: add `MSSQL_SA_PASSWORD=` (strong, ≥8 chars w/ complexity — SQL Server rejects
weak SA passwords and won't start otherwise) and `MSSQL_DB=bidparser`. Remove the SQLite-path
implication. `TZ` doc comment already covers the time-series buckets.

> **Healthcheck caveat (verified via Testcontainers issue #1220):** newer mssql images moved
> tools to `/opt/mssql-tools18/` and some variants no longer bundle `sqlcmd` at all. The
> healthcheck above is best-effort; **the real startup gate is the app's `EnableRetryOnFailure`**,
> which retries the connection during first boot regardless. At deploy, confirm the path exists in
> the 2025 image (`docker exec bidparser-mssql ls /opt/mssql-tools18/bin`); if absent, drop the
> healthcheck `test` to a TCP probe or remove it and lean on retry. `Encrypt=True;
> TrustServerCertificate=True` trusts the self-signed dev cert — a real cert is the prod-hardening
> follow-up.

---

## 7. Tests (all currently SQLite-coupled)

Tests use a **static async factory** `CustomTestFixture.CreateAsync()` (not xUnit fixture
injection), so the shared SQL Server container must be a process-wide singleton:

1. Add a `MsSqlTestContainer` helper exposing a `static Lazy<Task<MsSqlContainer>>` that builds and
   starts one `mcr.microsoft.com/mssql/server:2025-latest` on first use. Testcontainers' resource
   reaper (Ryuk) disposes it on process exit, so no explicit teardown is needed under xunit 2.9.3
   (which lacks assembly fixtures). `CreateAsync` awaits the container, then sets
   **`DB_CONNECTION_STRING`** to the container's connection string with a **unique
   `Database=test_{guid}`** per fixture (EF `MigrateAsync` creates the DB on startup → isolation).
   Replace the `DATABASE_URL` sqlite entry in `TestInfrastructure.cs:37`.
2. `HealthTests.cs:26,50` and `AuthFlowTests.cs:246` build their own env dicts with inline
   `sqlite:///` URLs — route them through the shared container connection string (extract a small
   helper, or have them call into the same `MsSqlTestContainer`).
3. **Rewrite `MigrationTests.cs`:** drop `using Microsoft.Data.Sqlite`, the `sqlite_master`/`PRAGMA
   journal_mode`/`EXPLAIN QUERY PLAN` reads, and the WAL assertion. Change the applied-migrations
   assertion (lines 47-55) from the 8 IDs to the single `…_InitialCreate`. Keep the admin-bootstrap
   assertions. Optionally assert CI collation via `INFORMATION_SCHEMA.COLUMNS.COLLATION_NAME` or
   index presence via `sys.indexes` using the EF connection.
4. **Delete `MetricsBackfillTests.cs`** — its premise is the discarded backfill migration +
   `datetime('now')` seeds.
5. Verify `HistoryTests.cs:259` raw `UPDATE … created_at = {0}` still runs on SqlServer (string→
   `datetime2` implicit conversion; adjust the format only if it errors).

`.github/workflows/build.yml`: `ubuntu-latest` ships a Docker daemon, so Testcontainers works;
confirm no extra service block is needed and that the first mssql image pull is tolerated (slow).

---

## 8. Execution order

1. **Phase 1 — infra:** `docker-compose.yml` (mssql service, volume, healthcheck, depends_on);
   `Dockerfile` (drop DB env); `.env.example` (MSSQL vars).
2. **Phase 2 — deps + wiring:** `Directory.Packages.props`; 3 csprojs; `AppOptions` refactor (§4);
   `Program.cs` `UseSqlServer` + drop interceptor registration; delete
   `SqlitePragmaConnectionInterceptor.cs`.
3. **Phase 3 — schema + SQL:** `AppDbContext` collation/column-type edits; delete migrations +
   snapshot; regenerate `InitialCreate`; `MetricsEndpoints` time-series rewrite; `HistoryEndpoints`
   `[` escaping.
4. **Phase 4 — data migration:** *Not applicable* (fresh start — intentionally skipped).
5. **Phase 5 — tests:** container fixture + `TestInfrastructure` swap; inline-URL tests; rewrite
   `MigrationTests`; delete `MetricsBackfillTests`; verify `HistoryTests`.
6. **Docs (post-migration, low risk):** update `CLAUDE.md` "EF Core + SQLite", `docs/project_memory.md`,
   `docs/DEPLOYMENT.md` references from `DATABASE_URL=sqlite:///…` to the new SqlServer wiring.

---

## 9. Risks & ambiguities

- **mssql image / sqlcmd healthcheck** (verified, Testcontainers #1220): newest images relocated/
  dropped `sqlcmd`. Mitigation: `EnableRetryOnFailure` is the true gate; confirm the tools path at
  deploy and downgrade the healthcheck if needed.
- **Testcontainers + CI Docker:** whole suite now needs a Docker daemon (local + CI). First image
  pull is slow; cache it. Pin a recent `Testcontainers.MsSql` 4.x that handles the new image's wait
  strategy.
- **Time-series timezone fidelity:** C# `TimeZoneInfo.Local` bucketing must use the same `TZ` the
  container runs with; verify daily buckets match around DST boundaries (the `ExportAsync` path
  already uses `ToLocalTime()`, so this is consistent with existing behavior).
- **Collation semantics:** `SQL_Latin1_General_CP1_CI_AS` is case-insensitive (intended for
  username uniqueness + filename search) but accent-sensitive and broader than SQLite ASCII-only
  `NOCASE`; confirm no test asserts ASCII-only edge behavior.
- **SA password policy:** weak `MSSQL_SA_PASSWORD` → container won't start, healthcheck never
  passes. Called out in `.env.example`.
- **`EnableRetryOnFailure` + transactions:** safe — the only multi-write path (`ParseService`) is a
  single atomic `SaveChangesAsync`; `RetentionService` uses standalone `ExecuteDeleteAsync`. Any
  future explicit `BeginTransaction` must be wrapped in the execution strategy.

---

## Verification (end-to-end)

1. `dotnet build BidParser.sln` clean.
2. `dotnet test BidParser.sln` green against the Testcontainers SQL Server (expect 176 − the
   deleted `MetricsBackfillTests` cases, ± any rewritten in `MigrationTests`). State the final count
   in the PR.
3. `cp .env.example .env`; set `SESSION_SECRET`, `MSSQL_SA_PASSWORD`, `FORWARDED_ALLOW_IPS`;
   `docker compose up -d`. Confirm `mssql` reaches healthy, `bidparser` starts after it, EF
   migration applied (logs), admin bootstrapped.
4. Log in; upload a sample quote; confirm `*_parsed.xlsx` downloads and rows appear in History and
   Metrics. Confirm the time-series chart renders correct daily buckets. Test history search with a
   filename containing `[` and mixed case (verifies item 6 + CI collation).
5. `sqlcmd`/Azure Data Studio: confirm native types — `IDENTITY`, `bit`, `decimal(p,s)`,
   `datetime2`, `nvarchar(max)` on `error_detail`, unique username index, CI collation on
   `username`/`source_filename`.
6. Open a PR (do not tag a release; doc updates included).
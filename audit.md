# BidParser — Audit & Remediation Plan

## Context

The BidParser backend was re-platformed from Python/FastAPI to **ASP.NET Core 10** on `main` (commit `9add69f`). A read-only audit across six vectors (async/concurrency, DI lifecycles, type safety, EF Core/data access, error handling/middleware, API contracts) surfaced **4 critical, 10 warning, and 5 optimization-level findings**. This document is the remediation roadmap: phased steps, file paths, acceptance criteria, and verification.

The plan is structured so **Phase 1 is the production-blocking work** (must ship before broader rollout), **Phase 2 hardens correctness and observability**, and **Phase 3 is idiomatic-C# hygiene** that can ride a follow-up PR.

---

## Findings recap

| # | Severity | Vector | File | Finding |
|---|----------|--------|------|---------|
| 1 | 🚨 CRITICAL | Middleware | `Program.cs:70-74` | `ForwardLimit = null`, `KnownProxies` unconfigured → `X-Forwarded-For` spoof |
| 2 | 🚨 CRITICAL | Error handling | `Program.cs` | No `IExceptionHandler` / `AddProblemDetails`; unhandled exceptions silent |
| 3 | 🚨 CRITICAL | Error handling | `ParseEndpoints.cs:110-115` | `catch (Exception ex)` returns `ex.Message` to client |
| 4 | 🚨 CRITICAL | API contracts | `Program.cs` / `ParseEndpoints.cs` | `/api/parse` has no rate limit |
| 5 | ⚠️ WARNING | Async | Auth/Users/Me endpoints, `SessionCookieAuthHandler` | Missing `CancellationToken` parameters + propagation |
| 6 | ⚠️ WARNING | Data access | `HistoryEndpoints.cs:45-46` | `.ToLower().Contains(lower)` defeats index, may eval client-side |
| 7 | ⚠️ WARNING | Data access | `AppDbContext.cs:59` | Missing composite index `(user_id, created_at desc)` |
| 8 | ⚠️ WARNING | Type safety | `ForeignUpliftWriter.cs:116` | `Convert.ToDouble(decimal)` for prices — precision loss |
| 9 | ⚠️ WARNING | Resource | `WorkbookReader.cs:8-12` | `XLWorkbook` never disposed |
| 10 | ⚠️ WARNING | Type safety | `User.cs:9` | `Role` is a loose `string`; no compile-time guarantee |
| 11 | ⚠️ WARNING | Type safety | `Entities/User.cs:14-15`, `ParseJob.cs:17` | Dual source of truth between `= DateTime.UtcNow` and `StampTimestamps()` |
| 12 | ⚠️ WARNING | API contracts | `ParseEndpoints.cs:44-78` | File upload trusts extension only — no magic-byte sniff |
| 13 | ⚠️ WARNING | Logging | All endpoints | Zero structured logs on auth/parse/admin events |
| 14 | ⚠️ WARNING | Middleware | `Program.cs` | No security headers (HSTS, X-Content-Type-Options, X-Frame-Options) |
| 15 | 💡 OPTIMIZATION | Data access | `RetentionService.cs:21-29` | Load-then-delete-each instead of `ExecuteDeleteAsync` |
| 16 | 💡 OPTIMIZATION | DI | `Program.cs:34` | No `AddDbContextPool` |
| 17 | 💡 OPTIMIZATION | Type safety | Multiple | Anonymous-object responses; should be typed records |
| 18 | 💡 OPTIMIZATION | API contracts | Multiple | Magic strings (vendor/slug/CRM template) duplicated |
| 19 | 💡 OPTIMIZATION | Style | Multiple | Primary constructors, collection expressions opportunities |

---

## Remediation roadmap

## Implementation status

- Phase 1 complete in commit `adb8be5` (`Harden production API blockers`).
  - Verified with `dotnet test BidParser.sln`: 57/57 passing.
  - Manual frontend happy-path verification remains outstanding.
- Phase 2 started.
  - Step 2.1 complete: `ForeignUpliftWriter` writes `decimal` values directly; template writer tests passed against existing golden workbooks, so no fixture regeneration was needed.
  - Step 2.2 complete: entity timestamp initializers removed; `AppDbContext.StampTimestamps()` is now the single source.
  - Verified with `dotnet test BidParser.sln`: 57/57 passing after Steps 2.1 and 2.2.

### Phase 1 — Production blockers · ~1 day · MUST ship before broader rollout

Goal: make the deployed service safe and operable. No new functionality; each step has a focused test.

**Step 1.1 — Lock down `ForwardedHeaders`** (finding #1)
- File: `src/BidParser.Api/Program.cs:70-74`
- Wire `AppOptions.ForwardedAllowIps` (already parsed, never used) into a new `ForwardedHeadersOptions` builder:

  ```csharp
  var fwdOpts = new ForwardedHeadersOptions
  {
      ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
      ForwardLimit = 1
  };
  fwdOpts.KnownProxies.Clear();
  fwdOpts.KnownNetworks.Clear();
  foreach (var ip in appOptions.ForwardedAllowIps)
  {
      if (IPAddress.TryParse(ip, out var parsed)) fwdOpts.KnownProxies.Add(parsed);
  }
  app.UseForwardedHeaders(fwdOpts);
  ```

- Update `AppOptions.FromConfiguration` to reject the default `"*"` and require an explicit allowlist in production (Development can keep loopback).
- Acceptance: integration test that sets `X-Forwarded-For: 1.2.3.4` from an untrusted source observes `RemoteIpAddress` unchanged; from a `KnownProxies` source observes the override.

**Step 1.2 — Global exception handler + ProblemDetails** (finding #2)
- New file: `src/BidParser.Api/Hosting/GlobalExceptionHandler.cs` implementing `IExceptionHandler`. Log `ex` at Error with `{Method} {Path}` context; respond `500` with a `ProblemDetails` body — never include `ex.Message` or stack trace.
- `Program.cs`: add `AddProblemDetails()`, `AddExceptionHandler<GlobalExceptionHandler>()` before `Build()`; add `app.UseExceptionHandler()` first in the pipeline (after `UseForwardedHeaders`).
- Acceptance: integration test mounts a dummy endpoint that throws `InvalidOperationException` — receives 500 ProblemDetails, no leak, and the test xUnit log captures the structured Error line.

**Step 1.3 — Remove `ParseEndpoints` catch-all** (finding #3)
- File: `src/BidParser.Api/Endpoints/ParseEndpoints.cs:110-115`
- Delete the generic `catch (Exception ex)`. Keep `catch (ParseError ex)` and add a structured `LogWarning` inside it with `{Filename}`, `{Slug}`, `{Stage}`.
- Acceptance: forcing a non-`ParseError` exception inside `ParseService.ParseAsync` (e.g. by injecting a `Mock<FileStorage>` that throws `IOException`) returns 500 (caught by 1.2), not 422. Existing parse-error tests still pass.

**Step 1.4 — Rate-limit `/api/parse`** (finding #4)
- File: `src/BidParser.Api/Program.cs` + `src/BidParser.Api/Endpoints/ParseEndpoints.cs`
- Add `builder.Services.AddRateLimiter(...)` with a `"parse"` policy: token-bucket, 10 tokens, 5 tokens/min replenish, partition key = current user id.
- Add `app.UseRateLimiter()` after auth/authz.
- Wire `.RequireRateLimiting("parse")` onto the `/api/parse` endpoint.
- Acceptance: integration test that posts 12 parses in quick succession sees 11th return `429` with `Retry-After`.

**Phase 1 verification gate**
- `dotnet test BidParser.sln` — all 54 existing + 3 new tests green.
- Manual: `dotnet run --project src/BidParser.Api` and run full happy path through the frontend; confirm 500-handler engages on an injected throw; confirm `X-Forwarded-For` from an untrusted source is ignored.

---

### Phase 2 — Correctness & observability · ~2-3 days

Goal: precision, indexing, observability, and consistent cancellation. Bundle as a single PR after Phase 1 ships, or split per-step if reviewers prefer.

**Step 2.1 — Fix decimal→double in Excel output** (finding #8)
- File: `src/BidParser.Output/ForeignUpliftWriter.cs:114-117`
- Replace `Convert.ToDouble(value)` with direct `cell.Value = value` (ClosedXML accepts `decimal` via implicit conversion). Drop the int-vs-double branching.
- Regenerate `samples/outputs/*_parsed.xlsx` and **byte-compare against current golden files**. If diff, hand-validate the new values against `docs/output_mapping.md` and commit new goldens.
- Acceptance: template-writer tests pass against (potentially refreshed) goldens; no test value depends on binary-float artefacts.

**Step 2.2 — Drop `DateTime.UtcNow` initializers** (finding #11)
- Files: `src/BidParser.Infrastructure/Entities/User.cs:14-15`, `ParseJob.cs:17`
- Remove `= DateTime.UtcNow` — `AppDbContext.StampTimestamps()` is the single source.
- Acceptance: existing tests pass; new DB rows still get `created_at` populated.

**Step 2.3 — Composite index for history pagination** (finding #7)
- File: `src/BidParser.Infrastructure/Persistence/AppDbContext.cs:59`
- Add `entity.HasIndex(j => new { j.UserId, j.CreatedAt }).IsDescending(false, true)`.
- Generate EF migration: `dotnet ef migrations add HistoryCompositeIndex --project src/BidParser.Infrastructure --startup-project src/BidParser.Api`.
- Acceptance: `EXPLAIN QUERY PLAN SELECT ... FROM parse_jobs WHERE user_id=? ORDER BY created_at DESC LIMIT ?` uses the new index. Migration applies cleanly to a fresh DB and to `data/db.sqlite`.

**Step 2.4 — Case-insensitive filename search done right** (finding #6)
- Files: `src/BidParser.Api/Endpoints/HistoryEndpoints.cs:43-47` + `AppDbContext.cs`
- Either declare `source_filename` as `HasColumnType("TEXT COLLATE NOCASE")` (mirroring `username`) and drop both `.ToLower()` calls, **or** switch to `EF.Functions.Like` with `%{escapedNeedle}%`. The collation approach is cleaner — pick that.
- Generate migration if column type changes.
- Acceptance: search `xq-4076` and `XQ-4076` return identical row sets; `EXPLAIN QUERY PLAN` shows no `lower()` wrap.

**Step 2.5 — File magic-byte sniffing** (finding #12)
- File: `src/BidParser.Infrastructure/Services/ParseService.cs` (helper inside; or new `Parsing.MagicBytes` utility under `BidParser.Parsing`).
- After upload save, peek first 4 bytes. Allowed: PDF (`25 50 44 46` = `%PDF`), XLSX (`50 4B 03 04` = `PK\x03\x04`). On mismatch throw `ParseError` with stage `"upload"` and hint `"Unsupported file format."`.
- Acceptance: upload a `.zip` renamed to `.pdf` and observe `422` with stage=`upload`. Existing valid-file tests pass.

**Step 2.6 — Structured logging across endpoints** (finding #13)
- Files: `AuthEndpoints.cs`, `MeEndpoints.cs`, `UsersEndpoints.cs`, `ParseEndpoints.cs`, `HistoryEndpoints.cs`
- Inject `ILogger<EndpointType>` (use a marker class per file, or `ILoggerFactory`).
- Emit, with named placeholders only:
  - `LogInformation("Login success {Username}")` / `LogWarning("Login failed {Username}")` (no password material)
  - `LogInformation("Parse {Slug} ok user={UserId} computed={Computed:F2} quoted={Quoted:F2} match={Match} ms={Ms}", …)`
  - `LogInformation("Admin {Action} user {TargetUserId} by {AdminUserId}")` on user create/patch/delete
  - `LogWarning("Authorization denied: password_change_required user={UserId}")` (in the auth handler)
- Acceptance: a manual smoke run prints the expected lines; nothing sensitive (password, hash, cookie payload, file body) appears in any log.

**Step 2.7 — CancellationToken propagation** (finding #5)
- Files: `AuthEndpoints.cs`, `UsersEndpoints.cs`, `MeEndpoints.cs`, `SessionCookieAuthHandler.cs`
- Add `CancellationToken ct` parameter to every async endpoint handler that doesn't have one. Pass `ct` to every EF `*Async` call. In `SessionCookieAuthHandler.HandleAuthenticateAsync`, use `Context.RequestAborted`.
- Acceptance: lint check — `grep -n "Async()" src/BidParser.Api` returns no zero-arg async DB calls. Existing tests pass.

**Step 2.8 — Security headers middleware** (finding #14)
- New file: `src/BidParser.Api/Middleware/SecurityHeadersMiddleware.cs` (or inline `app.Use(...)` for brevity).
- Set `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`. Set `Strict-Transport-Security` only when `Request.IsHttps` (which respects the forwarded-proto from Step 1.1).
- Wire before `UseStaticFiles` so it covers both API and SPA responses.
- Acceptance: integration test asserts all four headers on a `GET /api/healthz` response under simulated HTTPS.

**Phase 2 verification gate**
- `dotnet test BidParser.sln` — full pass (existing + ~6 new).
- Manual: `dotnet run` + Vite frontend. Full login → parse → history → download path. Confirm logs include the new structured lines.
- DB: drop `data/db.sqlite`, run `docker compose up`, confirm migrations apply and bootstrap admin is created.

---

### Phase 3 — Idiomatic .NET hygiene · ~1-2 days · Can be a follow-up PR

Goal: kill footguns, lift maintainability. Not blocking but cheap.

**Step 3.1 — `UserRole` enum + EF converter** (finding #10)
- Files: `src/BidParser.Infrastructure/Entities/User.cs:9`, `AppDbContext.cs`, every reference to `UserRole.Admin`/`UserRole.User` constants, `IsValidRole` helper, role-related tests.
- Replace `string Role` with `public UserRole Role { get; set; }`; add `entity.Property(u => u.Role).HasConversion<string>()`.
- Generate migration if column constraints change (likely none — value space stays `"admin"`/`"user"`).
- Acceptance: full test suite green; `Role = "Admin"` literal no longer compiles; bootstrap admin still seeds correctly.

**Step 3.2 — Dispose `XLWorkbook` from `WorkbookReader`** (finding #9)
- Files: `src/BidParser.Parsing/Xlsx/WorkbookReader.cs:8-12` and both XLSX parser callsites in `BidParser.Parsing/Nutanix/`.
- Change `ActiveSheet(string path): IXLWorksheet` → `Open(string path): XLWorkbook`. Callers now `using var workbook = WorkbookReader.Open(path); var sheet = workbook.Worksheets.First();`.
- Acceptance: parsing tests pass; no GC reliance for file release.

**Step 3.3 — Typed response records** (finding #17)
- New folder: `src/BidParser.Api/Contracts/` with records: `ErrorResponse`, `ParseErrorDetail`, `ParseErrorResponse`, `TooLargeResponse`, plus existing per-endpoint shapes (`UserPublic`, `HistoryResponse`, `HistoryRow` etc. can move here).
- Replace every `Results.Json(new { ... }, statusCode: ...)` with the typed record.
- Acceptance: byte-for-byte JSON output identical to before; integration tests untouched.

**Step 3.4 — Centralise magic strings** (finding #18)
- New file: `src/BidParser.Domain/Constants/Vendors.cs`, `Slugs.cs`, `CrmTemplates.cs` (or one `WellKnown.cs`).
- Move every string literal for vendor name (`"nutanix"`), parser slug (the five `nutanix_*` strings), CRM template (`"Foreign Uplift"`), role names (after Step 3.1 only `"admin"`/`"user"` if any string interop remains).
- Acceptance: `grep -rn "\"nutanix\"" src/` outside the constants file and `ParserRegistry.cs` returns nothing.

**Step 3.5 — `RetentionService` cleanup** (finding #15)
- File: `src/BidParser.Infrastructure/Services/RetentionService.cs:18-30`
- Project to anonymous DTO `{ Id, SourcePath, OutputPath }` before materialising. Delete files in the foreach, then `await _db.ParseJobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync(ct)`.
- Acceptance: existing retention integration test passes; new test seeds 1k rows and confirms cleanup completes without loading entities into change tracker.

**Step 3.6 — Optional: `AddDbContextPool`** (finding #16)
- File: `src/BidParser.Api/Program.cs:34`
- Switch `AddDbContext` → `AddDbContextPool`. Verify no DbContext state is mutated outside `SaveChangesAsync` (it isn't today).
- Acceptance: full test suite green; manual smoke shows no regression. **Skip if any reviewer concern** — the benefit is marginal for SQLite.

**Step 3.7 — Stylistic cleanup** (finding #19)
- Optional. Primary constructors on `ParseService`, `RetentionService`, `SessionCookieAuthHandler`. Collection expressions where natural.
- Acceptance: tests pass. Land in a single style-only commit.

**Phase 3 verification gate**
- `dotnet test BidParser.sln` — green.
- `cd frontend && npm run build` — green (no API contract changes).
- `docker compose build` — image builds cleanly.

---

## Cross-phase verification protocol

1. **After every step:** `dotnet test BidParser.sln` must remain green (54 existing tests + the new ones added in that step).
2. **End of Phase 1 and Phase 2:** end-to-end manual run:
   - `dotnet run --project src/BidParser.Api` + `cd frontend && npm run dev`
   - Log in as bootstrap admin, force password change, create a non-admin user, log in as that user, parse all five samples in `samples/inputs/`, verify each `_parsed.xlsx` downloads and validation toast matches expected.
   - Confirm structured log lines appear for each action (Phase 2+).
3. **End of Phase 2:** `docker compose up -d` from a clean `data/` directory; confirm migration applies, admin bootstrap seeds, and the full path works inside the container.
4. **Step 2.1 specifically:** byte-compare `samples/outputs/*_parsed.xlsx` against the regenerated set. If any bytes change, hand-validate against `docs/output_mapping.md` before committing new goldens.

---

## Critical files (consolidated)

- `src/BidParser.Api/Program.cs` — Phase 1.1, 1.2, 1.4; Phase 2.8; Phase 3.6
- `src/BidParser.Api/Endpoints/ParseEndpoints.cs` — Phase 1.3, 1.4; Phase 2.5 indirectly; Phase 3.3
- `src/BidParser.Api/Endpoints/HistoryEndpoints.cs` — Phase 2.4, 2.6, 2.7; Phase 3.3
- `src/BidParser.Api/Endpoints/UsersEndpoints.cs` — Phase 2.6, 2.7; Phase 3.1, 3.3
- `src/BidParser.Api/Endpoints/AuthEndpoints.cs` — Phase 2.6, 2.7; Phase 3.3
- `src/BidParser.Api/Endpoints/MeEndpoints.cs` — Phase 2.7; Phase 3.3
- `src/BidParser.Api/Auth/SessionCookieAuthHandler.cs` — Phase 2.6, 2.7
- `src/BidParser.Infrastructure/Persistence/AppDbContext.cs` — Phase 2.3, 2.4; Phase 3.1
- `src/BidParser.Infrastructure/Entities/User.cs`, `ParseJob.cs` — Phase 2.2; Phase 3.1
- `src/BidParser.Infrastructure/Services/RetentionService.cs` — Phase 3.5
- `src/BidParser.Infrastructure/Services/ParseService.cs` — Phase 2.5
- `src/BidParser.Output/ForeignUpliftWriter.cs` — Phase 2.1
- `src/BidParser.Parsing/Xlsx/WorkbookReader.cs` + XLSX parser callsites — Phase 3.2
- **New:** `src/BidParser.Api/Hosting/GlobalExceptionHandler.cs` — Phase 1.2
- **New:** `src/BidParser.Api/Middleware/SecurityHeadersMiddleware.cs` — Phase 2.8
- **New:** `src/BidParser.Api/Contracts/*.cs` — Phase 3.3
- **New:** `src/BidParser.Domain/Constants/*.cs` — Phase 3.4
- **New migrations:** Phase 2.3 (composite index), Phase 2.4 (NOCASE on filename), possibly Phase 3.1 (role conversion)

## Estimated effort

| Phase | Effort | Blocking? |
|---|---|---|
| Phase 1 | ~1 day | Yes — production blockers |
| Phase 2 | ~2-3 days | No, but ship before scale |
| Phase 3 | ~1-2 days | No, follow-up PR |
| **Total** | **~4-6 days** focused engineering |

## Out of scope

- Migration off the custom `AuthRateLimiter` to the .NET built-in rate limiter for the auth endpoints — keep both for now; Phase 1.4 only adds the built-in for `/parse`. Unify in a later sweep if desired.
- Content Security Policy beyond `default-src 'self'` — needs frontend asset inventory first; defer.
- OpenAPI/Swagger enablement — AGENTS.md treats this as intentional omission for an internal app.
- Replacing SQLite — out of architectural scope.

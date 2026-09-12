# Troubleshooting

Common failure modes and how to diagnose them. Deployment reference is in
[DEPLOYMENT.md](DEPLOYMENT.md); architecture context is in [architecture.md](architecture.md).

## Local build and tests

**API tests fail to start, or hang, with a container/Docker error.**
`tests/BidParser.Api.Tests` starts a real SQL Server testcontainer and needs a Docker-compatible
runtime. Confirm one is reachable:

```bash
docker info
```

With rootless Podman, the socket must be exported before running tests:

```bash
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
```

To make progress without a container runtime, run the parser and output suite instead — it covers
every parser and the workbook writer and needs nothing external:

```bash
dotnet test tests/BidParser.Parsing.Tests/BidParser.Parsing.Tests.csproj
```

**The backend starts but every request fails on the database.**
`dotnet run` does not start SQL Server. Either point `DB_CONNECTION_STRING` at an existing instance
or start just the bundled one:

```bash
docker compose up -d mssql
```

**Parser tests fail after a template or writer change.**
The workbooks in `samples/outputs/` are the test oracle — tests compare generated output cell by
cell against them. A failure means either the change was wrong, or the goldens need regenerating.
Never regenerate them to make a red test pass without reading the diff first; that is how an output
regression gets locked in as the expected result.

**`npm run build` fails on type errors but the dev server was fine.**
`npm run build` runs `tsc -b` before Vite. The dev server does not type-check, so type errors only
surface at build time. This is also what fails CI.

## Docker stack

**`docker compose up` exits complaining about a required variable.**
The compose file marks `SESSION_SECRET`, `MSSQL_SA_PASSWORD`, and `FORWARDED_ALLOW_IPS` as
required. Create `.env` from the template and fill them in:

```bash
cp .env.example .env
```

**The `mssql` container never becomes healthy, and the app container never starts.**
Almost always the SA password. SQL Server refuses to start if it does not meet complexity rules
(at least 8 characters with upper case, lower case, a digit, and a symbol), and the app container
waits on the health check, so the symptom is a stack that hangs rather than an obvious error. Check
directly:

```bash
docker compose logs mssql
```

**The app runs code that is not on your branch.**
The `bidparser` service declares both a `build:` context and a published `image:` name, so
`docker compose up -d` will reuse whatever is already tagged `ghcr.io/regalen/bidparser:latest`
locally — including an image pulled from the registry. Always build explicitly:

```bash
docker compose build
```

**Port 3447 is already in use.**
Both services use fixed container names (`bidparser`, `bidparser-mssql`), so a second stack cannot
run alongside the first. Find the existing one before assuming the port is held by something else:

```bash
docker ps --filter name=bidparser
```

**The stack keeps coming back after a reboot.**
Both services set `restart: unless-stopped`. `docker compose down` is the correct teardown; it
preserves the named volumes and therefore the database, users, and uploaded files.

Do not reach for `docker compose down -v` to "clean up" — the `-v` permanently destroys the
database, every user account including the admin password you set, and all stored files.

**The app container starts but `/api/healthz` never responds.**
Container start is not application readiness: EF Core migrations and admin seeding run at startup.
Allow up to roughly three minutes on a cold start, then check for a migration error:

```bash
docker compose logs bidparser
```

## Authentication and sessions

**Everyone was logged out after a restart or redeploy.**
The Data Protection keyring lives in `/data/dp-keys` and must persist. If the `/data` volume was
not mounted, was recreated, or was deleted, the keyring regenerates and every existing session
cookie becomes unreadable. Confirm the volume still exists:

```bash
docker volume ls
```

Rotating `SESSION_SECRET` has the same visible effect for a different reason — it changes the Data
Protection application discriminator, scoping new cookies away from old ones.

**The Dell integration started failing after a `SESSION_SECRET` rotation.**
Expected, and it does not indicate data loss. The stored Dell client secret is encrypted under the
old discriminator and can no longer be decrypted. An admin must re-enter and save the client secret
under the Dell API settings. Nothing else needs to change.

**Login succeeds but immediately bounces back to the login page, behind a reverse proxy.**
The session cookie is issued with `Secure` only when the request resolves to HTTPS after forwarded-
headers processing. If `FORWARDED_ALLOW_IPS` does not list the proxy's address *as the app container
sees it*, `X-Forwarded-Proto` is ignored, and a `Secure`-less cookie over HTTPS — or the reverse —
gets dropped by the browser. Set it to the proxy IP; do not set it to `*`, which is rejected in
production.

**Locked out of the admin account.**
There is no self-service recovery, and `ADMIN_PASSWORD` will not help: the bootstrap admin is seeded
only when the users table is empty and is ignored once any user exists. Recovery requires another
admin account to issue a reset, or direct database intervention.

**Repeated logins start returning 429.**
Auth endpoints are rate limited to `RATE_LIMIT_AUTH_PER_MIN` (default 5) per minute, counted both
per client IP and per submitted username. The limiter is in-memory, so restarting the app clears it.

## Parsing

**PdfPig 0.1.16 has a Type 3 collapsed-box compatibility layer — re-run the parser suite when
changing it.** PdfPig 0.1.15 changed Type 3 glyph boxes, and stable 0.1.16 retains that behaviour:
on some Chrome/Skia-produced PDFs, tight vertical word bounding boxes collapse to zero height. The
trigger is Type 3 fonts with a negative `FontMatrix` Y component and a Y-inverted `FontBBox`:

```
/FontMatrix [.00048828125 0 0 -.00048828125 0 0]
/FontBBox   [-106 483 1958 -1556]          <- lly (483) > ury (-1556)
```

The upstream change derives Type 3 letter boxes from character procedures rather than the
`FontBBox`. Tight boxes remain intentionally exposed as `PdfWord.Top`/`Bottom`: header windows and
parser-specific cleanup still use those meanings. They must not be recalibrated or silently
redefined to mask an upgrade.

Use stable placement geometry only on a collapsed page. When at least half of its non-whitespace
tight word boxes are at most 0.01 points high, `PdfWordCollector` stores the median
`StartBaseLine.Y` (with the origin flipped) as `LineY`, and derives each internal letter's X extent
from `StartBaseLine.X`/`EndBaseLine.X`. `RowsBetween` then groups on `LineY`; all other pages,
rotated text, and synthetic words retain the previous tight-box midpoint. Explicit whitespace words
are ignored when measuring the collapse ratio. `PageUsesBaselineGeometry` carries that exact
collector decision through word splitting and other transformations. The short-glyph repair uses
the same flag and skips those pages; it does not run a second collapse heuristic. The collector also
removes a complete `Page N of M` sequence only when its visible tokens share a visual line on one
page and lie at its edge, tolerating PdfPig's separate whitespace tokens, before a column boundary
can split it.

Across the current 24 PDF fixtures, every Nutanix `XQ-*` page has a non-whitespace collapse ratio
between 75.8% and 100%, while every Zebra and Lenovo page is at 0%. The nearest current page is
therefore 25.8 percentage points clear of the 50% decision boundary. The threshold is still a hard
page-level choice: a future mixed Type 1/Type 3 page could land near it and switch geometry mode
between pages in one table. Keep the page-level choice so a single page never mixes baseline and
tight-midpoint row coordinates, and review the measured ratios when introducing another mixed-font
PDF format.

Measured page-1 blast radius, 0.1.14 → 0.1.15, over `samples/inputs/`:

| Format | Words with a changed `Bottom` |
|---|---|
| Lenovo (`BRDAS*`, `BRPAS*`) | 0 of 253–720 — unaffected |
| Nutanix (`XQ-*`) | **all** words on every fixture (468 of 534 on `XQ-9100012`) |
| Zebra (`Zebra_PC_*`) | 4 per file |

This is deliberately not a per-vendor rule, a `Pa` workaround, or a broader row tolerance. The
focused geometry tests cover baseline grouping and vertical placement, collapsed boxes,
real-vs-false letter gaps, early footer filtering, rotated-text fallback, and the retained
short-glyph repair. The complete parser/output suite remains the required regression check; its
golden workbooks are the output authority.

**"The file is not recognised as …" instead of a parse result.**
This is a wrong-file-type response, not a crash. The selected parser could not find its table anchor
or a required column. When a sibling format of the same vendor scores confidently, the message names
it. Nothing is recorded and the upload is deleted — deliberately, because a mis-selection is user
error rather than a failure worth reviewing. Pick the correct file type, or use **Auto** where the
vendor offers it.

If the file genuinely is the selected format, the vendor has likely changed a header and the
parser's anchor needs revisiting.

**A recognised PDF reports `Expected N content columns but found M`.**
The parser found the table header, but the body no longer forms the expected number of horizontal
content bands. This is a fail-closed recognition error: continuing could move a price or quantity
into the wrong field without throwing. Compare the document with that parser's retained geometry
fixture and measured gutter constant. A vendor font/layout change may require a new measurement and
fixture; do not lower the gutter or hard-code X coordinates merely to make the sample parse.

**A parse succeeds but reports a totals mismatch.**
Validation compares `Σ(cost × qty)` against the quote's own total within a `0.01` tolerance. A
mismatch still produces a downloadable workbook — the warning is advisory. It usually means either
rows were missed or a price column was read from the wrong column. The mismatch is recorded and
appears in admin monitoring.

Formats whose source carries no quoted total (HP, HPE, Zebra) always report a match, because there
is nothing to compare against.

**Uploads fail at around 1 MB behind a reverse proxy.**
nginx-proxy-manager defaults `client_max_body_size` to 1 MB, well below the application's
`MAX_UPLOAD_MB` (default 10). Raise it on the proxy host. The application's own 413 remains
authoritative; the frontend's pre-flight check uses a matching constant in
`frontend/src/constants.ts`, which must be kept in step with the environment variable.

**A Dell quote ID returns a `dellApi` error.**
The failure is in fetching from Dell, before any parsing. Check the Dell API settings and use the
**Test connection** action, which validates the stored credentials against the token endpoint. Note
that `ApiVersion` must stay pinned at `4.0` — with the `Accepts-version` header absent, Dell serves
an unspecified default version and the fields both Dell parsers depend on bind silently to `0` or
null rather than erroring.

**Bid number shows as an em dash in Recent Uploads.**
Bid metadata extraction is best-effort by design and degrades to null rather than failing a parse,
so an unreadable header costs a label, not a workbook. `ParseService` logs a warning naming the file
and parser slug when this happens; a run of them means a vendor changed a header.

## Portable Windows desktop

**Windows SmartScreen warns or a managed device blocks `BidParser.exe`.**
The initial portable release is unsigned. SmartScreen may offer **More info → Run anyway**, while
managed endpoint policy may block unsigned executables entirely. Do not weaken corporate policy;
verify the SHA-256 shown on the public release and ask IT to allow that exact file. Authenticode is
deferred and must use an approved signing service when introduced.

**Guidance/defaults do not refresh, or no update appears.**
Routine remote failures are intentionally silent. The application remains usable with its embedded
schema-v1 documents and no-update state. Confirm the machine/proxy can reach both raw GitHub URLs
and the GitHub Releases API listed in [desktop.md](desktop.md#remote-configuration-and-trust-boundary).
A redirect, non-success status, response over the cap, unsupported schema, duplicate known entry,
unsafe markup, timeout, TLS failure, or malformed JSON all cause fallback. Before the public
repository's first commit/release, these endpoints legitimately return 404.

**The single-file executable extracts runtime files under `%TEMP%/.net`.**
Expected. `IncludeNativeLibrariesForSelfExtract=true` is required for the one-file distribution;
the extracted files are runtime components, not quote data. If endpoint policy blocks execution
from the temporary directory, coordinate an allowlisting decision or later signing strategy rather
than disabling file validation or moving quote data into the extraction path.

**Desktop publishing leaves DLLs, JSON, fonts, or PDBs beside the executable.**
The release contract is exactly one `BidParser.exe`. Publish with the checked-in `win-x64` profile
on Windows, from a clean output directory, then run `build/verify-desktop-publish.ps1`. Do not zip or
release a directory that fails this check. A Linux cross-publish cannot authoritatively validate
the Win32 icon/version resources.

**The same-repository release job fails after Docker publishing succeeds.**
Check that the workflow has `contents: write` permission for the repository `GITHUB_TOKEN`, the
two tracked `config/` documents are present, and Windows hosted-runner minutes are available. The
job is idempotent for the same tag/asset name, so correct the prerequisite and rerun the failed
workflow—do not create a second differently named asset.

**A desktop release tag is rejected before builds begin.**
`prepare-publish` accepts only supported `vMAJOR.MINOR.PATCH` SemVer tags whose commit is on `main`.
Merge the validated PR, pull `main`, and create the tag there. Never bypass the gate by loosening
the ancestry or tag checks.

## Reporting

**Runtime Configuration says a stored document needs repair.**
The parser registry or validation rules changed after that JSON was saved, or the row was edited
outside the application. The editor intentionally shows the raw stored JSON with the current
validation error. Correct the identifiers or values and save it again; the strict PUT validation
will replace it with canonical JSON. Until repaired, `/api/parse-ui-config` returns empty maps so
normal parsing and manual numeric entry remain available.

**A metrics or monitoring XLSX export fails before the download starts.**
Both exports use a seekable delete-on-close file under the operating system temporary directory and
open a dedicated non-retrying SQL connection for forward-only row streaming. Check that the app
identity can create files in the temporary directory and that the configured database permits a
second concurrent connection. Failed and cancelled exports dispose the file stream, so stale `.xlsx`
files should not remain.

**Metrics land on the wrong calendar day.**
The admin metrics time series buckets by *local* day, using the container's timezone. Set `TZ` in
`.env` to your operating timezone; it defaults to `Australia/Sydney`.

**A validation mismatch appears once in monitoring, not twice.**
Intended. A mismatch is written to both `parse_jobs` and `failed_parse_jobs`; the unified runs query
excludes the `failed_parse_jobs` copy so each mismatch surfaces once, sourced from the `ParseJob`
that owns the output file.

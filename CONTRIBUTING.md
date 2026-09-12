# Contributing

## Branch model

GitHub Flow. **`main` is the only long-lived branch, and it is always deployable.**
Direct commits to `main` are prohibited. All work happens on a short-lived feature branch off `main` and reaches `main` exclusively through a **squash-merged pull request**.

> **Note on enforcement:** Server-side branch protection and rulesets are unavailable on the free plan for private repositories. Enforcement is by documented convention and the `AGENTS.md` rules for AI assistants.

| Branch | Purpose | Deploys to |
|---|---|---|
| `main` | Single long-lived branch; always deployable | No artifact publication; a validated `v*` tag creates the Docker and desktop release |
| `<type>/<short-slug>` | Short-lived feature branch off `main` | *(tests only — no image published)* |

## Attribution & git identity

The canonical rule is the **Attribution policy** in [`AGENTS.md`](AGENTS.md#attribution-policy-canonical--applies-to-every-agent-and-every-automation); it applies to humans, AI coding agents, and automation alike. The operational summary:

- Commits must be authored and committed as `regalen <regalen@outlook.com>`. Verify before committing:
  ```bash
  git config user.name    # regalen
  git config user.email   # regalen@outlook.com
  ```
  If missing or wrong, set it **for this repository only** (never `--global` unless explicitly instructed):
  ```bash
  git config user.name "regalen"
  git config user.email "regalen@outlook.com"
  ```
- **No AI/bot attribution anywhere** — no `Co-authored-by:` or `Signed-off-by:` trailers naming Claude, Anthropic, Codex, OpenAI, Gemini, Google, Copilot, or any bot; no "Generated with …" / "AI-assisted" lines in commit messages, PR titles or descriptions, merge messages, tags, release notes, changelogs, code comments, or generated docs.
- Do not rewrite existing history purely to change attribution unless explicitly asked.

## Fixtures, sample data and privacy

This repository is public. Do not commit a raw customer file or any source material containing an
identifiable customer, reseller, employee, or non-public commercial data. A later deletion does
not remove material from Git history. Read the full policy in [`AGENTS.md`](AGENTS.md#sample-data-fixtures-and-commercially-sensitive-information) and the current
[fixture sanitisation record](docs/sample_fixture_sanitisation.md) before adding or replacing a
fixture.

Before committing a fixture or its golden output, confirm all of the following:

- [ ] The filename and contents contain no customer, reseller, or individual name.
- [ ] Contact names, email addresses, phone numbers, and postal addresses are synthetic or redacted.
- [ ] Account, quote, bid, contract, opportunity, deal-registration, serial, and source-system IDs are synthetic.
- [ ] Pricing, discounts, and metadata are synthetic or explicitly reviewed as publication-safe.
- [ ] Golden outputs, screenshots, logs, and browser-download copies were reviewed too.
- [ ] No raw version of the original source file was committed at any point.
- [ ] The corresponding parser/output tests still pass, and public browser samples are byte-identical to their input fixtures where applicable.

When in doubt, do not commit the fixture. Public supplier boilerplate may be retained only under
the documented exception in the fixture sanitisation record; it does not permit customer or
non-public commercial information.

## Branch naming

Use kebab-case `<type>/<short-slug>` matching Conventional Commit prefixes:

| Prefix | Use for | Example |
|---|---|---|
| `feat/` | New parser format, endpoint, or UI capability | `feat/lenovo-lbpe-isg` |
| `fix/` | Bug fix in existing behaviour | `fix/lenovo-report-type` |
| `docs/` | Documentation only | `docs/github-flow` |
| `chore/` | Dependencies, CI, tooling, config | `chore/bump-dotnet-sdk` |
| `refactor/` | Behaviour-preserving restructure | `refactor/crm-writer-layout` |
| `test/` | Test-only changes | `test/zebra-pdf-fixtures` |
| `perf/` | Performance improvements | `perf/pdf-word-grouping` |

**Two segments, always — no tooling markers.** Agent worktree tooling generates branch names of
its own (`claude/<slug>-<hash>` and similar). Those are not valid branch names in this repository:
no assistant, model, or vendor name belongs in a branch name, and neither does a generated hash
suffix. An agent that finds itself on such a branch renames it to the convention **before doing any
work**, while the branch is still empty:

```bash
git branch -m feat/<short-slug>
```

This is safe on an unpushed branch with no commits and works from inside a worktree. If the branch
has already been pushed, leave it and say so rather than rewriting a published ref. This mirrors
the Attribution policy in `AGENTS.md`: the tool used to make a change never appears in the record
of that change.

## Workflow

1. **Sync `main` and create a branch:**
   ```bash
   git checkout main && git pull
   git checkout -b <type>/<short-slug>
   ```
2. **Develop and test locally:**
   Commit locally. Before pushing, run the test suite and frontend build:
   ```bash
   dotnet test BidParser.sln
   cd frontend && npm run build
   ```
   *(Note: API tests require a Docker-compatible container runtime).*
3. **Local Docker validation (Optional):**
   For user-visible or structural changes, run a local Docker build to validate in the browser (see [Local validation (Docker)](#local-validation-docker) below).
4. **Push branch and open a PR:**
   ```bash
   git push -u origin <type>/<short-slug>
   ```
   Open a pull request into `main` on GitHub. Feature-branch pushes do not run CI until a PR exists. Opening or updating the PR runs the frontend build and full .NET suite against GitHub's synthetic merge commit, so one validation run covers the exact candidate being considered for merge.
5. **Squash merge and clean up:**
   - **Squash rule:** Write the PR title as a clean Conventional Commit changelog line (e.g. `feat: split Lenovo output by Solution ID`). Upon squash merge, this title becomes the single commit subject on `main` and populates GitHub Release notes.
   - **Attribution rule:** The PR title and description describe the *change*, never the AI or tool used to implement it. Because squashed titles feed the generated release notes, an attribution slip in a title propagates into every release. Strip any `Co-authored-by:`/`Signed-off-by:` trailer that names a bot or AI from the squash commit body before merging.
   - **No review gate:** As a solo maintainer codebase, self-merge as soon as CI checks pass green.
   - **Stale-base caveat:** A green run covers the PR merge candidate at the time that run started. If another PR merges into `main` afterward, update or rebase the branch to trigger a fresh validation run:
     ```bash
     git pull --rebase origin main && git push --force-with-lease
     ```
   - Delete the remote feature branch after merging.
6. **Sync `main` locally:**
   ```bash
   git checkout main && git pull
   ```

## Desktop development

The WPF project targets `net10.0-windows` with `EnableWindowsTargeting`, so Linux CI and developer
machines can compile it even though they cannot launch it or verify native Win32 resources:

```bash
dotnet build src/BidParser.Desktop/BidParser.Desktop.csproj --configuration Release
dotnet test tests/BidParser.Desktop.Configuration.Tests/BidParser.Desktop.Configuration.Tests.csproj
```

Run the parser/application/output suite as well whenever desktop selection, parsing, or writing
changes:

```bash
dotnet test tests/BidParser.Parsing.Tests/BidParser.Parsing.Tests.csproj
```

Release publishing must run on Windows from the checked-in profile:

```powershell
dotnet publish src/BidParser.Desktop/BidParser.Desktop.csproj `
  -p:PublishProfile=win-x64 `
  -p:BidParserVersion=1.0.0
```

The result must be exactly one `BidParser.exe`; `build/verify-desktop-publish.ps1` verifies that
contract, version metadata, startup, and SHA-256. Linux cross-publishing is useful only for checking
the bundle shape—it cannot authoritatively verify the executable's icon or Win32 version resources.
The complete manual Windows matrix is in [docs/desktop.md](docs/desktop.md#manual-windows-acceptance).

## Releasing

Cut a release by tagging `main` **only after your PR has been merged and `main` is pulled**:

```bash
git checkout main && git pull
git tag v1.2.3
git push origin v1.2.3
```

The `v*` tag triggers both deliverables from the same commit and SemVer: Docker `:<version>` +
`:latest` to GHCR, plus a verified portable `BidParser.exe` and `BidParser.exe.sha256` in the same
repository's GitHub Release. The workflow uses the scoped repository `GITHUB_TOKEN`; no external
release token or separate distribution repository is required. Before tagging, confirm the
two tracked `config/` documents are valid and available from their raw URLs. See
[docs/desktop.md](docs/desktop.md#release-prerequisites-and-runbook).

Releases carry no AI/bot attribution: tag messages, the release title, and the release body describe features, fixes, and technical changes only. The notes are generated from squashed PR titles, so they stay clean as long as PR titles do; if a generated note ever surfaces a tool name or bot contributor, edit the release body rather than leaving it published.

## CI trigger reference

`.github/workflows/build.yml`:

| Event | Runs | Docker tags | GitHub Release |
|---|---|---|---|
| Push to feature branch | *(none until a PR exists)* | *(none)* | *(none)* |
| PR opened or updated | Linux `validate` plus Windows desktop build/tests and one-file launch smoke | *(none)* | *(none)* |
| Merge / push to `main` | *(none)* | *(none)* | *(none)* |
| Valid `v*` tag on `main` | `prepare-publish`, Docker + Windows desktop builds, then `release` | `:<version>`, `:latest`, `sha-<short-sha>` | Same-repository release with `BidParser.exe` and `BidParser.exe.sha256` |

Documentation-only changes (`**/*.md`, `docs/**`, and `README*`) are ignored for PR validation.
Tag pushes are not path-filtered, so a release workflow cannot be skipped by a docs-only tagged
commit.

Linux validation, version gating, image publishing, and release coordination run on the
self-hosted runner (`runs-on: [self-hosted, linux, x64]`); there is no Linux-hosted fallback, so
those jobs queue if it is offline. Desktop validation/publishing runs on `windows-latest`. The
self-hosted runner is rootless Podman with no Buildx driver, so images use plain `docker build` and
have no registry-side layer cache.

The runner is privately operated and does not transfer with the repository — see
[Runner requirement](docs/DEPLOYMENT.md#runner-requirement--action-needed-at-handover) in the
deployment guide.

## Local validation (Docker)

To manually accept user-visible changes in the browser before opening a PR, spin up the complete application stack locally.

### 1. Pre-flight checks

- **Working branch:** Ensure you are on a feature branch (not `main`).
- **Working tree state:** Run `git status --short` and report it. The image is built from the **working tree**, not from `HEAD`, so uncommitted edits are included in what you are about to test.
- **Environment config:** Ensure `.env` exists in the repo root. If missing, bootstrap it from `.env.example`:
  ```bash
  cp .env.example .env
  # Replace SESSION_SECRET with a 32-byte hex string:
  sed -i "s/SESSION_SECRET=.*/SESSION_SECRET=$(openssl rand -hex 32)/" .env
  ```
- **Port availability:** Verify port `3447` is free.
- **Existing stack check:** Run `docker compose ps`, and `docker ps --filter name=bidparser` to catch a stack started from another working copy. Both services use fixed `container_name`s (`bidparser`, `bidparser-mssql`), so a second stack cannot run in parallel — it fails on a name/port conflict. If a stack is already up, **report it and ask** whether to reuse or rebuild. Never tear down, recreate, or delete an existing environment's containers, volumes, or database data without explicit confirmation.

### 2. Build explicitly

```bash
docker compose build
```

**Never rely on an implicit build.** The `bidparser` service in `docker-compose.yml` declares
*both* `build: { context: . }` **and** `image: ghcr.io/regalen/bidparser:latest`. A bare
`docker compose up -d` therefore reuses whatever `ghcr.io/regalen/bidparser:latest` already sits
in the local image store — including a production image previously pulled from GHCR — and you
would be manually accepting code that is not on your branch, with nothing appearing to go wrong.
Always build explicitly (`docker compose build`, or `docker compose up -d --build`).

Building overwrites the local `:latest` tag, replacing any pulled production image (harmless — it
is re-pullable). A cold build is slow: the Dockerfile runs the full `node:22-alpine` →
`tsc -b && vite build` stage plus `dotnet publish`.

### 3. Launch and readiness gating

```bash
docker compose up -d
```

- **Database health gate:** The `mssql` container has a health check (`interval 10s`, `start_period 30s`). The `bidparser` app container depends on `mssql` reaching healthy status, so launch may take up to ~130 seconds.
- **Readiness probe:** `bidparser` container start is not app readiness (EF Core migrations and admin seeding run at startup). Poll the health endpoint until it returns HTTP 200:
  ```bash
  curl -fsS http://localhost:3447/api/healthz
  ```
  Allow up to ~180 seconds total timeout.

### 4. Stop-and-report criteria

Halt execution and report logs if any of the following occur:
- Docker build failure (`tsc -b`, Vite, or `dotnet publish` failure).
- `mssql` container fails to reach healthy status (e.g. `MSSQL_SA_PASSWORD` failing complexity rules).
- EF Core migration error in `docker compose logs bidparser`.
- `/api/healthz` times out or fails.
- Port `3447` binding conflict.

### 5. Access & Credentials

- **URL:** `http://localhost:3447`
- **Credentials:**
  - **Fresh environment (empty volumes):** Log in with default admin credentials (`admin` / `changeme`). You will be prompted to set a new password on first login.
  - **Existing environment (named volumes present):** Named volumes `bidparser-data` and `bidparser-mssql-data` persist across runs. Password will be whatever was previously set.

  Check which case applies with `docker volume ls` before quoting credentials — the bootstrap
  admin is seeded *only when the users table is empty*, so `changeme` is correct on a fresh
  environment only.

### 6. Teardown and lifecycle

A validation run is a temporary environment with a defined lifecycle, not a background service.
Tear it down when validation is complete:
```bash
docker compose down
```

This matters beyond tidiness: both services set **`restart: unless-stopped`**, so a stack left
running does not merely idle — it **comes back on every reboot**, holding port `3447` and the SQL
Server container indefinitely.

- `docker compose down` is the normal cleanup action. It removes the validation containers and
  network and **preserves** the `bidparser-data` and `bidparser-mssql-data` named volumes, so
  users, uploads, and parse history survive to the next run.
- **`docker compose down -v` is only for a deliberate reset** of the local database/environment
  and requires explicit confirmation — `-v` permanently destroys local database data, users
  (including the admin password you set), and uploaded files.
- **Never run `docker system prune`, `docker volume prune`, or `docker image prune -a` without
  explicit confirmation.** These reach far outside this project and would evict unrelated images
  and volumes from the machine.
- Do not leave the stack running "just in case" — bring it back up on request instead.

### 7. Validation artifacts

- Do not leave temporary validation artifacts in the repository root: no dumped log files, no
  scratch compose overrides, no throwaway `.env` variants.
- `.env` is the one file that legitimately persists — it is gitignored and reused by later runs.
  Leave it in place.
- If a log excerpt or validation note genuinely needs to be kept, put it in `.ai/inbox/`
  following the existing convention (also gitignored) — never in the repo root, and never
  committed.
> **Warning on volume deletion:** `docker compose down` preserves named volumes and test data. Never run `docker compose down -v` without explicit confirmation, as `-v` permanently destroys local database data, users, and uploaded files.

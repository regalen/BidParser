# Nutanix Quote Parser — MVP Plan

## Context

Sales currently re-keys supplier quotes from various PDF/XLSX layouts into the internal `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` template. The parser logic is already designed, validated by hand, and documented — see `CLAUDE.md` plus the five per-format spec files in `docs/` (`software_only_pdf.md`, `software_only_xlsx.md`, `renewal_pdf.md`, `hardware_only_pdf.md`, `hardware_only_xlsx.md`) and the output mapping in `docs/output_mapping.md`. Five sample inputs have been parsed end-to-end and the resulting `XQ-*_parsed.xlsx` files under `samples/outputs/` match expected totals — they serve as golden fixtures for the template-writer regression test.

This plan covers the **MVP web app** that wraps that parser library: authentication, an upload-and-parse dashboard, per-user settings, admin user management, and a recent-uploads history. The visual design is locked to the V4 "Side panel" wireframe from the Claude Design handoff (unpacked in `/tmp/design_unpack/bidparser/`).

**Scope locked with the user:**
- **Single vendor**: Nutanix only. Architecture stays pluggable (registry-driven) so Dell/Lenovo can drop in later, but no other vendor ships in MVP.
- **Single-file upload**. The "batch parse" hint in the wireframe is a future-state affordance; MVP accepts one file per parse.
- **Auto-download flow** — no review screen. User clicks *Upload & parse*, the dropzone morphs into a progress panel (V6 state), the `_parsed.xlsx` downloads automatically. Validation runs server-side; mismatches return as a response header / toast, never as an approval gate.
- **Per-user remembered FX rate & margin** — the last values used by each user are persisted on their account and pre-fill on next login.
- **Env-var admin bootstrap** — `BOOTSTRAP_ADMIN_USERNAME` / `BOOTSTRAP_ADMIN_PASSWORD` seed the first admin on a fresh DB; defaults are `admin` / `changeme`; admin is created with `must_change_password=True`.
- **Stack**: Python/FastAPI backend + React/Vite/TypeScript frontend, deployed via `docker compose`. Tailwind for styling; Inter throughout per the design.

**Out of scope for MVP** (deferred):
- Multi-file batch upload.
- CSV vendor formats (mentioned in the design chat, no spec exists).
- Vendors other than Nutanix.
- Review/approve-before-export gate.
- Multi-tenancy / org boundaries.
- Email notifications, SSO, audit log beyond ParseJob history.

## Repository Layout

```
/home/adem/Documents/parser/
├── backend/
│   ├── app/
│   │   ├── main.py                       # FastAPI app, routers, startup hook (bootstrap admin)
│   │   ├── config.py                     # env: DB_URL, UPLOAD_DIR, SESSION_SECRET, BOOTSTRAP_*, RATE_LIMIT_*, RETENTION_DAYS
│   │   ├── db.py                         # SQLAlchemy engine + session
│   │   ├── models.py                     # User, ParseJob ORM models
│   │   ├── auth/
│   │   │   ├── passwords.py              # bcrypt hash/verify
│   │   │   ├── sessions.py               # signed httponly cookie issue/verify
│   │   │   ├── deps.py                   # current_user / require_admin FastAPI dependencies
│   │   │   └── rate_limit.py             # in-memory leaky bucket, 5 req/min/IP on /auth/login
│   │   ├── api/
│   │   │   ├── routes_auth.py            # /auth/login, /auth/logout, /auth/change-password
│   │   │   ├── routes_users.py           # /users (admin CRUD)
│   │   │   ├── routes_me.py              # /me, /me/settings (per-user FX + margin defaults)
│   │   │   ├── routes_parse.py           # /parsers, /parse, /history, /history/{id}/source, /history/{id}/output
│   │   │   └── schemas.py                # Pydantic request/response models
│   │   ├── parsers/                      # Already-designed parser package
│   │   │   ├── base.py                   # BaseParser ABC + LineItem / QuoteMetadata / ValidationResult / ParseResult
│   │   │   ├── registry.py               # PARSER_REGISTRY (explicit list)
│   │   │   ├── pdf_utils.py              # pdfplumber column/row helpers
│   │   │   ├── xlsx_utils.py             # openpyxl header-map / cell-anchor helpers
│   │   │   ├── software_only_pdf/parser.py
│   │   │   ├── software_only_xlsx/parser.py
│   │   │   ├── renewal_pdf/parser.py
│   │   │   ├── hardware_only_pdf/parser.py
│   │   │   └── hardware_only_xlsx/parser.py
│   │   ├── output/
│   │   │   └── template_writer.py        # implements output_mapping.md (writes Foreign Uplift xlsx)
│   │   ├── services/
│   │   │   ├── parse_service.py          # Orchestrates: save upload → run parser → write output → persist ParseJob → return file
│   │   │   └── retention.py              # Daily cleanup of ParseJob files older than RETENTION_DAYS
│   │   └── storage.py                    # UUID-named files under UPLOAD_DIR for both originals and parsed outputs
│   ├── tests/
│   │   ├── fixtures/                     # Symlinks to ../../../samples/inputs/*.pdf and *.xlsx
│   │   ├── expected/                     # Golden JSONs per sample
│   │   ├── test_software_only_pdf.py
│   │   ├── test_software_only_xlsx.py
│   │   ├── test_renewal_pdf.py
│   │   ├── test_hardware_only_pdf.py
│   │   ├── test_hardware_only_xlsx.py
│   │   ├── test_template_writer.py       # Compares emitted xlsx against samples/outputs/XQ-*_parsed.xlsx
│   │   ├── test_auth.py                  # login, lockout, change-password
│   │   ├── test_users_admin.py           # admin-only CRUD
│   │   ├── test_parse_api.py             # full upload → parse → download → history
│   │   └── test_rate_limit.py
│   ├── alembic/                          # schema migrations (initial migration only for MVP)
│   └── pyproject.toml
├── frontend/
│   ├── src/
│   │   ├── main.tsx
│   │   ├── App.tsx                       # Router + auth-aware shell
│   │   ├── api/client.ts                 # fetch wrapper (credentials: include, X-Requested-With), 401 redirect handler
│   │   ├── auth/AuthContext.tsx          # current user state, login/logout/changePassword actions
│   │   ├── design/tokens.ts              # CSS custom properties for V4 colours/spacings/typography
│   │   ├── pages/
│   │   │   ├── LoginPage.tsx
│   │   │   ├── ChangePasswordPage.tsx
│   │   │   ├── DashboardPage.tsx         # V4 implementation
│   │   │   └── SettingsPage.tsx          # Admin-only: user CRUD
│   │   ├── components/
│   │   │   ├── AppHeader.tsx             # Logo + AccountChip + nav
│   │   │   ├── AccountChip.tsx           # Username + dropdown (Settings link if admin, Logout)
│   │   │   ├── ParseSettingsCard.tsx     # Left card: vendor → file type → Nutanix block → CRM template → Upload button
│   │   │   ├── VendorSelect.tsx
│   │   │   ├── FileTypeSelect.tsx        # Cascades from vendor
│   │   │   ├── NutanixSettingsBlock.tsx  # FX rate + margin inputs
│   │   │   ├── CrmTemplateCallout.tsx    # Emerald derived block
│   │   │   ├── ResetButton.tsx           # Page-level destructive button
│   │   │   ├── Dropzone.tsx              # Single-file drag/drop; morphs to progress panel in-place
│   │   │   ├── ProgressPanel.tsx         # V6 state: per-file row with progress bar
│   │   │   └── RecentUploadsTable.tsx    # Server-paginated history with two action buttons per row
│   │   └── types.ts                      # Mirrors backend schemas
│   ├── package.json
│   ├── tailwind.config.ts
│   ├── postcss.config.js
│   ├── vite.config.ts
│   └── index.html
├── Dockerfile                            # multi-stage: Node FE build → Python runtime with FE bundled at /app/static/
├── docker-compose.yml                    # consumer-facing: pulls image from ghcr.io
├── .env.example                          # SESSION_SECRET, BOOTSTRAP_*, etc.
├── .github/workflows/build.yml           # Build & push multi-arch image to ghcr.io
├── samples/
│   ├── inputs/                           # existing supplier-issued source files (5 fixtures)
│   ├── outputs/                          # existing XQ-*_parsed.xlsx golden fixtures
│   └── template/                         # ANZ-GENERIC_ForeignUplift.xlsx (the output template)
├── docs/                                 # PLAN.md, output_mapping.md, per-format specs
└── CLAUDE.md                             # auto-loaded project guidance
```

## Data Model

```python
class User(Base):
    id: int                                # PK
    username: str                          # unique, case-insensitive
    password_hash: str                     # bcrypt
    role: Literal["admin", "user"]
    must_change_password: bool             # True after admin-issued reset; cleared on change
    fx_rate: Decimal | None                # last-used per-parse value, 4 d.p.
    margin: Decimal | None                 # last-used per-parse value, 2 d.p.
    created_at: datetime
    updated_at: datetime

class ParseJob(Base):
    id: int                                # PK (also surfaces in URLs)
    user_id: int                           # FK User
    vendor: str                            # "Nutanix"
    parser_slug: str                       # e.g. "renewal_pdf"
    source_filename: str                   # original upload name (for display)
    source_path: str                       # disk path of stored original (UUID-named)
    output_path: str                       # disk path of stored *_parsed.xlsx
    fx_rate: Decimal                       # snapshot of value used for this parse
    margin: Decimal                        # snapshot of value used for this parse
    computed_total: Decimal                # Σ cost × qty across line items
    quoted_total: Decimal | None           # quote's stated TOTAL (None if parser couldn't locate)
    totals_match: bool                     # |computed - quoted| < 0.01
    created_at: datetime

    # Note: only successful parses produce a ParseJob row. Failures are surfaced
    # inline in the UI and discarded (source file deleted, no row, no output).
```

Schema is managed by a single initial Alembic migration. SQLite database stored as a docker volume.

## Authentication & Authorization

**Storage**: bcrypt-hashed passwords (cost factor 12). Sessions issued as signed httponly cookies (HMAC-SHA256, secret from `SESSION_SECRET`). Cookie carries `{user_id, issued_at}` with a **hard 12-hour expiry from login** — no sliding refresh. After 12h the user must re-authenticate. Logout clears the current cookie only (no session-version rotation; other devices remain logged in until their own 12h expires).

**Endpoints** (all under `/api`):

| Method | Path | Auth | Notes |
|---|---|---|---|
| `POST` | `/auth/login` | none | Body `{username, password}`. Rate-limited (see below). Returns `{user: {id, username, role, must_change_password}}` plus sets the session cookie. |
| `POST` | `/auth/logout` | user | Clears the current session cookie. |
| `POST` | `/auth/change-password` | user | Body `{old_password, new_password}`. Validates `new_password` meets the rules below. Clears `must_change_password`. |
| `GET`  | `/me` | user | Returns the current user shape. |
| `PATCH` | `/me/settings` | user | Body `{fx_rate?, margin?}`. Updates the user's remembered defaults. |

**Password rules** (enforced on `/auth/change-password` only — bootstrap and reset both write the literal `changeme` and depend on `must_change_password` to force compliance on next login):
- Minimum 8 characters.
- Must contain at least one uppercase letter, one digit, and one symbol (non-alphanumeric).
- (NIST-style "not on a common-passwords blocklist" check is **out** for MVP — too much library footprint for an internal app.)

**Password recovery**: No self-service. Admins reset a user's password via `PATCH /api/users/{id}` with `{reset_password: true}`, which sets the stored password to `changeme` and `must_change_password=True`. The user logs in with `changeme`, is force-redirected to `/change-password`, and picks a new compliant password.

**Rate limit on the auth API**: 5 attempts per minute, applied on two independent dimensions:
- Per remote IP across the union of `/auth/login` + `/auth/change-password`.
- Per username on `/auth/login` (username is the value supplied in the request body — applied even before authentication succeeds, so attackers can't enumerate by hitting one username from many IPs).

Either bucket exhausting returns `429 Too Many Requests` with `Retry-After: <seconds>` and a generic body to avoid leaking which dimension tripped. In-memory leaky bucket (deque of timestamps per key); doesn't survive backend restart; can swap for Redis later.

**Role enforcement**: A `require_admin` FastAPI dependency wraps admin-only routes. A `require_user` dependency wraps the rest. All `/api/*` except `/auth/login` requires the cookie.

**CSRF**: All non-GET endpoints require a custom header `X-Requested-With: BidParser` set by the frontend `client.ts`. Combined with the SameSite=Lax cookie this is sufficient for an internal app without exposing form-encoded endpoints to third-party sites.

**Forced password change**: When the current user has `must_change_password=True`, the backend rejects all non-`/auth/*` and non-`/me` calls with `403 password_change_required`. The frontend redirects to `/change-password` on receipt and blocks navigation away until cleared.

**Multiple admins**: The backend enforces that `role=admin` cannot be removed from the *last* admin and an admin cannot delete themselves — both `PATCH /api/users/{id}` and `DELETE /api/users/{id}` return `409 Conflict` if the operation would leave zero admins.

**Bootstrap**: A FastAPI `startup` event reads `BOOTSTRAP_ADMIN_USERNAME` (default `admin`) and `BOOTSTRAP_ADMIN_PASSWORD` (default `changeme`). If no users exist in the DB, it creates that user with `role=admin`, `must_change_password=True`. On subsequent starts the env vars are ignored. The first login of that user is forced through the change-password flow before they can do anything else.

## Admin User Management

`/api/users` (admin only):

| Method | Path | Body | Response |
|---|---|---|---|
| `GET` | `/users` | — | `[{id, username, role, must_change_password, created_at}]` |
| `POST` | `/users` | `{username, role}` | Creates user with password `changeme` and `must_change_password=True`. Returns the user. |
| `PATCH` | `/users/{id}` | `{username?, role?, reset_password?: bool}` | Updates fields. If `reset_password=true`, resets to `changeme` and sets `must_change_password=True`. |
| `DELETE` | `/users/{id}` | — | Forbidden on self. |

## Parser Library

Already designed. The MVP imports it as-is. See `CLAUDE.md` for:
- The `LineItem` / `QuoteMetadata` / `ValidationResult` / `ParseResult` shape (incl. canonical field naming `vpn`/`cost`/`qty`/`term`/`msrp`).
- `BaseParser` ABC and the explicit `PARSER_REGISTRY` list.
- Anchor-based extraction rules (mandatory — never hard-code rows or columns).
- Per-format extraction algorithms for all five Nutanix formats.

Per-format spec files in `docs/` (`software_only_pdf.md`, `software_only_xlsx.md`, `renewal_pdf.md`, `hardware_only_pdf.md`, `hardware_only_xlsx.md`) are the authoritative extraction guides.

The Nutanix file-type dropdown surfaces these five formats by their `display_name`. Vendor → file-type cascade is driven by the registry: `list_parsers()` filtered by `vendor == "Nutanix"`.

## Output Template Writer

`backend/app/output/template_writer.py` implements the rules in `output_mapping.md`:
- Sheet `Foreign Uplift`, 27 columns A→AA, header row at row 2.
- Row 1 column L carries the optional note; row N+3 carries `*` in column B + the DO-NOT-DELETE warning in column D.
- Column H (MSRP) always empty; `msrp` lands in column U (Foreign MSRP) only.
- Column M (Serial Number) always empty; `serial_number` written verbatim into column R (Comments).
- Column N (Warranty/Duration) written only when `term >= 1`.
- Column K (Margin) and column V (Foreign Exchange Rate) come from the user's `margin` / `fx_rate` inputs for this parse (no longer hardcoded — the previous `5.00` / `1.000` constants were placeholders).
- Empty price cells written as `0` for bundled-component rows.
- Numbers written as raw values; dates as native `date` with cell `number_format = "DD/MM/YYYY"`.

A regression test reads the five committed `samples/outputs/XQ-*_parsed.xlsx` files as golden fixtures and asserts the writer reproduces them byte-for-cell when fed the same `LineItem` list + `fx_rate=1.000`, `margin=5.00`.

## Parsing API

`POST /api/parse` (multipart/form-data):
- `file`: single uploaded file. **Max 10 MB**, enforced client-side (reject before upload) and server-side (returns `413 Payload Too Large`). Accepted MIME: `application/pdf`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- `vendor`: `"Nutanix"`.
- `parser_slug`: one of `software_only_pdf`, `software_only_xlsx`, `renewal_pdf`, `hardware_only_pdf`, `hardware_only_xlsx`.
- `fx_rate`: float, validated 4 d.p.
- `margin`: float, validated 2 d.p.

Backend flow (in `parse_service.py`):
1. Persist the uploaded file under a UUID name in `UPLOAD_DIR/originals/`.
2. Resolve parser via `registry.get_parser(slug)`, run `parse(path)`.
3. Run validation (`Σ cost × qty` vs `quoted_total`, tolerance `Decimal("0.01")`).
4. Call `template_writer.write(...)` to produce the `_parsed.xlsx` under `UPLOAD_DIR/outputs/`.
5. Persist a `ParseJob` row (totals, paths, fx/margin snapshots).
6. Update the user's `fx_rate` and `margin` defaults to the values just used.
7. Return the parsed file as `200 OK` with:
   - `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
   - `Content-Disposition: attachment; filename="<input_basename>_parsed.xlsx"`
   - `X-Validation: match | mismatch` (the frontend reads this and shows a green toast on match, amber on mismatch — never blocks the download).
   - `X-Computed-Total`, `X-Quoted-Total` headers for the toast detail.

**Failure mode**: When parsing throws (PDF unreadable, header anchor not found, TOTAL missing, invalid file type), the backend returns `422` with `{detail: {stage, hint, message}}`. The uploaded source file is **discarded** — no ParseJob row is recorded and no original is retained on disk. The frontend renders the error inline in the dropzone area (red border, message text from `hint`), preserving the form so the user can fix the file type or try a different file. The dropzone resets to empty on the user's next interaction.

## CRM Import Template mapping

A simple hardcoded constant in `template_writer.py`:

```python
CRM_TEMPLATE_BY_VENDOR = {
    "Nutanix": "Foreign Uplift",
}
```

All five Nutanix file types map to "Foreign Uplift" (the only template defined in `output_mapping.md` and the only output template for MVP). The `CrmTemplateCallout` component in the frontend reads this via `GET /api/parsers` (each parser's response includes `vendor` and `crm_template` fields). When future vendors are added, extending this constant + adding the corresponding template writer is the only required code change for the mapping.

## Recent Uploads

`GET /api/history?limit=N&offset=M` (user-scoped — admins still only see their own history for MVP):
- Returns `{rows: [{id, source_filename, vendor, parser_slug, file_type_display, fx_rate, margin, when, totals_match}], total}` ordered desc by `created_at`.
- `when` is a relative time string computed server-side (`"2m ago"`, `"1h ago"`, `"Yesterday"`, `"2 days ago"`, then absolute date) — matches the design's "WHEN" column.

`GET /api/history/{id}/source` and `GET /api/history/{id}/output` stream the stored files, gated to the owning user. 404 if missing or expired.

**Retention**: `RETENTION_DAYS` (default 90). A daily background task (started from FastAPI lifespan) deletes ParseJob rows and their files older than the cutoff.

## Frontend (V4 implementation)

**Routing** (react-router-dom):
- `/login` — login form, rate-limit-aware (shows the Retry-After delta on 429).
- `/change-password` — forced when `must_change_password=true`; blocked from other pages until cleared.
- `/dashboard` — main parse UI. Default landing for authenticated users.
- `/settings` — admin-only user CRUD.
- A 401 from any API call clears the auth state and redirects to `/login`. A 403 with `password_change_required` redirects to `/change-password`.

**Design tokens** (`design/tokens.ts`): translate the wireframe's CSS custom properties (`--ink #2a2a2a`, `--paper #fdfcf8`, `--accent #0077d4`, success `#10b981/#ecfdf5/#047857`, destructive `#dc2626/#fef2f2/#fecaca`) into Tailwind theme extensions. Inter loaded via `@fontsource/inter` (so it works offline in the Docker image).

**`DashboardPage.tsx`** is the V4 layout. Concrete construction (matches `/tmp/design_unpack/bidparser/project/variants.jsx` lines 149–470):

- **AppHeader** (56px): app logo on the left (lettered tile + wordmark), `AccountChip` on the right (avatar with user initials + username + dropdown with "Settings" — admins only — and "Logout").
- **Page title row**: "New quote" + "UPLOAD A VENDOR QUOTE / BID TO PARSE" label on the left, `ResetButton` (destructive red styling) on the right.
- **Two-column layout** (`gap-6`, `align-items: stretch`):
  - **`ParseSettingsCard`** (left, 320px wide, bordered card):
    - `VendorSelect` — single option "Nutanix" for MVP (still a real select with a chevron, future-proofed for Dell/Lenovo).
    - `FileTypeSelect` — populated from `/api/parsers` filtered by vendor; shows the five Nutanix display names. Helper: "Types depend on the vendor."
    - Dashed divider, then `NutanixSettingsBlock` (visible only when vendor=Nutanix):
      - `EXCHANGE RATE · USD → AUD` — numeric input, 4 d.p., pre-filled from `/api/me` (user's last value); empty for brand-new users.
      - `MARGIN · %, 2 d.p.` — numeric input, 2 d.p., pre-filled from `/api/me`.
    - `CrmTemplateCallout` (emerald `#ecfdf5`/`#10b981`/`#047857`): label `CRM IMPORT TEMPLATE`, `AUTO` tag, value derived from the selected parser's `crm_template` ("Foreign Uplift" for every Nutanix parser).
    - Faint divider, then **Upload & parse** primary button (accent `#0077d4`). Disabled until vendor + file type + FX + margin + one file in dropzone are all valid.
    - Centered helper text under the button: "Output will automatically download once completed."

  `ResetButton` semantics (top-right of the page): clears the vendor select, file type select, **and** the dropped file. FX and margin inputs are wiped *visually* and then immediately repopulated from the user's remembered defaults (`/api/me` → `fx_rate`, `margin`). The user's stored defaults are not modified — only the form state. Recent Uploads is untouched.
  - **Right column** (flex 1):
    - `Dropzone` (180px tall, dashed border, cloud + arrow icon): single-file mode, accepts `.pdf` and `.xlsx`. Below it: `DRAG MULTIPLE FILES TO BATCH PARSE` helper (kept as the future-state hint per design, even though MVP is single-file).
    - On click of Upload & parse, the dropzone morphs in-place into `ProgressPanel` (V6 state): one file row with the filename, PDF/XLSX badge, a progress bar at parse-time, then "PARSED" green check on completion. The output xlsx triggers `window.location = response_blob_url` (or an anchor click) for the browser-native download. The dropzone returns to its empty state ~2s later.
    - `RecentUploadsTable` (bordered card, flex-grows to fill remaining height):
      - Header row: `RECENT UPLOADS` label + `LAST N` count on the right, slate-50 background (`#f8fafc`).
      - Column headers row (slate-50): FILE NAME, VENDOR, FILE TYPE, FX RATE, MARGIN, WHEN, FILES.
      - Body rows: ellipsised filename with PDF/XLSX badge, vendor, file type display name, FX (4 d.p., right-aligned, tabular-nums), margin (with % suffix), relative time, two icon buttons per row (download original = neutral icon-button; download CRM export = accent-tinted icon-button).
      - Pagination footer (slate-50): "SHOWING X – Y OF Z", prev-page chevron (disabled on page 1), numeric page buttons (active = inverted dark fill), next-page chevron. **Dynamic page size**: the right column is constrained to match the left settings card's height, so the table body's available height is computed at render and on resize. `pageSize = floor((bodyHeight - rowOffset) / rowHeight)` rounded down, minimum 1. The API call is `/api/history?limit={pageSize}&offset={page * pageSize}`. ResizeObserver recomputes on window resize; current page is preserved when possible.

**`LoginPage.tsx`**: matches the V4 design language exactly — same Inter typography, paper-tint background, AppHeader on top (logo only, no AccountChip), and a single centered bordered card. Username + password inputs styled as the `.sel` boxes from the wireframe (1.5px ink border, 8px radius), primary "Sign in" button styled as the accent-blue `.btn.primary`. Error chip on invalid credentials, dedicated 429 messaging ("Too many attempts — try again in N seconds"). After success, redirect to `/dashboard` (or `/change-password` if `must_change_password`).

**`ChangePasswordPage.tsx`**: same V4 visual language as `LoginPage.tsx` — AppHeader, paper-tint background, centered bordered card. Prompted automatically on first login of a fresh admin or any reset user. Old password + new password + confirm. Inline rules helper text under the new password field: "≥ 8 characters, must include an uppercase letter, a digit, and a symbol." Submit button stays disabled until all three rules + match check pass. On success → `/dashboard`. Back-button navigation away is blocked.

**`SettingsPage.tsx`** (admin only): a simple table of users with Add / Edit / Delete / Reset Password actions. Modal for create/edit. Self-delete is blocked client-side and server-side.

## Docker & Deployment

**Single container** that serves both the FastAPI API and the built React SPA from the same origin. No nginx inside the image — CORS isn't needed and an internal app at this volume doesn't benefit from a separate static-file server.

### Image structure

A single multi-stage `Dockerfile` at the repo root:

1. **Stage 1 — frontend build** (`node:20-alpine`): copy `frontend/`, run `npm ci && npm run build`. Output `frontend/dist/`.
2. **Stage 2 — runtime** (`python:3.12-slim`): install Python deps from `backend/pyproject.toml` (`fastapi`, `uvicorn[standard]`, `pdfplumber`, `openpyxl`, `pydantic>=2`, `python-multipart`, `bcrypt`, `sqlalchemy>=2`, `alembic`, `itsdangerous`, plus `pytest` + `httpx` for tests). Copy `backend/app/` into `/app/`. Copy the frontend build from stage 1 into `/app/static/`. Entrypoint runs `alembic upgrade head` then `uvicorn app.main:app --host 0.0.0.0 --port ${PORT} --proxy-headers --forwarded-allow-ips=${FORWARDED_ALLOW_IPS}`.

FastAPI app structure:
- `/api/*` → API routers.
- `/` → `StaticFiles(directory="static", html=True)` with an SPA fallback that returns `index.html` for any non-API path so client-side routing works on reload.

### docker-compose.yml (consumer-facing)

```yaml
services:
  bidparser:
    image: ghcr.io/<owner>/bidparser:latest
    container_name: bidparser
    ports:
      - "127.0.0.1:3447:3447"           # bound to loopback; expose via nginx-proxy-manager
    volumes:
      - ${DATA_DIR:-bidparser-data}:/data   # named volume by default, bind mount when DATA_DIR is set
    environment:
      - PORT=3447
      - BASE_URL=https://your-domain.com
      - SESSION_SECRET=replace-with-a-strong-random-secret
      - SESSION_LIFETIME_HOURS=12
      - BOOTSTRAP_ADMIN_USERNAME=admin
      - BOOTSTRAP_ADMIN_PASSWORD=changeme
      - RETENTION_DAYS=90
      - RATE_LIMIT_AUTH_PER_MIN=5
      - MAX_UPLOAD_MB=10
      - FORWARDED_ALLOW_IPS=*               # default; tighten to NPM's IP/CIDR in production
    restart: unless-stopped

volumes:
  bidparser-data:
```

### Single volume layout

```
/data
├── db.sqlite                         # SQLAlchemy DB (alembic-managed)
└── files/
    ├── originals/<uuid>.<ext>        # uploaded source files
    └── outputs/<uuid>.xlsx           # generated *_parsed.xlsx
```

Setting `DATA_DIR=/opt/bidparser/data` in the operator's `.env` swaps the named volume for a bind mount without any compose edit.

### Reverse proxy (nginx-proxy-manager)

App is intended to sit behind an upstream nginx-proxy-manager instance which handles TLS, HSTS, and routing. The container therefore:
- **Binds only to `127.0.0.1`** so it is unreachable from the public internet — only NPM (running on the same host) can proxy to it.
- **Honours `X-Forwarded-*` headers** via uvicorn's `--proxy-headers` flag and the `FORWARDED_ALLOW_IPS` setting. Rate limiting reads the real client IP from the forwarded chain, not the proxy IP.
- **Sets the session cookie `Secure` flag from `X-Forwarded-Proto`** — when NPM terminates TLS, the proxied request arrives as HTTP but the cookie still needs to be `Secure`. The auth middleware reads `request.url.scheme` after the proxy-headers middleware processes it.

Operator configuration on the NPM side (called out in the README):
- Proxy host: `http://127.0.0.1:3447` (or the docker network alias if NPM is dockerised on a shared network).
- **`client_max_body_size 10m`** — NPM defaults to 1 MB, which will silently reject the 10 MB uploads we allow. Set in the Advanced tab of the proxy host.
- Pass-through `X-Forwarded-*` headers is the NPM default; no custom config required.
- Force SSL + HSTS are recommended; both are NPM-side toggles.
- WebSocket support is not required by MVP.

### Build & release — GitHub Actions

`.github/workflows/build.yml`, triggered on:
- Push to `main` → tags `latest` + `sha-<7-char-sha>`.
- Push of a `v*` tag → tags the semver version + `latest`.

Steps:
1. Checkout.
2. `docker/setup-qemu-action` + `docker/setup-buildx-action` for multi-arch.
3. `docker/login-action` against `ghcr.io` using `${{ secrets.GITHUB_TOKEN }}`.
4. `docker/build-push-action` builds `linux/amd64,linux/arm64`, pushes to `ghcr.io/<owner>/bidparser` with the computed tags.
5. (Optional) `pytest` against the built image as a smoke test before the final push tag.

The image is **public on ghcr.io** unless the repo owner makes it private (a single click in package settings). Consumers pull with `docker compose pull && docker compose up -d`.

### Operator first-run

```sh
# 1. download just the compose file
curl -O https://raw.githubusercontent.com/<owner>/bidparser/main/docker-compose.yml

# 2. set required secret
echo "SESSION_SECRET=$(openssl rand -hex 32)" > .env

# 3. (optional) override defaults: BOOTSTRAP_ADMIN_PASSWORD, BASE_URL, DATA_DIR, FORWARDED_ALLOW_IPS

# 4. pull and run
docker compose pull
docker compose up -d

# 5. point nginx-proxy-manager at 127.0.0.1:3447, set client_max_body_size 10m, force SSL
```

Subsequent updates: `docker compose pull && docker compose up -d`. Alembic migrations run on container start so schema upgrades are automatic.

## Testing

Backend (`pytest -q` from `backend/` or `docker compose exec backend pytest -q`):

- **Parser tests** (one per format): golden JSON in `tests/expected/`, parametrised against the corresponding sample. Already designed; the existing CLAUDE.md per-format edge-case lists drive the test names.
- **Template writer test**: `test_template_writer.py` loads each committed `samples/outputs/XQ-*_parsed.xlsx`, feeds the equivalent `LineItem` list (built from the parser's golden output for that sample) into `template_writer.write(...)`, and asserts the produced workbook matches cell-by-cell (sheet name, headers, every populated cell coordinate, the `*` end-loop row).
- **Auth**: login success, wrong password, missing user, locked-out IP (5 attempts in <60s → 429), locked-out *username* (5 attempts on one username from different IPs → 429), `must_change_password` gate blocks `/api/parse` and unblocks after change-password. Password rules: rejects passwords < 8 chars, missing uppercase, missing digit, or missing symbol.
- **User admin**: non-admin gets 403 on `/users`, admin can CRUD, admin cannot delete self, admin cannot demote/delete the last remaining admin (409 Conflict), reset_password forces must_change_password=True and writes the literal `changeme` hash.
- **Parse API roundtrip**: log in, POST `/api/parse` with `XQ-4076249.pdf` + Nutanix/Software Only (PDF) + FX=1 + margin=5, assert returned file is `XQ-4076249_parsed.xlsx`, assert `X-Validation: match` header, assert a `ParseJob` row exists, assert subsequent `GET /api/me` returns the updated `fx_rate`/`margin` defaults.
- **History**: list returns the new job at top, source/output endpoints stream the right files, another user cannot see it.

Frontend (`npm run test` with Vitest; `npm run build` for the prod bundle):
- Component tests for the `VendorSelect → FileTypeSelect` cascade, the disabled state of Upload & parse, the `ResetButton` clearing all fields including the dropped file.
- One end-to-end happy-path test with Playwright optional (stretch).

## Verification — End-to-End

1. `cp .env.example .env`, then set `SESSION_SECRET=$(openssl rand -hex 32)` inside it.
2. Local dev: `docker compose up --build` (builds the Dockerfile locally). Released: `docker compose pull && docker compose up -d` (pulls the latest `ghcr.io/<owner>/bidparser:latest`).
3. Open `http://localhost:3447` (or whichever public URL NPM exposes) → land on `/login`.
4. Log in as `admin` / `changeme` → forced to `/change-password`. Set a new password ≥ 8 chars → land on `/dashboard`.
5. Vendor select: only "Nutanix" available. Choose it.
6. File Type select: shows all five Nutanix formats. Pick "Software Only (PDF)".
7. Enter FX `0.7354` (4 d.p.) and Margin `5.25` (2 d.p.). CRM IMPORT TEMPLATE callout shows "Foreign Uplift / AUTO".
8. Drop `samples/inputs/XQ-4076249.pdf` onto the dropzone. Upload & parse becomes enabled.
9. Click Upload & parse. Dropzone area morphs into a progress row showing the filename. ~1s later the file completes; browser downloads `XQ-4076249_parsed.xlsx`. A green toast confirms `Computed USD 1,625,358.51 = Quoted USD 1,625,358.51`.
10. Open the downloaded file: confirm column V = `0.7354`, column K = `5.25`, no other changes vs the committed sample output.
11. New row appears at the top of Recent Uploads with VENDOR=Nutanix, FILE TYPE=Software Only (PDF), FX=0.7354, MARGIN=5.25%, WHEN="just now".
12. Click the neutral download icon → original PDF re-downloads. Click the accent download icon → parsed XLSX re-downloads.
13. Click RESET → all fields cleared (vendor unset, file type unset, FX/margin cleared, dropzone empty). Recent Uploads untouched.
14. Re-log in next session: vendor unset, file type unset (RESET-style empty), but FX `0.7354` and Margin `5.25` are pre-filled (per-user remembered).
15. As admin, open `/settings`: create a non-admin user `salesperson1`. Log out, log in as `salesperson1` / `changeme`, accept the password rules (≥8 chars + uppercase + digit + symbol), parse a file, log out. Log back in as admin: confirm admin's history doesn't show salesperson1's parse (per-user isolation).
16. Test rate limit: fail 6 logins for one username (across different IPs is fine) within a minute → 6th returns 429 with a Retry-After header. Separately, fail 6 logins from one IP across different usernames → also 429.
17. Test failure UX: upload a corrupted PDF → red error banner appears inline in the dropzone area, no new row in Recent Uploads, dropzone resets on next interaction.
18. Test session expiry: wait 12 hours (or force-expire the cookie) → next API call returns 401 → redirected to /login.
19. Repeat step 8 with `samples/inputs/XQ-4108785.pdf` + Hardware Only (PDF) → confirm 11-row output, total `USD 22,491.87`, match toast.
20. Run `docker compose exec bidparser pytest -q /app/tests` — all tests pass inside the container. (Locally outside Docker: `cd backend && pytest -q`.)
21. CI smoke: every push to `main` triggers `.github/workflows/build.yml`, which builds the multi-arch image, runs `pytest`, and publishes to `ghcr.io/<owner>/bidparser:latest` + `:sha-<7>`.

## Critical Files

- [backend/app/main.py](backend/app/main.py) — FastAPI app + startup hook (bootstrap admin, retention task) + StaticFiles SPA mount.
- [backend/app/auth/sessions.py](backend/app/auth/sessions.py) — signed cookie implementation (hard 12h expiry, Secure flag derived from `X-Forwarded-Proto`).
- [backend/app/auth/rate_limit.py](backend/app/auth/rate_limit.py) — per-IP and per-username leaky-bucket limiter.
- [backend/app/models.py](backend/app/models.py) — User + ParseJob.
- [backend/app/api/routes_parse.py](backend/app/api/routes_parse.py) — `/parse`, `/history`, `/history/{id}/...`.
- [backend/app/api/routes_users.py](backend/app/api/routes_users.py) — admin user CRUD.
- [backend/app/api/routes_me.py](backend/app/api/routes_me.py) — per-user settings.
- [backend/app/output/template_writer.py](backend/app/output/template_writer.py) — implements [output_mapping.md](output_mapping.md).
- [backend/app/parsers/](backend/app/parsers/) — already designed; see CLAUDE.md.
- [frontend/src/App.tsx](frontend/src/App.tsx) — router + auth shell.
- [frontend/src/pages/DashboardPage.tsx](frontend/src/pages/DashboardPage.tsx) — V4 implementation.
- [frontend/src/pages/SettingsPage.tsx](frontend/src/pages/SettingsPage.tsx) — admin user CRUD.
- [frontend/src/design/tokens.ts](frontend/src/design/tokens.ts) — visual tokens from the wireframe.
- [Dockerfile](Dockerfile) — multi-stage build (Node FE build → Python runtime with FE bundled).
- [docker-compose.yml](docker-compose.yml) — single-service consumer-facing compose (image from ghcr.io).
- [.github/workflows/build.yml](.github/workflows/build.yml) — multi-arch build & push to ghcr.io.

## Extensibility — Adding the Next Supplier Format

To add e.g. a new Dell or Lenovo format (or a new Nutanix file type), the developer touches:

1. **One new parser module**: `backend/app/parsers/<slug>/parser.py` implementing `BaseParser` (declares `slug`, `display_name`, `vendor`, `accepted_mime`, `crm_template`, `parse()`, optional `detect()`).
2. **One registry entry**: append the class to `PARSER_REGISTRY` in `backend/app/parsers/registry.py`. This is the only registration point.
3. **One fixture + golden JSON**: drop the sample file under `backend/tests/fixtures/`, hand-validate the expected output, commit it as `backend/tests/expected/<sample>.json`. Add a parametrised test case in a new `test_<slug>.py`.
4. **Optionally a new spec markdown** (`<vendor>_<format>.md`) at the repo root mirroring the existing five Nutanix specs. CLAUDE.md gets a line linking to it.
5. **If the format maps to a new output template**, add an entry to `CRM_TEMPLATE_BY_VENDOR` (or define a new key) and implement the corresponding `template_writer` for that template. For any Nutanix file type the existing Foreign Uplift writer is reused — no template work needed.

The developer **does not touch**: API routes, frontend components (the dashboard dropdowns auto-populate from `GET /api/parsers`), Docker config, validation logic, auth, history, or any other parser. The Pluggable design is deliberate so the parser surface area is the entire change for a new format.

## Design Reference

The Claude Design handoff bundle is at `/tmp/design_unpack/bidparser/`. Implement directly against `bidparser/project/variants.jsx` lines 149–470 (the `V4_SidePanel` component). Read the chat transcript at `bidparser/chats/chat1.md` for the rationale behind each refinement (RESET button styling, emerald CRM-template callout, slate-50 table chrome, Inter typography). The other variants (`V1`–`V3`, `V5`, `V6`) and the design canvas chrome (`design-canvas.jsx`) are not part of MVP — V6's progress-panel pattern is the only thing we reuse, embedded inline in the dropzone area.

---
description: "Deployment, database management, migrations, cookie protection keyrings, background services, and versioning rules"
alwaysApply: true
---
# BidParser - Deployment & Operations Rules

This rule defines runtime configurations, database settings, containerized environments, retention schedules, and version release pipelines.

## 1. Database & Persistence Layer

- **SQLite WAL Mode**: The SQLite database uses a WAL (Write-Ahead Logging) connection interceptor configured at context initialization (`AddDbContextPool` in API startup).
- **Stamping Entity Timestamps**: Timestamps (`CreatedAt`, `UpdatedAt`) are stamped exclusively by `AppDbContext.StampTimestamps()`. Do not use `= DateTime.UtcNow` inline property initializers or manual property sets on EF entities.
- **Migrations**: Database migrations run at application startup inside the `MigratorHostedService` via `Database.MigrateAsync()`. No separate startup entrypoint script should be used.
- **Admin Seeding**: The first admin user is seeded at startup by `BootstrapAdminHostedService` when zero users exist. Credentials are read from `ADMIN_USERNAME` and `ADMIN_PASSWORD`.
- **Database File and Directory Defaults**:
  - SQLite database is placed at `/data/db.sqlite` inside the Docker container.
  - Uploaded files are stored under `/data/files/`.

---

## 2. Cookie Security & Keyring Persistence

- **Keyring Folder**: ASP.NET Core Data Protection keys are stored under `/data/dp-keys/`.
- **Keyring Persistence**: The `/data/dp-keys/` directory must be mapped to persistent storage (e.g., a Docker volume). Deleting or failing to mount this directory will destroy the cookie-encryption keys and immediately invalidate all user session cookies.
- **`SESSION_SECRET`**:
  - The `SESSION_SECRET` environment variable acts as the Data Protection app-name discriminator, **not** the cryptographic signature key (which is generated automatically inside `/data/dp-keys`).
  - Scoping or rotating `SESSION_SECRET` isolates sessions and effectively logs all users out.
  - Set a secure value for `SESSION_SECRET` in production. Local development defaults to `dev-only-change-me`.

---

## 3. Background Services & Retention

- **Retention Lifecycle**: `RetentionBackgroundService` runs on a 24-hour cadence (with a sleep-first start behavior).
- **Cleanup execution**: It calls `RetentionService.CleanupOldParseJobsAsync` to delete database records and associated disk files for jobs older than the configured `RETENTION_DAYS` (default is 90 days).

---

## 4. Environment Variables

Reference standard environment variables in deployment profiles:

| Variable | Description | Default |
|---|---|---|
| `ADMIN_USERNAME` | Seed username for the first admin | `admin` |
| `ADMIN_PASSWORD` | Seed password for the first admin | `changeme` |
| `MAX_UPLOAD_MB` | Maximum allowed upload size in MB | `10` |
| `RATE_LIMIT_AUTH_PER_MIN`| Rate limits for auth attempts per minute | `5` |
| `RETENTION_DAYS` | Storage duration of parsed files and logs | `90` |
| `SESSION_LIFETIME_HOURS` | Lifetime duration of session cookie | `12` |
| `SESSION_SECRET` | Session cookie discriminator | `dev-only-change-me` |

---

## 5. Release Versioning Rules

We adhere strictly to **Semantic Versioning 2.0.0**:
- **Format**: Release tags must be in the form `vMAJOR.MINOR.PATCH` (e.g., `v1.0.0`, `v0.1.0`).
- **Increments**:
  - `MAJOR`: Incompatible API contract, database schema, or configuration/deployment changes.
  - `MINOR`: Backwards-compatible functionality.
  - `PATCH`: Backwards-compatible bug fixes.
- **Prereleases**: Standard suffix additions are allowed (e.g., `v1.0.0-rc.1`).
- **CI/CD Workflow**: Pushing a `v*` tag triggers the GitHub Actions CI/CD pipeline (`.github/workflows/build.yml`), building a Docker image and publishing it to GHCR with the corresponding version tag. Pushing to `main` builds a `latest` and `sha-<short-sha>` image tag.

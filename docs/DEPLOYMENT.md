# Web Deployment Guide

BidParser ships as a single Docker container that serves the ASP.NET Core API and the React SPA from the same origin. No nginx or separate static-file server is required inside the image.

The portable Windows host is released separately and has no server deployment. See
[desktop.md](desktop.md) for packaging, public-repository prerequisites, and Windows acceptance.

## Quick Start

The repository is private, so copy `docker-compose.yml` to the target host yourself — from a clone
or over `scp`. There is no public URL to fetch it from.

Run everything below from the directory containing `docker-compose.yml`.

```sh
# 1. Create .env — SESSION_SECRET, SQL Server password, and proxy IP are required.
#    Replace the SA password with your own; the value below is a placeholder.
echo "SESSION_SECRET=$(openssl rand -hex 32)" > .env
echo "MSSQL_SA_PASSWORD=Change_This_Strong_P@ssw0rd" >> .env
echo "FORWARDED_ALLOW_IPS=127.0.0.1" >> .env

# 2. (Optional) Override defaults
# echo "ADMIN_PASSWORD=something-stronger" >> .env
# echo "DATA_DIR=/opt/bidparser/data" >> .env

# 3. Authenticate to the registry (the package is private, like the repository)
#    Use a GitHub personal access token with read:packages.
echo "$GITHUB_TOKEN" | docker login ghcr.io -u <github-username> --password-stdin

# 4. Pull and run
docker compose pull
docker compose up -d
```

> The `bidparser` service declares both an `image:` and a `build:` context. On a server that has
> only the compose file, always `docker compose pull` first — there is no source tree to build from.
> Developers running from a clone should do the opposite and `docker compose build` explicitly, so
> that `up` does not silently reuse a previously pulled production image. See
> [../CONTRIBUTING.md](../CONTRIBUTING.md#local-validation-docker).

The app is now listening on port `3447` on the Docker host. On first start it creates the database, runs migrations, and bootstraps an admin user (`admin` / `changeme` by default). The admin is forced to change their password on first login.

## Updating

```sh
docker compose pull
docker compose up -d
```

Schema migrations run automatically inside `MigratorHostedService` at startup, so upgrades are applied without manual steps.

## Environment Variables

All configuration is via environment variables. Set them in a `.env` file next to `docker-compose.yml`.

| Variable | Default | Description |
|---|---|---|
| `SESSION_SECRET` | _(required)_ | Data Protection app-name discriminator. **Not** a cryptographic signing key — the actual signing material is the keyring in `/data/dp-keys`. Generate with `openssl rand -hex 32`. Changing this value scopes new cookies away from old ones (effectively logs everyone out) and makes the stored Dell API secret unreadable until an admin re-enters it; the keyring is what must be deleted for a hard session reset. |
| `ADMIN_USERNAME` | `admin` | Username for the initial admin user (only used on first run when no users exist). |
| `ADMIN_PASSWORD` | `changeme` | Password for the initial admin user (only used on first run). |
| `TZ` | `Australia/Sydney` | Controls server-local time used for the Utilisation Dashboard's daily buckets. Set to your operating timezone in `.env` if different. |
| `SESSION_LIFETIME_HOURS` | `12` | Hard session expiry from login. No sliding refresh. |
| `RETENTION_DAYS` | `90` | Uploaded files and parse history older than this are deleted daily. |
| `RATE_LIMIT_AUTH_PER_MIN` | `5` | Max login/change-password attempts per minute per IP and per username. |
| `MAX_UPLOAD_MB` | `10` | Maximum upload file size. |
| `DB_CONNECTION_STRING` | _(assembled by compose)_ | SQL Server connection string. Set automatically by `docker-compose.yml` from `MSSQL_SA_PASSWORD` and `MSSQL_DB`. Override only if pointing at an external SQL Server instance. |
| `MSSQL_SA_PASSWORD` | _(required)_ | SA password for the bundled SQL Server container. Must meet SQL Server complexity requirements (≥8 chars, upper + lower + digit + symbol). |
| `MSSQL_DB` | `bidparser` | Database name created inside the SQL Server container. |
| `DATA_DIR` | _(named volume)_ | Set to a host path (e.g. `/opt/bidparser/data`) to use a bind mount instead of a Docker named volume. |
| `FORWARDED_ALLOW_IPS` | _(required)_ | Comma-separated IPs trusted for `X-Forwarded-*` headers. Set this to the reverse proxy IP address as seen by the app container. |

## Data Volume

Persistent state is split across two volumes:

**App container (`/data`)** — named volume `bidparser-data`, or a `DATA_DIR` bind mount:

```
/data
├── dp-keys/                          # ASP.NET Core Data Protection keyring
└── files/
    ├── originals/<uuid>.<ext>        # Uploaded source files
    └── outputs/<uuid>.xlsx           # Generated output files (<stem>_<FileToken>.xlsx)
```

**SQL Server container (`/var/opt/mssql`)** — named volume `bidparser-mssql-data`. Contains the SQL Server database files. Managed entirely by SQL Server; do not bind-mount a path shared with the app container.

**`/data/dp-keys` must persist across container restarts.** This directory holds the Data Protection keyring — the cryptographic material used to protect session cookies. If it is deleted or not mounted, the keyring regenerates on next start and all existing sessions become invalid (everyone is logged out).

By default `docker-compose.yml` uses Docker named volumes (`bidparser-data` and `bidparser-mssql-data`), which Docker creates automatically if they do not exist. To use a bind mount for app files, set `DATA_DIR` in your `.env`:

```sh
echo "DATA_DIR=/opt/bidparser/data" >> .env
```

## Dell Quote API

Dell Quote API settings are managed by an administrator under **Settings → Dell API** and stored in the singleton `dell_api_settings` database row. The client secret is write-only in the browser and encrypted before it is stored; leaving the secret field blank keeps the current value. Use **Test connection** after saving to verify the token endpoint and credentials.

**API version** is sent as Dell's `Accepts-version` request header and defaults to `4.0`. Dell assumes an unspecified default version when the header is omitted, and the parsers rely on response fields introduced after v1, so leave this pinned unless Dell publishes a newer version and the payload has been re-verified against `samples/inputs/Dell_*.json`. Existing installations are backfilled to `4.0` by the `AddDellApiVersion` migration.

Secret protection uses the same ASP.NET Core Data Protection keyring persisted in `/data/dp-keys`. Keep the `/data` volume and its keyring across deployments. Rotating `SESSION_SECRET` changes the Data Protection application discriminator, so the existing Dell client secret can no longer be decrypted even when the keyring is intact. After a rotation, an administrator must re-enter and save the Dell client secret under **Settings → Dell API**.

### Hard session reset

To invalidate all active sessions (e.g. after a security incident):

```sh
docker compose down
# If using named volume:
docker volume rm bidparser-data
# If using bind mount, delete dp-keys from your DATA_DIR:
# rm -rf /opt/bidparser/data/dp-keys
docker compose up -d
```

The keyring regenerates on next start. All users will need to log in again.

## Reverse Proxy

The compose file publishes container port `3447` to host port `3447`. The application serves plain
HTTP and does not terminate TLS itself, so put it behind a reverse proxy in any deployment that is
not purely local.

The settings below are written for nginx-proxy-manager (NPM), which is what the current deployment
uses; the same three requirements — a large enough body limit, pass-through of `X-Forwarded-*`, and
a matching `FORWARDED_ALLOW_IPS` — apply to any proxy.

### NPM configuration

1. **Proxy host**: point to `http://<docker-host-ip>:3447`, or to the Docker network alias if NPM runs on a shared Docker network.
2. **`client_max_body_size 10m`**: NPM defaults to 1 MB, which silently rejects the 10 MB uploads the app allows. Set this in the **Advanced** tab of the proxy host.
3. **Force SSL + HSTS**: recommended; both are NPM-side toggles.
4. **`X-Forwarded-*` headers**: passed through by default in NPM — no custom config needed. The app reads `X-Forwarded-Proto` to decide whether to set `Secure` on session cookies.
5. **WebSocket support**: not required.

### Security notes

- Session cookies are issued with `Secure=True` only when the request arrives over HTTPS (detected via `X-Forwarded-Proto` after `ForwardedHeadersMiddleware` processing). Local HTTP development keeps `Secure=False` so the browser accepts the cookie.
- Rate limiting reads the real client IP from the trusted `X-Forwarded-For` chain, not the proxy IP.
- `FORWARDED_ALLOW_IPS` must be explicit in production. Do not use `*`; untrusted forwarded headers are ignored.

## Building the Image Locally

```sh
# From the repository root
docker compose build

# Or build directly
docker build -t bidparser:local .
```

Then update `docker-compose.yml` to use `image: bidparser:local` instead of the ghcr.io reference.

## Local Development

Run backend and frontend separately without Docker:

```sh
# Backend (ASP.NET Core, defaults to http://localhost:5000)
dotnet run --project src/BidParser.Api

# Frontend (Vite dev server, proxies /api to http://127.0.0.1:5000)
cd frontend && npm run dev
```

The Vite proxy target can be overridden: `VITE_API_PROXY_TARGET=http://127.0.0.1:5000 npm run dev`.

The backend still needs a reachable SQL Server. Either set `DB_CONNECTION_STRING` to an existing
instance, or start only the bundled database container:

```sh
docker compose up -d mssql
```

See [../README.md](../README.md) for the wider getting-started walkthrough and
[troubleshooting.md](troubleshooting.md) when something does not start.

## First Login Walkthrough

1. Open `http://localhost:3447` (or your proxied domain) — you land on `/login`.
2. Log in with the bootstrap admin credentials (`admin` / `changeme` unless you overrode
   `ADMIN_USERNAME` / `ADMIN_PASSWORD`) — you are redirected to `/change-password`.
3. Set a new password (minimum 8 characters, must include an uppercase letter, a digit, and a symbol).
4. You land on the dashboard. Create additional users from the admin menu under **Users**.

Creating a user generates a **random one-time temporary password**, returned once in the response
and shown to the admin to hand over; there is no shared default credential. The new user must
change it on first login. Resetting a password works the same way, and also invalidates every
existing session belonging to that user.

The bootstrap admin is seeded **only when the users table is empty**. Once any user exists,
`ADMIN_USERNAME` and `ADMIN_PASSWORD` are ignored — they cannot be used to reset a forgotten admin
password.

## CI/CD (GitHub Actions)

The workflow at `.github/workflows/build.yml` gates web and desktop artifacts by event:

| Event | Job that runs | Result |
|---|---|---|
| Pull request to `main` | `validate`, `validate-windows` | Frontend/full .NET suite on GitHub-hosted `ubuntu-latest` plus desktop build/tests, one-file publish verification, and launch smoke on `windows-latest`. No artifact published. |
| Push / merge to `main` | *(none)* | No artifact publication. |
| Valid `v*` tag on `main` | `prepare-publish`, `build-and-push`, `desktop-build`, `release` | Validates SemVer/main ancestry, publishes the versioned Docker image, builds/tests/verifies one Windows `BidParser.exe`, then creates the same-repository GitHub Release with the EXE and checksum. |

Pushing to a feature branch with no open pull request runs nothing.

Documentation-only changes (`**/*.md`, `docs/**`, `README*`) are excluded from pull-request
validation. Tag pushes are not path-filtered, so a release tag can never be silently skipped.

`prepare-publish` rejects malformed SemVer tags and tags whose commit is not on `main`. Its one
version stamps API/Desktop assembly metadata, the frontend footer, Docker build, and the GitHub
Release. Tag builds remove one leading `v`.

### Hosted runner requirement

Linux validation, version gating, Docker publishing, and release coordination run on GitHub-hosted
`ubuntu-latest`; desktop validation and publishing run on GitHub-hosted `windows-latest`. No
self-hosted runner, LXC, Podman service, Docker socket, or runner-host SDK installation is needed.

Private repositories consume GitHub-hosted Actions minutes. The workflow uses ordinary `docker
build` and pushes only tags generated for the triggering event, keeping the published image set
explicit.

Publishing to GHCR and creating the desktop release use the repository `GITHUB_TOKEN`, scoped to
`packages: write` and `contents: write` respectively. The tracked `config/` documents must be
present before tagging; Windows hosted-runner minutes must be available. Release tags must follow
Semantic Versioning 2.0.0 and point to `main`, for example `v1.0.0` or `v1.2.3`.

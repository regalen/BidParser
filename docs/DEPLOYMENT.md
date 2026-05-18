# Deployment Guide

BidParser ships as a single Docker container that serves the ASP.NET Core API and the React SPA from the same origin. No nginx or separate static-file server is required inside the image.

## Quick Start

```sh
# 1. Download the compose file
curl -O https://raw.githubusercontent.com/regalen/bidparser/main/docker-compose.yml

# 2. Create .env with a session secret
echo "SESSION_SECRET=$(openssl rand -hex 32)" > .env

# 3. (Optional) Override defaults
# echo "ADMIN_PASSWORD=something-stronger" >> .env
# echo "DATA_DIR=/opt/bidparser/data" >> .env

# 4. Pull and run
docker compose pull
docker compose up -d
```

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
| `SESSION_SECRET` | _(required)_ | Data Protection app-name discriminator. **Not** a cryptographic signing key — the actual signing material is the keyring in `/data/dp-keys`. Generate with `openssl rand -hex 32`. Changing this value scopes new cookies away from old ones (effectively logs everyone out), but the keyring is what must be deleted for a hard reset. |
| `ADMIN_USERNAME` | `admin` | Username for the initial admin user (only used on first run when no users exist). |
| `ADMIN_PASSWORD` | `changeme` | Password for the initial admin user (only used on first run). |
| `SESSION_LIFETIME_HOURS` | `12` | Hard session expiry from login. No sliding refresh. |
| `RETENTION_DAYS` | `90` | Uploaded files and parse history older than this are deleted daily. |
| `RATE_LIMIT_AUTH_PER_MIN` | `5` | Max login/change-password attempts per minute per IP and per username. |
| `MAX_UPLOAD_MB` | `10` | Maximum upload file size. |
| `DATABASE_URL` | `sqlite:///data/db.sqlite` | SQLite connection URL. Relative paths resolve inside the container. |
| `DATA_DIR` | _(named volume)_ | Set to a host path (e.g. `/opt/bidparser/data`) to use a bind mount instead of a Docker named volume. |
| `FORWARDED_ALLOW_IPS` | `*` | IPs trusted for `X-Forwarded-*` headers. Tighten to your reverse proxy's IP/CIDR in production. |

## Data Volume

All persistent state lives under `/data` inside the container:

```
/data
├── db.sqlite                         # SQLite database (EF Core managed)
├── dp-keys/                          # ASP.NET Core Data Protection keyring
└── files/
    ├── originals/<uuid>.<ext>        # Uploaded source files
    └── outputs/<uuid>.xlsx           # Generated *_parsed.xlsx files
```

**`/data/dp-keys` must persist across container restarts.** This directory holds the Data Protection keyring — the cryptographic material used to protect session cookies. If it is deleted or not mounted, the keyring regenerates on next start and all existing sessions become invalid (everyone is logged out).

By default `docker-compose.yml` uses a Docker named volume (`bidparser-data`), which Docker creates automatically if it does not exist. To use a bind mount instead, set `DATA_DIR` in your `.env`:

```sh
echo "DATA_DIR=/opt/bidparser/data" >> .env
```

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

## Reverse Proxy (nginx-proxy-manager)

The compose file publishes container port `3447` to host port `3447`. Place it behind a reverse proxy (nginx-proxy-manager or similar) for TLS termination.

### NPM configuration

1. **Proxy host**: point to `http://<docker-host-ip>:3447`, or to the Docker network alias if NPM runs on a shared Docker network.
2. **`client_max_body_size 10m`**: NPM defaults to 1 MB, which silently rejects the 10 MB uploads the app allows. Set this in the **Advanced** tab of the proxy host.
3. **Force SSL + HSTS**: recommended; both are NPM-side toggles.
4. **`X-Forwarded-*` headers**: passed through by default in NPM — no custom config needed. The app reads `X-Forwarded-Proto` to decide whether to set `Secure` on session cookies.
5. **WebSocket support**: not required.

### Security notes

- Session cookies are issued with `Secure=True` only when the request arrives over HTTPS (detected via `X-Forwarded-Proto` after `ForwardedHeadersMiddleware` processing). Local HTTP development keeps `Secure=False` so the browser accepts the cookie.
- Rate limiting reads the real client IP from the `X-Forwarded-For` chain, not the proxy IP.
- Consider tightening `FORWARDED_ALLOW_IPS` to your proxy's IP/CIDR rather than leaving it as `*`.

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

## First Login Walkthrough

1. Open `http://localhost:3447` (or your NPM-proxied domain) — you land on `/login`.
2. Log in as `admin` / `changeme` — you are redirected to `/change-password`.
3. Set a new password (minimum 8 characters, must include an uppercase letter, a digit, and a symbol).
4. You land on the dashboard. Create additional users via **Settings** (admin only).

New users are created with the password `changeme` and are forced to change it on first login, same as the bootstrap admin.

## CI/CD (GitHub Actions)

The workflow at `.github/workflows/build.yml` builds multi-arch (`linux/amd64`, `linux/arm64`) images and pushes to ghcr.io. It triggers on:

- Push to `main` — tags `latest` + `sha-<short-sha>`
- Push of a `v*` SemVer tag — tags the SemVer version + `latest`

No manual setup is required beyond the default `GITHUB_TOKEN` permissions for packages. Release tags must follow Semantic Versioning 2.0.0, for example `v0.1.0`, `v1.0.0`, or `v1.2.3`.

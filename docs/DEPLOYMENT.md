# Deployment Guide

BidParser ships as a single Docker container that serves the FastAPI API and the React SPA from the same origin. No nginx or separate static-file server is required inside the image.

## Quick Start

```sh
# 1. Download the compose file
curl -O https://raw.githubusercontent.com/regalen/bidparser/main/docker-compose.yml

# 2. Create .env with a session secret
echo "SESSION_SECRET=$(openssl rand -hex 32)" > .env

# 3. (Optional) Override defaults
# echo "BOOTSTRAP_ADMIN_PASSWORD=something-stronger" >> .env
# echo "DATA_DIR=/opt/bidparser/data" >> .env

# 4. Pull and run
docker compose pull
docker compose up -d
```

The app is now listening on `127.0.0.1:3447`. On first start it creates the database, runs migrations, and bootstraps an admin user (`admin` / `changeme` by default). The admin is forced to change their password on first login.

## Updating

```sh
docker compose pull
docker compose up -d
```

Alembic migrations run automatically on container start, so schema upgrades are applied without manual steps.

## Environment Variables

All configuration is via environment variables. Set them in a `.env` file next to `docker-compose.yml`.

| Variable | Default | Description |
|---|---|---|
| `SESSION_SECRET` | _(required)_ | HMAC-SHA256 signing key for session cookies. Generate with `openssl rand -hex 32`. |
| `BOOTSTRAP_ADMIN_USERNAME` | `admin` | Username for the initial admin user (only used on first run). |
| `BOOTSTRAP_ADMIN_PASSWORD` | `changeme` | Password for the initial admin user (only used on first run). |
| `SESSION_LIFETIME_HOURS` | `12` | Hard session expiry from login. No sliding refresh. |
| `RETENTION_DAYS` | `90` | Uploaded files and parse history older than this are deleted daily. |
| `RATE_LIMIT_AUTH_PER_MIN` | `5` | Max login/change-password attempts per minute per IP and per username. |
| `MAX_UPLOAD_MB` | `10` | Maximum upload file size. |
| `DATA_DIR` | _(named volume)_ | Set to a host path (e.g. `/opt/bidparser/data`) to use a bind mount instead of a Docker named volume. |
| `FORWARDED_ALLOW_IPS` | `*` | IPs trusted for `X-Forwarded-*` headers. Tighten to your reverse proxy's IP/CIDR in production. |
| `BASE_URL` | `http://localhost:3447` | Public URL of the app. Used for constructing absolute URLs behind a reverse proxy. |

## Data Volume

All persistent state lives under `/data` inside the container:

```
/data
├── db.sqlite                         # SQLite database (Alembic-managed)
└── files/
    ├── originals/<uuid>.<ext>        # Uploaded source files
    └── outputs/<uuid>.xlsx           # Generated *_parsed.xlsx files
```

By default `docker-compose.yml` uses a Docker named volume (`bidparser-data`). To use a bind mount instead, set `DATA_DIR` in your `.env`:

```sh
echo "DATA_DIR=/opt/bidparser/data" >> .env
```

## Reverse Proxy (nginx-proxy-manager)

The container binds only to `127.0.0.1:3447` — it is not directly reachable from the network. Place it behind a reverse proxy (nginx-proxy-manager or similar) for TLS termination.

### NPM configuration

1. **Proxy host**: point to `http://127.0.0.1:3447` (or the Docker network alias if NPM runs on a shared Docker network).
2. **`client_max_body_size 10m`**: NPM defaults to 1 MB, which silently rejects the 10 MB uploads the app allows. Set this in the **Advanced** tab of the proxy host.
3. **Force SSL + HSTS**: recommended; both are NPM-side toggles.
4. **`X-Forwarded-*` headers**: passed through by default in NPM — no custom config needed. The app reads `X-Forwarded-Proto` to decide whether to set `Secure` on session cookies.
5. **WebSocket support**: not required.

### Security notes

- Session cookies are issued with `Secure=True` only when the request arrives over HTTPS (detected via `X-Forwarded-Proto` after proxy-header processing). Local HTTP development keeps `Secure=False` so the browser accepts the cookie.
- Rate limiting reads the real client IP from the `X-Forwarded-For` chain, not the proxy IP.
- Consider tightening `FORWARDED_ALLOW_IPS` to your proxy's IP/CIDR rather than leaving it as `*`.

## Building the Image Locally

If you want to build the image yourself instead of pulling from ghcr.io:

```sh
# From the repository root
docker compose build

# Or build directly
docker build -t bidparser:local .
```

Then update `docker-compose.yml` to use `image: bidparser:local` instead of the ghcr.io reference.

## First Login Walkthrough

1. Open `http://localhost:3447` (or your NPM-proxied domain) — you land on `/login`.
2. Log in as `admin` / `changeme` — you are redirected to `/change-password`.
3. Set a new password (minimum 8 characters, must include an uppercase letter, a digit, and a symbol).
4. You land on the dashboard. Create additional users via **Settings** (admin only).

New users are created with the password `changeme` and are forced to change it on first login, same as the bootstrap admin.

## CI/CD (GitHub Actions)

A workflow is scaffolded at `.github/workflows/build.yml` that builds multi-arch (`linux/amd64`, `linux/arm64`) images and pushes to ghcr.io. It triggers on:

- Push to `main` — tags `latest` + `sha-<short-sha>`
- Push of a `v*` tag — tags the semver version + `latest`

This workflow is **deferred** — it will begin publishing images once the repository is pushed to GitHub with Actions enabled. No manual setup is required beyond the default `GITHUB_TOKEN` permissions for packages.

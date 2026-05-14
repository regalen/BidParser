# Stage 1 — build frontend
FROM node:20-alpine AS frontend-build
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2 — Python runtime with built frontend
FROM python:3.12-slim AS runtime

# System deps for pdfplumber (pdfminer.six uses no native libs) and general health
RUN apt-get update && apt-get install -y --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Install Python dependencies
COPY backend/pyproject.toml .
RUN pip install --no-cache-dir .

# Copy backend application code
COPY backend/app ./app
COPY backend/alembic.ini .
COPY backend/alembic ./alembic

# Copy built frontend into /app/static/
COPY --from=frontend-build /build/dist ./static

# Data volume mount point
VOLUME /data

ENV PORT=3447
EXPOSE ${PORT}

# Entrypoint: run migrations then start uvicorn
COPY <<'EOF' /app/entrypoint.sh
#!/bin/sh
set -e
cd /app
python -m alembic upgrade head
exec python -m uvicorn app.main:app \
    --host 0.0.0.0 \
    --port "${PORT:-3447}" \
    --proxy-headers \
    --forwarded-allow-ips "${FORWARDED_ALLOW_IPS:-*}"
EOF
RUN chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]

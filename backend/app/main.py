from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from sqlalchemy import func, select

from app.api import routes_auth, routes_me, routes_parse, routes_users
from app.auth.passwords import hash_password
from app.config import get_settings
from app.db import SessionLocal, engine
from app.models import Base, User
from app.services.fx_rates import fetch_bloomberg_aud_usd_rate, update_user_default_fx_rates
from app.services.retention import cleanup_old_parse_jobs
from app.storage import ensure_storage_dirs

logger = logging.getLogger(__name__)

STATIC_DIR = Path(__file__).resolve().parent.parent / "static"
_RETENTION_INTERVAL = 24 * 60 * 60  # once per day
_FX_RATE_INTERVAL = 24 * 60 * 60  # once per day


async def _retention_loop() -> None:
    while True:
        await asyncio.sleep(_RETENTION_INTERVAL)
        try:
            with SessionLocal() as db:
                deleted = cleanup_old_parse_jobs(db)
            if deleted:
                logger.info("Retention cleanup: removed %d expired parse jobs", deleted)
        except Exception:
            logger.exception("Retention cleanup failed")


async def _fx_rate_loop() -> None:
    while True:
        try:
            rate = await asyncio.to_thread(fetch_bloomberg_aud_usd_rate)
            with SessionLocal() as db:
                updated = update_user_default_fx_rates(db, rate)
            logger.info("Updated %d user default FX rates to AUD:USD %s", updated, rate)
        except Exception:
            logger.exception("Daily Bloomberg AUD:USD refresh failed")
        await asyncio.sleep(_FX_RATE_INTERVAL)


@asynccontextmanager
async def lifespan(app: FastAPI):
    ensure_storage_dirs()
    Base.metadata.create_all(bind=engine)
    bootstrap_admin()
    retention_task = asyncio.create_task(_retention_loop())
    fx_rate_task = asyncio.create_task(_fx_rate_loop())
    yield
    retention_task.cancel()
    fx_rate_task.cancel()


app = FastAPI(title="BidParser API", lifespan=lifespan)

app.include_router(routes_auth.router, prefix="/api")
app.include_router(routes_me.router, prefix="/api")
app.include_router(routes_users.router, prefix="/api")
app.include_router(routes_parse.router, prefix="/api")

if STATIC_DIR.is_dir():
    app.mount("/assets", StaticFiles(directory=STATIC_DIR / "assets"), name="static-assets")

    @app.get("/{full_path:path}")
    async def spa_fallback(full_path: str):
        file = STATIC_DIR / full_path
        if file.is_file():
            return FileResponse(file)
        return FileResponse(STATIC_DIR / "index.html")


def bootstrap_admin() -> None:
    settings = get_settings()
    with SessionLocal() as db:
        user_count = db.scalar(select(func.count()).select_from(User)) or 0
        if user_count:
            return
        admin = User(
            username=settings.bootstrap_admin_username,
            name="Administrator",
            password_hash=hash_password(settings.bootstrap_admin_password),
            role="admin",
            must_change_password=True,
        )
        db.add(admin)
        db.commit()

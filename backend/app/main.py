from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI
from sqlalchemy import func, select

from app.api import routes_auth, routes_me, routes_parse, routes_users
from app.auth.passwords import hash_password
from app.config import get_settings
from app.db import SessionLocal, engine
from app.models import Base, User
from app.storage import ensure_storage_dirs


@asynccontextmanager
async def lifespan(app: FastAPI):
    ensure_storage_dirs()
    Base.metadata.create_all(bind=engine)
    bootstrap_admin()
    yield


app = FastAPI(title="BidParser API", lifespan=lifespan)
api = FastAPI(title="BidParser API")

app.include_router(routes_auth.router, prefix="/api")
app.include_router(routes_me.router, prefix="/api")
app.include_router(routes_users.router, prefix="/api")
app.include_router(routes_parse.router, prefix="/api")


def bootstrap_admin() -> None:
    settings = get_settings()
    with SessionLocal() as db:
        user_count = db.scalar(select(func.count()).select_from(User)) or 0
        if user_count:
            return
        admin = User(
            username=settings.bootstrap_admin_username,
            password_hash=hash_password(settings.bootstrap_admin_password),
            role="admin",
            must_change_password=True,
        )
        db.add(admin)
        db.commit()

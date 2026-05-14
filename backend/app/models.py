from __future__ import annotations

from datetime import datetime, timezone
from decimal import Decimal
from typing import Literal

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, Numeric, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class User(Base):
    __tablename__ = "users"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    username: Mapped[str] = mapped_column(String(128), unique=True, index=True, nullable=False)
    password_hash: Mapped[str] = mapped_column(String(255), nullable=False)
    role: Mapped[Literal["admin", "user"]] = mapped_column(String(16), nullable=False, default="user")
    must_change_password: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True)
    fx_rate: Mapped[Decimal | None] = mapped_column(Numeric(12, 4), nullable=True)
    margin: Mapped[Decimal | None] = mapped_column(Numeric(12, 2), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow)

    parse_jobs: Mapped[list[ParseJob]] = relationship(back_populates="user", cascade="all, delete-orphan")


class ParseJob(Base):
    __tablename__ = "parse_jobs"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    user_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False, index=True)
    vendor: Mapped[str] = mapped_column(String(64), nullable=False)
    parser_slug: Mapped[str] = mapped_column(String(128), nullable=False)
    source_filename: Mapped[str] = mapped_column(String(255), nullable=False)
    source_path: Mapped[str] = mapped_column(String(1024), nullable=False)
    output_path: Mapped[str] = mapped_column(String(1024), nullable=False)
    fx_rate: Mapped[Decimal] = mapped_column(Numeric(12, 4), nullable=False)
    margin: Mapped[Decimal] = mapped_column(Numeric(12, 2), nullable=False)
    computed_total: Mapped[Decimal] = mapped_column(Numeric(14, 2), nullable=False)
    quoted_total: Mapped[Decimal | None] = mapped_column(Numeric(14, 2), nullable=True)
    totals_match: Mapped[bool] = mapped_column(Boolean, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow, server_default=func.now())

    user: Mapped[User] = relationship(back_populates="parse_jobs")

from __future__ import annotations

from datetime import datetime
from decimal import Decimal
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


class UserPublic(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    username: str
    name: str | None = None
    role: Literal["admin", "user"]
    must_change_password: bool
    default_vendor: str | None = None
    fx_rate_pegged: bool = False
    fx_rate: Decimal | None = None
    fx_rate_updated_at: datetime | None = None
    margin: Decimal | None = None
    created_at: datetime | None = None


class LoginRequest(BaseModel):
    username: str = Field(min_length=1)
    password: str = Field(min_length=1)


class LoginResponse(BaseModel):
    user: UserPublic


class ChangePasswordRequest(BaseModel):
    old_password: str
    new_password: str


class SettingsUpdate(BaseModel):
    default_vendor: str | None = Field(default=None, min_length=1, max_length=64)
    fx_rate_pegged: bool | None = None
    fx_rate: Decimal | None = Field(default=None, ge=0)
    margin: Decimal | None = Field(default=None, ge=0)


class UserCreate(BaseModel):
    username: str = Field(min_length=1, max_length=128)
    name: str = Field(min_length=1, max_length=255)
    role: Literal["admin", "user"] = "user"


class UserUpdate(BaseModel):
    username: str | None = Field(default=None, min_length=1, max_length=128)
    name: str | None = Field(default=None, min_length=1, max_length=255)
    role: Literal["admin", "user"] | None = None
    reset_password: bool = False


class ParserInfo(BaseModel):
    slug: str
    display_name: str
    vendor: str
    accepted_mime: str
    crm_template: str


class HistoryRow(BaseModel):
    id: int
    source_filename: str
    vendor: str
    parser_slug: str
    file_type_display: str
    fx_rate: Decimal
    margin: Decimal
    when: str
    totals_match: bool


class HistoryResponse(BaseModel):
    rows: list[HistoryRow]
    total: int

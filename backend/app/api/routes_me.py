from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.orm import Session

from app.api.schemas import SettingsUpdate, UserPublic
from app.auth.deps import current_user, require_active_user, require_csrf
from app.db import get_db
from app.models import User
from app.parsers.registry import PARSER_REGISTRY


router = APIRouter(tags=["me"])


@router.get("/me", response_model=UserPublic)
def me(user: User = Depends(current_user)) -> User:
    return user


@router.patch("/me/settings", response_model=UserPublic, dependencies=[Depends(require_csrf)])
def update_settings(payload: SettingsUpdate, user: User = Depends(require_active_user), db: Session = Depends(get_db)) -> User:
    if payload.default_vendor is not None:
        default_vendor = payload.default_vendor.strip()
        known_vendors = {parser.vendor for parser in PARSER_REGISTRY}
        if default_vendor not in known_vendors:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Unknown vendor.")
        user.default_vendor = default_vendor
    if payload.fx_rate_pegged is not None:
        user.fx_rate_pegged = payload.fx_rate_pegged
    if payload.fx_rate is not None:
        user.fx_rate = payload.fx_rate
    if payload.margin is not None:
        user.margin = payload.margin
    db.add(user)
    db.commit()
    db.refresh(user)
    return user

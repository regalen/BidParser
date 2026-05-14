from __future__ import annotations

from fastapi import Depends, Header, HTTPException, Request, status
from sqlalchemy.orm import Session

from app.auth.sessions import COOKIE_NAME, read_session_token
from app.db import get_db
from app.models import User


def require_csrf(x_requested_with: str | None = Header(default=None)) -> None:
    if x_requested_with != "BidParser":
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="csrf_required")


def current_user(request: Request, db: Session = Depends(get_db)) -> User:
    payload = read_session_token(request.cookies.get(COOKIE_NAME))
    if payload is None:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="not_authenticated")
    user = db.get(User, int(payload["user_id"]))
    if user is None:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="not_authenticated")
    return user


def require_active_user(user: User = Depends(current_user)) -> User:
    if user.must_change_password:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="password_change_required")
    return user


def require_admin(user: User = Depends(require_active_user)) -> User:
    if user.role != "admin":
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="admin_required")
    return user

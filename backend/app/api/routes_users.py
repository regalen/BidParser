from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.api.schemas import UserCreate, UserPublic, UserUpdate
from app.auth.deps import require_admin, require_csrf
from app.auth.passwords import hash_password
from app.db import get_db
from app.models import User


router = APIRouter(prefix="/users", tags=["users"])


@router.get("", response_model=list[UserPublic])
def list_users(admin: User = Depends(require_admin), db: Session = Depends(get_db)) -> list[User]:
    return list(db.scalars(select(User).order_by(User.username)).all())


@router.post("", response_model=UserPublic, dependencies=[Depends(require_csrf)])
def create_user(payload: UserCreate, admin: User = Depends(require_admin), db: Session = Depends(get_db)) -> User:
    username = payload.username.strip()
    _ensure_username_available(db, username)
    user = User(
        username=username,
        name=payload.name.strip(),
        role=payload.role,
        password_hash=hash_password("changeme"),
        must_change_password=True,
    )
    db.add(user)
    db.commit()
    db.refresh(user)
    return user


@router.patch("/{user_id}", response_model=UserPublic, dependencies=[Depends(require_csrf)])
def update_user(user_id: int, payload: UserUpdate, admin: User = Depends(require_admin), db: Session = Depends(get_db)) -> User:
    user = db.get(User, user_id)
    if user is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found.")
    if payload.username is not None and payload.username.strip().lower() != user.username.lower():
        _ensure_username_available(db, payload.username.strip())
        user.username = payload.username.strip()
    if payload.name is not None:
        user.name = payload.name.strip()
    if payload.role is not None and payload.role != user.role:
        if user.role == "admin" and payload.role != "admin" and _admin_count(db) <= 1:
            raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Cannot remove the last admin.")
        user.role = payload.role
    if payload.reset_password:
        user.password_hash = hash_password("changeme")
        user.must_change_password = True
    db.add(user)
    db.commit()
    db.refresh(user)
    return user


@router.delete("/{user_id}", dependencies=[Depends(require_csrf)])
def delete_user(user_id: int, admin: User = Depends(require_admin), db: Session = Depends(get_db)) -> dict[str, bool]:
    if user_id == admin.id:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Admins cannot delete themselves.")
    user = db.get(User, user_id)
    if user is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found.")
    if user.role == "admin" and _admin_count(db) <= 1:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Cannot remove the last admin.")
    db.delete(user)
    db.commit()
    return {"ok": True}


def _admin_count(db: Session) -> int:
    return db.scalar(select(func.count()).select_from(User).where(User.role == "admin")) or 0


def _ensure_username_available(db: Session, username: str) -> None:
    existing = db.scalar(select(User).where(func.lower(User.username) == username.lower()))
    if existing is not None:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Username already exists.")

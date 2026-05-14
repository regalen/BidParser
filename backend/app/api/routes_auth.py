from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Request, Response, status
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.api.schemas import ChangePasswordRequest, LoginRequest, LoginResponse
from app.auth.deps import current_user, require_csrf
from app.auth.passwords import validate_new_password, verify_password
from app.auth.rate_limit import check_bucket, client_ip
from app.auth.sessions import clear_session_cookie, create_session_token, set_session_cookie
from app.db import get_db
from app.models import User


router = APIRouter(prefix="/auth", tags=["auth"])


@router.post("/login", response_model=LoginResponse, dependencies=[Depends(require_csrf)])
def login(payload: LoginRequest, request: Request, response: Response, db: Session = Depends(get_db)) -> LoginResponse:
    username_key = payload.username.strip().lower()
    check_bucket(f"ip:{client_ip(request)}")
    check_bucket(f"username:{username_key}")

    user = db.scalar(select(User).where(func.lower(User.username) == username_key))
    if user is None or not verify_password(payload.password, user.password_hash):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid username or password.")

    token = create_session_token(user.id)
    set_session_cookie(response, token, secure=_request_is_secure(request))
    return LoginResponse(user=user)


@router.post("/logout", dependencies=[Depends(require_csrf)])
def logout(response: Response, user: User = Depends(current_user)) -> dict[str, bool]:
    clear_session_cookie(response)
    return {"ok": True}


@router.post("/change-password", dependencies=[Depends(require_csrf)])
def change_password(
    payload: ChangePasswordRequest,
    request: Request,
    user: User = Depends(current_user),
    db: Session = Depends(get_db),
) -> dict[str, bool]:
    check_bucket(f"ip:{client_ip(request)}")
    if not verify_password(payload.old_password, user.password_hash):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid password.")
    errors = validate_new_password(payload.new_password)
    if errors:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=errors)
    from app.auth.passwords import hash_password

    user.password_hash = hash_password(payload.new_password)
    user.must_change_password = False
    db.add(user)
    db.commit()
    return {"ok": True}


def _request_is_secure(request: Request) -> bool:
    return request.headers.get("x-forwarded-proto", request.url.scheme) == "https"

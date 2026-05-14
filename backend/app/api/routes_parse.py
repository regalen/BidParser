from __future__ import annotations

from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile, status
from fastapi.responses import FileResponse
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.api.schemas import HistoryResponse, ParserInfo
from app.auth.deps import require_active_user, require_csrf
from app.db import get_db
from app.models import ParseJob, User
from app.parsers.registry import PARSER_REGISTRY, get_parser
from app.services.parse_service import parse_upload


router = APIRouter(tags=["parse"])


@router.get("/parsers", response_model=list[ParserInfo])
def list_parsers(user: User = Depends(require_active_user)) -> list[ParserInfo]:
    return [
        ParserInfo(
            slug=parser.slug,
            display_name=parser.display_name,
            vendor=parser.vendor,
            accepted_mime=parser.accepted_mime,
            crm_template=parser.crm_template,
        )
        for parser in PARSER_REGISTRY
    ]


@router.post("/parse", dependencies=[Depends(require_csrf)])
async def parse_file(
    file: UploadFile = File(...),
    vendor: str = Form(...),
    parser_slug: str = Form(...),
    fx_rate: Decimal = Form(...),
    margin: Decimal = Form(...),
    user: User = Depends(require_active_user),
    db: Session = Depends(get_db),
) -> FileResponse:
    result = await parse_upload(db=db, user=user, file=file, vendor=vendor, parser_slug=parser_slug, fx_rate=fx_rate, margin=margin)
    response = FileResponse(
        result.output_path,
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        filename=result.output_filename,
    )
    response.headers["X-Validation"] = "match" if result.job.totals_match else "mismatch"
    response.headers["X-Computed-Total"] = str(result.job.computed_total)
    response.headers["X-Quoted-Total"] = "" if result.job.quoted_total is None else str(result.job.quoted_total)
    return response


@router.get("/history", response_model=HistoryResponse)
def history(limit: int = 10, offset: int = 0, user: User = Depends(require_active_user), db: Session = Depends(get_db)) -> HistoryResponse:
    limit = min(max(limit, 1), 100)
    offset = max(offset, 0)
    total = db.scalar(select(func.count()).select_from(ParseJob).where(ParseJob.user_id == user.id)) or 0
    jobs = db.scalars(
        select(ParseJob)
        .where(ParseJob.user_id == user.id)
        .order_by(ParseJob.created_at.desc(), ParseJob.id.desc())
        .limit(limit)
        .offset(offset)
    ).all()
    rows = []
    for job in jobs:
        parser = get_parser(job.parser_slug)
        rows.append(
            {
                "id": job.id,
                "source_filename": job.source_filename,
                "vendor": job.vendor,
                "parser_slug": job.parser_slug,
                "file_type_display": parser.display_name,
                "fx_rate": job.fx_rate,
                "margin": job.margin,
                "when": _relative_when(job.created_at),
                "totals_match": job.totals_match,
            }
        )
    return HistoryResponse(rows=rows, total=total)


@router.get("/history/{job_id}/source")
def download_source(job_id: int, user: User = Depends(require_active_user), db: Session = Depends(get_db)) -> FileResponse:
    job = _job_for_user(db, user, job_id)
    path = Path(job.source_path)
    if not path.exists():
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="File not found.")
    return FileResponse(path, filename=job.source_filename)


@router.get("/history/{job_id}/output")
def download_output(job_id: int, user: User = Depends(require_active_user), db: Session = Depends(get_db)) -> FileResponse:
    job = _job_for_user(db, user, job_id)
    path = Path(job.output_path)
    if not path.exists():
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="File not found.")
    return FileResponse(
        path,
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        filename=f"{Path(job.source_filename).stem}_parsed.xlsx",
    )


def _job_for_user(db: Session, user: User, job_id: int) -> ParseJob:
    job = db.get(ParseJob, job_id)
    if job is None or job.user_id != user.id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Job not found.")
    return job


def _relative_when(value: datetime) -> str:
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    delta = datetime.now(timezone.utc) - value.astimezone(timezone.utc)
    seconds = int(delta.total_seconds())
    if seconds < 60:
        return "just now"
    minutes = seconds // 60
    if minutes < 60:
        return f"{minutes}m ago"
    hours = minutes // 60
    if hours < 24:
        return f"{hours}h ago"
    days = hours // 24
    if days == 1:
        return "Yesterday"
    if days < 7:
        return f"{days} days ago"
    return value.strftime("%d/%m/%Y")

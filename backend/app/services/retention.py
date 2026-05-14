from __future__ import annotations

from datetime import timedelta

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.config import get_settings
from app.models import ParseJob, utcnow
from app.storage import delete_file


def cleanup_old_parse_jobs(db: Session) -> int:
    cutoff = utcnow() - timedelta(days=get_settings().retention_days)
    jobs = db.scalars(select(ParseJob).where(ParseJob.created_at < cutoff)).all()
    for job in jobs:
        delete_file(job.source_path)
        delete_file(job.output_path)
        db.delete(job)
    db.commit()
    return len(jobs)

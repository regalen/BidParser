from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal
from pathlib import Path

from fastapi import HTTPException, UploadFile, status
from sqlalchemy.orm import Session

from app.config import get_settings
from app.models import ParseJob, User
from app.output.template_writer import output_filename, write_foreign_uplift
from app.parsers.base import ParseError
from app.parsers.registry import get_parser
from app.storage import delete_file, save_upload, stored_original_path, stored_output_path


PDF_MIME = "application/pdf"
XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
ACCEPTED_EXTENSIONS = {".pdf": PDF_MIME, ".xlsx": XLSX_MIME}


@dataclass(frozen=True)
class ParseServiceResult:
    job: ParseJob
    output_filename: str
    output_path: Path


async def parse_upload(
    *,
    db: Session,
    user: User,
    file: UploadFile,
    vendor: str,
    parser_slug: str,
    fx_rate: Decimal,
    margin: Decimal,
) -> ParseServiceResult:
    parser = _resolve_parser(parser_slug, vendor)
    _validate_upload_type(file, parser.accepted_mime)

    source_filename = Path(file.filename or "quote").name
    source_path = stored_original_path(source_filename)
    output_path = stored_output_path()

    try:
        await save_upload(file, source_path, max_bytes=get_settings().max_upload_bytes)
        result = parser.parse(source_path)
        write_foreign_uplift(
            result.line_items,
            output_path,
            margin=margin,
            fx_rate=fx_rate,
            vendor_name=vendor.upper(),
            currency=result.metadata.currency,
        )
    except ValueError as exc:
        delete_file(source_path)
        delete_file(output_path)
        raise HTTPException(status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE, detail="File is too large.") from exc
    except ParseError as exc:
        delete_file(source_path)
        delete_file(output_path)
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail={"stage": exc.stage, "hint": exc.hint, "message": str(exc)},
        ) from exc
    except Exception as exc:
        delete_file(source_path)
        delete_file(output_path)
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail={"stage": "parse", "hint": "Could not parse this file.", "message": str(exc)},
        ) from exc

    job = ParseJob(
        user_id=user.id,
        vendor=vendor,
        parser_slug=parser.slug,
        source_filename=source_filename,
        source_path=str(source_path),
        output_path=str(output_path),
        fx_rate=fx_rate.quantize(Decimal("0.0001")),
        margin=margin.quantize(Decimal("0.01")),
        computed_total=result.validation.computed_total,
        quoted_total=result.validation.quoted_total,
        totals_match=result.validation.matches,
    )
    user.fx_rate = job.fx_rate
    user.margin = job.margin
    db.add(job)
    db.add(user)
    db.commit()
    db.refresh(job)
    return ParseServiceResult(job=job, output_filename=output_filename(source_filename), output_path=output_path)


def _resolve_parser(parser_slug: str, vendor: str):
    try:
        parser = get_parser(parser_slug)
    except KeyError as exc:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Unknown parser.") from exc
    if parser.vendor != vendor:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Parser does not match vendor.")
    return parser


def _validate_upload_type(file: UploadFile, expected_mime: str) -> None:
    filename = file.filename or ""
    suffix = Path(filename).suffix.lower()
    by_extension = ACCEPTED_EXTENSIONS.get(suffix)
    if by_extension is None:
        raise HTTPException(status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE, detail="Only PDF and XLSX files are supported.")
    if by_extension != expected_mime:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="File extension does not match selected parser.")

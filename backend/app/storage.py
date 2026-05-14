from __future__ import annotations

import shutil
from pathlib import Path
from uuid import uuid4

from fastapi import UploadFile

from app.config import get_settings


def ensure_storage_dirs() -> None:
    settings = get_settings()
    (settings.upload_dir / "originals").mkdir(parents=True, exist_ok=True)
    (settings.upload_dir / "outputs").mkdir(parents=True, exist_ok=True)


def stored_original_path(filename: str) -> Path:
    suffix = Path(filename).suffix.lower()
    return get_settings().upload_dir / "originals" / f"{uuid4().hex}{suffix}"


def stored_output_path() -> Path:
    return get_settings().upload_dir / "outputs" / f"{uuid4().hex}.xlsx"


async def save_upload(upload: UploadFile, path: Path, *, max_bytes: int) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    total = 0
    try:
        with path.open("wb") as output:
            while chunk := await upload.read(1024 * 1024):
                total += len(chunk)
                if total > max_bytes:
                    raise ValueError("File is too large.")
                output.write(chunk)
    except Exception:
        path.unlink(missing_ok=True)
        raise
    finally:
        await upload.close()
    return total


def delete_file(path: str | Path | None) -> None:
    if path:
        Path(path).unlink(missing_ok=True)


def copy_file(source: Path, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)

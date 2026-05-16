from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


def _root() -> Path:
    return Path(__file__).resolve().parents[2]


@dataclass(frozen=True)
class Settings:
    database_url: str = os.getenv("DATABASE_URL", f"sqlite:///{_root() / 'data' / 'db.sqlite'}")
    upload_dir: Path = Path(os.getenv("UPLOAD_DIR", str(_root() / "data" / "files")))
    session_secret: str = os.getenv("SESSION_SECRET", "dev-only-change-me")
    session_lifetime_hours: int = int(os.getenv("SESSION_LIFETIME_HOURS", "12"))
    bootstrap_admin_username: str = os.getenv("ADMIN_USERNAME", "admin")
    bootstrap_admin_password: str = os.getenv("ADMIN_PASSWORD", "changeme")
    retention_days: int = int(os.getenv("RETENTION_DAYS", "90"))
    rate_limit_auth_per_min: int = int(os.getenv("RATE_LIMIT_AUTH_PER_MIN", "5"))
    max_upload_mb: int = int(os.getenv("MAX_UPLOAD_MB", "10"))

    @property
    def max_upload_bytes(self) -> int:
        return self.max_upload_mb * 1024 * 1024


def get_settings() -> Settings:
    return Settings()

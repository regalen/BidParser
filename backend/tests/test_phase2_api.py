from __future__ import annotations

import os
import tempfile
from pathlib import Path

os.environ.setdefault("DATABASE_URL", f"sqlite:///{Path(tempfile.mkdtemp()) / 'test.sqlite'}")
os.environ.setdefault("UPLOAD_DIR", str(Path(tempfile.mkdtemp()) / "files"))
os.environ.setdefault("SESSION_SECRET", "test-secret")
os.environ.setdefault("ADMIN_USERNAME", "admin")
os.environ.setdefault("ADMIN_PASSWORD", "changeme")

from fastapi.testclient import TestClient

from app.auth.rate_limit import clear_rate_limits
from app.main import app, bootstrap_admin
from app.models import Base
from app.db import engine


ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "samples" / "inputs"
HEADERS = {"X-Requested-With": "BidParser"}


def reset_app() -> None:
    clear_rate_limits()
    Base.metadata.drop_all(bind=engine)
    Base.metadata.create_all(bind=engine)
    bootstrap_admin()


def login(client: TestClient, username: str = "admin", password: str = "Admin123!") -> None:
    response = client.post("/api/auth/login", json={"username": username, "password": password}, headers=HEADERS)
    assert response.status_code == 200, response.text


def unlock_admin(client: TestClient) -> None:
    response = client.post("/api/auth/login", json={"username": "admin", "password": "changeme"}, headers=HEADERS)
    assert response.status_code == 200, response.text
    assert response.json()["user"]["must_change_password"] is True
    response = client.post(
        "/api/auth/change-password",
        json={"old_password": "changeme", "new_password": "Admin123!"},
        headers=HEADERS,
    )
    assert response.status_code == 200, response.text


def test_bootstrap_login_and_forced_password_change_gate() -> None:
    reset_app()
    with TestClient(app) as client:
        response = client.post("/api/auth/login", json={"username": "admin", "password": "changeme"}, headers=HEADERS)
        assert response.status_code == 200
        assert response.json()["user"]["role"] == "admin"
        assert response.json()["user"]["must_change_password"] is True

        blocked = client.get("/api/parsers")
        assert blocked.status_code == 403
        assert blocked.json()["detail"] == "password_change_required"

        weak = client.post(
            "/api/auth/change-password",
            json={"old_password": "changeme", "new_password": "weak"},
            headers=HEADERS,
        )
        assert weak.status_code == 400

        changed = client.post(
            "/api/auth/change-password",
            json={"old_password": "changeme", "new_password": "Admin123!"},
            headers=HEADERS,
        )
        assert changed.status_code == 200
        assert client.get("/api/parsers").status_code == 200


def test_admin_user_crud_and_last_admin_guards() -> None:
    reset_app()
    with TestClient(app) as client:
        unlock_admin(client)
        response = client.post("/api/users", json={"username": "salesperson1", "name": "Sales Person", "role": "user"}, headers=HEADERS)
        assert response.status_code == 200, response.text
        user_id = response.json()["id"]
        assert response.json()["must_change_password"] is True

        users = client.get("/api/users")
        assert users.status_code == 200
        assert {user["username"] for user in users.json()} == {"admin", "salesperson1"}

        self_demote = client.patch("/api/users/1", json={"role": "user"}, headers=HEADERS)
        assert self_demote.status_code == 409

        self_delete = client.delete("/api/users/1", headers=HEADERS)
        assert self_delete.status_code == 409

        reset = client.patch(f"/api/users/{user_id}", json={"reset_password": True}, headers=HEADERS)
        assert reset.status_code == 200
        assert reset.json()["must_change_password"] is True


def test_parse_roundtrip_history_downloads_and_settings() -> None:
    reset_app()
    with TestClient(app) as client:
        unlock_admin(client)
        parsers = client.get("/api/parsers")
        assert parsers.status_code == 200
        assert {parser["slug"] for parser in parsers.json()} >= {"nutanix_software_only_pdf", "nutanix_hardware_only_xlsx"}

        with (SAMPLES / "XQ-4076249.pdf").open("rb") as handle:
            response = client.post(
                "/api/parse",
                data={"vendor": "Nutanix", "parser_slug": "nutanix_software_only_pdf", "fx_rate": "0.7354", "margin": "5.25"},
                files={"file": ("XQ-4076249.pdf", handle, "application/pdf")},
                headers=HEADERS,
            )
        assert response.status_code == 200, response.text
        assert response.headers["X-Validation"] == "match"
        assert response.headers["X-Computed-Total"] == "1625358.51"
        assert response.headers["X-Quoted-Total"] == "1625358.51"
        assert response.headers["content-disposition"].endswith('filename="XQ-4076249_parsed.xlsx"')
        assert response.content.startswith(b"PK")

        me = client.get("/api/me")
        assert me.status_code == 200
        assert me.json()["default_vendor"] == "Nutanix"
        assert me.json()["fx_rate"] == "0.7354"
        assert me.json()["margin"] == "5.25"

        settings = client.patch("/api/me/settings", json={"default_vendor": "Nutanix", "margin": "7.50"}, headers=HEADERS)
        assert settings.status_code == 200, settings.text
        assert settings.json()["default_vendor"] == "Nutanix"
        assert settings.json()["fx_rate_pegged"] is False
        assert settings.json()["margin"] == "7.50"

        pegged_settings = client.patch("/api/me/settings", json={"fx_rate_pegged": True}, headers=HEADERS)
        assert pegged_settings.status_code == 200
        assert pegged_settings.json()["fx_rate_pegged"] is True

        bad_settings = client.patch("/api/me/settings", json={"default_vendor": "Unknown"}, headers=HEADERS)
        assert bad_settings.status_code == 400

        history = client.get("/api/history?limit=5&offset=0")
        assert history.status_code == 200
        assert history.json()["total"] == 1
        row = history.json()["rows"][0]
        assert row["source_filename"] == "XQ-4076249.pdf"
        assert row["file_type_display"] == "Software Only (PDF)"

        source = client.get(f"/api/history/{row['id']}/source")
        assert source.status_code == 200
        assert source.content.startswith(b"%PDF")

        output = client.get(f"/api/history/{row['id']}/output")
        assert output.status_code == 200
        assert output.content.startswith(b"PK")


def test_history_filename_search() -> None:
    reset_app()
    with TestClient(app) as client:
        unlock_admin(client)

        with (SAMPLES / "XQ-4076249.pdf").open("rb") as handle:
            first = client.post(
                "/api/parse",
                data={"vendor": "Nutanix", "parser_slug": "nutanix_software_only_pdf", "fx_rate": "0.7354", "margin": "5.25"},
                files={"file": ("XQ-4076249.pdf", handle, "application/pdf")},
                headers=HEADERS,
            )
        assert first.status_code == 200, first.text

        with (SAMPLES / "XQ-4128926.pdf").open("rb") as handle:
            second = client.post(
                "/api/parse",
                data={"vendor": "Nutanix", "parser_slug": "nutanix_renewal_pdf", "fx_rate": "0.7354", "margin": "5.25"},
                files={"file": ("XQ-4128926.pdf", handle, "application/pdf")},
                headers=HEADERS,
            )
        assert second.status_code == 200, second.text

        all_rows = client.get("/api/history").json()
        assert all_rows["total"] == 2

        matched = client.get("/api/history?q=4076").json()
        assert matched["total"] == 1
        assert matched["rows"][0]["source_filename"] == "XQ-4076249.pdf"

        case_insensitive = client.get("/api/history?q=xq-4128").json()
        assert case_insensitive["total"] == 1
        assert case_insensitive["rows"][0]["source_filename"] == "XQ-4128926.pdf"

        broad = client.get("/api/history?q=XQ").json()
        assert broad["total"] == 2

        empty = client.get("/api/history?q=does-not-exist").json()
        assert empty["total"] == 0
        assert empty["rows"] == []

        blank = client.get("/api/history?q=%20%20").json()
        assert blank["total"] == 2


def test_history_is_user_scoped() -> None:
    reset_app()
    with TestClient(app) as client:
        unlock_admin(client)
        response = client.post("/api/users", json={"username": "salesperson1", "name": "Sales Person", "role": "user"}, headers=HEADERS)
        client.post("/api/auth/logout", headers=HEADERS)
        response = client.post("/api/auth/login", json={"username": "salesperson1", "password": "changeme"}, headers=HEADERS)
        assert response.status_code == 200
        changed = client.post(
            "/api/auth/change-password",
            json={"old_password": "changeme", "new_password": "Sales123!"},
            headers=HEADERS,
        )
        assert changed.status_code == 200

        with (SAMPLES / "XQ-4108785.xlsx").open("rb") as handle:
            parsed = client.post(
                "/api/parse",
                data={"vendor": "Nutanix", "parser_slug": "nutanix_hardware_only_xlsx", "fx_rate": "1.0000", "margin": "5.00"},
                files={"file": ("XQ-4108785.xlsx", handle, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")},
                headers=HEADERS,
            )
        assert parsed.status_code == 200, parsed.text
        job_id = client.get("/api/history").json()["rows"][0]["id"]

        client.post("/api/auth/logout", headers=HEADERS)
        login(client)
        assert client.get("/api/history").json()["total"] == 0
        assert client.get(f"/api/history/{job_id}/source").status_code == 404


def test_login_rate_limits_by_username_across_ips() -> None:
    reset_app()
    with TestClient(app) as client:
        for index in range(5):
            response = client.post(
                "/api/auth/login",
                json={"username": "admin", "password": "wrong"},
                headers={**HEADERS, "X-Forwarded-For": f"10.0.0.{index}"},
            )
            assert response.status_code == 401

        limited = client.post(
            "/api/auth/login",
            json={"username": "admin", "password": "wrong"},
            headers={**HEADERS, "X-Forwarded-For": "10.0.0.99"},
        )
        assert limited.status_code == 429
        assert "Retry-After" in limited.headers

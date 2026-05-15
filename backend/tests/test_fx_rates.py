from __future__ import annotations

from decimal import Decimal

from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

from app.auth.passwords import hash_password
from app.models import Base, User
from app.services.fx_rates import parse_aud_usd_rate, update_user_default_fx_rates


def test_parse_bloomberg_aud_usd_rate_from_price_payload() -> None:
    assert parse_aud_usd_rate('{"security":"AUDUSD:CUR","price":"0.66436"}') == Decimal("0.6644")


def test_update_user_default_fx_rates() -> None:
    engine = create_engine("sqlite:///:memory:")
    Base.metadata.create_all(bind=engine)
    Session = sessionmaker(bind=engine)
    db_session = Session()
    pegged_user = User(
        username="rate-user",
        name="Rate User",
        password_hash=hash_password("Password123!"),
        must_change_password=False,
        fx_rate_pegged=True,
    )
    manual_user = User(
        username="manual-rate-user",
        name="Manual Rate User",
        password_hash=hash_password("Password123!"),
        must_change_password=False,
        fx_rate=Decimal("0.7000"),
        fx_rate_pegged=False,
    )
    db_session.add_all([pegged_user, manual_user])
    db_session.commit()

    updated = update_user_default_fx_rates(db_session, Decimal("0.654321"))

    db_session.refresh(pegged_user)
    db_session.refresh(manual_user)
    assert updated == 1
    assert pegged_user.fx_rate == Decimal("0.6543")
    assert pegged_user.fx_rate_updated_at is not None
    assert manual_user.fx_rate == Decimal("0.7000")
    assert manual_user.fx_rate_updated_at is None
    db_session.close()

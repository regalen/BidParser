"""Remove Bloomberg FX-rate peg fields.

Revision ID: 0004_remove_fx_rate_peg
Revises: 0003_user_defaults
Create Date: 2026-05-16
"""

from alembic import op
import sqlalchemy as sa


revision = "0004_remove_fx_rate_peg"
down_revision = "0003_user_defaults"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.drop_column("users", "fx_rate_updated_at")
    op.drop_column("users", "fx_rate_pegged")


def downgrade() -> None:
    op.add_column("users", sa.Column("fx_rate_pegged", sa.Boolean(), server_default="0", nullable=False))
    op.add_column("users", sa.Column("fx_rate_updated_at", sa.DateTime(timezone=True), nullable=True))

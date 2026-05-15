"""Add per-user parse defaults.

Revision ID: 0003_user_defaults
Revises: 0002_user_name
Create Date: 2026-05-15
"""

from alembic import op
import sqlalchemy as sa


revision = "0003_user_defaults"
down_revision = "0002_user_name"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("users", sa.Column("default_vendor", sa.String(length=64), nullable=True))
    op.add_column("users", sa.Column("fx_rate_pegged", sa.Boolean(), server_default="0", nullable=False))
    op.add_column("users", sa.Column("fx_rate_updated_at", sa.DateTime(timezone=True), nullable=True))


def downgrade() -> None:
    op.drop_column("users", "fx_rate_updated_at")
    op.drop_column("users", "fx_rate_pegged")
    op.drop_column("users", "default_vendor")

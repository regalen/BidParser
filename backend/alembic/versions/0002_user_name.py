"""Add display name to users.

Revision ID: 0002_user_name
Revises: 0001_initial
"""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision = "0002_user_name"
down_revision = "0001_initial"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("users", sa.Column("name", sa.String(length=255), nullable=True))
    # Backfill existing rows so the UI has something to display until admins
    # update the names manually.
    op.execute("UPDATE users SET name = username WHERE name IS NULL")


def downgrade() -> None:
    op.drop_column("users", "name")

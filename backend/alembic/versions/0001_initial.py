from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision = "0001_initial"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "users",
        sa.Column("id", sa.Integer(), primary_key=True),
        sa.Column("username", sa.String(length=128), nullable=False),
        sa.Column("password_hash", sa.String(length=255), nullable=False),
        sa.Column("role", sa.String(length=16), nullable=False),
        sa.Column("must_change_password", sa.Boolean(), nullable=False),
        sa.Column("fx_rate", sa.Numeric(12, 4), nullable=True),
        sa.Column("margin", sa.Numeric(12, 2), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index(op.f("ix_users_username"), "users", ["username"], unique=True)
    op.create_table(
        "parse_jobs",
        sa.Column("id", sa.Integer(), primary_key=True),
        sa.Column("user_id", sa.Integer(), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("vendor", sa.String(length=64), nullable=False),
        sa.Column("parser_slug", sa.String(length=128), nullable=False),
        sa.Column("source_filename", sa.String(length=255), nullable=False),
        sa.Column("source_path", sa.String(length=1024), nullable=False),
        sa.Column("output_path", sa.String(length=1024), nullable=False),
        sa.Column("fx_rate", sa.Numeric(12, 4), nullable=False),
        sa.Column("margin", sa.Numeric(12, 2), nullable=False),
        sa.Column("computed_total", sa.Numeric(14, 2), nullable=False),
        sa.Column("quoted_total", sa.Numeric(14, 2), nullable=True),
        sa.Column("totals_match", sa.Boolean(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index(op.f("ix_parse_jobs_user_id"), "parse_jobs", ["user_id"], unique=False)


def downgrade() -> None:
    op.drop_index(op.f("ix_parse_jobs_user_id"), table_name="parse_jobs")
    op.drop_table("parse_jobs")
    op.drop_index(op.f("ix_users_username"), table_name="users")
    op.drop_table("users")

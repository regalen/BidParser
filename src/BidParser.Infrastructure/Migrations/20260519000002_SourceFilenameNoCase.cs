using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260519000002_SourceFilenameNoCase")]
public partial class SourceFilenameNoCase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        RebuildParseJobs(migrationBuilder, "TEXT COLLATE NOCASE");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RebuildParseJobs(migrationBuilder, "TEXT");
    }

    private static void RebuildParseJobs(MigrationBuilder migrationBuilder, string sourceFilenameType)
    {
        migrationBuilder.DropIndex(
            name: "ix_parse_jobs_user_id",
            table: "parse_jobs");

        migrationBuilder.DropIndex(
            name: "ix_parse_jobs_user_id_created_at",
            table: "parse_jobs");

        migrationBuilder.Sql($"""
            CREATE TABLE "parse_jobs_rebuild" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_parse_jobs" PRIMARY KEY AUTOINCREMENT,
                "user_id" INTEGER NOT NULL,
                "vendor" TEXT NOT NULL,
                "parser_slug" TEXT NOT NULL,
                "source_filename" {sourceFilenameType} NOT NULL,
                "source_path" TEXT NOT NULL,
                "output_path" TEXT NOT NULL,
                "fx_rate" TEXT NOT NULL,
                "margin" TEXT NOT NULL,
                "computed_total" TEXT NOT NULL,
                "quoted_total" TEXT NULL,
                "totals_match" INTEGER NOT NULL,
                "created_at" TEXT NOT NULL,
                CONSTRAINT "FK_parse_jobs_users_user_id" FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE CASCADE
            );
            """);

        migrationBuilder.Sql("""
            INSERT INTO "parse_jobs_rebuild" (
                "id",
                "user_id",
                "vendor",
                "parser_slug",
                "source_filename",
                "source_path",
                "output_path",
                "fx_rate",
                "margin",
                "computed_total",
                "quoted_total",
                "totals_match",
                "created_at"
            )
            SELECT
                "id",
                "user_id",
                "vendor",
                "parser_slug",
                "source_filename",
                "source_path",
                "output_path",
                "fx_rate",
                "margin",
                "computed_total",
                "quoted_total",
                "totals_match",
                "created_at"
            FROM "parse_jobs";
            """);

        migrationBuilder.DropTable(name: "parse_jobs");

        migrationBuilder.RenameTable(
            name: "parse_jobs_rebuild",
            newName: "parse_jobs");

        migrationBuilder.CreateIndex(
            name: "ix_parse_jobs_user_id",
            table: "parse_jobs",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_parse_jobs_user_id_created_at",
            table: "parse_jobs",
            columns: new[] { "user_id", "created_at" },
            descending: new[] { false, true });
    }
}

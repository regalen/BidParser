using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParseMetricsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parse_metrics",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    parse_job_id = table.Column<int>(type: "INTEGER", nullable: true),
                    user_username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    vendor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    parser_slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    source_filename = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 255, nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    quoted_total = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: true),
                    computed_total = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    totals_match = table.Column<bool>(type: "INTEGER", nullable: false),
                    fx_rate = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                    margin = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parse_metrics", x => x.id);
                    table.ForeignKey(
                        name: "FK_parse_metrics_parse_jobs_parse_job_id",
                        column: x => x.parse_job_id,
                        principalTable: "parse_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_parse_metrics_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parse_metrics_created_at",
                table: "parse_metrics",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_parse_metrics_parse_job_id",
                table: "parse_metrics",
                column: "parse_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_parse_metrics_parser_slug_created_at",
                table: "parse_metrics",
                columns: new[] { "parser_slug", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parse_metrics_user_id_created_at",
                table: "parse_metrics",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parse_metrics_vendor_created_at",
                table: "parse_metrics",
                columns: new[] { "vendor", "created_at" });

            migrationBuilder.Sql(@"
                INSERT INTO parse_metrics (
                    user_id, parse_job_id, user_username, user_name,
                    vendor, parser_slug, source_filename, currency,
                    quoted_total, computed_total, totals_match, fx_rate, margin, created_at
                )
                SELECT pj.user_id, pj.id, u.username, u.name,
                       pj.vendor, pj.parser_slug, pj.source_filename, 'USD',
                       pj.quoted_total, pj.computed_total, pj.totals_match, pj.fx_rate, pj.margin, pj.created_at
                FROM parse_jobs pj
                INNER JOIN users u ON u.id = pj.user_id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parse_metrics");
        }
    }
}

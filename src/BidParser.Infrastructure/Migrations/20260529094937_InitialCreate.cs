using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    password_hash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false),
                    default_vendor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    fx_rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    margin = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    im = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "failed_parse_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: true),
                    user_username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    vendor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    parser_slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_filename = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    source_path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    hint = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    message = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    computed_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    quoted_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    error_detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    fx_rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    margin = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failed_parse_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_failed_parse_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "parse_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    vendor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    parser_slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    crm_template = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_filename = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    source_path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    output_path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    fx_rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    margin = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    computed_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    quoted_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    totals_match = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parse_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_parse_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parse_metrics",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: true),
                    parse_job_id = table.Column<int>(type: "int", nullable: true),
                    user_username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    vendor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    parser_slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_filename = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    quoted_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    computed_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    totals_match = table.Column<bool>(type: "bit", nullable: false),
                    fx_rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    margin = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "ix_failed_parse_jobs_category_created_at",
                table: "failed_parse_jobs",
                columns: new[] { "category", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_failed_parse_jobs_created_at",
                table: "failed_parse_jobs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_failed_parse_jobs_user_id_created_at",
                table: "failed_parse_jobs",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_parse_jobs_user_id",
                table: "parse_jobs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_parse_jobs_user_id_created_at",
                table: "parse_jobs",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

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

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "failed_parse_jobs");

            migrationBuilder.DropTable(
                name: "parse_metrics");

            migrationBuilder.DropTable(
                name: "parse_jobs");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

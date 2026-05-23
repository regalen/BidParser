using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedParseJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "failed_parse_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    user_username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    vendor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    parser_slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    source_filename = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 255, nullable: false),
                    source_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    hint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    error_detail = table.Column<string>(type: "TEXT", nullable: false),
                    fx_rate = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                    margin = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "failed_parse_jobs");
        }
    }
}

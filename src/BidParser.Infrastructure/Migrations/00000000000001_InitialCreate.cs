using System;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("00000000000001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                username = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: false),
                name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                password_hash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                must_change_password = table.Column<bool>(type: "INTEGER", nullable: false),
                default_vendor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                fx_rate = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: true),
                margin = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "parse_jobs",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                user_id = table.Column<int>(type: "INTEGER", nullable: false),
                vendor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                parser_slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                source_filename = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                source_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                output_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                fx_rate = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                margin = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                computed_total = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                quoted_total = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: true),
                totals_match = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
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

        migrationBuilder.CreateIndex(
            name: "ix_parse_jobs_user_id",
            table: "parse_jobs",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_users_username",
            table: "users",
            column: "username",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "parse_jobs");
        migrationBuilder.DropTable(name: "users");
    }
}

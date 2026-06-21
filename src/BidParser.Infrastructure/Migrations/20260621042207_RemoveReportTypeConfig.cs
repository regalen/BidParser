using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReportTypeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_type_configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_type_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    parser_slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    report_type = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_type_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_type_configs_parser_slug",
                table: "report_type_configs",
                column: "parser_slug",
                unique: true);
        }
    }
}

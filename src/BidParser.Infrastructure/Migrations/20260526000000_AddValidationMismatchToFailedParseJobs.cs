using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationMismatchToFailedParseJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "computed_total",
                table: "failed_parse_jobs",
                type: "TEXT",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quoted_total",
                table: "failed_parse_jobs",
                type: "TEXT",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "computed_total",
                table: "failed_parse_jobs");

            migrationBuilder.DropColumn(
                name: "quoted_total",
                table: "failed_parse_jobs");
        }
    }
}

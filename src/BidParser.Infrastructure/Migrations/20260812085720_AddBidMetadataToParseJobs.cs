using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBidMetadataToParseJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bid_number",
                table: "parse_jobs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "SQL_Latin1_General_CP1_CI_AS");

            migrationBuilder.AddColumn<string>(
                name: "bid_revision",
                table: "parse_jobs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "SQL_Latin1_General_CP1_CI_AS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bid_number",
                table: "parse_jobs");

            migrationBuilder.DropColumn(
                name: "bid_revision",
                table: "parse_jobs");
        }
    }
}

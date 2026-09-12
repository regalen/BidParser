using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDellApiVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfills the singleton row so an already-configured deployment pins Accepts-version
            // instead of sending a blank header. Matches DellApiSettingsService.InitialApiVersion.
            migrationBuilder.AddColumn<string>(
                name: "api_version",
                table: "dell_api_settings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "4.0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "api_version",
                table: "dell_api_settings");
        }
    }
}

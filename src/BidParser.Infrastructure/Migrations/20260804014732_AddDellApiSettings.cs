using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDellApiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dell_api_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    token_url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    client_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    client_secret_protected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    quote_url_template = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    default_locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    client_id_header = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    use_basic_auth_for_token = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dell_api_settings", x => x.id);
                    table.CheckConstraint("ck_dell_api_settings_singleton", "[id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dell_api_settings");
        }
    }
}

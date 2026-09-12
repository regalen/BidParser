using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLenovoVendorSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The "Lenovo" vendor was split into "Lenovo ISG" and "Lenovo IDG" dropdown entries.
            // Every existing format (LBP-E ISG, LBP-I ISG) belongs to Lenovo ISG, so a saved
            // default_vendor of "Lenovo" must follow — otherwise it no longer matches any
            // dropdown entry and the user's vendor selection silently resets on next login.
            migrationBuilder.Sql(
                "UPDATE users SET default_vendor = 'Lenovo ISG' WHERE default_vendor = 'Lenovo';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE users SET default_vendor = 'Lenovo' WHERE default_vendor = 'Lenovo ISG';");
        }
    }
}

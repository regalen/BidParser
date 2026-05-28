using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParseJobCrmTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "crm_template",
                table: "parse_jobs",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with each parser's default CRM template.
            // Multi-template parsers (hp_bid_xlsx, lenovo_brda_dcg_*) fall back to
            // their default — we don't know the historical user choice.
            migrationBuilder.Sql("""
                UPDATE parse_jobs SET crm_template = CASE parser_slug
                    WHEN 'nutanix_software_only_pdf'  THEN 'Foreign Uplift'
                    WHEN 'nutanix_software_only_xlsx' THEN 'Foreign Uplift'
                    WHEN 'nutanix_renewal_pdf'        THEN 'Foreign Uplift'
                    WHEN 'nutanix_hardware_only_pdf'  THEN 'Foreign Uplift'
                    WHEN 'nutanix_hardware_only_xlsx' THEN 'Foreign Uplift'
                    WHEN 'hp_bid_xlsx'                THEN 'No Calculation'
                    WHEN 'hp_oneconfig_xlsx'          THEN '% Off RRP with Uplift'
                    WHEN 'lenovo_brda_dcg_pdf'        THEN 'No Calculation'
                    WHEN 'lenovo_brda_dcg_xlsx'       THEN 'No Calculation'
                    ELSE 'No Calculation'
                END
                WHERE crm_template = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "crm_template",
                table: "parse_jobs");
        }
    }
}

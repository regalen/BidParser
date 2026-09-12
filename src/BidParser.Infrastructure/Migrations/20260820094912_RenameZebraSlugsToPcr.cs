using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameZebraSlugsToPcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE parse_jobs SET parser_slug = 'zebra_pcr_pdf' WHERE parser_slug = 'zebra_price_concession_pdf';
                UPDATE parse_jobs SET parser_slug = 'zebra_pcr_xls' WHERE parser_slug = 'zebra_price_concession_xls';

                UPDATE parse_metrics SET parser_slug = 'zebra_pcr_pdf' WHERE parser_slug = 'zebra_price_concession_pdf';
                UPDATE parse_metrics SET parser_slug = 'zebra_pcr_xls' WHERE parser_slug = 'zebra_price_concession_xls';

                UPDATE failed_parse_jobs SET parser_slug = 'zebra_pcr_pdf' WHERE parser_slug = 'zebra_price_concession_pdf';
                UPDATE failed_parse_jobs SET parser_slug = 'zebra_pcr_xls' WHERE parser_slug = 'zebra_price_concession_xls';

                UPDATE runtime_configs
                SET json_payload = REPLACE(REPLACE(json_payload, '"zebra_price_concession_pdf"', '"zebra_pcr_pdf"'), '"zebra_price_concession_xls"', '"zebra_pcr_xls"')
                WHERE [key] = 'guidanceMessages';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE parse_jobs SET parser_slug = 'zebra_price_concession_pdf' WHERE parser_slug = 'zebra_pcr_pdf';
                UPDATE parse_jobs SET parser_slug = 'zebra_price_concession_xls' WHERE parser_slug = 'zebra_pcr_xls';

                UPDATE parse_metrics SET parser_slug = 'zebra_price_concession_pdf' WHERE parser_slug = 'zebra_pcr_pdf';
                UPDATE parse_metrics SET parser_slug = 'zebra_price_concession_xls' WHERE parser_slug = 'zebra_pcr_xls';

                UPDATE failed_parse_jobs SET parser_slug = 'zebra_price_concession_pdf' WHERE parser_slug = 'zebra_pcr_pdf';
                UPDATE failed_parse_jobs SET parser_slug = 'zebra_price_concession_xls' WHERE parser_slug = 'zebra_pcr_xls';

                UPDATE runtime_configs
                SET json_payload = REPLACE(REPLACE(json_payload, '"zebra_pcr_pdf"', '"zebra_price_concession_pdf"'), '"zebra_pcr_xls"', '"zebra_price_concession_xls"')
                WHERE [key] = 'guidanceMessages';
                """);
        }
    }
}

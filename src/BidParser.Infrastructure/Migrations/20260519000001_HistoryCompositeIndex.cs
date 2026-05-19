using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidParser.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260519000001_HistoryCompositeIndex")]
public partial class HistoryCompositeIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_parse_jobs_user_id_created_at",
            table: "parse_jobs",
            columns: new[] { "user_id", "created_at" },
            descending: new[] { false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_parse_jobs_user_id_created_at",
            table: "parse_jobs");
    }
}

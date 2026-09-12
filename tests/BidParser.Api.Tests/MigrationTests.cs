using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task ExistingParseJobsKeepValuesAndHaveNullBidMetadata()
    {
        var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260809070121_AddDellApiVersion");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO users (username, password_hash, role, must_change_password, created_at, updated_at)
            VALUES ('bid-migration-user', 'hash', 'user', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO parse_jobs (
                user_id, vendor, parser_slug, crm_template, source_filename, source_path, output_path,
                fx_rate, margin, computed_total, quoted_total, totals_match, split_by_solution_id, import_type, created_at)
            VALUES (
                SCOPE_IDENTITY(), 'Nutanix', 'nutanix_software_only_pdf', 'Foreign Uplift', 'old.pdf',
                '/old/source.pdf', '/old/output.xlsx', 1, 5, 12.34, 12.34, 1, 0, 'manual', SYSUTCDATETIME());
            """);

        await migrator.MigrateAsync();

        var row = await db.Database.SqlQueryRaw<BidMigrationRow>("""
            SELECT source_filename AS SourceFilename, computed_total AS ComputedTotal,
                   bid_number AS BidNumber, bid_revision AS BidRevision
            FROM parse_jobs WHERE source_filename = 'old.pdf'
            """).SingleAsync();
        row.SourceFilename.Should().Be("old.pdf");
        row.ComputedTotal.Should().Be(12.34m);
        row.BidNumber.Should().BeNull();
        row.BidRevision.Should().BeNull();
    }

    [Fact]
    public async Task ExistingParseJobsBackfillSolutionIdSplitToFalse()
    {
        var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260805110005_AddImportType");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO users (username, password_hash, role, must_change_password, created_at, updated_at)
            VALUES ('migration-user', 'hash', 'user', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO parse_jobs (
                user_id, vendor, parser_slug, crm_template, source_filename, source_path, output_path,
                fx_rate, margin, computed_total, quoted_total, totals_match, import_type, created_at)
            VALUES (
                SCOPE_IDENTITY(), 'Lenovo', 'lenovo_lbpe_isg_xls', 'No Calculation', 'old.xls',
                '/old/source.xls', '/old/output.xlsx', 1, 0, 1, 1, 1, 'manual', SYSUTCDATETIME());
            """);

        await migrator.MigrateAsync();

        var splitValue = await db.Database.SqlQueryRaw<int>(
                "SELECT CAST(split_by_solution_id AS int) AS Value FROM parse_jobs WHERE source_filename = 'old.xls'")
            .SingleAsync();
        splitValue.Should().Be(0);
    }

    [Fact]
    public async Task ExistingUsersDefaultVendorLenovoBackfillsToLenovoIsg()
    {
        var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260812085720_AddBidMetadataToParseJobs");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO users (username, password_hash, role, must_change_password, default_vendor, created_at, updated_at)
            VALUES ('vendor-migration-user', 'hash', 'user', 0, 'Lenovo', SYSUTCDATETIME(), SYSUTCDATETIME());
            """);

        await migrator.MigrateAsync();

        var defaultVendor = await db.Database.SqlQueryRaw<string>(
                "SELECT default_vendor AS Value FROM users WHERE username = 'vendor-migration-user'")
            .SingleAsync();
        defaultVendor.Should().Be("Lenovo ISG");
    }

    [Fact]
    public async Task FreshDatabaseMigratesAndBootstrapsAdmin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
            using var environment = new ScopedEnvironment(new Dictionary<string, string>
            {
                ["DB_CONNECTION_STRING"] = connectionString,
                ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
                ["ADMIN_USERNAME"] = "phase2-admin",
                ["ADMIN_PASSWORD"] = "change-me-123!"
            });
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var health = await client.GetAsync("/api/healthz");
            health.IsSuccessStatusCode.Should().BeTrue();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync();

            admin.Username.Should().Be("phase2-admin");
            admin.Name.Should().Be("Administrator");
            admin.Role.Should().Be(UserRole.Admin);
            admin.MustChangePassword.Should().BeTrue();
            BCrypt.Net.BCrypt.Verify("change-me-123!", admin.PasswordHash).Should().BeTrue();

            var migrationIds = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            migrationIds.Should().Contain(id => id.EndsWith("_InitialCreate"));
            migrationIds.Should().Contain(id => id.EndsWith("_AddReportTypeConfig"));
            migrationIds.Should().Contain(id => id.EndsWith("_RemoveReportTypeConfig"));
            migrationIds.Should().Contain(id => id.EndsWith("_AddDellApiSettings"));
            migrationIds.Should().Contain(id => id.EndsWith("_AddRuntimeConfiguration"));
            migrationIds.Should().Contain(id => id.EndsWith("_RenameZebraSlugsToPcr"));

            var dellSettingsTableCount = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'dell_api_settings'")
                .SingleAsync();
            dellSettingsTableCount.Should().Be(1);

            // The obsolete report-type table remains dropped; guidance now lives in runtime_configs.
            var reportTypeTableCount = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'report_type_configs'")
                .SingleAsync();
            reportTypeTableCount.Should().Be(0);

            var runtimeConfigColumns = await db.Database.SqlQueryRaw<string>("""
                SELECT COLUMN_NAME AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'runtime_configs'
                """).ToListAsync();
            runtimeConfigColumns.Should().BeEquivalentTo("key", "json_payload", "created_at", "updated_at");

            var runtimeConfigPrimaryKey = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value
                FROM sys.key_constraints
                WHERE [type] = 'PK' AND [name] = 'PK_runtime_configs'
                """).SingleAsync();
            runtimeConfigPrimaryKey.Should().Be(1);

            // Verify CI collation on username and source_filename via INFORMATION_SCHEMA
            var usernameCollation = await db.Database.SqlQueryRaw<string>(
                "SELECT COLLATION_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'users' AND COLUMN_NAME = 'username'")
                .SingleAsync();
            usernameCollation.Should().Be("SQL_Latin1_General_CP1_CI_AS");

            // Verify composite descending index on parse_jobs exists
            var indexExists = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = 'ix_parse_jobs_user_id_created_at'")
                .SingleAsync();
            indexExists.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RenameZebraSlugsToPcr_UpAndDown_MigrateLegacyRecordsAndGuidanceCorrectly()
    {
        var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260817090817_AddRuntimeConfiguration");

        var testTimestamp = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync();
        }
        cmd.CommandText = """
            INSERT INTO users (username, password_hash, role, must_change_password, created_at, updated_at)
            VALUES ('zebra-migration-user', 'hash', 'user', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO parse_jobs (
                user_id, vendor, parser_slug, crm_template, source_filename, source_path, output_path,
                fx_rate, margin, computed_total, quoted_total, totals_match, split_by_solution_id, import_type, created_at)
            VALUES
                (@userId, 'Zebra', 'zebra_price_concession_pdf', 'No Calculation', 'Zebra_PC_97000001_V2.0.pdf', '/tmp/1.pdf', '/tmp/1.xlsx', 1, 0, 100, 100, 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'Zebra', 'zebra_price_concession_xls', 'No Calculation', 'Zebra_PC_97000001.xls', '/tmp/2.xls', '/tmp/2.xlsx', 1, 0, 200, 200, 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'HP', 'hp_bid_xlsx', 'No Calculation', 'Deals.xlsx', '/tmp/3.xlsx', '/tmp/3.xlsx', 1, 0, 300, 300, 1, 0, 'manual', SYSUTCDATETIME());

            INSERT INTO parse_metrics (
                user_id, user_username, vendor, parser_slug, source_filename, currency,
                computed_total, quoted_total, totals_match, fx_rate, margin, import_type, created_at)
            VALUES
                (@userId, 'zebra-migration-user', 'Zebra', 'zebra_price_concession_pdf', 'Zebra_PC_97000001_V2.0.pdf', 'AUD', 100, 100, 1, 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'zebra-migration-user', 'Zebra', 'zebra_price_concession_xls', 'Zebra_PC_97000001.xls', 'AUD', 200, 200, 1, 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'zebra-migration-user', 'HP', 'hp_bid_xlsx', 'Deals.xlsx', 'AUD', 300, 300, 1, 1, 0, 'manual', SYSUTCDATETIME());

            INSERT INTO failed_parse_jobs (
                user_id, user_username, vendor, parser_slug, source_filename, source_path, category,
                stage, hint, message, error_detail, fx_rate, margin, import_type, created_at)
            VALUES
                (@userId, 'zebra-migration-user', 'Zebra', 'zebra_price_concession_pdf', 'fail1.pdf', '/tmp/fail1.pdf', 'parsererror', 'extract', 'hint', 'msg', 'detail', 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'zebra-migration-user', 'Zebra', 'zebra_price_concession_xls', 'fail2.xls', '/tmp/fail2.xls', 'parsererror', 'extract', 'hint', 'msg', 'detail', 1, 0, 'manual', SYSUTCDATETIME()),
                (@userId, 'zebra-migration-user', 'HP', 'hp_bid_xlsx', 'fail3.xlsx', '/tmp/fail3.xlsx', 'parsererror', 'extract', 'hint', 'msg', 'detail', 1, 0, 'manual', SYSUTCDATETIME());

            INSERT INTO runtime_configs ([key], json_payload, created_at, updated_at)
            VALUES
                ('guidanceMessages', '[{"fileTypes":["hp_bid_xlsx","zebra_price_concession_pdf","zebra_price_concession_xls"],"html":"<p>Special Zebra & HP instructions with Price Concession notes</p>"}]', '2026-08-17T12:00:00Z', '2026-08-17T12:00:00Z'),
                ('vendorDefaults', '[{"vendors":["Zebra"],"onCostPct":2.85}]', '2026-08-17T12:00:00Z', '2026-08-17T12:00:00Z');
            """;
        await cmd.ExecuteNonQueryAsync();

        // Forward migration (Up)
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var jobSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM parse_jobs ORDER BY id").ToListAsync();
        jobSlugs.Should().Equal("zebra_pcr_pdf", "zebra_pcr_xls", "hp_bid_xlsx");

        var metricSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM parse_metrics ORDER BY id").ToListAsync();
        metricSlugs.Should().Equal("zebra_pcr_pdf", "zebra_pcr_xls", "hp_bid_xlsx");

        var failedJobSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM failed_parse_jobs ORDER BY id").ToListAsync();
        failedJobSlugs.Should().Equal("zebra_pcr_pdf", "zebra_pcr_xls", "hp_bid_xlsx");

        var guidanceConfig = await db.RuntimeConfigs.SingleAsync(c => c.Key == "guidanceMessages");
        guidanceConfig.JsonPayload.Should().Contain("\"zebra_pcr_pdf\"");
        guidanceConfig.JsonPayload.Should().Contain("\"zebra_pcr_xls\"");
        guidanceConfig.JsonPayload.Should().NotContain("zebra_price_concession_pdf");
        guidanceConfig.JsonPayload.Should().NotContain("zebra_price_concession_xls");
        guidanceConfig.JsonPayload.Should().Contain("<p>Special Zebra & HP instructions with Price Concession notes</p>");
        guidanceConfig.CreatedAt.Should().Be(testTimestamp);
        guidanceConfig.UpdatedAt.Should().Be(testTimestamp);

        var vendorDefaults = await db.RuntimeConfigs.SingleAsync(c => c.Key == "vendorDefaults");
        vendorDefaults.JsonPayload.Should().Be("[{\"vendors\":[\"Zebra\"],\"onCostPct\":2.85}]");
        vendorDefaults.CreatedAt.Should().Be(testTimestamp);
        vendorDefaults.UpdatedAt.Should().Be(testTimestamp);

        // Reversible migration (Down)
        await migrator.MigrateAsync("20260817090817_AddRuntimeConfiguration");
        db.ChangeTracker.Clear();

        var revertedJobSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM parse_jobs ORDER BY id").ToListAsync();
        revertedJobSlugs.Should().Equal("zebra_price_concession_pdf", "zebra_price_concession_xls", "hp_bid_xlsx");

        var revertedMetricSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM parse_metrics ORDER BY id").ToListAsync();
        revertedMetricSlugs.Should().Equal("zebra_price_concession_pdf", "zebra_price_concession_xls", "hp_bid_xlsx");

        var revertedFailedJobSlugs = await db.Database.SqlQueryRaw<string>(
            "SELECT parser_slug AS Value FROM failed_parse_jobs ORDER BY id").ToListAsync();
        revertedFailedJobSlugs.Should().Equal("zebra_price_concession_pdf", "zebra_price_concession_xls", "hp_bid_xlsx");

        var revertedGuidance = await db.RuntimeConfigs.SingleAsync(c => c.Key == "guidanceMessages");
        revertedGuidance.JsonPayload.Should().Contain("\"zebra_price_concession_pdf\"");
        revertedGuidance.JsonPayload.Should().Contain("\"zebra_price_concession_xls\"");
        revertedGuidance.JsonPayload.Should().NotContain("zebra_pcr_pdf");
        revertedGuidance.JsonPayload.Should().NotContain("zebra_pcr_xls");
        revertedGuidance.JsonPayload.Should().Contain("<p>Special Zebra & HP instructions with Price Concession notes</p>");
        revertedGuidance.CreatedAt.Should().Be(testTimestamp);
        revertedGuidance.UpdatedAt.Should().Be(testTimestamp);
    }

    private sealed class BidMigrationRow
    {
        public required string SourceFilename { get; init; }
        public decimal ComputedTotal { get; init; }
        public string? BidNumber { get; init; }
        public string? BidRevision { get; init; }
    }
}

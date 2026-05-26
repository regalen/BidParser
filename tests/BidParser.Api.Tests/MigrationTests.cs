using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task FreshDatabaseMigratesAndBootstrapsAdmin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "db.sqlite");

        try
        {
            using var environment = new ScopedEnvironment(new Dictionary<string, string>
            {
                ["DATABASE_URL"] = $"sqlite:///{dbPath}",
                ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
                ["ADMIN_USERNAME"] = "phase2-admin",
                ["ADMIN_PASSWORD"] = "change-me-123!"
            });
            using var factory = CreateFactory();
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

            var migrationIds = await db.Database.GetAppliedMigrationsAsync();
            migrationIds.Should().Equal(
                "00000000000001_InitialCreate",
                "20260519000001_HistoryCompositeIndex",
                "20260519000002_SourceFilenameNoCase",
                "20260523045730_AddParseMetricsLedger",
                "20260523052812_AddFailedParseJobs",
                "20260526000000_AddValidationMismatchToFailedParseJobs");

            var userTableSql = await ReadScalarAsync(dbPath, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'users';");
            userTableSql.Should().Contain("TEXT COLLATE NOCASE");

            var parseJobsTableSql = await ReadScalarAsync(dbPath, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'parse_jobs';");
            parseJobsTableSql.Should().Contain("\"source_filename\" TEXT COLLATE NOCASE");

            var historyPlan = await ReadQueryPlanAsync(
                dbPath,
                "EXPLAIN QUERY PLAN SELECT id FROM parse_jobs WHERE user_id = 1 ORDER BY created_at DESC LIMIT 10;");
            historyPlan.Should().Contain("ix_parse_jobs_user_id_created_at");

            var journalMode = await ReadScalarAsync(dbPath, "PRAGMA journal_mode;");
            journalMode.Should().Be("wal");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>();
    }

    private static async Task<string> ReadScalarAsync(string dbPath, string commandText)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? string.Empty;
    }

    private static async Task<string> ReadQueryPlanAsync(string dbPath, string commandText)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();

        var details = new List<string>();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        return string.Join(Environment.NewLine, details);
    }
}

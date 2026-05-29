using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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

            var migrationIds = await db.Database.GetAppliedMigrationsAsync();
            migrationIds.Should().ContainSingle().Which.Should().EndWith("_InitialCreate");

            // Verify CI collation on username and source_filename via INFORMATION_SCHEMA
            var usernameCollation = await db.Database.SqlQueryRaw<string>(
                "SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'users' AND COLUMN_NAME = 'username'")
                .SingleAsync();
            usernameCollation.Should().Be("SQL_Latin1_General_CP1_CI_AS");

            // Verify composite descending index on parse_jobs exists
            var indexExists = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE name = 'ix_parse_jobs_user_id_created_at'")
                .SingleAsync();
            indexExists.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

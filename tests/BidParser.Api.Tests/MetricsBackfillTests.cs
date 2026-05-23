using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MetricsBackfillTests
{
    [Fact]
    public async Task MigrationBackfillsParseMetrics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "db.sqlite");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            // First, run up to the previous migration
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            using (var db = new AppDbContext(options))
            {
                var migrator = db.Database.GetInfrastructure().GetRequiredService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                // We migrate to "20260519000002_SourceFilenameNoCase", the migration right before "AddParseMetricsLedger"
                await migrator.MigrateAsync("20260519000002_SourceFilenameNoCase");
            }

            // Seed User and pre-migration ParseJobs
            using (var db = new AppDbContext(options))
            {
                var user = new User
                {
                    Username = "testuser",
                    Name = "Test User",
                    PasswordHash = "hash",
                    Role = UserRole.User,
                    MustChangePassword = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                var jobs = new[]
                {
                    new ParseJob
                    {
                        UserId = user.Id,
                        Vendor = "Nutanix",
                        ParserSlug = "nutanix_software_only_pdf",
                        SourceFilename = "file1.pdf",
                        SourcePath = "/data/files/file1.pdf",
                        OutputPath = "/data/files/out1.xlsx",
                        FxRate = 1.0m,
                        Margin = 10m,
                        ComputedTotal = 100m,
                        QuotedTotal = 100m,
                        TotalsMatch = true,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new ParseJob
                    {
                        UserId = user.Id,
                        Vendor = "Nutanix",
                        ParserSlug = "nutanix_renewal_pdf",
                        SourceFilename = "file2.pdf",
                        SourcePath = "/data/files/file2.pdf",
                        OutputPath = "/data/files/out2.xlsx",
                        FxRate = 1.5m,
                        Margin = 20m,
                        ComputedTotal = 200m,
                        QuotedTotal = 250m,
                        TotalsMatch = false,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    }
                };
                db.ParseJobs.AddRange(jobs);
                await db.SaveChangesAsync();
            }

            // Now apply the new migration
            using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
            }

            // Assert backfilled ParseMetric rows
            using (var db = new AppDbContext(options))
            {
                var metrics = await db.ParseMetrics.OrderBy(m => m.Id).ToListAsync();
                metrics.Should().HaveCount(2);

                metrics[0].UserUsername.Should().Be("testuser");
                metrics[0].UserName.Should().Be("Test User");
                metrics[0].Vendor.Should().Be("Nutanix");
                metrics[0].ParserSlug.Should().Be("nutanix_software_only_pdf");
                metrics[0].SourceFilename.Should().Be("file1.pdf");
                metrics[0].Currency.Should().Be("USD"); // Default from backfill
                metrics[0].ComputedTotal.Should().Be(100m);
                metrics[0].QuotedTotal.Should().Be(100m);
                metrics[0].TotalsMatch.Should().BeTrue();
                metrics[0].FxRate.Should().Be(1.0m);
                metrics[0].Margin.Should().Be(10m);

                metrics[1].Vendor.Should().Be("Nutanix");
                metrics[1].ParserSlug.Should().Be("nutanix_renewal_pdf");
                metrics[1].TotalsMatch.Should().BeFalse();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

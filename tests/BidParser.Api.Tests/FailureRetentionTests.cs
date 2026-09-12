namespace BidParser.Api.Tests;

using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FailureRetentionTests
{
    [Fact]
    public async Task RetentionService_PurgesOldFailedParseJobsAndFiles()
    {
        using var fixture = await ApiTestFixture.CreateAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();

        // Need to know the upload directory. Can get it from options or just write somewhere
        var sourcePath = Path.Combine(Path.GetTempPath(), $"failed_test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        var oldFailure = new FailedParseJob
        {
            UserUsername = "admin",
            Vendor = "Nutanix",
            ParserSlug = "test_slug",
            SourceFilename = "failed_test.pdf",
            SourcePath = sourcePath,
            Category = FailureCategory.UnhandledException,
            ErrorDetail = "System.Exception: Test",
            FxRate = 1.0m,
            Margin = 0.0m,
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };

        db.FailedParseJobs.Add(oldFailure);
        await db.SaveChangesAsync();

        var deleted = await retention.CleanupOldParseJobsAsync(90);

        Assert.False(File.Exists(sourcePath));
        var count = await db.FailedParseJobs.CountAsync();
        Assert.Equal(0, count);
    }
}

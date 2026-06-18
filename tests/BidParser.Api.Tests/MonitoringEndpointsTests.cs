namespace BidParser.Api.Tests;

using System.Net;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MonitoringEndpointsTests
{
    [Fact]
    public async Task FailureSourceDownload_RequiresAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockUserAsync(client);

        var sourceRes = await client.GetAsync("/api/monitoring/failures/1/source");
        sourceRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSource_StreamsFile_AndReturns404IfMissing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sourcePath = Path.Combine(fixture.UploadDir, "test.pdf");
        await File.WriteAllTextAsync(sourcePath, "test_file_content");

        var failure = new FailedParseJob
        {
            UserUsername = "admin",
            Vendor = "Nutanix",
            ParserSlug = "test_slug",
            SourceFilename = "test_download.pdf",
            SourcePath = sourcePath,
            Category = FailureCategory.UnhandledException,
            ErrorDetail = "error",
            FxRate = 1m,
            Margin = 0m,
        };
        db.FailedParseJobs.Add(failure);
        await db.SaveChangesAsync();

        var sourceRes = await client.GetAsync($"/api/monitoring/failures/{failure.Id}/source");
        sourceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        sourceRes.Content.Headers.ContentDisposition!.FileName.Should().Be("test_download.pdf");
        var content = await sourceRes.Content.ReadAsStringAsync();
        content.Should().Be("test_file_content");

        File.Delete(sourcePath);

        var sourceResAfterDelete = await client.GetAsync($"/api/monitoring/failures/{failure.Id}/source");
        sourceResAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

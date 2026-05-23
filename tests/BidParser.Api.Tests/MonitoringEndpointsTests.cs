namespace BidParser.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MonitoringEndpointsTests
{
    [Fact]
    public async Task MonitoringEndpoints_RequireAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockUserAsync(client);

        var listRes = await client.GetAsync("/api/monitoring/failures");
        listRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sourceRes = await client.GetAsync("/api/monitoring/failures/1/source");
        sourceRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetFailures_ReturnsExpectedShapeAndPagination()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sourcePath = Path.Combine(fixture.UploadDir, "test.pdf");
        await File.WriteAllTextAsync(sourcePath, "test");

        for (int i = 0; i < 30; i++)
        {
            db.FailedParseJobs.Add(new FailedParseJob
            {
                UserId = 1,
                UserUsername = "admin",
                Vendor = "Nutanix",
                ParserSlug = "nutanix_software_only_pdf",
                SourceFilename = $"file_{i}.pdf",
                SourcePath = sourcePath,
                Category = FailureCategory.ParserError,
                Stage = "extract",
                Hint = "hint",
                Message = "message",
                ErrorDetail = "error",
                FxRate = 1m,
                Margin = 0m,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();

        var res = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/monitoring/failures?limit=25");
        res.GetProperty("total").GetInt32().Should().Be(30);
        
        var items = res.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(25);
        
        var first = items[0];
        first.GetProperty("parser_display_name").GetString().Should().Be("Software Only (PDF)");
        first.GetProperty("source_available").GetBoolean().Should().BeTrue();
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

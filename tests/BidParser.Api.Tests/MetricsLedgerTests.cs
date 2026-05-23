using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MetricsLedgerTests
{
    private static Task<HttpResponseMessage> ParseAsync(HttpClient client, string sourcePdf, string parserSlug)
    {
        var form = new MultipartFormDataContent();
        var fileBytes = File.ReadAllBytes(sourcePdf);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "file", Path.GetFileName(sourcePdf));
        form.Add(new StringContent("Nutanix"), "vendor");
        form.Add(new StringContent(parserSlug), "parser_slug");
        form.Add(new StringContent("1.52"), "fx_rate");
        form.Add(new StringContent("15.0"), "margin");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task SuccessfulParseWritesParseMetricAndParseJob()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sourcePdf = Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf");
        var parseRes = await ParseAsync(client, sourcePdf, "nutanix_software_only_pdf");
        parseRes.EnsureSuccessStatusCode();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.ParseJobs.SingleAsync();
        var metric = await db.ParseMetrics.SingleAsync();

        metric.ParseJobId.Should().Be(job.Id);
        metric.UserId.Should().Be(job.UserId);
        metric.UserUsername.Should().Be("admin");
        metric.Vendor.Should().Be("Nutanix");
        metric.ParserSlug.Should().Be("nutanix_software_only_pdf");
        metric.SourceFilename.Should().Be("XQ-4076249.pdf");
        metric.Currency.Should().Be("USD");
        metric.QuotedTotal.Should().Be(job.QuotedTotal);
        metric.ComputedTotal.Should().Be(job.ComputedTotal);
        metric.TotalsMatch.Should().Be(job.TotalsMatch);
        metric.FxRate.Should().Be(1.52m);
        metric.Margin.Should().Be(15.0m);
    }

    [Fact]
    public async Task ParseFailureWritesNoMetric()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var invalidSource = Path.Combine(tempDir, "invalid.pdf");
        await File.WriteAllTextAsync(invalidSource, "not a real pdf");

        try
        {
            var parseRes = await ParseAsync(client, invalidSource, "nutanix_software_only_pdf");
            parseRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var metrics = await db.ParseMetrics.ToListAsync();
            metrics.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ParseJobRetentionLeavesMetricIntact()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sourcePdf = Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf");
        var parseRes = await ParseAsync(client, sourcePdf, "nutanix_software_only_pdf");
        parseRes.EnsureSuccessStatusCode();

        using (var scope1 = fixture.Factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var job1 = await db1.ParseJobs.SingleAsync();
            job1.CreatedAt = DateTime.UtcNow.AddDays(-100);
            await db1.SaveChangesAsync();
        }

        using (var scope2 = fixture.Factory.Services.CreateScope())
        {
            var retentionService = scope2.ServiceProvider.GetRequiredService<RetentionService>();
            await retentionService.CleanupOldParseJobsAsync(90, CancellationToken.None);
        }

        using (var scope3 = fixture.Factory.Services.CreateScope())
        {
            var db3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobs = await db3.ParseJobs.ToListAsync();
            jobs.Should().BeEmpty();

            var metric = await db3.ParseMetrics.SingleAsync();
            metric.ParseJobId.Should().BeNull();
            metric.SourceFilename.Should().Be("XQ-4076249.pdf");
        }
    }

    [Fact]
    public async Task DeleteUserLeavesMetricIntact()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var createUserRes = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "user2", name = "User Two", role = "user" });
        createUserRes.EnsureSuccessStatusCode();
        var user2 = await createUserRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var user2Id = user2.GetProperty("id").GetInt32();

        await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        var loginRes2 = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "user2", password = "changeme" });
        loginRes2.EnsureSuccessStatusCode();

        var changeRes = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "NewPassword1!" });
        changeRes.EnsureSuccessStatusCode();

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sourcePdf = Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf");
        var parseRes = await ParseAsync(client, sourcePdf, "nutanix_software_only_pdf");
        parseRes.EnsureSuccessStatusCode();

        await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        var relogin = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "admin", password = "Admin123!" });
        relogin.EnsureSuccessStatusCode();

        var deleteRes = await ApiTestFixture.DeleteWithCsrfAsync(client, $"/api/users/{user2Id}");
        deleteRes.EnsureSuccessStatusCode();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var u2 = await db.Users.FirstOrDefaultAsync(u => u.Id == user2Id);
        u2.Should().BeNull();

        var metric = await db.ParseMetrics.SingleAsync(m => m.UserUsername == "user2");
        metric.UserId.Should().BeNull();
    }
}

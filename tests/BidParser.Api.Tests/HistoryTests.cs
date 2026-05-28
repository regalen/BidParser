using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class HistoryTests
{
    // ── list endpoint ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HistoryListShowsJobsAfterParse()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));
        await PostParseAsync(client, bytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "0.7400", "7.50");

        var response = await client.GetFromJsonAsync<JsonElement>("/api/history");
        response.GetProperty("total").GetInt32().Should().Be(1);
        var row = response.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("source_filename").GetString().Should().Be("XQ-4076249.pdf");
        row.GetProperty("vendor").GetString().Should().Be("Nutanix");
        row.GetProperty("parser_slug").GetString().Should().Be("nutanix_software_only_pdf");
        row.GetProperty("file_type_display").GetString().Should().Be("Software Only (PDF)");
        row.GetProperty("crm_template").GetString().Should().Be("Foreign Uplift");
        row.GetProperty("totals_match").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HistoryListUserScopedAndQFilter()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // Upload two files
        var root = FindRepoRoot();
        var pdfBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));
        var xlsxBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4108785.xlsx"));
        await PostParseAsync(client, pdfBytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");
        await PostParseAsync(client, xlsxBytes, "XQ-4108785.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", "nutanix_hardware_only_xlsx", "1.0", "5.0");

        // Create a second user and parse under their account
        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users",
            new { username = "user2", name = "User Two", role = "user" });
        create.EnsureSuccessStatusCode();

        using var client2 = fixture.Factory.CreateClient();
        await ApiTestFixture.PostJsonWithCsrfAsync(client2, "/api/auth/login",
            new { username = "user2", password = "changeme" });
        await ApiTestFixture.PostJsonWithCsrfAsync(client2, "/api/auth/change-password",
            new { old_password = "changeme", new_password = "User2Pass1!" });
        await PostParseAsync(client2, pdfBytes, "user2_quote.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        // Admin sees only their own 2 jobs
        var adminHistory = await client.GetFromJsonAsync<JsonElement>("/api/history?limit=100");
        adminHistory.GetProperty("total").GetInt32().Should().Be(2);

        // User2 sees only their 1 job
        var user2History = await client2.GetFromJsonAsync<JsonElement>("/api/history?limit=100");
        user2History.GetProperty("total").GetInt32().Should().Be(1);

        // q filter: match "4076249"
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/history?q=4076249");
        filtered.GetProperty("total").GetInt32().Should().Be(1);
        filtered.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("source_filename").GetString().Should().Contain("4076249");

        var lowerFiltered = await client.GetFromJsonAsync<JsonElement>("/api/history?q=xq-4076");
        lowerFiltered.GetProperty("total").GetInt32().Should().Be(1);
        lowerFiltered.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("source_filename").GetString().Should().Be("XQ-4076249.pdf");

        var upperFiltered = await client.GetFromJsonAsync<JsonElement>("/api/history?q=XQ-4076");
        upperFiltered.GetProperty("total").GetInt32().Should().Be(1);
        upperFiltered.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("source_filename").GetString().Should().Be("XQ-4076249.pdf");

        // whitespace-only q treated as no filter
        var unfiltered = await client.GetFromJsonAsync<JsonElement>("/api/history?q=++++");
        unfiltered.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task HistoryListPagination()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));
        for (var i = 0; i < 3; i++)
        {
            await PostParseAsync(client, bytes, $"quote_{i}.pdf", "application/pdf",
                "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");
        }

        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/history?limit=2&offset=0");
        page1.GetProperty("total").GetInt32().Should().Be(3);
        page1.GetProperty("rows").EnumerateArray().Should().HaveCount(2);

        var page2 = await client.GetFromJsonAsync<JsonElement>("/api/history?limit=2&offset=2");
        page2.GetProperty("total").GetInt32().Should().Be(3);
        page2.GetProperty("rows").EnumerateArray().Should().HaveCount(1);
    }

    // ── relative-when formatting ──────────────────────────────────────────────

    [Fact]
    public async Task HistoryRelativeWhenFormatsAllBranches()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync();

        var cases = new (double SecondsAgo, string Expected)[]
        {
            (30, "just now"),
            (300, "5m ago"),
            (3 * 3600, "3h ago"),
            (30 * 3600, "Yesterday"),
            (3 * 24 * 3600, "3 days ago"),
            (14 * 24 * 3600, DateTime.UtcNow.AddSeconds(-14 * 24 * 3600).ToString("dd/MM/yyyy"))
        };

        var jobIds = new List<int>();
        foreach (var (secondsAgo, _) in cases)
        {
            var job = new ParseJob
            {
                UserId = admin.Id,
                Vendor = "Nutanix",
                ParserSlug = "nutanix_software_only_pdf",
                CrmTemplate = "Foreign Uplift",
                SourceFilename = $"when_test_{secondsAgo}.pdf",
                SourcePath = "/fake/source.pdf",
                OutputPath = "/fake/output.xlsx",
                FxRate = 1m,
                Margin = 5m,
                ComputedTotal = 100m,
                TotalsMatch = true
            };
            db.ParseJobs.Add(job);
            await db.SaveChangesAsync();

            var createdAt = DateTime.UtcNow.AddSeconds(-secondsAgo);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE parse_jobs SET created_at = {0} WHERE id = {1}",
                createdAt.ToString("yyyy-MM-dd HH:mm:ss.fffffff"),
                job.Id);

            jobIds.Add(job.Id);
        }

        var response = await client.GetFromJsonAsync<JsonElement>("/api/history?limit=100");
        var rows = response.GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetInt32());

        foreach (var (id, (_, expected)) in jobIds.Zip(cases))
        {
            rows[id].GetProperty("when").GetString().Should().Be(expected, $"job id={id} ({expected})");
        }
    }

    // ── downloads ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HistoryDownloadRoundtrip()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));
        await PostParseAsync(client, bytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        var history = await client.GetFromJsonAsync<JsonElement>("/api/history");
        var jobId = history.GetProperty("rows").EnumerateArray().First().GetProperty("id").GetInt32();

        var source = await client.GetAsync($"/api/history/{jobId}/source");
        source.StatusCode.Should().Be(HttpStatusCode.OK);
        var sourceBytes = await source.Content.ReadAsByteArrayAsync();
        sourceBytes.Length.Should().BeGreaterThan(0);

        var output = await client.GetAsync($"/api/history/{jobId}/output");
        output.StatusCode.Should().Be(HttpStatusCode.OK);
        var outputBytes = await output.Content.ReadAsByteArrayAsync();
        outputBytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HistoryDownloadCrossUserReturns404()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // Admin uploads a file
        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));
        await PostParseAsync(client, bytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");
        var history = await client.GetFromJsonAsync<JsonElement>("/api/history");
        var adminJobId = history.GetProperty("rows").EnumerateArray().First().GetProperty("id").GetInt32();

        // Create user2 and log them in
        await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users",
            new { username = "user2", name = "User Two", role = "user" });
        using var client2 = fixture.Factory.CreateClient();
        await ApiTestFixture.PostJsonWithCsrfAsync(client2, "/api/auth/login",
            new { username = "user2", password = "changeme" });
        await ApiTestFixture.PostJsonWithCsrfAsync(client2, "/api/auth/change-password",
            new { old_password = "changeme", new_password = "User2Pass1!" });

        // User2 can't see or download admin's job
        var sourceResponse = await client2.GetAsync($"/api/history/{adminJobId}/source");
        sourceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outputResponse = await client2.GetAsync($"/api/history/{adminJobId}/output");
        outputResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── retention ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetentionDeletesExpiredJobsAndFiles()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync();

        // Write real files to disk so the retention can delete them
        var uploadDir = scope.ServiceProvider.GetRequiredService<BidParser.Infrastructure.Storage.FileStorage>();
        var sourceFile = Path.Combine(Path.GetTempPath(), $"ret_source_{Guid.NewGuid():N}.pdf");
        var outputFile = Path.Combine(Path.GetTempPath(), $"ret_output_{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(sourceFile, "fake pdf");
        File.WriteAllText(outputFile, "fake xlsx");

        var job = new ParseJob
        {
            UserId = admin.Id,
            Vendor = "Nutanix",
            ParserSlug = "nutanix_software_only_pdf",
            CrmTemplate = "Foreign Uplift",
            SourceFilename = "old_quote.pdf",
            SourcePath = sourceFile,
            OutputPath = outputFile,
            FxRate = 1m,
            Margin = 5m,
            ComputedTotal = 100m,
            TotalsMatch = true
        };
        db.ParseJobs.Add(job);
        await db.SaveChangesAsync();

        // Age the job to 1000 days ago (well past any retention window)
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE parse_jobs SET created_at = {0} WHERE id = {1}",
            DateTime.UtcNow.AddDays(-1000).ToString("yyyy-MM-dd HH:mm:ss.fffffff"),
            job.Id);

        // Add a fresh job that should NOT be deleted (retention = 90 days)
        var freshJob = new ParseJob
        {
            UserId = admin.Id,
            Vendor = "Nutanix",
            ParserSlug = "nutanix_software_only_pdf",
            CrmTemplate = "Foreign Uplift",
            SourceFilename = "fresh_quote.pdf",
            SourcePath = "/fake/fresh_source.pdf",
            OutputPath = "/fake/fresh_output.xlsx",
            FxRate = 1m,
            Margin = 5m,
            ComputedTotal = 100m,
            TotalsMatch = true
        };
        db.ParseJobs.Add(freshJob);
        await db.SaveChangesAsync();

        var retentionService = scope.ServiceProvider.GetRequiredService<RetentionService>();
        var deleted = await retentionService.CleanupOldParseJobsAsync(retentionDays: 90);

        deleted.Should().Be(1);
        (await db.ParseJobs.AnyAsync(j => j.Id == job.Id)).Should().BeFalse();
        (await db.ParseJobs.AnyAsync(j => j.Id == freshJob.Id)).Should().BeTrue();
        File.Exists(sourceFile).Should().BeFalse();
        File.Exists(outputFile).Should().BeFalse();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client,
        byte[] fileBytes,
        string filename,
        string contentType,
        string vendor,
        string parserSlug,
        string fxRate,
        string margin)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parser_slug");
        form.Add(new StringContent(fxRate), "fx_rate");
        form.Add(new StringContent(margin), "margin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return client.SendAsync(request);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

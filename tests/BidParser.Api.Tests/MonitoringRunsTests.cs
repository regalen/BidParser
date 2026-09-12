namespace BidParser.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MonitoringRunsTests
{
    [Fact]
    public async Task RunsAndJobDownloads_RequireAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockUserAsync(client);

        (await client.GetAsync("/api/monitoring/runs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/monitoring/jobs/1/source")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/monitoring/jobs/1/output")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Runs_UnifiesSuccessfulJobsAndFailures_AndDedupesValidationMismatch()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        var outPath = Path.Combine(fixture.UploadDir, "out.xlsx");
        await File.WriteAllTextAsync(src, "src");
        await File.WriteAllTextAsync(outPath, "out");

        // Successful job
        db.ParseJobs.Add(NewJob(adminId, src, outPath, "success.pdf", totalsMatch: true, createdAt: Hours(1)));
        // Validation mismatch — exists as BOTH a ParseJob and a (best-effort) FailedParseJob.
        db.ParseJobs.Add(NewJob(adminId, src, outPath, "mismatch.pdf", totalsMatch: false, createdAt: Hours(2)));
        db.FailedParseJobs.Add(NewFailure(adminId, src, "mismatch.pdf", FailureCategory.ValidationMismatch, Hours(2)));
        // Genuine recognition/parse failure
        db.FailedParseJobs.Add(NewFailure(adminId, src, "bad.pdf", FailureCategory.ParserError, Hours(3)));
        await db.SaveChangesAsync();

        var res = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs");
        var items = res.GetProperty("items").EnumerateArray().ToList();

        // 3 rows: success job + mismatch job + parserError failure. The ValidationMismatch
        // FailedParseJob is suppressed in favour of its ParseJob.
        res.GetProperty("total").GetInt32().Should().Be(3);
        items.Should().HaveCount(3);

        var byFile = items.ToDictionary(i => i.GetProperty("sourceFilename").GetString()!);

        byFile["success.pdf"].GetProperty("status").GetString().Should().Be("success");
        byFile["success.pdf"].GetProperty("kind").GetString().Should().Be("job");
        byFile["success.pdf"].GetProperty("outputAvailable").GetBoolean().Should().BeTrue();

        var mismatch = byFile["mismatch.pdf"];
        mismatch.GetProperty("status").GetString().Should().Be("validationMismatch");
        mismatch.GetProperty("kind").GetString().Should().Be("job");
        mismatch.GetProperty("outputAvailable").GetBoolean().Should().BeTrue();

        var failure = byFile["bad.pdf"];
        failure.GetProperty("status").GetString().Should().Be("parserError");
        failure.GetProperty("kind").GetString().Should().Be("failure");
        failure.GetProperty("outputAvailable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Runs_StatusFilter_NarrowsAcrossTables()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        db.ParseJobs.Add(NewJob(adminId, src, src, "ok.pdf", totalsMatch: true, createdAt: Hours(1)));
        db.FailedParseJobs.Add(NewFailure(adminId, src, "boom.pdf", FailureCategory.UnhandledException, Hours(2)));
        await db.SaveChangesAsync();

        var success = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?status=success");
        success.GetProperty("total").GetInt32().Should().Be(1);
        success.GetProperty("items").EnumerateArray().Single()
            .GetProperty("sourceFilename").GetString().Should().Be("ok.pdf");

        var unhandled = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?status=unhandledException");
        unhandled.GetProperty("total").GetInt32().Should().Be(1);
        unhandled.GetProperty("items").EnumerateArray().Single()
            .GetProperty("sourceFilename").GetString().Should().Be("boom.pdf");
    }

    [Fact]
    public async Task Runs_VendorAndUserFilters_Narrow()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var nutanix = NewJob(adminId, src, src, "n.pdf", totalsMatch: true, createdAt: Hours(1));
        var hp = NewJob(adminId, src, src, "h.pdf", totalsMatch: true, createdAt: Hours(2));
        hp.Vendor = "HP";
        db.ParseJobs.AddRange(nutanix, hp);
        await db.SaveChangesAsync();

        var byVendor = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?vendor=HP");
        byVendor.GetProperty("total").GetInt32().Should().Be(1);
        byVendor.GetProperty("items").EnumerateArray().Single()
            .GetProperty("vendor").GetString().Should().Be("HP");

        var byUser = await client.GetFromJsonAsync<JsonElement>($"/api/monitoring/runs?userId={adminId}");
        byUser.GetProperty("total").GetInt32().Should().Be(2);

        var byMissingUser = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?userId=99999");
        byMissingUser.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Runs_ImportTypeFilter_And_Property_Surfaced()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var autoJob = NewJob(adminId, src, src, "auto.pdf", totalsMatch: true, createdAt: Hours(1));
        autoJob.ImportType = ImportType.Auto;
        var manualJob = NewJob(adminId, src, src, "manual.pdf", totalsMatch: true, createdAt: Hours(2));
        manualJob.ImportType = ImportType.Manual;
        var nullJob = NewJob(adminId, src, src, "null.pdf", totalsMatch: true, createdAt: Hours(3));
        nullJob.ImportType = null;

        db.ParseJobs.AddRange(autoJob, manualJob, nullJob);
        await db.SaveChangesAsync();

        var autoRes = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?importType=auto");
        autoRes.GetProperty("total").GetInt32().Should().Be(1);
        var autoItem = autoRes.GetProperty("items").EnumerateArray().Single();
        autoItem.GetProperty("sourceFilename").GetString().Should().Be("auto.pdf");
        autoItem.GetProperty("importType").GetString().Should().Be("auto");

        var manualRes = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?importType=manual");
        manualRes.GetProperty("total").GetInt32().Should().Be(1);
        var manualItem = manualRes.GetProperty("items").EnumerateArray().Single();
        manualItem.GetProperty("sourceFilename").GetString().Should().Be("manual.pdf");
        manualItem.GetProperty("importType").GetString().Should().Be("manual");

        var allRes = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs");
        var allItems = allRes.GetProperty("items").EnumerateArray().ToList();
        var nullItem = allItems.First(i => i.GetProperty("sourceFilename").GetString() == "null.pdf");
        nullItem.GetProperty("importType").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task JobDownloads_StreamSourceAndOutput_AndReturn404IfMissing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        var outPath = Path.Combine(fixture.UploadDir, "out.xlsx");
        await File.WriteAllTextAsync(src, "source-bytes");
        await File.WriteAllTextAsync(outPath, "output-bytes");

        var job = NewJob(adminId, src, outPath, "quote.pdf", totalsMatch: true, createdAt: Hours(1));
        job.BidNumber = "BID-123";
        job.BidRevision = "4";
        db.ParseJobs.Add(job);
        await db.SaveChangesAsync();

        var sourceRes = await client.GetAsync($"/api/monitoring/jobs/{job.Id}/source");
        sourceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        sourceRes.Content.Headers.ContentDisposition!.FileName.Should().Be("quote.pdf");
        (await sourceRes.Content.ReadAsStringAsync()).Should().Be("source-bytes");

        var outputRes = await client.GetAsync($"/api/monitoring/jobs/{job.Id}/output");
        outputRes.StatusCode.Should().Be(HttpStatusCode.OK);
        outputRes.Content.Headers.ContentDisposition!.FileName.Should().Be("BID-123_4_ForeignUplift.xlsx");
        (await outputRes.Content.ReadAsStringAsync()).Should().Be("output-bytes");

        File.Delete(outPath);
        (await client.GetAsync($"/api/monitoring/jobs/{job.Id}/output")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static DateTime Hours(int h) => DateTime.UtcNow.AddHours(-24).AddHours(h);

    private static ParseJob NewJob(
        int userId, string src, string outPath, string filename, bool totalsMatch, DateTime createdAt) => new()
    {
        UserId = userId,
        Vendor = "Nutanix",
        ParserSlug = "nutanix_software_only_pdf",
        CrmTemplate = "Foreign Uplift",
        SourceFilename = filename,
        SourcePath = src,
        OutputPath = outPath,
        FxRate = 1m,
        Margin = 0m,
        ComputedTotal = 100m,
        QuotedTotal = totalsMatch ? 100m : 200m,
        TotalsMatch = totalsMatch,
        CreatedAt = createdAt,
    };

    private static FailedParseJob NewFailure(
        int userId, string src, string filename, FailureCategory category, DateTime createdAt) => new()
    {
        UserId = userId,
        UserUsername = "admin",
        Vendor = "Nutanix",
        ParserSlug = "nutanix_software_only_pdf",
        SourceFilename = filename,
        SourcePath = src,
        Category = category,
        ErrorDetail = "error",
        FxRate = 1m,
        Margin = 0m,
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task Runs_RespectsRangeAll()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        // Old job outside the 30 day default window
        db.ParseJobs.Add(NewJob(adminId, src, src, "old.pdf", totalsMatch: true, createdAt: DateTime.UtcNow.AddDays(-60)));
        await db.SaveChangesAsync();

        var defaultRes = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs");
        defaultRes.GetProperty("total").GetInt32().Should().Be(0);

        var allRes = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?range=all");
        allRes.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Runs_DefaultBoundaryCoverage()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var today = DateTime.UtcNow;

        // Included: Today - 29 days
        db.ParseJobs.Add(NewJob(adminId, src, src, "included.pdf", totalsMatch: true, createdAt: today.AddDays(-29)));
        // Excluded: Today - 30 days
        db.ParseJobs.Add(NewJob(adminId, src, src, "excluded.pdf", totalsMatch: true, createdAt: today.AddDays(-30)));
        await db.SaveChangesAsync();

        var res = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs");
        res.GetProperty("total").GetInt32().Should().Be(1);
        var items = res.GetProperty("items").EnumerateArray().ToList();
        items[0].GetProperty("sourceFilename").GetString().Should().Be("included.pdf");
    }

    [Fact]
    public async Task Runs_DatabasePagination()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync()).Id;

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var dt = DateTime.UtcNow;

        // Equal timestamps force the deterministic kind/id tie-breakers to execute.
        db.ParseJobs.Add(NewJob(adminId, src, src, "job1.pdf", totalsMatch: true, createdAt: dt));
        db.FailedParseJobs.Add(NewFailure(adminId, src, "fail1.pdf", FailureCategory.ParserError, dt));
        db.ParseJobs.Add(NewJob(adminId, src, src, "job2.pdf", totalsMatch: true, createdAt: dt));
        db.FailedParseJobs.Add(NewFailure(adminId, src, "fail2.pdf", FailureCategory.ParserError, dt));
        db.ParseJobs.Add(NewJob(adminId, src, src, "job3.pdf", totalsMatch: true, createdAt: dt));

        await db.SaveChangesAsync();

        // 5 total items
        // Kind descending puts jobs before failures; id descending stabilises each kind.

        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?limit=2&offset=0");
        page1.GetProperty("total").GetInt32().Should().Be(5);
        var p1Items = page1.GetProperty("items").EnumerateArray().ToList();
        p1Items.Should().HaveCount(2);
        p1Items[0].GetProperty("sourceFilename").GetString().Should().Be("job3.pdf");
        p1Items[1].GetProperty("sourceFilename").GetString().Should().Be("job2.pdf");

        var page2 = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?limit=2&offset=2");
        page2.GetProperty("total").GetInt32().Should().Be(5);
        var p2Items = page2.GetProperty("items").EnumerateArray().ToList();
        p2Items.Should().HaveCount(2);
        p2Items[0].GetProperty("sourceFilename").GetString().Should().Be("job1.pdf");
        p2Items[1].GetProperty("sourceFilename").GetString().Should().Be("fail2.pdf");

        var page3 = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?limit=2&offset=4");
        page3.GetProperty("total").GetInt32().Should().Be(5);
        var p3Items = page3.GetProperty("items").EnumerateArray().ToList();
        p3Items.Should().HaveCount(1);
        p3Items[0].GetProperty("sourceFilename").GetString().Should().Be("fail1.pdf");

        var repeatedPage2 = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/runs?limit=2&offset=2");
        repeatedPage2.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sourceFilename").GetString())
            .Should().Equal("job1.pdf", "fail2.pdf");

        p1Items.Concat(p2Items).Concat(p3Items)
            .Select(item => item.GetProperty("sourceFilename").GetString())
            .Should().OnlyHaveUniqueItems();
    }
}

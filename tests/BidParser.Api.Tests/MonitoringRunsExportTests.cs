namespace BidParser.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

public sealed class MonitoringRunsExportTests
{
    private static readonly string[] ExpectedHeaders =
    [
        "kind", "id", "status", "failure_category", "created_at", "user_id", "user_username", "user_name",
        "vendor", "parser_slug", "crm_template", "source_filename", "bid_number", "bid_revision",
        "source_path", "output_path", "fx_rate", "margin", "computed_total", "quoted_total",
        "totals_match", "split_by_solution_id", "import_type", "stage", "hint", "message", "error_detail",
        "source_available", "output_available"
    ];

    [Fact]
    public async Task Export_RequiresAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockUserAsync(client);

        var response = await client.GetAsync("/api/monitoring/runs/export");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_MatchesUnifiedListAndDedupes_ValuesMappedCorrectly_WithFormulaSafety()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync();

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        var outPath = Path.Combine(fixture.UploadDir, "out.xlsx");
        await File.WriteAllTextAsync(src, "src");
        await File.WriteAllTextAsync(outPath, "out");

        var dt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var job = NewJob(user.Id, src, outPath, "success.pdf", totalsMatch: true, createdAt: dt);
        job.User = user;
        job.ImportType = ImportType.Auto;
        job.BidNumber = "B-123";
        job.BidRevision = "R-1";
        db.ParseJobs.Add(job);

        var failure = NewFailure(user.Id, src, "bad.pdf", FailureCategory.ParserError, dt.AddHours(1));
        // Test all four formula-risk prefixes
        failure.Message = "=1+1";
        failure.Hint = "+A1";
        failure.ErrorDetail = "-B2";
        failure.SourceFilename = "@SUM";
        db.FailedParseJobs.Add(failure);

        db.ParseJobs.Add(NewJob(user.Id, src, outPath, "mismatch.pdf", totalsMatch: false, createdAt: dt.AddHours(2)));
        db.FailedParseJobs.Add(NewFailure(user.Id, src, "mismatch.pdf", FailureCategory.ValidationMismatch, dt.AddHours(2)));

        await db.SaveChangesAsync();

        var res = await client.GetAsync("/api/monitoring/runs/export?range=all");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.FileName.Should().Be("parser_runs_all.xlsx");

        fixture.Factory.Services.GetRequiredService<BidParser.Api.Endpoints.IExportEnvironment>()
            .Should().BeOfType<BidParser.Api.Endpoints.DefaultExportEnvironment>();

        var bytes = await res.Content.ReadAsByteArrayAsync();
        AssertOpenXmlValid(bytes);
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        var rows = ws.RowsUsed().ToList();
        rows.Should().HaveCount(4); // Header + 3 rows

        var headers = rows[0].Cells(1, 29).Select(c => c.GetString()).ToArray();
        headers.Should().Equal(ExpectedHeaders);

        // Row 2 is mismatch job (newest)
        var row2 = rows[1];
        row2.Cell(1).GetString().Should().Be("job");
        row2.Cell(3).GetString().Should().Be("validationMismatch");
        row2.Cell(5).DataType.Should().Be(XLDataType.DateTime);
        row2.Cell(17).DataType.Should().Be(XLDataType.Number);
        row2.Cell(21).DataType.Should().Be(XLDataType.Boolean);
        row2.Cell(21).GetBoolean().Should().BeFalse();
        row2.Cell(23).GetString().Should().Be("manual"); // ImportType default manual in NewJob

        // Blank cells where fields don't apply for jobs vs failures
        row2.Cell(4).GetString().Should().BeEmpty(); // failure_category is blank for jobs

        // Row 3 is failure (parserError)
        var row3 = rows[2];
        row3.Cell(1).GetString().Should().Be("failure");
        row3.Cell(3).GetString().Should().Be("parserError");
        row3.Cell(4).GetString().Should().Be("parserError");
        row3.Cell(11).GetString().Should().BeEmpty(); // crm_template blank for failure

        // Formula safety
        row3.Cell(12).HasFormula.Should().BeFalse();
        row3.Cell(12).GetString().Should().Be("@SUM");
        row3.Cell(25).HasFormula.Should().BeFalse();
        row3.Cell(25).GetString().Should().Be("+A1");
        row3.Cell(26).HasFormula.Should().BeFalse();
        row3.Cell(26).GetString().Should().Be("=1+1");
        row3.Cell(27).HasFormula.Should().BeFalse();
        row3.Cell(27).GetString().Should().Be("-B2");

        // Row 4 is success auto job
        var row4 = rows[3];
        row4.Cell(1).GetString().Should().Be("job");
        row4.Cell(3).GetString().Should().Be("success");
        row4.Cell(13).GetString().Should().Be("B-123");
        row4.Cell(14).GetString().Should().Be("R-1");
        row4.Cell(23).GetString().Should().Be("auto");
        row4.Cell(5).GetDateTime().Should().Be(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeZoneInfo.Local));
    }

    [Fact]
    public async Task Export_EveryFilter_AndPaginationIndependence()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync();
        var user2 = new User { Name = "Other User", Username = "otheruser", PasswordHash = "hash" };
        db.Users.Add(user2);
        await db.SaveChangesAsync();

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var dt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Decoy and target for each filter to prove it works independently
        // 1. vendor
        var vendorTarget = NewJob(user.Id, src, src, "vendor_target.pdf", totalsMatch: true, createdAt: dt);
        vendorTarget.Vendor = "TargetVendor";
        var vendorDecoy = NewJob(user.Id, src, src, "vendor_decoy.pdf", totalsMatch: true, createdAt: dt.AddMinutes(1));
        vendorDecoy.Vendor = "OtherVendor";

        // 2. parserSlug
        var slugTarget = NewJob(user.Id, src, src, "slug_target.pdf", totalsMatch: true, createdAt: dt.AddMinutes(2));
        slugTarget.ParserSlug = "target_slug";
        var slugDecoy = NewJob(user.Id, src, src, "slug_decoy.pdf", totalsMatch: true, createdAt: dt.AddMinutes(3));
        slugDecoy.ParserSlug = "other_slug";

        // 3. userId
        var userTarget = NewJob(user.Id, src, src, "user_target.pdf", totalsMatch: true, createdAt: dt.AddMinutes(4));
        var userDecoy = NewJob(user2.Id, src, src, "user_decoy.pdf", totalsMatch: true, createdAt: dt.AddMinutes(5));

        // 4. importType
        var typeTarget = NewJob(user.Id, src, src, "type_target.pdf", totalsMatch: true, createdAt: dt.AddMinutes(6));
        typeTarget.ImportType = ImportType.Auto;
        var typeDecoy = NewJob(user.Id, src, src, "type_decoy.pdf", totalsMatch: true, createdAt: dt.AddMinutes(7));
        typeDecoy.ImportType = ImportType.Manual;

        // 5. bounded dates
        var dateTarget = NewJob(user.Id, src, src, "date_target.pdf", totalsMatch: true, createdAt: dt.AddDays(-10));
        var dateDecoy = NewJob(user.Id, src, src, "date_decoy.pdf", totalsMatch: true, createdAt: dt.AddDays(-20));

        db.ParseJobs.AddRange(vendorTarget, vendorDecoy, slugTarget, slugDecoy, userTarget, userDecoy, typeTarget, typeDecoy, dateTarget, dateDecoy);

        // 6. all statuses (decoy is a different status)
        var s1 = NewJob(user.Id, src, src, "stat_success.pdf", totalsMatch: true, createdAt: dt.AddHours(1));
        var s2 = NewJob(user.Id, src, src, "stat_valmis.pdf", totalsMatch: false, createdAt: dt.AddHours(2));
        var s3 = NewFailure(user.Id, src, "stat_magic.pdf", FailureCategory.MagicByteMismatch, dt.AddHours(3));
        var s4 = NewFailure(user.Id, src, "stat_parser.pdf", FailureCategory.ParserError, dt.AddHours(4));
        var s5 = NewFailure(user.Id, src, "stat_unhandled.pdf", FailureCategory.UnhandledException, dt.AddHours(5));

        // Keep userTarget as the only row owned by the first user so userId is exercised
        // independently rather than relying on a compound filter to isolate it.
        foreach (var job in new[] { vendorTarget, vendorDecoy, slugTarget, slugDecoy, userDecoy, typeTarget, typeDecoy, dateTarget, dateDecoy, s1, s2 })
        {
            job.UserId = user2.Id;
        }
        foreach (var failureRow in new[] { s3, s4, s5 })
        {
            failureRow.UserId = user2.Id;
        }

        db.ParseJobs.AddRange(s1, s2);
        db.FailedParseJobs.AddRange(s3, s4, s5);

        await db.SaveChangesAsync();

        async Task AssertFilter(string qs, string expectedFilename)
        {
            // Call List API
            var listRes = await client.GetAsync($"/api/monitoring/runs?{qs}");
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var listData = await listRes.Content.ReadFromJsonAsync<BidParser.Api.Contracts.MonitoringRunsResponse>();

            // Call Export API
            var exportRes = await client.GetAsync($"/api/monitoring/runs/export?{qs}");
            exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var ms = new MemoryStream(await exportRes.Content.ReadAsByteArrayAsync());
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheet(1);
            var rows = ws.RowsUsed().ToList();

            // Assert Export vs Expected
            rows.Should().HaveCount(2); // Header + 1
            ws.Row(2).Cell(12).GetString().Should().Be(expectedFilename);

            // Assert Export vs List exactly
            listData!.Items.Should().HaveCount(1);
            listData.Items[0].SourceFilename.Should().Be(expectedFilename);
            listData.Items[0].Id.Should().Be((int)ws.Row(2).Cell(2).GetDouble());
            listData.Items[0].Kind.Should().Be(ws.Row(2).Cell(1).GetString());
            listData.Items[0].Status.Should().Be(ws.Row(2).Cell(3).GetString());
        }

        // Run filter assertions
        await AssertFilter("range=all&vendor=TargetVendor", "vendor_target.pdf");
        await AssertFilter("range=all&parserSlug=target_slug", "slug_target.pdf");
        await AssertFilter($"range=all&userId={user.Id}", "user_target.pdf");

        await AssertFilter("range=all&importType=auto", "type_target.pdf");

        // Bounded date test
        var from = dt.AddDays(-12).ToString("yyyy-MM-dd");
        var to = dt.AddDays(-8).ToString("yyyy-MM-dd");
        await AssertFilter($"from={from}&to={to}", "date_target.pdf");

        // Status tests (reusing s1..s5 logic using timestamps to isolate if needed, but since we query by status AND date range to isolate)
        // Let's just query by status, there are multiple successes, so we can't expect exactly 1 row.
        // We will assert list vs export for a status query directly.
        async Task AssertListVsExportExact(string qs)
        {
            var listRes = await client.GetAsync($"/api/monitoring/runs?{qs}");
            var listData = await listRes.Content.ReadFromJsonAsync<BidParser.Api.Contracts.MonitoringRunsResponse>();

            var exportRes = await client.GetAsync($"/api/monitoring/runs/export?{qs}");
            using var ms = new MemoryStream(await exportRes.Content.ReadAsByteArrayAsync());
            using var wb = new XLWorkbook(ms);
            var rows = wb.Worksheet(1).RowsUsed().Skip(1).ToList();

            rows.Should().HaveCount(listData!.Items.Count);
            for(int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var item = listData.Items[i];
                row.Cell(1).GetString().Should().Be(item.Kind);
                ((int)row.Cell(2).GetDouble()).Should().Be(item.Id);
                row.Cell(3).GetString().Should().Be(item.Status);
                row.Cell(12).GetString().Should().Be(item.SourceFilename);
            }
        }

        await AssertListVsExportExact("range=all&status=success");
        await AssertListVsExportExact("range=all&status=validationMismatch");
        await AssertListVsExportExact("range=all&status=magicByteMismatch");
        await AssertListVsExportExact("range=all&status=parserError");
        await AssertListVsExportExact("range=all&status=unhandledException");

        // Verify page/limit/offset are ignored by export (typeTarget should still appear)
        var resPage = await client.GetAsync("/api/monitoring/runs/export?range=all&importType=auto&page=99&limit=1&offset=500");
        using var msPage = new MemoryStream(await resPage.Content.ReadAsByteArrayAsync());
        using var wbPage = new XLWorkbook(msPage);
        wbPage.Worksheet(1).RowsUsed().Should().HaveCount(2); // Still gives target row ignoring offset
    }

    [Fact]
    public async Task Export_Rollover_ExactHeaders_AndCleanup_AndContextVerification()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        var env = TestExportEnvironment.FromFixture(fixture);

        using var client = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<BidParser.Api.Endpoints.IExportEnvironment>(env);
            });
        }).CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync();

        Directory.CreateDirectory(fixture.UploadDir);
        var src = Path.Combine(fixture.UploadDir, "src.pdf");
        await File.WriteAllTextAsync(src, "src");

        var dt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            db.ParseJobs.Add(NewJob(user.Id, src, src, $"job{i}.pdf", totalsMatch: true, createdAt: dt.AddHours(i)));
        }
        await db.SaveChangesAsync();

        env.WorksheetRowLimit = 3;

        var res = await client.GetAsync("/api/monitoring/runs/export?range=all");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var ms = new MemoryStream(await res.Content.ReadAsByteArrayAsync());
        using var wb = new XLWorkbook(ms);

        wb.Worksheets.Count.Should().Be(3);
        wb.Worksheet(1).Name.Should().Be("Parser Runs");
        wb.Worksheet(2).Name.Should().Be("Parser Runs 2");
        wb.Worksheet(3).Name.Should().Be("Parser Runs 3");

        for (int i = 1; i <= 3; i++)
        {
            var ws = wb.Worksheet(i);
            ws.Row(1).Cells(1, 29).Select(cell => cell.GetString()).Should().Equal(ExpectedHeaders);
        }

        wb.Worksheet(1).RowsUsed().Should().HaveCount(3); // H + 2
        wb.Worksheet(2).RowsUsed().Should().HaveCount(3); // H + 2
        wb.Worksheet(3).RowsUsed().Should().HaveCount(2); // H + 1

        res.Content.Dispose();

        await Task.Delay(100);

        env.TempStream.IsDisposed.Should().BeTrue("Temp file should be disposed on stream close");
    }

    [Fact]
    public async Task Export_Filename_BoundedAndAll()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var resBounded = await client.GetAsync("/api/monitoring/runs/export?from=2023-01-01&to=2023-01-31");
        resBounded.Content.Headers.ContentDisposition!.FileName.Should().Be("parser_runs_2023-01-01_2023-01-31.xlsx");

        var resAll = await client.GetAsync("/api/monitoring/runs/export?range=all");
        resAll.Content.Headers.ContentDisposition!.FileName.Should().Be("parser_runs_all.xlsx");
    }

    private static ParseJob NewJob(int userId, string src, string outPath, string filename, bool totalsMatch, DateTime createdAt) => new()
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
        ImportType = ImportType.Manual
    };

    private static FailedParseJob NewFailure(int userId, string src, string filename, FailureCategory category, DateTime createdAt) => new()
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

    private static void AssertOpenXmlValid(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        new OpenXmlValidator().Validate(document).Should().BeEmpty();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public class LinePricingParseTests
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Theory]
    [InlineData(ParserSlugs.JuniperIrisXlsx, "Juniper_Iris_Synthetic.xlsx", true)]
    [InlineData(ParserSlugs.JuniperCpqXlsx, "Juniper_CPQ_900001.xlsx", false)]
    [InlineData(ParserSlugs.JuniperIndirectXlsx, "Juniper_Indirect_900001.xlsx", false)]
    public async Task Preview_JuniperFormats_ReturnsLinesAndStrings_AndPersistsNothing(
        string slug, string filename, bool imRequired)
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", filename));

        using var response = await PostPreviewAsync(client, bytes, filename, XlsxMime, Vendors.Juniper, slug);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("parserSlug").GetString().Should().Be(slug);
        body.GetProperty("currency").GetString().Should().Be("USD");
        body.GetProperty("crmTemplate").GetString().Should().Be(CrmTemplates.ForeignUplift);
        body.GetProperty("usesFxRate").GetBoolean().Should().BeTrue();
        body.GetProperty("imPercentEditable").GetBoolean().Should().BeTrue();
        body.GetProperty("imPercentRequired").GetBoolean().Should().Be(imRequired);

        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().NotBeEmpty();

        var first = lines[0];
        first.GetProperty("msrp").ValueKind.Should().Be(JsonValueKind.String);
        if (imRequired)
        {
            first.GetProperty("sourceCost").ValueKind.Should().Be(JsonValueKind.Null);
            first.GetProperty("derivedImPercent").ValueKind.Should().Be(JsonValueKind.Null);
        }
        else
        {
            first.GetProperty("sourceCost").ValueKind.Should().Be(JsonValueKind.String);
            first.GetProperty("derivedImPercent").ValueKind.Should().Be(JsonValueKind.String);
        }

        // Verify no ParseJob, ParseMetric, FailedParseJob, or user.DefaultVendor change, and no retained upload
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ParseJobs.CountAsync()).Should().Be(0);
            (await db.ParseMetrics.CountAsync()).Should().Be(0);
            (await db.FailedParseJobs.CountAsync()).Should().Be(0);
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            admin.DefaultVendor.Should().BeNull();
        }

        Directory.GetFiles(fixture.UploadDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_CpqSourceCost_RemainsUnroundedStringOnWire()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var cpqPath = Path.Combine(root, "samples", "inputs", "Juniper_CPQ_900001.xlsx");
        byte[] mutatedBytes;
        using (var wb = new XLWorkbook(cpqPath))
        {
            var ws = wb.Worksheet(1);
            var qtyCol = ws.CellsUsed(c => c.GetFormattedString().Trim() == "Quantity").Single().Address.ColumnNumber;
            var netCol = ws.CellsUsed(c => c.GetFormattedString().Trim() == "Extended Net Price").Single().Address.ColumnNumber;
            var listCol = ws.CellsUsed(c => c.GetFormattedString().Trim() == "List Price Per").Single().Address.ColumnNumber;
            var headerRow = ws.CellsUsed(c => c.GetFormattedString().Trim() == "SKU").First().Address.RowNumber;
            // Row headerRow + 1 is structural option summary; headerRow + 2 is first product row
            var productRow = headerRow + 2;
            ws.Cell(productRow, qtyCol).Value = 1;
            ws.Cell(productRow, listCol).Value = 427m;
            ws.Cell(productRow, netCol).Style.NumberFormat.Format = "0.0000";
            ws.Cell(productRow, netCol).Value = 362.9512m;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            mutatedBytes = ms.ToArray();
        }

        using var response = await PostPreviewAsync(
            client, mutatedBytes, "Juniper_CPQ_900001.xlsx", XlsxMime, Vendors.Juniper, ParserSlugs.JuniperCpqXlsx);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = body.GetProperty("lines")[0];
        first.GetProperty("sourceCost").GetString().Should().Be("362.9512");
        first.GetProperty("derivedImPercent").GetString().Should().Be("15.00");
    }

    [Fact]
    public async Task Preview_ErrorHandling_WrongFileType_Currency_NonLinePricing_And_QuoteId()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var cpqBytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", "Juniper_CPQ_900001.xlsx"));

        // 1. Wrong file type -> 422 fileType, nothing recorded
        using (var wrongTypeResp = await PostPreviewAsync(
            client, cpqBytes, "Juniper_CPQ_900001.xlsx", XlsxMime, Vendors.Juniper, ParserSlugs.JuniperIndirectXlsx))
        {
            wrongTypeResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var err = await wrongTypeResp.Content.ReadFromJsonAsync<JsonElement>();
            err.GetProperty("detail").GetProperty("stage").GetString().Should().Be("fileType");
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.FailedParseJobs.CountAsync()).Should().Be(0);
        }

        // 2. Non-line-pricing parser -> 400
        var hpBytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", "Deals_Sample_01_HPI.xlsx"));
        using (var nonLineResp = await PostPreviewAsync(
            client, hpBytes, "Deals_Sample_01_HPI.xlsx", XlsxMime, Vendors.Hp, ParserSlugs.HpBidXlsx))
        {
            nonLineResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 3. quoteId -> 400
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(Vendors.Dell), "vendor");
            form.Add(new StringContent(ParserSlugs.DellAuto), "parserSlug");
            form.Add(new StringContent("3000123456789.1"), "quoteId");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/parse/preview") { Content = form };
            req.Headers.Add("X-Requested-With", "BidParser");
            using var quoteIdResp = await client.SendAsync(req);
            quoteIdResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 4. Currency failure -> 422 currency, FailedParseJob recorded
        var irisPath = Path.Combine(root, "samples", "inputs", "Juniper_Iris_Synthetic.xlsx");
        byte[] audIrisBytes;
        using (var wb = new XLWorkbook(irisPath))
        {
            var ws = wb.Worksheet(1);
            var col = ws.Row(1).CellsUsed().Single(c => c.GetString() == "Price List").Address.ColumnNumber;
            ws.Cell(2, col).Value = "Juniper Price List (AUD)";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            audIrisBytes = ms.ToArray();
        }

        using (var currResp = await PostPreviewAsync(
            client, audIrisBytes, "Iris_AUD.xlsx", XlsxMime, Vendors.Juniper, ParserSlugs.JuniperIrisXlsx))
        {
            currResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var err = await currResp.Content.ReadFromJsonAsync<JsonElement>();
            err.GetProperty("detail").GetProperty("stage").GetString().Should().Be("currency");
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.FailedParseJobs.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Parse_LinePricingValidation_And_HappyPathWithBlendedMargin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var irisBytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", "Juniper_Iris_Synthetic.xlsx"));

        // 1. Juniper without linePricing -> 400, nothing recorded
        using (var missingLp = await PostParseFormAsync(client, irisBytes, "Iris.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx, fxRate: "0.6520"))
        {
            missingLp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 2. With header margin + linePricing -> 400
        var validLpJson = JsonSerializer.Serialize(new[]
        {
            new { index = 0, vpn = "S-SRX340-A1-1", margin = "4.76", imPercent = "15.00" },
            new { index = 1, vpn = "S-SRX300-A1-1", margin = "10.00", imPercent = "27.53" },
        });
        using (var bothResp = await PostParseFormAsync(
            client, irisBytes, "Iris.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx,
            fxRate: "0.6520", margin: "5.00", linePricing: validLpJson))
        {
            bothResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 3. With VPN mismatch -> 400
        var mismatchLpJson = JsonSerializer.Serialize(new[]
        {
            new { index = 0, vpn = "S-SRX340-A1-1", margin = "4.76", imPercent = "15.00" },
            new { index = 1, vpn = "WRONG-SKU", margin = "10.00", imPercent = "27.53" },
        });
        using (var vpnResp = await PostParseFormAsync(
            client, irisBytes, "Iris.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx,
            fxRate: "0.6520", linePricing: mismatchLpJson))
        {
            vpnResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 3b. IM% above 100 -> 400 with the range message (a negative cost must never reach CRM)
        var overLimitLpJson = JsonSerializer.Serialize(new[]
        {
            new { index = 0, vpn = "S-SRX340-A1-1", margin = "4.76", imPercent = "100.01" },
            new { index = 1, vpn = "S-SRX300-A1-1", margin = "10.00", imPercent = "27.53" },
        });
        using (var overLimitResp = await PostParseFormAsync(
            client, irisBytes, "Iris.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx,
            fxRate: "0.6520", linePricing: overLimitLpJson))
        {
            overLimitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await overLimitResp.Content.ReadAsStringAsync()).Should().Contain(
                "Line 1 (S-SRX340-A1-1): IM% must be between 0 and 100 with at most two decimal places.");
        }

        // 4. Non-Juniper parser + linePricing -> 400
        var hpBytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", "Deals_Sample_01_HPI.xlsx"));
        using (var nonJuniperResp = await PostParseFormAsync(
            client, hpBytes, "Deals.xlsx", Vendors.Hp, ParserSlugs.HpBidXlsx,
            fxRate: "1.0000", linePricing: validLpJson))
        {
            nonJuniperResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ParseJobs.CountAsync()).Should().Be(0);
            (await db.FailedParseJobs.CountAsync()).Should().Be(0);
        }

        // 5. Happy path: per-line col K/T written, ParseJob.Margin & ParseMetric.Margin = blended margin
        using (var okResp = await PostParseFormAsync(
            client, irisBytes, "Iris.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx,
            fxRate: "0.6520", linePricing: validLpJson))
        {
            okResp.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var outStream = await okResp.Content.ReadAsStreamAsync();
            using var wb = new XLWorkbook(outStream);
            var ws = wb.Worksheet(CrmTemplates.ForeignUplift);
            ws.Cell(3, 11).GetValue<decimal>().Should().Be(4.76m);
            ws.Cell(3, 20).GetValue<decimal>().Should().Be(348.50m); // 410 * 0.85
            ws.Cell(4, 11).GetValue<decimal>().Should().Be(10.00m);
            ws.Cell(4, 20).GetValue<decimal>().Should().Be(202.92m); // round2(280 * (1 - 0.2753)) = 202.92
        }

        // Expected blended margin:
        // Line 1: qty 2, unitCost 348.50 -> extCost 697.00, unitPrice round2(348.50 / (1 - 0.0476)) = 365.92 -> extPrice 731.84
        // Line 2: qty 1, unitCost 202.92 -> extCost 202.92, unitPrice round2(202.92 / 0.90) = 225.47 -> extPrice 225.47
        // TotalCost = 899.92, TotalPrice = 957.31, Margin$ = 57.39, Margin% = round2(57.39 / 957.31 * 100) = 5.99
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ParseJobs.SingleAsync();
            var metric = await db.ParseMetrics.SingleAsync();
            job.Margin.Should().Be(5.99m);
            metric.Margin.Should().Be(5.99m);
            job.FxRate.Should().Be(0.6520m);
        }
    }

    [Fact]
    public async Task Preview_UsesSeparateRateLimitBucket_DoesNotConsumeParseTokens()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var irisBytes = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "inputs", "Juniper_Iris_Synthetic.xlsx"));
        var validLpJson = JsonSerializer.Serialize(new[]
        {
            new { index = 0, vpn = "S-SRX340-A1-1", margin = "5.00", imPercent = "15.00" },
            new { index = 1, vpn = "S-SRX300-A1-1", margin = "5.00", imPercent = "15.00" },
        });

        // Exhaust all 10 preview tokens
        for (var i = 0; i < 10; i++)
        {
            using var prev = await PostPreviewAsync(
                client, irisBytes, "Juniper_Iris_Synthetic.xlsx", XlsxMime, Vendors.Juniper, ParserSlugs.JuniperIrisXlsx);
            prev.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 11th preview is rate-limited (429)
        using (var prev11 = await PostPreviewAsync(
            client, irisBytes, "Juniper_Iris_Synthetic.xlsx", XlsxMime, Vendors.Juniper, ParserSlugs.JuniperIrisXlsx))
        {
            prev11.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        // /api/parse still has its full token bucket available
        using var parseResp = await PostParseFormAsync(
            client, irisBytes, "Juniper_Iris_Synthetic.xlsx", Vendors.Juniper, ParserSlugs.JuniperIrisXlsx,
            fxRate: "0.6520", linePricing: validLpJson);
        parseResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> PostPreviewAsync(
        HttpClient client, byte[] bytes, string filename, string contentType, string vendor, string parserSlug)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parserSlug");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/parse/preview") { Content = form };
        req.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PostParseFormAsync(
        HttpClient client,
        byte[] bytes,
        string filename,
        string vendor,
        string parserSlug,
        string? fxRate = null,
        string? margin = null,
        string? imPercent = null,
        string? linePricing = null)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new(XlsxMime);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parserSlug");
        if (fxRate is not null) form.Add(new StringContent(fxRate), "fxRate");
        if (margin is not null) form.Add(new StringContent(margin), "margin");
        if (imPercent is not null) form.Add(new StringContent(imPercent), "imPercent");
        if (linePricing is not null) form.Add(new StringContent(linePricing), "linePricing");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        req.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(req);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BidParser.sln")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }
}

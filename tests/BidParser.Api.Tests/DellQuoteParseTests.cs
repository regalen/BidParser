using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Dell;
using BidParser.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class DellQuoteParseTests
{
    private const string PeripheralsQuoteId = "9000000000003.1";
    private const string AposQuoteId = "9000000000001.1";

    [Fact]
    public async Task Api_fetched_quotes_parse_auto_detect_persist_and_always_apply_sku_filters()
    {
        var root = FindRepoRoot();
        var peripheralsBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Dell_Peripherals_Sample.json"));
        var aposBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Dell_APOS_Sample.json"));
        var quoteClient = new StubDellQuoteClient((number, _, _) =>
            number == "9000000000001" ? aposBytes : peripheralsBytes);
        using var fixture = await CustomTestFixture.CreateAsync(dellQuoteClient: quoteClient);
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await ConfigureSettingsAsync(client);

        using var baselineResponse = await PostQuoteAsync(
            client, PeripheralsQuoteId, ParserSlugs.DellAuto);
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        baselineResponse.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which
            .Should().Be(ParserSlugs.DellCtoJson);
        baselineResponse.Content.Headers.ContentDisposition!.FileName!.Trim('"')
            .Should().Be("9000000000003_1_NoCalculation.xlsx");
        var baselineWorkbook = await baselineResponse.Content.ReadAsByteArrayAsync();
        AssertWorkbookMatchesGolden(
            baselineWorkbook,
            Path.Combine(root, "samples", "outputs", "Dell_Peripherals_Sample_NoCalculation.xlsx"));
        CountDataRows(baselineWorkbook).Should().Be(29);

        // The sub-component opt-out has been withdrawn: ParseEndpoints pins the flag to false and
        // never reads the form field, so posting it either way must leave the output untouched.
        foreach (var wireValue in new[] { "false", "true" })
        {
            using var response = await PostQuoteAsync(
                client,
                PeripheralsQuoteId,
                ParserSlugs.DellAuto,
                includeSubComponents: wireValue);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var workbook = await response.Content.ReadAsByteArrayAsync();
            ReadWorkbookCells(workbook).Should().Equal(ReadWorkbookCells(baselineWorkbook));
            CountDataRows(workbook).Should().Be(29);
        }

        using var aposResponse = await PostQuoteAsync(client, AposQuoteId, ParserSlugs.DellAuto);
        aposResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        aposResponse.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which
            .Should().Be(ParserSlugs.DellAposJson);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = await db.ParseJobs.OrderBy(job => job.Id).ToListAsync();
        jobs.Should().HaveCount(4);
        jobs.Should().OnlyContain(job => job.CrmTemplate == CrmTemplates.NoCalculation);
        jobs[0].SourceFilename.Should().Be("9000000000003.1.json");
        (await db.ParseMetrics.CountAsync()).Should().Be(4);
        File.ReadAllBytes(jobs[0].SourcePath).Should().Equal(peripheralsBytes);
        quoteClient.Calls.Should().HaveCount(4);
        quoteClient.Calls[0].Should().Be(("9000000000003", "1", "en-au"));
    }

    [Fact]
    public async Task Dell_template_and_source_validation_rejects_invalid_requests()
    {
        var root = FindRepoRoot();
        var peripheralsBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Dell_Peripherals_Sample.json"));
        var aposBytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Dell_APOS_Sample.json"));
        var quoteClient = new StubDellQuoteClient((number, _, _) =>
            number == "9000000000001" ? aposBytes : peripheralsBytes);
        using var fixture = await CustomTestFixture.CreateAsync(dellQuoteClient: quoteClient);
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await ConfigureSettingsAsync(client);

        using var upliftResponse = await PostQuoteAsync(
            client,
            PeripheralsQuoteId,
            ParserSlugs.DellAuto,
            crmTemplate: CrmTemplates.Uplift);
        upliftResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(upliftResponse)).Should().Be("Unknown CRM template for this parser.");

        // Dell has no manual file-type selection: dell_auto is the only accepted slug, even
        // when posted directly against the API (not just hidden in the SPA dropdown).
        foreach (var (quoteId, slug) in new[]
        {
            (PeripheralsQuoteId, ParserSlugs.DellCtoJson),
            (AposQuoteId, ParserSlugs.DellAposJson),
        })
        {
            using var response = await PostQuoteAsync(client, quoteId, slug);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ApiTestFixture.DetailAsync(response)).Should().Be("Dell only supports automatic file-type detection.");
        }

        using var both = await PostBothSourcesAsync(client, peripheralsBytes);
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(both)).Should().Be("Provide either file or quoteId, not both.");

        using var neither = await PostFormAsync(client, new Dictionary<string, string>
        {
            ["vendor"] = Vendors.Dell,
            ["parserSlug"] = ParserSlugs.DellAuto,
        });
        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(neither)).Should().Be("file or quoteId is required.");

        using var wrongVendor = await PostFormAsync(client, new Dictionary<string, string>
        {
            ["quoteId"] = PeripheralsQuoteId,
            ["vendor"] = Vendors.Nutanix,
            ["parserSlug"] = ParserSlugs.NutanixAuto,
        });
        wrongVendor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(wrongVendor)).Should().Be("quoteId is only supported for Dell.");

        using var invalidId = await PostQuoteAsync(client, "9000000000003", ParserSlugs.DellAuto);
        invalidId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(invalidId)).Should().Be("Invalid Dell quote ID.");
    }

    [Fact]
    public async Task Every_Dell_fetch_failure_returns_the_dellApi_stage_and_persists_nothing()
    {
        var failures = new Queue<DellApiException>(
        [
            new(DellApiFailure.BadRequest, "bad request", 400),
            new(DellApiFailure.Unauthorized, "unauthorized", 401),
            new(DellApiFailure.Forbidden, "forbidden", 403),
            new(DellApiFailure.NotFound, "not found", 404),
            new(DellApiFailure.RateLimited, "rate limited", 429),
            new(DellApiFailure.Upstream, "upstream", 500),
            new(DellApiFailure.Timeout, "timeout"),
        ]);
        var quoteClient = new StubDellQuoteClient((_, _, _) => throw failures.Dequeue());
        using var fixture = await CustomTestFixture.CreateAsync(dellQuoteClient: quoteClient);
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await ConfigureSettingsAsync(client);

        for (var index = 0; index < 7; index++)
        {
            var quoteId = $"37000281314{index:D2}.1";
            using var response = await PostQuoteAsync(client, quoteId, ParserSlugs.DellAuto);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("detail").GetProperty("stage").GetString().Should().Be("dellApi");
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseMetrics.CountAsync()).Should().Be(0);
        (await db.FailedParseJobs.CountAsync()).Should().Be(0);
        var originals = Path.Combine(fixture.UploadDir, "originals");
        if (Directory.Exists(originals))
        {
            Directory.GetFiles(originals, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Unconfigured_Dell_API_returns_422_before_client_or_persistence()
    {
        var quoteClient = new StubDellQuoteClient((_, _, _) => Array.Empty<byte>());
        using var fixture = await CustomTestFixture.CreateAsync(dellQuoteClient: quoteClient);
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostQuoteAsync(client, PeripheralsQuoteId, ParserSlugs.DellAuto);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("dellApi");
        detail.GetProperty("message").GetString().Should().Be(DellApiMessages.NotConfigured);
        quoteClient.Calls.Should().BeEmpty();
    }

    private static async Task ConfigureSettingsAsync(HttpClient client)
    {
        using var response = await ApiTestFixture.PutJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings",
            new
            {
                TokenUrl = DellApiSettingsService.DefaultTokenUrl,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                QuoteUrlTemplate = DellApiSettingsService.DefaultQuoteUrlTemplate,
                DefaultLocale = "en-au",
                ClientIdHeader = DellApiSettingsService.InitialClientIdHeader,
                ApiVersion = DellApiSettingsService.InitialApiVersion,
                UseBasicAuthForToken = false,
            });
        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> PostQuoteAsync(
        HttpClient client,
        string quoteId,
        string parserSlug,
        string? includeSubComponents = null,
        string? crmTemplate = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["quoteId"] = quoteId,
            ["vendor"] = Vendors.Dell,
            ["parserSlug"] = parserSlug,
        };
        if (includeSubComponents is not null) fields["includeSubComponents"] = includeSubComponents;
        if (crmTemplate is not null) fields["crmTemplate"] = crmTemplate;
        return PostFormAsync(client, fields);
    }

    private static async Task<HttpResponseMessage> PostBothSourcesAsync(HttpClient client, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        form.Add(file, "file", "Dell_Peripherals_Sample.json");
        form.Add(new StringContent(PeripheralsQuoteId), "quoteId");
        form.Add(new StringContent(Vendors.Dell), "vendor");
        form.Add(new StringContent(ParserSlugs.DellAuto), "parserSlug");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        IReadOnlyDictionary<string, string> fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(request);
    }

    private static int CountDataRows(byte[] workbookBytes)
    {
        using var stream = new MemoryStream(workbookBytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var row = 3;
        while (sheet.Cell(row, 2).GetFormattedString() != "*") row++;
        return row - 3;
    }

    private static void AssertWorkbookMatchesGolden(byte[] actualBytes, string expectedPath)
    {
        ReadWorkbookCells(actualBytes).Should().Equal(ReadWorkbookCells(File.ReadAllBytes(expectedPath)));
    }

    private static IReadOnlyList<string> ReadWorkbookCells(byte[] workbookBytes)
    {
        using var stream = new MemoryStream(workbookBytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        var values = new List<string>(lastRow * 27);
        for (var row = 1; row <= lastRow; row++)
        {
            for (var column = 1; column <= 27; column++)
            {
                values.Add(sheet.Cell(row, column).GetFormattedString());
            }
        }
        return values;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class StubDellQuoteClient(
        Func<string, string, string, byte[]> response) : IDellQuoteClient
    {
        public List<(string Number, string Version, string Locale)> Calls { get; } = [];

        public Task<DellQuoteFetchResult> GetQuoteAsync(
            string quoteNumber,
            string quoteVersion,
            string locale,
            CancellationToken ct)
        {
            Calls.Add((quoteNumber, quoteVersion, locale));
            return Task.FromResult(new DellQuoteFetchResult(
                response(quoteNumber, quoteVersion, locale),
                "application/json"));
        }
    }
}

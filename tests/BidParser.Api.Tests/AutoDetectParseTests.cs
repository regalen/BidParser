using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class AutoDetectParseTests
{
    [Fact]
    public async Task AutoDetect_Xlsx_Resolves_Correct_Parser_And_Persists_Resolved_Slug()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        // XQ-9100010.xlsx is a Nutanix Renewal XLSX
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-9100010.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "XQ-9100010.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", ParserSlugs.NutanixAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(ParserSlugs.NutanixRenewalXlsx);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(ParserSlugs.NutanixRenewalXlsx);
        job.ImportType.Should().Be(ImportType.Auto);

        var metric = await db.ParseMetrics.SingleAsync();
        metric.ParserSlug.Should().Be(ParserSlugs.NutanixRenewalXlsx);
        metric.ImportType.Should().Be(ImportType.Auto);
    }

    [Fact]
    public async Task ManualSelection_Persists_ImportType_Manual()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-9100010.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "XQ-9100010.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", ParserSlugs.NutanixRenewalXlsx);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ImportType.Should().Be(ImportType.Manual);

        var metric = await db.ParseMetrics.SingleAsync();
        metric.ImportType.Should().Be(ImportType.Manual);
    }

    [Fact]
    public async Task AutoDetect_Pdf_Resolves_Correct_Parser()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        // XQ-9100003.pdf is a Nutanix Hardware Only PDF
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-9100003.pdf"));

        using var response = await PostParseAsync(
            client, bytes, "XQ-9100003.pdf",
            "application/pdf",
            "Nutanix", ParserSlugs.NutanixAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(ParserSlugs.NutanixHardwareOnlyPdf);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(ParserSlugs.NutanixHardwareOnlyPdf);
    }

    [Fact]
    public async Task AutoDetect_NoMatch_Returns_422_FileTypeError_And_Records_Nothing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        // An HP file uploaded under Nutanix Auto
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "99000001.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "99000001.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", ParserSlugs.NutanixAuto);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("fileType");
        detail.GetProperty("message").GetString().Should().Be(AutoDetectTypes.NoMatchMessage);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FailedParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseMetrics.CountAsync()).Should().Be(0);

        if (Directory.Exists(fixture.UploadDir))
        {
            Directory.GetFiles(fixture.UploadDir, "*", SearchOption.AllDirectories)
                .Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AutoDetect_UnsupportedExtension_Returns_415()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var bytes = "some plain text content"u8.ToArray();

        using var response = await PostParseAsync(
            client, bytes, "document.txt",
            "text/plain",
            "Nutanix", ParserSlugs.NutanixAuto);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task AutoDetect_VendorMismatch_Returns_400()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-9100010.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "XQ-9100010.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "HP", ParserSlugs.NutanixAuto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("detail").GetString().Should().Be("Parser does not match vendor.");
    }

    [Fact]
    public async Task GetParsers_Includes_Synthesized_Auto_Entries()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.GetAsync("/api/parsers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        parsers.Should().NotBeNull();

        // Every registered parser, plus one synthesized Auto entry per vendor in AutoDetectTypes.
        // Derived rather than hardcoded: this suite needs Docker, so a stale literal here only
        // surfaces in CI. ParserRegistryTests (which runs without Docker) is the canary that
        // guards the registry's exact contents and order.
        var expectedCount = new ParserRegistry().Parsers.Count + AutoDetectTypes.All.Count;
        parsers!.Count.Should().Be(expectedCount);

        var nutanixEntries = parsers.Where(p => p.GetProperty("vendor").GetString() == "Nutanix").ToList();
        nutanixEntries.Should().HaveCount(7);
        var firstNutanix = nutanixEntries[0];
        firstNutanix.GetProperty("slug").GetString().Should().Be(ParserSlugs.NutanixAuto);
        firstNutanix.GetProperty("displayName").GetString().Should().Be("Auto (detect format)");
        firstNutanix.TryGetProperty("reportType", out _).Should().BeFalse();
        firstNutanix.GetProperty("supportsSubComponentDetail").GetBoolean().Should().BeFalse();
        firstNutanix.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString()).Should().ContainSingle().Which.Should().Be("Foreign Uplift");

        var dellEntries = parsers.Where(p => p.GetProperty("vendor").GetString() == Vendors.Dell).ToList();
        dellEntries.Should().HaveCount(3);
        var firstDell = dellEntries[0];
        firstDell.GetProperty("slug").GetString().Should().Be(ParserSlugs.DellAuto);
        firstDell.GetProperty("displayName").GetString().Should().Be(AutoDetectTypes.DisplayName);
        firstDell.TryGetProperty("reportType", out _).Should().BeFalse();
        firstDell.GetProperty("supportsSubComponentDetail").GetBoolean().Should().BeFalse();
        firstDell.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString()).Should().ContainSingle().Which.Should().Be(CrmTemplates.NoCalculation);

        parsers.Single(p => p.GetProperty("slug").GetString() == ParserSlugs.DellCtoJson)
            .GetProperty("supportsSubComponentDetail").GetBoolean().Should().BeTrue();
        parsers.Single(p => p.GetProperty("slug").GetString() == ParserSlugs.DellAposJson)
            .GetProperty("supportsSubComponentDetail").GetBoolean().Should().BeFalse();
        parsers.Single(p => p.GetProperty("slug").GetString() == ParserSlugs.HpBidXlsx)
            .GetProperty("supportsSubComponentDetail").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetParsers_Includes_Synthesized_ZebraAuto_Entry()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.GetAsync("/api/parsers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        parsers.Should().NotBeNull();

        var zebraEntries = parsers!.Where(p => p.GetProperty("vendor").GetString() == "Zebra").ToList();
        zebraEntries.Should().HaveCount(3); // Auto + PDF + XLS
        var first = zebraEntries[0];
        first.GetProperty("slug").GetString().Should().Be(ParserSlugs.ZebraAuto);
        first.GetProperty("displayName").GetString().Should().Be(AutoDetectTypes.DisplayName);
        first.TryGetProperty("reportType", out _).Should().BeFalse();
        first.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString())
            .Should().Equal("No Calculation", "Uplift");
    }

    [Fact]
    public async Task AutoDetect_Zebra_Pdf_Resolves_Correct_Parser()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Zebra_PC_97000001_V2.0.pdf"));

        using var response = await PostParseAsync(
            client, bytes, "Zebra_PC_97000001_V2.0.pdf",
            "application/pdf",
            "Zebra", ParserSlugs.ZebraAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(ParserSlugs.ZebraPcrPdf);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(ParserSlugs.ZebraPcrPdf);

        var metric = await db.ParseMetrics.SingleAsync();
        metric.ParserSlug.Should().Be(ParserSlugs.ZebraPcrPdf);
    }

    [Fact]
    public async Task AutoDetect_Zebra_Xls_Resolves_Correct_Parser()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Zebra_PC_97000001.xls"));

        using var response = await PostParseAsync(
            client, bytes, "Zebra_PC_97000001.xls",
            "application/vnd.ms-excel",
            "Zebra", ParserSlugs.ZebraAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(ParserSlugs.ZebraPcrXls);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(ParserSlugs.ZebraPcrXls);
    }

    [Theory]
    [InlineData("BRDAS019000004V1.pdf", "application/pdf", ParserSlugs.LenovoLbpiIsgPdf)]
    [InlineData("BRDAD019200001.xls", "application/vnd.ms-excel", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_04.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ParserSlugs.LenovoLbpeIsgXls)]
    public async Task AutoDetect_Lenovo_Resolves_Correct_Parser(string inputName, string mime, string expectedSlug)
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", inputName));

        using var response = await PostParseAsync(
            client, bytes, inputName, mime, Vendors.LenovoIsg, ParserSlugs.LenovoAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(expectedSlug);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(expectedSlug);
        job.ImportType.Should().Be(ImportType.Auto);
    }

    [Fact]
    public async Task GetParsers_Includes_Synthesized_LenovoAuto_Entry()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.GetAsync("/api/parsers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        var lenovoEntries = parsers!.Where(p => p.GetProperty("vendor").GetString() == Vendors.LenovoIsg).ToList();
        lenovoEntries.Should().HaveCount(3); // Auto + LBP-E XLSX + LBP-I PDF
        var first = lenovoEntries[0];
        first.GetProperty("slug").GetString().Should().Be(ParserSlugs.LenovoAuto);
        first.GetProperty("displayName").GetString().Should().Be(AutoDetectTypes.DisplayName);
        first.TryGetProperty("reportType", out _).Should().BeFalse();
        first.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString())
            .Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        first.GetProperty("acceptedMimes").EnumerateArray().Select(t => t.GetString())
            .Should().Equal(
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/pdf");
        // The Auto entry advertises the split checkbox because every Lenovo parser supports it.
        first.GetProperty("supportsSolutionIdSplit").GetBoolean().Should().BeTrue();

        var lbpe = lenovoEntries.Single(p => p.GetProperty("slug").GetString() == ParserSlugs.LenovoLbpeIsgXls);
        lbpe.GetProperty("displayName").GetString().Should().Be("LBP-E ISG Quote (XLSX)");
        lbpe.GetProperty("acceptedMime").GetString().Should().Be("application/vnd.ms-excel");
        lbpe.GetProperty("acceptedMimes").EnumerateArray().Select(t => t.GetString())
            .Should().Equal("application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        lbpe.GetProperty("crmTemplate").GetString().Should().Be(CrmTemplates.NoCalculation);
        lbpe.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString())
            .Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        lbpe.GetProperty("supportsSolutionIdSplit").GetBoolean().Should().BeTrue();
        lbpe.TryGetProperty("reportType", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetParsers_LenovoIdg_HasExactlyOneFileTypeAndNoAutoEntry()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.GetAsync("/api/parsers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        var idgEntries = parsers!.Where(p => p.GetProperty("vendor").GetString() == Vendors.LenovoIdg).ToList();

        idgEntries.Should().ContainSingle();
        var idg = idgEntries[0];
        idg.GetProperty("slug").GetString().Should().Be(ParserSlugs.LenovoLbpiIdgPdf);
        idg.GetProperty("displayName").GetString().Should().Be("LBP-I IDG Quote (PDF)");
        idg.GetProperty("acceptedMime").GetString().Should().Be("application/pdf");
        idg.GetProperty("crmTemplate").GetString().Should().Be(CrmTemplates.NoCalculation);
        idg.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString())
            .Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        idg.GetProperty("supportsSolutionIdSplit").GetBoolean().Should().BeFalse();
        idg.TryGetProperty("reportType", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Dell_CTO_Sample.json", ParserSlugs.DellCtoJson)]
    [InlineData("Dell_APOS_Sample.json", ParserSlugs.DellAposJson)]
    public async Task AutoDetect_DellJson_Resolves_Correct_Parser(string inputName, string expectedSlug)
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", inputName));

        using var response = await PostParseAsync(
            client, bytes, inputName, "application/json", Vendors.Dell, ParserSlugs.DellAuto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle().Which.Should().Be(expectedSlug);
    }

    [Fact]
    public async Task GetParsers_Includes_Synthesized_DellAuto_Entry()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.GetAsync("/api/parsers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        var dellEntries = parsers!.Where(p => p.GetProperty("vendor").GetString() == Vendors.Dell).ToList();
        dellEntries.Should().HaveCount(3); // Auto + CTO + APOS
        var first = dellEntries[0];
        first.GetProperty("slug").GetString().Should().Be(ParserSlugs.DellAuto);
        first.GetProperty("displayName").GetString().Should().Be(AutoDetectTypes.DisplayName);
        // Dell is No Calculation only — the Quote API path writes without FX or margin inputs.
        first.GetProperty("availableTemplates").EnumerateArray().Select(t => t.GetString())
            .Should().Equal(CrmTemplates.NoCalculation);
    }

    [Fact]
    public async Task AutoDetect_Zebra_NoMatch_Returns_422_FileTypeError_And_Records_Nothing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Quote_9400000001.xls"));

        using var response = await PostParseAsync(
            client, bytes, "Quote_9400000001.xls",
            "application/vnd.ms-excel",
            "Zebra", ParserSlugs.ZebraAuto);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("fileType");
        detail.GetProperty("message").GetString().Should().Be(AutoDetectTypes.NoMatchMessage);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FailedParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseMetrics.CountAsync()).Should().Be(0);

        if (Directory.Exists(fixture.UploadDir))
        {
            Directory.GetFiles(fixture.UploadDir, "*", SearchOption.AllDirectories)
                .Should().BeEmpty();
        }
    }

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client, byte[] fileBytes, string filename, string contentType,
        string vendor, string parserSlug)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parserSlug");
        form.Add(new StringContent("0.75"), "fxRate");
        form.Add(new StringContent("0.10"), "margin");

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

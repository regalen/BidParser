using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

/// <summary>
/// Integration tests for HP Bid (XLSX) parser — /api/parsers and /api/parse endpoints.
/// </summary>
public sealed class HpParseTests
{
    // ── /api/parsers ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsersEndpoint_ExposesHpWithAvailableTemplates()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var parsers = await client.GetFromJsonAsync<JsonElement>("/api/parsers");
        var hp = parsers.EnumerateArray()
            .First(p => p.GetProperty("vendor").GetString() == "HP");

        hp.GetProperty("slug").GetString().Should().Be("hp_bid_xlsx");
        hp.GetProperty("displayName").GetString().Should().Be("HP Bid (XLSX)");
        hp.GetProperty("crmTemplate").GetString().Should().Be("No Calculation");

        var templates = hp.GetProperty("availableTemplates").EnumerateArray()
            .Select(t => t.GetString())
            .ToList();
        templates.Should().Equal("No Calculation", "Uplift");
    }

    [Fact]
    public async Task ParsersEndpoint_ExposesHpServicesWithAvailableTemplatesAndSplit()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var parsers = await client.GetFromJsonAsync<JsonElement>("/api/parsers");
        var hpServices = parsers.EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "hp_services_xlsx");

        hpServices.GetProperty("displayName").GetString().Should().Be("Services (XLSX)");
        hpServices.GetProperty("vendor").GetString().Should().Be("HP");
        hpServices.GetProperty("crmTemplate").GetString().Should().Be("No Calculation");
        hpServices.GetProperty("supportsSolutionIdSplit").GetBoolean().Should().BeTrue();
        hpServices.GetProperty("solutionSplitLabel").GetString().Should().Be("SAID");

        var templates = hpServices.GetProperty("availableTemplates").EnumerateArray()
            .Select(t => t.GetString())
            .ToList();
        templates.Should().Equal("No Calculation", "Uplift");
    }

    [Fact]
    public async Task ParsersEndpoint_NutanixParsers_StillHaveSingleTemplate()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var parsers = await client.GetFromJsonAsync<JsonElement>("/api/parsers");
        var nutanixParsers = parsers.EnumerateArray()
            .Where(p => p.GetProperty("vendor").GetString() == "Nutanix")
            .ToList();

        nutanixParsers.Should().NotBeEmpty();
        foreach (var p in nutanixParsers)
        {
            var templates = p.GetProperty("availableTemplates").EnumerateArray()
                .Select(t => t.GetString())
                .ToList();
            templates.Should().ContainSingle().Which.Should().Be("Foreign Uplift");
        }
    }

    // ── /api/parse ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseHp_NoCalculation_Roundtrip()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals_Sample_02_HPI.xlsx"));

        using var response = await PostHpParseAsync(client, bytes, "Deals_Sample_02_HPI.xlsx",
            "No Calculation", margin: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"')
            .Should().Be("99010002_2_NoCalculation.xlsx");
        // HP has no quoted total — header is present with empty value
        response.Headers.TryGetValues("X-Quoted-Total", out var qtValues).Should().BeTrue();
        qtValues!.Should().ContainSingle().Which.Should().Be("");
        // Validation matches (no quoted total to compare)
        response.Headers.GetValues("X-Validation").Should().ContainSingle().Which.Should().Be("match");
        // HP deals are always AUD
        response.Headers.GetValues("X-Currency").Should().ContainSingle().Which.Should().Be("AUD");
    }

    [Fact]
    public async Task ParseHp_Uplift_Roundtrip()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals_Sample_02_HPI.xlsx"));

        using var response = await PostHpParseAsync(client, bytes, "Deals_Sample_02_HPI.xlsx",
            "Uplift", margin: "7.50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Validation").Should().ContainSingle().Which.Should().Be("match");
    }

    [Fact]
    public async Task ParseHp_UnknownTemplate_Returns400()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals_Sample_02_HPI.xlsx"));

        // Passing a Nutanix template to an HP parser should be rejected
        using var response = await PostHpParseAsync(client, bytes, "Deals_Sample_02_HPI.xlsx",
            "Foreign Uplift", margin: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Unknown CRM template for this parser.");
    }

    [Fact]
    public async Task ParseHp_DefaultTemplateWhenCrmTemplateOmitted()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals_Sample_02_HPI.xlsx"));

        // Omitting crmTemplate should default to the parser's CrmTemplate ("No Calculation")
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "Deals_Sample_02_HPI.xlsx");
        form.Add(new StringContent("HP"), "vendor");
        form.Add(new StringContent("hp_bid_xlsx"), "parserSlug");
        // fxRate and margin intentionally omitted

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MeSettings_AcceptsHpAsVendor()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings",
            new { DefaultVendor = "HP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("defaultVendor").GetString().Should().Be("HP");
    }

    // ── HP OneConfig (XLSX) ───────────────────────────────────────────────────

    [Fact]
    public async Task ParsersEndpoint_ExposesHpOneConfigWithSingleTemplate()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var parsers = await client.GetFromJsonAsync<JsonElement>("/api/parsers");
        var oneConfig = parsers.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("slug").GetString() == "hp_oneconfig_xlsx");

        oneConfig.ValueKind.Should().NotBe(JsonValueKind.Undefined, "hp_oneconfig_xlsx should be registered");
        oneConfig.GetProperty("displayName").GetString().Should().Be("OneConfig (XLSX)");
        oneConfig.GetProperty("vendor").GetString().Should().Be("HP");
        oneConfig.GetProperty("crmTemplate").GetString().Should().Be("% Off RRP with Uplift");

        var templates = oneConfig.GetProperty("availableTemplates").EnumerateArray()
            .Select(t => t.GetString())
            .ToList();
        templates.Should().ContainSingle().Which.Should().Be("% Off RRP with Uplift");
    }

    [Fact]
    public async Task ParseHpOneConfig_Roundtrip()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "99000001.xlsx"));

        using var response = await PostHpOneConfigParseAsync(client, bytes, "99000001.xlsx", margin: "5", im: "30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"')
            .Should().Be("99000001_1_PercentOffWithUplift.xlsx");
        response.Headers.TryGetValues("X-Quoted-Total", out var qtValues).Should().BeTrue();
        qtValues!.Should().ContainSingle().Which.Should().Be("");
        response.Headers.GetValues("X-Validation").Should().ContainSingle().Which.Should().Be("match");
        response.Headers.GetValues("X-Currency").Should().ContainSingle().Which.Should().Be("AUD");
    }

    [Fact]
    public async Task ParseHpOneConfig_MissingIm_Returns400()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "99000001.xlsx"));

        // im intentionally omitted
        using var response = await PostHpOneConfigParseAsync(client, bytes, "99000001.xlsx", margin: "5", im: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("IM% is required for OneConfig (XLSX).");

        // L2: a user-input 400 is not a genuine parse failure — it must not be
        // recorded as a FailedParseJob, and the stored upload must be cleaned up.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FailedParseJobs.AnyAsync()).Should().BeFalse();
        (await db.ParseJobs.AnyAsync()).Should().BeFalse();
        var originalsDir = Path.Combine(fixture.UploadDir, "originals");
        if (Directory.Exists(originalsDir))
        {
            Directory.GetFiles(originalsDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task MeSettings_PersistsImPercent()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings",
            new { ImPercent = "30.00" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("imPercent").GetString().Should().Be("30.00");
    }

    [Fact]
    public async Task Me_ReturnsImPercent()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // Persist a value first
        await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings", new { ImPercent = "25.50" });

        var meJson = await client.GetFromJsonAsync<JsonElement>("/api/me");
        meJson.GetProperty("imPercent").GetString().Should().Be("25.50");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostHpOneConfigParseAsync(
        HttpClient client,
        byte[] fileBytes,
        string filename,
        string? margin,
        string? im)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent("HP"), "vendor");
        form.Add(new StringContent("hp_oneconfig_xlsx"), "parserSlug");
        if (margin is not null) form.Add(new StringContent(margin), "margin");
        if (im is not null) form.Add(new StringContent(im), "imPercent");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostHpParseAsync(
        HttpClient client,
        byte[] fileBytes,
        string filename,
        string crmTemplate,
        string? margin)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent("HP"), "vendor");
        form.Add(new StringContent("hp_bid_xlsx"), "parserSlug");
        form.Add(new StringContent(crmTemplate), "crmTemplate");
        if (margin is not null)
        {
            form.Add(new StringContent(margin), "margin");
        }

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

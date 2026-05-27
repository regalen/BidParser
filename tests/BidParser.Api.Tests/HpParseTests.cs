using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
        hp.GetProperty("display_name").GetString().Should().Be("HP Bid (XLSX)");
        hp.GetProperty("crm_template").GetString().Should().Be("No Calculation");

        var templates = hp.GetProperty("available_templates").EnumerateArray()
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
            var templates = p.GetProperty("available_templates").EnumerateArray()
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
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        using var response = await PostHpParseAsync(client, bytes, "Deals20260518T043243_HPI.xlsx",
            "No Calculation", margin: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"')
            .Should().Be("Deals20260518T043243_HPI_parsed.xlsx");
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
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        using var response = await PostHpParseAsync(client, bytes, "Deals20260518T043243_HPI.xlsx",
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
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        // Passing a Nutanix template to an HP parser should be rejected
        using var response = await PostHpParseAsync(client, bytes, "Deals20260518T043243_HPI.xlsx",
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
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        // Omitting crm_template should default to the parser's CrmTemplate ("No Calculation")
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "Deals20260518T043243_HPI.xlsx");
        form.Add(new StringContent("HP"), "vendor");
        form.Add(new StringContent("hp_bid_xlsx"), "parser_slug");
        // fx_rate and margin intentionally omitted

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
            new { default_vendor = "HP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("default_vendor").GetString().Should().Be("HP");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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
        form.Add(new StringContent("hp_bid_xlsx"), "parser_slug");
        form.Add(new StringContent(crmTemplate), "crm_template");
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Api.Hosting;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class RuntimeConfigurationEndpointsTests
{
    [Fact]
    public async Task Admin_get_returns_raw_invalid_documents_for_repair()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string invalidStoredGuidance = "[{\"fileTypes\":[\"retired_slug\"],\"html\":\"<p>Repair me</p>\"}]";
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.RuntimeConfigs.SingleAsync(config => config.Key == RuntimeConfigKeys.GuidanceMessages);
            row.JsonPayload = invalidStoredGuidance;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/admin/runtime-config");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var guidance = payload.GetProperty("guidanceMessages");
        guidance.GetProperty("json").GetString().Should().Be(invalidStoredGuidance);
        guidance.GetProperty("validationError").GetString().Should().Contain("concrete registered parser slugs");
    }

    [Fact]
    public async Task Public_vendor_defaults_use_fixed_scale_strings_end_to_end()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string update = "[{\"vendors\":[\"Zebra\"],\"fxRate\":1.2,\"margin\":5,\"imPercent\":2.5,\"onCostPct\":2.85}]";
        using var saved = await ApiTestFixture.PutJsonWithCsrfAsync(
            client, "/api/admin/runtime-config/vendor-defaults", new { json = update });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        using var response = await client.GetAsync("/api/parse-ui-config");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var zebra = payload.GetProperty("vendorDefaults").GetProperty(Vendors.Zebra);
        zebra.GetProperty("fxRate").ValueKind.Should().Be(JsonValueKind.String);
        zebra.GetProperty("fxRate").GetString().Should().Be("1.2000");
        zebra.GetProperty("margin").GetString().Should().Be("5.00");
        zebra.GetProperty("imPercent").GetString().Should().Be("2.50");
        zebra.GetProperty("onCostPct").GetString().Should().Be("2.85");
    }

    [Fact]
    public async Task Bootstrap_seeds_every_legacy_mapping_and_never_overwrites_administrator_edits()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var publicConfig = await client.GetFromJsonAsync<JsonElement>("/api/parse-ui-config");
        publicConfig.GetProperty("vendorDefaults").GetProperty(Vendors.Zebra)
            .GetProperty("onCostPct").GetString().Should().Be("2.85");

        var guidance = publicConfig.GetProperty("guidanceByParserSlug");
        var expectedGuidance = new Dictionary<string, string>
        {
            [ParserSlugs.NutanixSoftwareOnlyPdf] = "Standard",
            [ParserSlugs.NutanixSoftwareOnlyXlsx] = "Standard",
            [ParserSlugs.NutanixRenewalPdf] = "Start End Date",
            [ParserSlugs.NutanixRenewalXlsx] = "Start End Date",
            [ParserSlugs.NutanixHardwareOnlyPdf] = "Standard",
            [ParserSlugs.NutanixHardwareOnlyXlsx] = "Standard",
            [ParserSlugs.HpBidXlsx] = "Hardware SOH",
            [ParserSlugs.HpGlobalBidXlsx] = "Hardware SOH",
            [ParserSlugs.HpOneConfigXlsx] = "Standard",
            [ParserSlugs.HpeBidXlsx] = "Hardware SOH",
            [ParserSlugs.LenovoLbpeIsgXls] = "Standard",
            [ParserSlugs.LenovoLbpiIsgPdf] = "Standard",
            [ParserSlugs.LenovoLbpiIdgPdf] = "Standard",
            [ParserSlugs.ZebraPcrPdf] = "Hardware SOH",
            [ParserSlugs.ZebraPcrXls] = "Hardware SOH",
            [ParserSlugs.DellCtoJson] = "Hardware SOH",
            [ParserSlugs.DellAposJson] = "Start End Date",
            [ParserSlugs.CiscoCcwQuoteXls] = "Hardware SOH Disc %"
        };
        guidance.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(expectedGuidance.Keys);
        foreach (var expected in expectedGuidance)
        {
            guidance.GetProperty(expected.Key).GetString().Should().Contain(expected.Value);
        }

        const string update = "[{\"vendors\":[\"Zebra\"],\"onCostPct\":3.5}]";
        using var saved = await ApiTestFixture.PutJsonWithCsrfAsync(
            client, "/api/admin/runtime-config/vendor-defaults", new { json = update });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RuntimeConfigs.SingleAsync(config => config.Key == RuntimeConfigKeys.VendorDefaults);
        row.JsonPayload.Should().Contain("3.5");
        row.CreatedAt.Should().NotBe(default);
        row.UpdatedAt.Should().NotBe(default);

        var updatedAt = row.UpdatedAt;
        var bootstrap = new RuntimeConfigurationBootstrapHostedService(
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>());
        await bootstrap.StartAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        row = await db.RuntimeConfigs.SingleAsync(config => config.Key == RuntimeConfigKeys.VendorDefaults);
        row.JsonPayload.Should().Contain("3.5");
        row.UpdatedAt.Should().Be(updatedAt);

        await db.RuntimeConfigs.ExecuteDeleteAsync();
        var scopeFactory = fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => new RuntimeConfigurationBootstrapHostedService(scopeFactory)
                .StartAsync(CancellationToken.None)));

        db.ChangeTracker.Clear();
        (await db.RuntimeConfigs.Select(config => config.Key).ToListAsync())
            .Should().BeEquivalentTo(RuntimeConfigKeys.GuidanceMessages, RuntimeConfigKeys.VendorDefaults);
    }

    [Fact]
    public async Task Runtime_configuration_enforces_active_user_admin_and_csrf_boundaries()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        (await client.GetAsync("/api/parse-ui-config")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/admin/runtime-config")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await ApiTestFixture.UnlockUserAsync(client);
        (await client.GetAsync("/api/parse-ui-config")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/admin/runtime-config")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var userPut = await ApiTestFixture.PutJsonWithCsrfAsync(
            client, "/api/admin/runtime-config/vendor-defaults", new { json = "[]" });
        userPut.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Runtime_configuration_validates_schemas_values_and_html_and_updates_independently()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var noCsrf = await client.PutAsJsonAsync(
            "/api/admin/runtime-config/guidance-messages", new { json = "[]" });
        noCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string[] invalidGuidance =
        [
            "{}",
            "[{}]",
            "[{\"fileTypes\":[],\"html\":\"<p>x</p>\"}]",
            "[{\"fileTypes\":[\"unknown_slug\"],\"html\":\"<p>x</p>\"}]",
            "[{\"fileTypes\":[\"nutanix_auto\"],\"html\":\"<p>x</p>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\",\"nutanix_software_only_pdf\"],\"html\":\"<p>x</p>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<p>x</p>\",\"extra\":true}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<script>alert(1)</script>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<a href='https://example.test'>x</a>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<p onclick='x()'>x</p>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<span style='background:red'>x</span>\"}]",
            "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<p style='color:red'>x</p>\"}]"
        ];
        foreach (var json in invalidGuidance)
        {
            using var rejected = await ApiTestFixture.PutJsonWithCsrfAsync(
                client, "/api/admin/runtime-config/guidance-messages", new { json });
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, json);
        }

        const string safeHtml = "[{\"fileTypes\":[\"nutanix_software_only_pdf\"],\"html\":\"<p><strong>Approved</strong></p><ul><li><span style='color: red'>guidance</span></li></ul>\"}]";
        using var accepted = await ApiTestFixture.PutJsonWithCsrfAsync(
            client, "/api/admin/runtime-config/guidance-messages", new { json = safeHtml });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var canonicalGuidance = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        canonicalGuidance.GetProperty("json").GetString().Should().Contain("<p><strong>Approved</strong></p>")
            .And.NotContain("\\u003C");

        string[] invalidDefaults =
        [
            "{}",
            "[{}]",
            "[{\"vendors\":[],\"margin\":1}]",
            "[{\"vendors\":[\"Unknown\"],\"margin\":1}]",
            "[{\"vendors\":[\"Zebra\",\"Zebra\"],\"margin\":1}]",
            "[{\"vendors\":[\"Zebra\"]}]",
            "[{\"vendors\":[\"Zebra\"],\"unknown\":1}]",
            "[{\"vendors\":[\"Zebra\"],\"margin\":-1}]",
            "[{\"vendors\":[\"Zebra\"],\"margin\":\"1\"}]",
            "[{\"vendors\":[\"Zebra\"],\"fxRate\":100000000}]",
            "[{\"vendors\":[\"Zebra\"],\"margin\":10000000000}]"
        ];
        foreach (var json in invalidDefaults)
        {
            using var rejected = await ApiTestFixture.PutJsonWithCsrfAsync(
                client, "/api/admin/runtime-config/vendor-defaults", new { json });
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, json);
        }

        const string validDefaults = "[{\"vendors\":[\"Zebra\"],\"fxRate\":1.23456,\"margin\":5.255,\"imPercent\":2,\"onCostPct\":3.5}]";
        using var defaultsSaved = await ApiTestFixture.PutJsonWithCsrfAsync(
            client, "/api/admin/runtime-config/vendor-defaults", new { json = validDefaults });
        defaultsSaved.StatusCode.Should().Be(HttpStatusCode.OK, await defaultsSaved.Content.ReadAsStringAsync());
        var canonicalDefaults = await defaultsSaved.Content.ReadFromJsonAsync<JsonElement>();
        canonicalDefaults.GetProperty("json").GetString().Should().Contain("1.2346").And.Contain("5.26");

        var publicConfig = await client.GetFromJsonAsync<JsonElement>("/api/parse-ui-config");
        publicConfig.GetProperty("guidanceByParserSlug")
            .GetProperty(ParserSlugs.NutanixSoftwareOnlyPdf).GetString().Should().Contain("Approved");
        var zebra = publicConfig.GetProperty("vendorDefaults").GetProperty(Vendors.Zebra);
        zebra.GetProperty("fxRate").GetString().Should().Be("1.2346");
        zebra.GetProperty("margin").GetString().Should().Be("5.26");

        var adminConfig = await client.GetFromJsonAsync<JsonElement>("/api/admin/runtime-config");
        adminConfig.GetProperty("guidanceMessages").GetProperty("json").GetString().Should().Contain("Approved");
    }

    [Fact]
    public async Task Missing_configuration_rows_return_safe_empty_public_maps()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RuntimeConfigs.ExecuteDeleteAsync();
        }

        var publicConfig = await client.GetFromJsonAsync<JsonElement>("/api/parse-ui-config");
        publicConfig.GetProperty("vendorDefaults").EnumerateObject().Should().BeEmpty();
        publicConfig.GetProperty("guidanceByParserSlug").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_first_saves_do_not_surface_primary_key_failures()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RuntimeConfigs
                .Where(config => config.Key == RuntimeConfigKeys.VendorDefaults)
                .ExecuteDeleteAsync();
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(value =>
            ApiTestFixture.PutJsonWithCsrfAsync(
                client,
                "/api/admin/runtime-config/vendor-defaults",
                new { json = $"[{{\"vendors\":[\"Zebra\"],\"margin\":{value}}}]" })));

        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }
}

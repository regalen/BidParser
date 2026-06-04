using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class ReportTypesAdminTests
{
    private static async Task<string> FirstParserSlugAsync(HttpClient client)
    {
        var parsers = await client.GetFromJsonAsync<JsonElement[]>("/api/parsers");
        parsers.Should().NotBeNull();
        return parsers![0].GetProperty("slug").GetString()!;
    }

    private static async Task<string?> ReportTypeForAsync(HttpClient client, string slug)
    {
        var parsers = await client.GetFromJsonAsync<JsonElement[]>("/api/parsers");
        var match = parsers!.First(p => p.GetProperty("slug").GetString() == slug);
        var prop = match.GetProperty("report_type");
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
    }

    [Fact]
    public async Task AdminCanSetUpdateAndClearReportType()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var slug = await FirstParserSlugAsync(client);
        (await ReportTypeForAsync(client, slug)).Should().BeNull();

        var set = await ApiTestFixture.PutJsonWithCsrfAsync(client, $"/api/report-types/{slug}", new { report_type = "Standard" });
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReportTypeForAsync(client, slug)).Should().Be("Standard");

        // Whitespace is trimmed on save.
        var update = await ApiTestFixture.PutJsonWithCsrfAsync(client, $"/api/report-types/{slug}", new { report_type = "  Start End Date  " });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReportTypeForAsync(client, slug)).Should().Be("Start End Date");

        // Empty string clears the mapping.
        var clear = await ApiTestFixture.PutJsonWithCsrfAsync(client, $"/api/report-types/{slug}", new { report_type = "" });
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReportTypeForAsync(client, slug)).Should().BeNull();
    }

    [Fact]
    public async Task UnknownSlugReturnsNotFound()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/report-types/not_a_real_parser", new { report_type = "Standard" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Unknown parser.");
    }

    [Fact]
    public async Task NonAdminCannotConfigureReportTypes()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var slug = await FirstParserSlugAsync(client);

        // Create the non-admin user while still authenticated as admin, then switch to it.
        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "salesperson1", name = "Sales Person", role = "user" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "salesperson1", password = "changeme" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "Sales123!" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ApiTestFixture.PutJsonWithCsrfAsync(client, $"/api/report-types/{slug}", new { report_type = "Standard" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BidParser.Infrastructure.Dell;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class DellSettingsEndpointsTests
{
    private const string ClientSecret = "admin-entered-dell-secret";

    [Fact]
    public async Task Admin_can_save_read_preserve_and_test_settings_without_secret_disclosure()
    {
        var tokenHandler = new TokenHandler();
        using var fixture = await CustomTestFixture.CreateAsync(configureServices: services =>
            services.AddHttpClient(DellAuthTokenProvider.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => tokenHandler));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/admin/dell-settings");
        initial.GetProperty("clientSecretConfigured").GetBoolean().Should().BeFalse();
        initial.TryGetProperty("clientSecret", out _).Should().BeFalse();
        initial.GetProperty("apiVersion").GetString().Should().Be(DellApiSettingsService.InitialApiVersion);

        var missingCsrf = await client.PutAsJsonAsync("/api/admin/dell-settings", SettingsPayload(ClientSecret));
        missingCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var saved = await ApiTestFixture.PutJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings",
            SettingsPayload(ClientSecret));
        var savedBody = await saved.Content.ReadAsStringAsync();
        saved.StatusCode.Should().Be(HttpStatusCode.OK, savedBody);
        savedBody.Should().NotContain(ClientSecret);
        using var savedJson = JsonDocument.Parse(savedBody);
        var savedRoot = savedJson.RootElement;
        savedRoot.GetProperty("clientSecretConfigured").GetBoolean().Should().BeTrue();
        savedRoot.TryGetProperty("clientSecret", out _).Should().BeFalse();
        savedRoot.TryGetProperty("clientSecretProtected", out _).Should().BeFalse();

        using var update = await ApiTestFixture.PutJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings",
            SettingsPayload(null, "en-nz"));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<DellApiSettingsService>();
            var config = await settingsService.GetAsync(CancellationToken.None);
            config.Should().NotBeNull();
            config!.ClientSecret.Should().Be(ClientSecret);
            config.DefaultLocale.Should().Be("en-nz");
        }

        var testMissingCsrf = await client.PostAsJsonAsync("/api/admin/dell-settings/test", new { });
        testMissingCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var tested = await ApiTestFixture.PostJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings/test",
            new { });
        tested.StatusCode.Should().Be(HttpStatusCode.OK);
        tokenHandler.CallCount.Should().Be(1);

        using var invalid = await ApiTestFixture.PutJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings",
            SettingsPayload(null) with { TokenUrl = "http://example.test/token" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(invalid)).Should().Contain("absolute HTTPS URL");
    }

    [Fact]
    public async Task Non_admin_is_forbidden_from_all_Dell_settings_routes()
    {
        using var fixture = await CustomTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var create = await ApiTestFixture.PostJsonWithCsrfAsync(
            client,
            "/api/users",
            new { username = "dell-user", name = "Dell User", role = "user" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tempPassword = created.GetProperty("tempPassword").GetString()!;
        await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        await ApiTestFixture.PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { username = "dell-user", password = tempPassword });
        await ApiTestFixture.PostJsonWithCsrfAsync(
            client,
            "/api/auth/change-password",
            new { OldPassword = tempPassword, NewPassword = "DellUser123!" });

        (await client.GetAsync("/api/admin/dell-settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ApiTestFixture.PutJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings",
            SettingsPayload(ClientSecret))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ApiTestFixture.PostJsonWithCsrfAsync(
            client,
            "/api/admin/dell-settings/test",
            new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static SettingsRequest SettingsPayload(string? secret, string locale = "en-au") => new(
        DellApiSettingsService.DefaultTokenUrl,
        "client-id",
        secret,
        DellApiSettingsService.DefaultQuoteUrlTemplate,
        locale,
        DellApiSettingsService.InitialClientIdHeader,
        DellApiSettingsService.InitialApiVersion,
        false);

    private sealed record SettingsRequest(
        string TokenUrl,
        string ClientId,
        string? ClientSecret,
        string QuoteUrlTemplate,
        string DefaultLocale,
        string ClientIdHeader,
        string ApiVersion,
        bool UseBasicAuthForToken);

    private sealed class TokenHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"test-connection-token\",\"expires_in\":3600}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}

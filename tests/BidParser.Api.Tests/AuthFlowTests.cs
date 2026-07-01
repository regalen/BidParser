using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BidParser.Api.Auth;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task BootstrapLoginAndForcedPasswordChangeGate()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "admin", password = "changeme" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginJson = await login.Content.ReadFromJsonAsync<JsonElement>();
        loginJson.GetProperty("user").GetProperty("role").GetString().Should().Be("admin");
        loginJson.GetProperty("user").GetProperty("must_change_password").GetBoolean().Should().BeTrue();

        var blocked = await client.GetAsync("/api/parsers");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ApiTestFixture.DetailAsync(blocked)).Should().Be("password_change_required");

        var weak = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "weak" });
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var weakJson = await weak.Content.ReadFromJsonAsync<JsonElement>();
        weakJson.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.Array);

        var changed = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "Admin123!" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var parsers = await client.GetAsync("/api/parsers");
        parsers.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PasswordChangeRevokesOtherSessionsButKeepsCurrent()
    {
        // M1: sessions are bound to a fingerprint of the password hash. Changing
        // the password re-issues the acting session's cookie (it survives) while
        // every other session for that user is revoked.
        using var fixture = await ApiTestFixture.CreateAsync();
        using var admin = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(admin);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(admin, "/api/users",
            new { username = "userb", name = "User B", role = "user" });
        create.EnsureSuccessStatusCode();
        var temp = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temp_password").GetString()!;

        // Session 1: log in with the temp password and set a real one.
        using var s1 = fixture.Factory.CreateClient();
        (await ApiTestFixture.PostJsonWithCsrfAsync(s1, "/api/auth/login", new { username = "userb", password = temp }))
            .EnsureSuccessStatusCode();
        (await ApiTestFixture.PostJsonWithCsrfAsync(s1, "/api/auth/change-password",
            new { old_password = temp, new_password = "UserB123!" })).EnsureSuccessStatusCode();
        (await s1.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Session 2: a separate login with the current password.
        using var s2 = fixture.Factory.CreateClient();
        (await ApiTestFixture.PostJsonWithCsrfAsync(s2, "/api/auth/login", new { username = "userb", password = "UserB123!" }))
            .EnsureSuccessStatusCode();
        (await s2.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Session 1 changes the password again: its own cookie is re-issued and
        // survives; session 2's cookie is bound to the now-stale hash → revoked.
        (await ApiTestFixture.PostJsonWithCsrfAsync(s1, "/api/auth/change-password",
            new { old_password = "UserB123!", new_password = "UserB456!" })).EnsureSuccessStatusCode();
        (await s1.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s2.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminPasswordResetRevokesUserSession()
    {
        // M1: an admin password reset changes the user's hash, so the user's
        // existing session is revoked on its next request.
        using var fixture = await ApiTestFixture.CreateAsync();
        using var admin = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(admin);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(admin, "/api/users",
            new { username = "userb", name = "User B", role = "user" });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("user").GetProperty("id").GetInt32();
        var temp = created.GetProperty("temp_password").GetString()!;

        using var userClient = fixture.Factory.CreateClient();
        (await ApiTestFixture.PostJsonWithCsrfAsync(userClient, "/api/auth/login", new { username = "userb", password = temp }))
            .EnsureSuccessStatusCode();
        (await ApiTestFixture.PostJsonWithCsrfAsync(userClient, "/api/auth/change-password",
            new { old_password = temp, new_password = "UserB123!" })).EnsureSuccessStatusCode();
        (await userClient.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var reset = await ApiTestFixture.PatchJsonWithCsrfAsync(admin, $"/api/users/{userId}", new { reset_password = true });
        reset.EnsureSuccessStatusCode();

        (await userClient.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginIsCaseInsensitiveAndSettingsPreserveDecimalScale()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "ADMIN", password = "changeme" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var changed = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "Admin123!" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings", new { default_vendor = "Nutanix", fx_rate = "0.74", margin = "7.5" });
        settings.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await settings.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("default_vendor").GetString().Should().Be("Nutanix");
        json.GetProperty("fx_rate").GetString().Should().Be("0.7400");
        json.GetProperty("margin").GetString().Should().Be("7.50");

        var me = await client.GetAsync("/api/me");
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>();
        meJson.GetProperty("fx_rate").GetString().Should().Be("0.7400");
        meJson.GetProperty("margin").GetString().Should().Be("7.50");
    }

    [Fact]
    public async Task LockedUserPolicyAllowsOnlyLoggedInEndpoints()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "admin", password = "changeme" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var weakChange = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "weak" });
        weakChange.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var lockedEndpoints = new List<Task<HttpResponseMessage>>
        {
            ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings", new { default_vendor = "Nutanix" }),
            client.GetAsync("/api/parsers"),
            client.PostAsync("/api/parse", new FormUrlEncodedContent([])),
            client.GetAsync("/api/history"),
            client.GetAsync("/api/users")
        };

        foreach (var response in await Task.WhenAll(lockedEndpoints))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ApiTestFixture.DetailAsync(response)).Should().Be("password_change_required");
        }

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = new StringContent(string.Empty)
        };
        logoutRequest.Headers.Add("X-Requested-With", "BidParser");
        var logout = await client.SendAsync(logoutRequest);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoginRateLimitsByUsernameAcrossIps()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        for (var index = 0; index < 5; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { username = "admin", password = "wrong" })
            };
            request.Headers.Add("X-Requested-With", "BidParser");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{index}");

            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var limitedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "admin", password = "wrong" })
        };
        limitedRequest.Headers.Add("X-Requested-With", "BidParser");
        limitedRequest.Headers.Add("X-Forwarded-For", "10.0.0.99");

        var limited = await client.SendAsync(limitedRequest);
        limited.StatusCode.Should().Be((HttpStatusCode)429);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await ApiTestFixture.DetailAsync(limited)).Should().Be("Too many attempts. Please try again later.");
    }

    [Fact]
    public async Task FrameworkAndCsrfErrorsUseDetailShape()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var missingCsrf = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "changeme" });
        missingCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ApiTestFixture.DetailAsync(missingCsrf)).Should().Be("csrf_required");

        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("not-json", Encoding.UTF8)
        };
        malformedRequest.Content.Headers.ContentType = new("application/json");
        malformedRequest.Headers.Add("X-Requested-With", "BidParser");
        var malformed = await client.SendAsync(malformedRequest);
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(malformed)).Should().Be("Invalid request body.");
    }

}

internal sealed class ApiTestFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly ScopedEnvironment _environment;

    private ApiTestFixture(string tempDir, ScopedEnvironment environment, WebApplicationFactory<Program> factory)
    {
        _tempDir = tempDir;
        _environment = environment;
        Factory = factory;
        UploadDir = Path.Combine(tempDir, "files");
    }

    public WebApplicationFactory<Program> Factory { get; }
    public string UploadDir { get; }

    public static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("detail").GetString() ?? string.Empty;
    }

    public static Task<HttpResponseMessage> PostJsonWithCsrfAsync<T>(HttpClient client, string url, T body)
    {
        return SendJsonWithCsrfAsync(client, HttpMethod.Post, url, body);
    }

    public static Task<HttpResponseMessage> PatchJsonWithCsrfAsync<T>(HttpClient client, string url, T body)
    {
        return SendJsonWithCsrfAsync(client, HttpMethod.Patch, url, body);
    }

    public static Task<HttpResponseMessage> PutJsonWithCsrfAsync<T>(HttpClient client, string url, T body)
    {
        return SendJsonWithCsrfAsync(client, HttpMethod.Put, url, body);
    }

    public static async Task<HttpResponseMessage> DeleteWithCsrfAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(request);
    }

    public static async Task UnlockAdminAsync(HttpClient client)
    {
        var login = await PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "admin", password = "changeme" });
        login.EnsureSuccessStatusCode();

        var changed = await PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = "changeme", new_password = "Admin123!" });
        changed.EnsureSuccessStatusCode();
    }

    public static async Task UnlockUserAsync(HttpClient client)
    {
        var createUserRes = await PostJsonWithCsrfAsync(client, "/api/users", new { username = "user1", name = "User One", role = "user" });
        if (!createUserRes.IsSuccessStatusCode)
        {
            // fallback if it was already created, or we need to login as admin to create
            await UnlockAdminAsync(client);
            createUserRes = await PostJsonWithCsrfAsync(client, "/api/users", new { username = "user1", name = "User One", role = "user" });
            await PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        }

        var createdJson = await createUserRes.Content.ReadFromJsonAsync<JsonElement>();
        var tempPassword = createdJson.GetProperty("temp_password").GetString()!;

        var login = await PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "user1", password = tempPassword });
        login.EnsureSuccessStatusCode();

        var changed = await PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = tempPassword, new_password = "User123!" });
        changed.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendJsonWithCsrfAsync<T>(HttpClient client, HttpMethod method, string url, T body)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Requested-With", "BidParser");
        return await client.SendAsync(request);
    }

    public static async Task<ApiTestFixture> CreateAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var connectionString = await MsSqlTestContainer.GetConnectionStringAsync($"test_{Guid.NewGuid():N}");
        var environment = new ScopedEnvironment(new Dictionary<string, string>
        {
            ["DB_CONNECTION_STRING"] = connectionString,
            ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
            ["SESSION_SECRET"] = $"test-secret-{Guid.NewGuid():N}",
            ["ADMIN_USERNAME"] = "admin",
            ["ADMIN_PASSWORD"] = "changeme",
            ["RATE_LIMIT_AUTH_PER_MIN"] = "5"
        });

        var factory = new WebApplicationFactory<Program>();
        await using var scope = factory.Services.CreateAsyncScope();
        var limiter = scope.ServiceProvider.GetRequiredService<AuthRateLimiter>();
        limiter.Clear();

        return new ApiTestFixture(tempDir, environment, factory);
    }

    public void Dispose()
    {
        Factory.Dispose();
        _environment.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }
}

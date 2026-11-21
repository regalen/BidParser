using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class AnnouncementEndpointsTests
{
    [Fact]
    public async Task Enabled_announcement_is_served_with_a_stable_session_key_until_it_is_saved_again()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/announcement");
        initial.GetProperty("html").ValueKind.Should().Be(JsonValueKind.Null);
        initial.GetProperty("dismissalKey").ValueKind.Should().Be(JsonValueKind.Null);

        const string html = "Maintenance on <strong>Saturday</strong>, <em>briefly</em> <u>offline</u>. <a href=\"https://example.invalid/status\">Status</a>";
        using (var saved = await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = true, html }))
        {
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("enabled").GetBoolean().Should().BeTrue();
            body.GetProperty("html").GetString().Should().Be(html);
        }

        var first = await client.GetFromJsonAsync<JsonElement>("/api/announcement");
        first.GetProperty("html").GetString().Should().Be(html);
        var key = first.GetProperty("dismissalKey").GetString();
        key.Should().MatchRegex("^[0-9a-f]{32}$");

        var again = await client.GetFromJsonAsync<JsonElement>("/api/announcement");
        again.GetProperty("dismissalKey").GetString().Should().Be(key, "the key identifies the session, not the request");

        (await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = true, html = "Updated <b>notice</b>" }))
            .EnsureSuccessStatusCode();
        var updated = await client.GetFromJsonAsync<JsonElement>("/api/announcement");
        updated.GetProperty("dismissalKey").GetString().Should().NotBe(key, "a saved change must reappear for users who dismissed the old text");

        (await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = false, html = "Updated <b>notice</b>" }))
            .EnsureSuccessStatusCode();
        var disabled = await client.GetFromJsonAsync<JsonElement>("/api/announcement");
        disabled.GetProperty("html").ValueKind.Should().Be(JsonValueKind.Null);

        var admin = await client.GetFromJsonAsync<JsonElement>("/api/admin/announcement");
        admin.GetProperty("enabled").GetBoolean().Should().BeFalse();
        admin.GetProperty("html").GetString().Should().Be("Updated <b>notice</b>", "disabling keeps the text for later reuse");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>Hello")]
    [InlineData("<a href=\"javascript:alert(1)\">Click</a>")]
    [InlineData("<strong onclick=\"alert(1)\">Hi</strong>")]
    [InlineData("<p>Paragraphs are not allowed</p>")]
    [InlineData("<span style=\"color:red\">Styled</span>")]
    [InlineData("<img src=\"https://example.invalid/x.png\">")]
    public async Task Unsupported_or_unsafe_markup_is_rejected(string html)
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = true, html });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Contain("unsupported markup");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<strong> </strong>")]
    public async Task Enabling_requires_visible_text_but_a_disabled_announcement_may_be_blank(string html)
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var enabled = await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = true, html });
        enabled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(enabled)).Should().Contain("Enter announcement text");

        using var disabled = await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = false, html });
        disabled.StatusCode.Should().Be(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_administrators_can_manage_the_announcement_but_every_active_user_can_read_it()
    {
        using var fixture = await ApiTestFixture.CreateAsync(authRateLimitPerMin: 100);
        using var admin = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(admin);
        (await ApiTestFixture.PutJsonWithCsrfAsync(admin, "/api/admin/announcement", new { enabled = true, html = "Hello <i>team</i>" }))
            .EnsureSuccessStatusCode();

        using var anonymous = fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/announcement")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var created = await ApiTestFixture.PostJsonWithCsrfAsync(admin, "/api/users", new { username = "reader", name = "Reader", role = "user" });
        created.EnsureSuccessStatusCode();
        var temp = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tempPassword").GetString()!;
        using var user = fixture.Factory.CreateClient();
        (await ApiTestFixture.PostJsonWithCsrfAsync(user, "/api/auth/login", new { username = "reader", password = temp })).EnsureSuccessStatusCode();
        (await user.GetAsync("/api/announcement")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a forced password change comes first");
        (await ApiTestFixture.PostJsonWithCsrfAsync(user, "/api/auth/change-password", new { OldPassword = temp, NewPassword = "Reader123!" }))
            .EnsureSuccessStatusCode();
        var visible = await user.GetFromJsonAsync<JsonElement>("/api/announcement");
        visible.GetProperty("html").GetString().Should().Be("Hello <i>team</i>");

        (await user.GetAsync("/api/admin/announcement")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var put = await ApiTestFixture.PutJsonWithCsrfAsync(user, "/api/admin/announcement", new { enabled = false, html = "" });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Save_requires_the_csrf_header()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await client.PutAsJsonAsync("/api/admin/announcement", new { enabled = true, html = "Hi" });
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonElement>("/api/announcement")).GetProperty("html").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_corrupt_stored_announcement_hides_the_banner_and_is_returned_raw_for_repair()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        (await ApiTestFixture.PutJsonWithCsrfAsync(client, "/api/admin/announcement", new { enabled = true, html = "Hi" }))
            .EnsureSuccessStatusCode();

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.RuntimeConfigs.SingleAsync(config => config.Key == RuntimeConfigKeys.Announcement);
            row.JsonPayload = "{\"enabled\":true,\"html\":\"<p>Not allowed</p>\"}";
            await db.SaveChangesAsync();
        }

        (await client.GetFromJsonAsync<JsonElement>("/api/announcement")).GetProperty("html").ValueKind.Should().Be(JsonValueKind.Null);
        var admin = await client.GetFromJsonAsync<JsonElement>("/api/admin/announcement");
        admin.GetProperty("html").GetString().Should().Be("<p>Not allowed</p>");
        admin.GetProperty("validationError").GetString().Should().Contain("unsupported markup");
    }

    [Fact]
    public void Dismissal_key_changes_per_login_per_user_every_24_hours_and_per_save()
    {
        var updatedAt = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        const long issuedAt = 1_800_000_000;
        var key = AnnouncementService.DismissalKey(7, issuedAt, issuedAt + 60, updatedAt);

        AnnouncementService.DismissalKey(7, issuedAt, issuedAt + (23 * 3600), updatedAt)
            .Should().Be(key, "the banner stays dismissed within the first 24 hours of a session");
        AnnouncementService.DismissalKey(7, issuedAt, issuedAt + (24 * 3600), updatedAt)
            .Should().NotBe(key, "a session that outlives 24 hours sees the banner again");
        AnnouncementService.DismissalKey(7, issuedAt + 5, issuedAt + 65, updatedAt)
            .Should().NotBe(key, "a new login is a new session");
        AnnouncementService.DismissalKey(8, issuedAt, issuedAt + 60, updatedAt)
            .Should().NotBe(key, "users sharing a browser do not share a dismissal");
        AnnouncementService.DismissalKey(7, issuedAt, issuedAt + 60, updatedAt.AddSeconds(1))
            .Should().NotBe(key, "a saved change reappears");
    }
}

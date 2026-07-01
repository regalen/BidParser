using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class UsersAdminTests
{
    [Fact]
    public async Task AdminUserCrudAndLastAdminGuards()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "salesperson1", name = "Sales Person", role = "user" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("user").GetProperty("id").GetInt32();
        created.GetProperty("user").GetProperty("must_change_password").GetBoolean().Should().BeTrue();
        created.GetProperty("temp_password").GetString().Should().HaveLength(10);

        var users = await client.GetFromJsonAsync<JsonElement[]>("/api/users");
        users.Should().NotBeNull();
        users!.Select(user => user.GetProperty("username").GetString()).Should().BeEquivalentTo("admin", "salesperson1");

        var selfDemote = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/users/1", new { role = "user" });
        selfDemote.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiTestFixture.DetailAsync(selfDemote)).Should().Be("Cannot remove the last admin.");

        var selfDelete = await ApiTestFixture.DeleteWithCsrfAsync(client, "/api/users/1");
        selfDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiTestFixture.DetailAsync(selfDelete)).Should().Be("Admins cannot delete themselves.");

        var update = await ApiTestFixture.PatchJsonWithCsrfAsync(client, $"/api/users/{userId}", new { username = "salesperson2", name = "Sales Updated", role = "admin" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("user").GetProperty("username").GetString().Should().Be("salesperson2");
        updated.GetProperty("user").GetProperty("name").GetString().Should().Be("Sales Updated");
        updated.GetProperty("user").GetProperty("role").GetString().Should().Be("admin");
        // No reset requested → no temp password returned.
        updated.GetProperty("temp_password").ValueKind.Should().Be(JsonValueKind.Null);

        var reset = await ApiTestFixture.PatchJsonWithCsrfAsync(client, $"/api/users/{userId}", new { reset_password = true });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetJson = await reset.Content.ReadFromJsonAsync<JsonElement>();
        resetJson.GetProperty("user").GetProperty("must_change_password").GetBoolean().Should().BeTrue();
        resetJson.GetProperty("temp_password").GetString().Should().HaveLength(10);

        var delete = await ApiTestFixture.DeleteWithCsrfAsync(client, $"/api/users/{userId}");
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UsernameUniquenessIsCaseInsensitive()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var duplicateCreate = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "Admin", name = "Duplicate", role = "user" });
        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiTestFixture.DetailAsync(duplicateCreate)).Should().Be("Username already exists.");

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "salesperson1", name = "Sales Person", role = "user" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("user").GetProperty("id").GetInt32();

        var duplicateUpdate = await ApiTestFixture.PatchJsonWithCsrfAsync(client, $"/api/users/{userId}", new { username = "ADMIN" });
        duplicateUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiTestFixture.DetailAsync(duplicateUpdate)).Should().Be("Username already exists.");
    }

    [Fact]
    public async Task NonAdminCannotUseUsersEndpoints()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "salesperson1", name = "Sales Person", role = "user" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tempPassword = created.GetProperty("temp_password").GetString()!;

        var logout = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "salesperson1", password = tempPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = tempPassword, new_password = "Sales123!" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await client.GetAsync("/api/users");
        users.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ApiTestFixture.DetailAsync(users)).Should().Be("admin_required");
    }
}

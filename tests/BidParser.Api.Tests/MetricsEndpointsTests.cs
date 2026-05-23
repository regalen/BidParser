using System.Net;
using System.Net.Http.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MetricsEndpointsTests
{
    private static async Task SeedMetricsAsync(ApiTestFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Clear existing just in case
        db.ParseMetrics.RemoveRange(db.ParseMetrics);

        var adminId = await db.Users.Where(u => u.Role == BidParser.Infrastructure.Entities.UserRole.Admin).Select(u => u.Id).FirstOrDefaultAsync();

        var today = DateTime.UtcNow;

        db.ParseMetrics.AddRange(
            new ParseMetric
            {
                UserId = adminId == 0 ? null : adminId,
                UserUsername = "admin",
                UserName = "Administrator",
                Vendor = "Nutanix",
                ParserSlug = "nutanix_software_only_pdf",
                SourceFilename = "file1.pdf",
                Currency = "USD",
                QuotedTotal = 100,
                ComputedTotal = 100,
                TotalsMatch = true,
                FxRate = 1.0m,
                Margin = 0m,
                CreatedAt = today.AddDays(-5)
            },
            new ParseMetric
            {
                UserId = null, // Set to null to avoid FK constraint error
                UserUsername = "sales1",
                UserName = "Sales Person",
                Vendor = "Nutanix",
                ParserSlug = "nutanix_hardware_only_pdf",
                SourceFilename = "file2.pdf",
                Currency = "USD",
                QuotedTotal = 200,
                ComputedTotal = 250,
                TotalsMatch = false,
                FxRate = 1.0m,
                Margin = 0m,
                CreatedAt = today.AddDays(-5)
            },
            new ParseMetric
            {
                UserId = null,
                UserUsername = "sales1",
                UserName = "Sales Person",
                Vendor = "OtherVendor",
                ParserSlug = "other_parser",
                SourceFilename = "file3.pdf",
                Currency = "USD",
                QuotedTotal = 300,
                ComputedTotal = 300,
                TotalsMatch = true,
                FxRate = 1.0m,
                Margin = 0m,
                CreatedAt = today.AddDays(-40) // Outside default 30-day window
            }
        );

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SummaryRequiresAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var adminClient = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(adminClient);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(adminClient, "/api/users", new { username = "normal", name = "Normal", role = "user" });
        create.EnsureSuccessStatusCode();

        using var normalClient = fixture.Factory.CreateClient();
        var login = await ApiTestFixture.PostJsonWithCsrfAsync(normalClient, "/api/auth/login", new { username = "normal", password = "changeme" });
        login.EnsureSuccessStatusCode();

        var change = await ApiTestFixture.PostJsonWithCsrfAsync(normalClient, "/api/auth/change-password", new { old_password = "changeme", new_password = "User123!" });
        change.EnsureSuccessStatusCode();

        var response = await normalClient.GetAsync("/api/metrics/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SummaryReturnsCorrectAggregationsInDefaultWindow()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var response = await client.GetAsync("/api/metrics/summary");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // Default window should catch the 2 recent metrics, but not the 40-days-old one
        var kpis = json.GetProperty("kpis");
        kpis.GetProperty("total_parses").GetInt32().Should().Be(2);
        kpis.GetProperty("active_users").GetInt32().Should().Be(2);
        kpis.GetProperty("active_vendors").GetInt32().Should().Be(1);
        kpis.GetProperty("mismatch_rate").GetString().Should().Be("0.5000"); // 1 mismatch out of 2

        var byUser = json.GetProperty("by_user").EnumerateArray().ToList();
        byUser.Should().HaveCount(2);

        var byVendor = json.GetProperty("by_vendor").EnumerateArray().ToList();
        byVendor.Should().ContainSingle();
        byVendor[0].GetProperty("vendor").GetString().Should().Be("Nutanix");
        byVendor[0].GetProperty("count").GetInt32().Should().Be(2);

        var timeSeries = json.GetProperty("time_series").EnumerateArray().ToList();
        timeSeries.Should().HaveCount(1);
        timeSeries[0].GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task SummaryRespectsFilters()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var response = await client.GetAsync("/api/metrics/summary?userId=1");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var kpis = json.GetProperty("kpis");
        
        kpis.GetProperty("total_parses").GetInt32().Should().Be(1);
        kpis.GetProperty("mismatch_rate").GetString().Should().Be("0.0000");
    }

    [Fact]
    public async Task SummaryRespectsCustomDateRange()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var today = DateTime.Now;
        var from = today.AddDays(-50).ToString("yyyy-MM-dd");
        var to = today.AddDays(-35).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/metrics/summary?from={from}&to={to}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var kpis = json.GetProperty("kpis");
        
        kpis.GetProperty("total_parses").GetInt32().Should().Be(1);
        var byVendor = json.GetProperty("by_vendor").EnumerateArray().ToList();
        byVendor.Should().ContainSingle();
        byVendor[0].GetProperty("vendor").GetString().Should().Be("OtherVendor");
    }
}

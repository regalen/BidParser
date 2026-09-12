using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
                ImportType = ImportType.Auto,
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
                ImportType = ImportType.Manual,
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
                ImportType = null,
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
        var normalTemp = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tempPassword").GetString()!;

        using var normalClient = fixture.Factory.CreateClient();
        var login = await ApiTestFixture.PostJsonWithCsrfAsync(normalClient, "/api/auth/login", new { username = "normal", password = normalTemp });
        login.EnsureSuccessStatusCode();

        var change = await ApiTestFixture.PostJsonWithCsrfAsync(normalClient, "/api/auth/change-password", new { OldPassword = normalTemp, NewPassword = "User123!" });
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
        kpis.GetProperty("totalParses").GetInt32().Should().Be(2);
        kpis.GetProperty("activeUsers").GetInt32().Should().Be(2);
        kpis.GetProperty("activeVendors").GetInt32().Should().Be(1);
        kpis.GetProperty("mismatchRate").GetString().Should().Be("0.5000"); // 1 mismatch out of 2

        var byUser = json.GetProperty("byUser").EnumerateArray().ToList();
        byUser.Should().HaveCount(2);

        var byVendor = json.GetProperty("byVendor").EnumerateArray().ToList();
        byVendor.Should().ContainSingle();
        byVendor[0].GetProperty("vendor").GetString().Should().Be("Nutanix");
        byVendor[0].GetProperty("count").GetInt32().Should().Be(2);

        var byImportType = json.GetProperty("byImportType").EnumerateArray().ToList();
        byImportType.Should().HaveCount(2);

        var timeSeries = json.GetProperty("timeSeries").EnumerateArray().ToList();
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

        kpis.GetProperty("totalParses").GetInt32().Should().Be(1);
        kpis.GetProperty("mismatchRate").GetString().Should().Be("0.0000");
    }

    [Fact]
    public async Task SummaryRespectsImportTypeFilter()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var response = await client.GetAsync("/api/metrics/summary?importType=auto");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var kpis = json.GetProperty("kpis");
        kpis.GetProperty("totalParses").GetInt32().Should().Be(1);

        var byImportType = json.GetProperty("byImportType").EnumerateArray().ToList();
        byImportType.Should().ContainSingle();
        byImportType[0].GetProperty("importType").GetString().Should().Be("auto");
        byImportType[0].GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task MismatchRateIsZeroStringOnEmptyRange()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // No seed data — the default 30-day window is empty.
        var response = await client.GetAsync("/api/metrics/summary");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var kpis = json.GetProperty("kpis");

        kpis.GetProperty("totalParses").GetInt32().Should().Be(0);
        kpis.GetProperty("mismatchRate").GetString().Should().Be("0");
    }

    [Fact]
    public async Task TimeSeriesBucketsByLocalDate()
    {
        var previousTz = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "UTC");
        TimeZoneInfo.ClearCachedData();

        try
        {
            using var fixture = await ApiTestFixture.CreateAsync();
            using var client = fixture.Factory.CreateClient();
            await ApiTestFixture.UnlockAdminAsync(client);

            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ParseMetrics.RemoveRange(db.ParseMetrics);
                var anchor = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
                db.ParseMetrics.Add(new ParseMetric
                {
                    UserId = null,
                    UserUsername = "tz-test",
                    Vendor = "Nutanix",
                    ParserSlug = "nutanix_software_only_pdf",
                    SourceFilename = "tz.pdf",
                    Currency = "USD",
                    ComputedTotal = 1m,
                    TotalsMatch = true,
                    FxRate = 1m,
                    Margin = 0m,
                    CreatedAt = anchor
                });
                await db.SaveChangesAsync();
            }

            var response = await client.GetAsync("/api/metrics/summary?from=2026-05-10&to=2026-05-10");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var ts = json.GetProperty("timeSeries").EnumerateArray().Single();
            ts.GetProperty("date").GetString().Should().Be("2026-05-10");
            ts.GetProperty("count").GetInt32().Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previousTz);
            TimeZoneInfo.ClearCachedData();
        }
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

        kpis.GetProperty("totalParses").GetInt32().Should().Be(1);
        var byVendor = json.GetProperty("byVendor").EnumerateArray().ToList();
        byVendor.Should().ContainSingle();
        byVendor[0].GetProperty("vendor").GetString().Should().Be("OtherVendor");
    }

    [Fact]
    public async Task SummaryRespectsRangeAllAndMonthlyGrouping()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ParseMetrics.RemoveRange(db.ParseMetrics);

            // March 2025
            db.ParseMetrics.Add(new ParseMetric { UserUsername = "test1", Vendor = "V1", ParserSlug = "P1", ImportType = ImportType.Auto, SourceFilename = "t1.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc) });
            db.ParseMetrics.Add(new ParseMetric { UserUsername = "test1", Vendor = "V2", ParserSlug = "P2", ImportType = ImportType.Manual, SourceFilename = "t2.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2025, 3, 20, 12, 0, 0, DateTimeKind.Utc) });
            // August 2025
            db.ParseMetrics.Add(new ParseMetric { UserUsername = "test2", Vendor = "V1", ParserSlug = "P1", ImportType = ImportType.Auto, SourceFilename = "t3.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2025, 8, 5, 12, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/metrics/summary?range=all");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("range").GetProperty("mode").GetString().Should().Be("all");
        json.GetProperty("timeSeriesGranularity").GetString().Should().Be("month");
        json.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(3);

        var timeSeries = json.GetProperty("timeSeries").EnumerateArray().ToList();
        timeSeries.Should().HaveCount(2);

        timeSeries[0].GetProperty("date").GetString().Should().Be("2025-03-01");
        timeSeries[0].GetProperty("count").GetInt32().Should().Be(2);

        timeSeries[1].GetProperty("date").GetString().Should().Be("2025-08-01");
        timeSeries[1].GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SummaryRespectsFiltersUnderRangeAll()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.Users.Where(u => u.Role == BidParser.Infrastructure.Entities.UserRole.Admin).Select(u => u.Id).FirstOrDefaultAsync();
            db.ParseMetrics.RemoveRange(db.ParseMetrics);

            db.ParseMetrics.Add(new ParseMetric { UserId = adminId, UserUsername = "test1", Vendor = "V1", ParserSlug = "P1", ImportType = ImportType.Auto, SourceFilename = "t1.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc) });
            db.ParseMetrics.Add(new ParseMetric { UserId = null, UserUsername = "test2", Vendor = "V2", ParserSlug = "P2", ImportType = ImportType.Manual, SourceFilename = "t2.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2025, 3, 20, 12, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        // Vendor filter
        var r1 = await client.GetFromJsonAsync<JsonElement>("/api/metrics/summary?range=all&vendor=V1");
        r1.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(1);

        // User filter
        var r2 = await client.GetFromJsonAsync<JsonElement>("/api/metrics/summary?range=all&userId=1");
        r2.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(1);

        // Parser filter
        var r3 = await client.GetFromJsonAsync<JsonElement>("/api/metrics/summary?range=all&parserSlug=P2");
        r3.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(1);

        // ImportType filter
        var r4 = await client.GetFromJsonAsync<JsonElement>("/api/metrics/summary?range=all&importType=manual");
        r4.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SummaryRejectsInvalidParameterCombinations()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var cases = new[]
        {
            "/api/metrics/summary?range=all&from=2026-01-01",
            "/api/metrics/summary?range=all&to=2026-01-31",
            "/api/metrics/summary?range=all&from=2026-01-01&to=2026-01-31",
            "/api/metrics/summary?range=unknown",
            "/api/metrics/summary?from=2026-01-01", // Missing 'to'
            "/api/metrics/summary?to=2026-01-31", // Missing 'from'
            "/api/metrics/summary?from=invalid&to=2026-01-31", // Invalid 'from'
            "/api/metrics/summary?from=2026-01-01&to=invalid", // Invalid 'to'
            "/api/metrics/summary?from=2026-01-31&to=2026-01-01" // Reversed dates
        };

        foreach (var c in cases)
        {
            var res = await client.GetAsync(c);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Expected BadRequest for {c}");
        }
    }

    [Fact]
    public async Task TimeSeriesBucketsByNonUtcDstLocalDate()
    {
        var previousTz = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "Europe/London");
        TimeZoneInfo.ClearCachedData();

        try
        {
            using var fixture = await ApiTestFixture.CreateAsync();
            using var client = fixture.Factory.CreateClient();
            await ApiTestFixture.UnlockAdminAsync(client);

            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ParseMetrics.RemoveRange(db.ParseMetrics);

                // Summer (BST): UTC+1. Midnight UTC is 01:00 BST next day.
                // June 10 23:30 UTC -> June 11 00:30 BST (Date: June 11)
                db.ParseMetrics.Add(new ParseMetric { UserUsername = "test", Vendor = "V", ParserSlug = "P", SourceFilename = "t1.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2026, 6, 10, 23, 30, 0, DateTimeKind.Utc) });

                // Winter (GMT): UTC+0. Midnight UTC is 00:00 GMT next day.
                // Jan 10 23:30 UTC -> Jan 10 23:30 GMT (Date: Jan 10)
                db.ParseMetrics.Add(new ParseMetric { UserUsername = "test", Vendor = "V", ParserSlug = "P", SourceFilename = "t2.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = new DateTime(2026, 1, 10, 23, 30, 0, DateTimeKind.Utc) });
                await db.SaveChangesAsync();
            }

            // A fixed-offset implementation using current offset would shift one of these incorrectly.
            // For example, if run in summer (UTC+1), the winter date (Jan 10 23:30) would be incorrectly bucketed to Jan 11.
            var response = await client.GetAsync("/api/metrics/summary?range=all");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var ts = json.GetProperty("timeSeries").EnumerateArray().ToList();

            // Expected monthly buckets: Jan and June
            ts.Should().HaveCount(2);
            ts[0].GetProperty("date").GetString().Should().Be("2026-01-01");
            ts[0].GetProperty("count").GetInt32().Should().Be(1);

            ts[1].GetProperty("date").GetString().Should().Be("2026-06-01");
            ts[1].GetProperty("count").GetInt32().Should().Be(1);

            // Also verify daily buckets using bounded range
            var dailyRes = await client.GetAsync("/api/metrics/summary?from=2026-01-01&to=2026-12-31");
            dailyRes.EnsureSuccessStatusCode();
            var dailyJson = await dailyRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var dailyTs = dailyJson.GetProperty("timeSeries").EnumerateArray().ToList();

            dailyTs.Should().HaveCount(2);
            dailyTs[0].GetProperty("date").GetString().Should().Be("2026-01-10");
            dailyTs[1].GetProperty("date").GetString().Should().Be("2026-06-11");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previousTz);
            TimeZoneInfo.ClearCachedData();
        }
    }

    [Fact]
    public async Task Summary_DefaultBoundaryCoverage()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ParseMetrics.RemoveRange(db.ParseMetrics);

            var today = DateTime.UtcNow;

            // Included: Today - 29 days
            db.ParseMetrics.Add(new ParseMetric { UserUsername = "u", Vendor = "V", ParserSlug = "P", SourceFilename = "t1.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = today.AddDays(-29) });
            // Excluded: Today - 30 days
            db.ParseMetrics.Add(new ParseMetric { UserUsername = "u", Vendor = "V", ParserSlug = "P", SourceFilename = "t2.pdf", Currency = "USD", ComputedTotal = 1m, TotalsMatch = true, FxRate = 1m, Margin = 0m, CreatedAt = today.AddDays(-30) });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/metrics/summary");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        json.GetProperty("kpis").GetProperty("totalParses").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Summary_BoundedUsesDailyGranularity()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var fromStr = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/metrics/summary?from={fromStr}&to={todayStr}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("timeSeriesGranularity").GetString().Should().Be("day");
        json.GetProperty("range").GetProperty("mode").GetString().Should().Be("bounded");

        var ts = json.GetProperty("timeSeries").EnumerateArray().ToList();
        ts.Should().NotBeEmpty();
        ts[0].GetProperty("date").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }
}

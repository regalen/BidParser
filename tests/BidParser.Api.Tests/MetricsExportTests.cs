using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class MetricsExportTests
{
    private static async Task SeedMetricsAsync(ApiTestFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            }
        );

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExportRequiresAdmin()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        await ApiTestFixture.UnlockAdminAsync(client);

        var create = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/users", new { username = "normal", name = "Normal", role = "user" });
        create.EnsureSuccessStatusCode();
        var normalTemp = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temp_password").GetString()!;

        await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/logout", new { });
        var login = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/login", new { username = "normal", password = normalTemp });
        login.EnsureSuccessStatusCode();

        var change = await ApiTestFixture.PostJsonWithCsrfAsync(client, "/api/auth/change-password", new { old_password = normalTemp, new_password = "User123!" });
        change.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/metrics/export");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportFiltersByVendorAndIncludesDateRangeInFilename()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ParseMetrics.RemoveRange(db.ParseMetrics);
            var now = DateTime.UtcNow;

            db.ParseMetrics.AddRange(
                new ParseMetric
                {
                    UserId = null,
                    UserUsername = "u",
                    Vendor = "Nutanix",
                    ParserSlug = "nutanix_software_only_pdf",
                    SourceFilename = "keep.pdf",
                    Currency = "USD",
                    ComputedTotal = 100,
                    TotalsMatch = true,
                    FxRate = 1m,
                    Margin = 0m,
                    CreatedAt = now.AddDays(-2)
                },
                new ParseMetric
                {
                    UserId = null,
                    UserUsername = "u",
                    Vendor = "Other",
                    ParserSlug = "other_parser",
                    SourceFilename = "drop.pdf",
                    Currency = "USD",
                    ComputedTotal = 50,
                    TotalsMatch = true,
                    FxRate = 1m,
                    Margin = 0m,
                    CreatedAt = now.AddDays(-2)
                });
            await db.SaveChangesAsync();
        }

        var from = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd");
        var to = DateTime.Today.ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"/api/metrics/export?vendor=Nutanix&from={from}&to={to}");
        response.EnsureSuccessStatusCode();

        var filename = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition!.FileName!;
        filename.Should().Contain(from);
        filename.Should().Contain(to);

        var stream = await response.Content.ReadAsStreamAsync();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.Single();
        ws.Cell(2, 6).GetString().Should().Be("keep.pdf");
        ws.Cell(3, 6).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public async Task ExportReturnsValidExcelFile()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedMetricsAsync(fixture);

        var response = await client.GetAsync("/api/metrics/export");
        response.EnsureSuccessStatusCode();

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var contentDisposition = response.Content.Headers.ContentDisposition;
        contentDisposition.Should().NotBeNull();
        contentDisposition!.DispositionType.Should().Be("attachment");
        contentDisposition.FileNameStar.Should().StartWith("utilisation_");
        contentDisposition.FileNameStar.Should().EndWith(".xlsx");

        var stream = await response.Content.ReadAsStreamAsync();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.Single();
        ws.Name.Should().Be("Utilisation");

        // Header check
        ws.Cell(1, 1).GetString().Should().Be("Date");
        ws.Cell(1, 4).GetString().Should().Be("Vendor");
        ws.Cell(1, 6).GetString().Should().Be("Source Filename");

        // Data row check (row 2)
        ws.Cell(2, 4).GetString().Should().Be("Nutanix");
        ws.Cell(2, 6).GetString().Should().Be("file1.pdf");
        ws.Cell(2, 8).GetDouble().Should().Be(100);
        ws.Cell(2, 10).GetBoolean().Should().BeTrue();
    }
}

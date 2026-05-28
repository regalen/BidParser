using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class ParseTests
{
    [Fact]
    public async Task ParseRoundtripDownloadsFile()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));

        using var response = await PostParseAsync(client, bytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "0.7400", "7.50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be("XQ-4076249_parsed.xlsx");
        response.Headers.GetValues("X-Validation").Should().ContainSingle().Which.Should().Be("match");
        response.Headers.GetValues("X-Computed-Total").Should().ContainSingle().Which.Should().Be("1625358.51");
        response.Headers.GetValues("X-Quoted-Total").Should().ContainSingle().Which.Should().Be("1625358.51");
    }

    [Fact]
    public async Task ParseErrors_UnknownParser()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
            "Nutanix", "unknown_parser_slug", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Unknown parser.");
    }

    [Fact]
    public async Task ParseErrors_VendorMismatch()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
            "NotNutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Parser does not match vendor.");
    }

    [Fact]
    public async Task ParseErrors_ExtensionMismatch()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // PDF parser receiving an XLSX file
        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("File extension does not match selected parser.");
    }

    [Fact]
    public async Task ParseErrors_UploadTooLarge()
    {
        using var fixture = await CustomTestFixture.CreateAsync(maxUploadMb: 0);
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // Any non-empty file exceeds a 0-byte limit
        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("File is too large.");
    }

    [Fact]
    public async Task ParseErrors_UnsupportedExtension()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.doc", "application/msword",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Only PDF, XLS, and XLSX files are supported.");
    }

    [Fact]
    public async Task ParseErrors_MagicBytesMismatch()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var zipRenamedToPdf = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        using var response = await PostParseAsync(client, zipRenamedToPdf, "renamed.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("upload");
        detail.GetProperty("hint").GetString().Should().Be("Unsupported file format.");
        detail.GetProperty("message").GetString().Should().Be("Unsupported file format.");
    }

    [Fact]
    public async Task ParseErrors_ParseError()
    {
        var parseErrorParser = new TestParser(
            "test-parse-error", "TestVendor", "application/pdf",
            _ => throw new ParseError("test-stage", "test hint", "test error message"));

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(parseErrorParser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, MinimalPdfBytes(), "test.pdf", "application/pdf",
            "TestVendor", "test-parse-error", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("test-stage");
        detail.GetProperty("hint").GetString().Should().Be("test hint");
        detail.GetProperty("message").GetString().Should().Be("test error message");
    }

    [Fact]
    public async Task ParseErrors_GenericException()
    {
        var exceptionParser = new TestParser(
            "test-exception", "TestVendor", "application/pdf",
            _ => throw new InvalidOperationException("something went wrong"));

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(exceptionParser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, MinimalPdfBytes(), "test.pdf", "application/pdf",
            "TestVendor", "test-exception", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetInt32().Should().Be(500);
        json.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        json.GetProperty("detail").GetString().Should().Be("The request could not be completed.");
        json.ToString().Should().NotContain("something went wrong");
    }

    [Fact]
    public async Task ParseRateLimitsByAuthenticatedUser()
    {
        var parser = new TestParser(
            "test-rate-limit", "TestVendor", "application/pdf",
            _ => new ParseResult
            {
                Metadata = new QuoteMetadata
                {
                    QuoteNumber = "T-001",
                    Supplier = "Test",
                    Currency = "USD",
                    QuotedTotal = null,
                    SourceFilename = "test.pdf",
                    ParserSlug = "test-rate-limit"
                },
                LineItems = Array.Empty<LineItem>(),
                Validation = new ValidationResult
                {
                    ComputedTotal = 0m,
                    QuotedTotal = null,
                    Matches = true,
                    Difference = 0m
                }
            });

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(parser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        for (var index = 0; index < 10; index++)
        {
            using var response = await PostParseAsync(client, MinimalPdfBytes(), $"test-{index}.pdf", "application/pdf",
                "TestVendor", "test-rate-limit", "1.0", "5.0");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var limited = await PostParseAsync(client, MinimalPdfBytes(), "test-limited.pdf", "application/pdf",
            "TestVendor", "test-rate-limit", "1.0", "5.0");
        limited.StatusCode.Should().Be((HttpStatusCode)429);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await ApiTestFixture.DetailAsync(limited)).Should().Be("Too many parse requests. Please try again later.");
    }

    [Fact]
    public async Task ForwardedHeadersIgnoreUntrustedSource()
    {
        using var fixture = await CustomTestFixture.CreateAsync(forwardedAllowIps: "203.0.113.10", environmentName: "Testing");
        using var client = fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/connection");
        request.Headers.Add("X-Test-Remote-Ip", "198.51.100.25");
        request.Headers.Add("X-Forwarded-For", "1.2.3.4");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("remote_ip").GetString().Should().NotBe("1.2.3.4");
        json.GetProperty("scheme").GetString().Should().Be("http");
    }

    [Fact]
    public async Task ForwardedHeadersAllowConfiguredProxy()
    {
        using var fixture = await CustomTestFixture.CreateAsync(forwardedAllowIps: "127.0.0.1,::1", environmentName: "Testing");
        using var client = fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/connection");
        request.Headers.Add("X-Test-Remote-Ip", "127.0.0.1");
        request.Headers.Add("X-Forwarded-For", "1.2.3.4");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("remote_ip").GetString().Should().Be("1.2.3.4");
        json.GetProperty("scheme").GetString().Should().Be("https");
    }

    [Fact]
    public async Task ParseUpdatesDefaultVendorButNotNumerics()
    {
        // Parse persists last-used vendor on the User row, but fx_rate / margin /
        // im_percent are intentionally NOT persisted — the dashboard re-prompts
        // the user every parse so a stale numeric never gets silently re-applied.
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));

        using var parseResponse = await PostParseAsync(client, bytes, "XQ-4076249.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "0.6543", "8.75");
        parseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        me.GetProperty("default_vendor").GetString().Should().Be("Nutanix");
        me.GetProperty("fx_rate").ValueKind.Should().Be(JsonValueKind.Null);
        me.GetProperty("margin").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ParseQuotedTotalHeaderEmptyWhenNull()
    {
        var nullTotalParser = new TestParser(
            "test-null-total", "TestVendor", "application/pdf",
            _ => new ParseResult
            {
                Metadata = new QuoteMetadata
                {
                    QuoteNumber = "T-001",
                    Supplier = "Test",
                    Currency = "USD",
                    QuotedTotal = null,
                    SourceFilename = "test.pdf",
                    ParserSlug = "test-null-total"
                },
                LineItems = Array.Empty<LineItem>(),
                Validation = new ValidationResult
                {
                    ComputedTotal = 0m,
                    QuotedTotal = null,
                    Matches = true,
                    Difference = 0m
                }
            });

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(nullTotalParser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        using var response = await PostParseAsync(client, MinimalPdfBytes(), "test.pdf", "application/pdf",
            "TestVendor", "test-null-total", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Quoted-Total", out var values).Should().BeTrue(
            "X-Quoted-Total header must be present even when QuotedTotal is null");
        values!.Should().ContainSingle().Which.Should().Be("");
    }

    [Fact]
    public async Task ParseFilenameSanitized()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));

        using var response = await PostParseAsync(client, bytes, "../../etc/passwd.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf", "1.0", "5.0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.OrderByDescending(j => j.Id).FirstAsync();
        job.SourceFilename.Should().Be("passwd.pdf");
        Path.IsPathRooted(job.SourcePath).Should().BeTrue();
        job.SourcePath.Should().NotContain("..");
    }

    [Fact]
    public async Task DefaultVendorValidationTest()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var bad = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings",
            new { default_vendor = "Cisco" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(bad)).Should().Be("Unknown vendor.");

        var good = await ApiTestFixture.PatchJsonWithCsrfAsync(client, "/api/me/settings",
            new { default_vendor = "Nutanix" });
        good.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await good.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("default_vendor").GetString().Should().Be("Nutanix");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client,
        byte[] fileBytes,
        string filename,
        string contentType,
        string vendor,
        string parserSlug,
        string fxRate,
        string margin)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parser_slug");
        form.Add(new StringContent(fxRate), "fx_rate");
        form.Add(new StringContent(margin), "margin");

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

    private static byte[] MinimalPdfBytes() => "%PDF-1.7\n"u8.ToArray();

}

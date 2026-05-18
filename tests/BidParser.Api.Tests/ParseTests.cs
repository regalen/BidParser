using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
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
        // Results.File sets filename* (RFC 5987); fall back to filename if needed
        var downloadName = response.Content.Headers.ContentDisposition.FileNameStar
            ?? response.Content.Headers.ContentDisposition.FileName?.Trim('"');
        downloadName.Should().Be("XQ-4076249_parsed.xlsx");
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
        (await ApiTestFixture.DetailAsync(response)).Should().Be("Only PDF and XLSX files are supported.");
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

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
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

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
            "TestVendor", "test-exception", "1.0", "5.0");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("parse");
        detail.GetProperty("hint").GetString().Should().Be("Could not parse this file.");
        detail.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseUpdatesUserDefaults()
    {
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
        me.GetProperty("fx_rate").GetString().Should().Be("0.6543");
        me.GetProperty("margin").GetString().Should().Be("8.75");
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

        using var response = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
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

    // ── test infrastructure ───────────────────────────────────────────────────

    private sealed class CustomTestFixture : IDisposable
    {
        private readonly string _tempDir;
        private readonly ScopedEnvironment _env;

        private CustomTestFixture(string tempDir, ScopedEnvironment env, WebApplicationFactory<Program> factory)
        {
            _tempDir = tempDir;
            _env = env;
            Factory = factory;
        }

        public WebApplicationFactory<Program> Factory { get; }

        public static async Task<CustomTestFixture> CreateAsync(IParserRegistry? registry = null, int maxUploadMb = 10)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var env = new ScopedEnvironment(new Dictionary<string, string>
            {
                ["DATABASE_URL"] = $"sqlite:///{Path.Combine(tempDir, "db.sqlite")}",
                ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
                ["SESSION_SECRET"] = $"test-{Guid.NewGuid():N}",
                ["ADMIN_USERNAME"] = "admin",
                ["ADMIN_PASSWORD"] = "changeme",
                ["RATE_LIMIT_AUTH_PER_MIN"] = "5",
                ["MAX_UPLOAD_MB"] = maxUploadMb.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            WebApplicationFactory<Program> factory;
            if (registry is not null)
            {
                factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                    builder.ConfigureServices(services =>
                    {
                        var d = services.SingleOrDefault(s => s.ServiceType == typeof(IParserRegistry));
                        if (d is not null) services.Remove(d);
                        services.AddSingleton<IParserRegistry>(registry);
                    }));
            }
            else
            {
                factory = new WebApplicationFactory<Program>();
            }

            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<AuthRateLimiter>().Clear();

            return new CustomTestFixture(tempDir, env, factory);
        }

        public void Dispose()
        {
            Factory.Dispose();
            _env.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private sealed class TestRegistry : IParserRegistry
    {
        public TestRegistry(params IParser[] parsers) => Parsers = parsers;
        public IReadOnlyList<IParser> Parsers { get; }
    }

    private sealed class TestParser : IParser
    {
        private readonly Func<string, ParseResult> _parse;

        public TestParser(string slug, string vendor, string acceptedMime, Func<string, ParseResult> parse)
        {
            Slug = slug;
            DisplayName = slug;
            Vendor = vendor;
            AcceptedMime = acceptedMime;
            _parse = parse;
        }

        public string Slug { get; }
        public string DisplayName { get; }
        public string Vendor { get; }
        public string AcceptedMime { get; }
        public string CrmTemplate { get; } = "Foreign Uplift";

        public ParseResult Parse(string path) => _parse(path);
    }
}

namespace BidParser.Api.Tests;

using System.Net;
using System.Net.Http.Headers;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FailureRecordingTests
{
    private static byte[] MinimalPdfBytes() => "%PDF-1.7\n"u8.ToArray();

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client, byte[] fileBytes, string filename, string contentType,
        string vendor, string parserSlug, string fxRate = "0.75", string margin = "0.10")
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parser_slug");
        form.Add(new StringContent(fxRate), "fx_rate");
        form.Add(new StringContent(margin), "margin");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task MagicByteMismatchRecordsFailureAndKeepsSource()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await PostParseAsync(client, "Not a PDF file"u8.ToArray(), "fake.pdf", "application/pdf",
            "Nutanix", "nutanix_software_only_pdf");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failure = await db.FailedParseJobs.SingleAsync();

        failure.Category.Should().Be(FailureCategory.MagicByteMismatch);
        failure.Stage.Should().Be("upload");
        failure.UserUsername.Should().Be("admin");
        failure.Vendor.Should().Be("Nutanix");
        failure.ParserSlug.Should().Be("nutanix_software_only_pdf");
        failure.SourceFilename.Should().Be("fake.pdf");
        File.Exists(failure.SourcePath).Should().BeTrue();
    }

    [Fact]
    public async Task ParseErrorFromParserRecordsStructuredFields()
    {
        var parser = new TestParser(
            "test-parse-error", "TestVendor", "application/pdf",
            _ => throw new ParseError("extract", "TOTAL line not found", "TOTAL line not found"));

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(parser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await PostParseAsync(client, MinimalPdfBytes(), "broken.pdf", "application/pdf",
            "TestVendor", "test-parse-error");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failure = await db.FailedParseJobs.SingleAsync();

        failure.Category.Should().Be(FailureCategory.ParserError);
        failure.Stage.Should().Be("extract");
        failure.Hint.Should().Be("TOTAL line not found");
        failure.Message.Should().Be("TOTAL line not found");
        failure.ErrorDetail.Should().Contain("ParseError");
        File.Exists(failure.SourcePath).Should().BeTrue();
    }

    [Fact]
    public async Task UnhandledExceptionRecordsCategoryWithNullStructuredFields()
    {
        var parser = new TestParser(
            "test-unhandled", "TestVendor", "application/pdf",
            _ => throw new InvalidOperationException("something went wrong"));

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(parser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var response = await PostParseAsync(client, MinimalPdfBytes(), "broken.pdf", "application/pdf",
            "TestVendor", "test-unhandled");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failure = await db.FailedParseJobs.SingleAsync();

        failure.Category.Should().Be(FailureCategory.UnhandledException);
        failure.Stage.Should().BeNull();
        failure.Hint.Should().BeNull();
        failure.Message.Should().BeNull();
        failure.ErrorDetail.Should().Contain("InvalidOperationException");
        failure.ErrorDetail.Should().Contain("something went wrong");
        File.Exists(failure.SourcePath).Should().BeTrue();
    }

    [Fact]
    public async Task PreSaveValidationFailuresRecordNothing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // 415 — unsupported extension
        var extRes = await PostParseAsync(client, new byte[] { 1 }, "weird.doc", "application/msword",
            "Nutanix", "nutanix_software_only_pdf");
        extRes.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        // 400 — extension mismatch with selected parser
        var mismatchRes = await PostParseAsync(client, new byte[] { 1 }, "wrong.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", "nutanix_software_only_pdf");
        mismatchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 400 — unknown parser slug
        var unknownRes = await PostParseAsync(client, new byte[] { 1 }, "test.pdf", "application/pdf",
            "Nutanix", "unknown_parser_slug");
        unknownRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.FailedParseJobs.AnyAsync()).Should().BeFalse();
        var originalsDir = Path.Combine(fixture.UploadDir, "originals");
        if (Directory.Exists(originalsDir))
        {
            Directory.GetFiles(originalsDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UserDefaultsAreNotPersistedOnFailure()
    {
        var parser = new TestParser(
            "test-parse-error-defaults", "TestVendor", "application/pdf",
            _ => throw new ParseError("extract", "boom", "boom"));

        using var fixture = await CustomTestFixture.CreateAsync(new TestRegistry(parser));
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        // capture admin's pre-failure defaults
        decimal? originalFxRate;
        decimal? originalMargin;
        string? originalVendor;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            originalFxRate = admin.FxRate;
            originalMargin = admin.Margin;
            originalVendor = admin.DefaultVendor;
        }

        var response = await PostParseAsync(client, MinimalPdfBytes(), "broken.pdf", "application/pdf",
            "TestVendor", "test-parse-error-defaults", fxRate: "1.2345", margin: "9.99");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            admin.FxRate.Should().Be(originalFxRate);
            admin.Margin.Should().Be(originalMargin);
            admin.DefaultVendor.Should().Be(originalVendor);
        }
    }
}

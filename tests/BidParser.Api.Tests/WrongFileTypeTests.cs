using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

// A user selecting the wrong file type for a real quote should be told the file is not
// recognised (with a suggestion when confident), WITHOUT recording any monitoring entry,
// parse job, or metric — and the stored upload should be discarded.
public sealed class WrongFileTypeTests
{
    [Fact]
    public async Task WrongFileType_SuggestsCorrectType_AndRecordsNothing()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        // XQ-4176792.xlsx is a Renewal (XLSX); deliberately select Software Only (XLSX).
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "XQ-4176792.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "XQ-4176792.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", "nutanix_software_only_xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("file_type");
        detail.GetProperty("message").GetString().Should()
            .Be("The file is not recognised as Software Only (XLSX) and appears to be a Renewal (XLSX). Select the correct file type and try again.");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FailedParseJobs.CountAsync()).Should().Be(0, "wrong-type is not a monitored failure");
        (await db.ParseJobs.CountAsync()).Should().Be(0);
        (await db.ParseMetrics.CountAsync()).Should().Be(0);

        // The stored upload is discarded (upload dir holds no leftover files).
        if (Directory.Exists(fixture.UploadDir))
        {
            Directory.GetFiles(fixture.UploadDir, "*", SearchOption.AllDirectories)
                .Should().BeEmpty("the upload should be deleted for a wrong-type rejection");
        }
    }

    [Fact]
    public async Task WrongFileType_UnknownVendorFile_FallsBackToGenericMessage()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var root = FindRepoRoot();
        // An HP file selected as a Nutanix type: no Nutanix sibling matches → generic message.
        var bytes = File.ReadAllBytes(Path.Combine(root, "samples", "inputs", "55648855.xlsx"));

        using var response = await PostParseAsync(
            client, bytes, "55648855.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Nutanix", "nutanix_software_only_xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = json.GetProperty("detail");
        detail.GetProperty("stage").GetString().Should().Be("file_type");
        detail.GetProperty("message").GetString().Should()
            .Be("The file is not recognised as Software Only (XLSX). Check the selected file type and try again.");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FailedParseJobs.CountAsync()).Should().Be(0);
    }

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client, byte[] fileBytes, string filename, string contentType,
        string vendor, string parserSlug)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parser_slug");
        form.Add(new StringContent("0.75"), "fx_rate");
        form.Add(new StringContent("0.10"), "margin");

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
}

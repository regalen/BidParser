namespace BidParser.Api.Tests;

using System.Net;
using System.Net.Http.Headers;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class FailureRecordingTests
{
    [Fact]
    public async Task ParseService_OnMagicByteMismatch_RecordsFailureAndKeepsSource()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var form = new MultipartFormDataContent();
        var fileBytes = "Not a PDF file"u8.ToArray();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "file", "fake.pdf");
        form.Add(new StringContent("Nutanix"), "vendor");
        form.Add(new StringContent("nutanix_software_only_pdf"), "parser_slug");
        form.Add(new StringContent("0.75"), "fx_rate");
        form.Add(new StringContent("0.10"), "margin");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstAsync(u => u.Username == "admin");
        var failures = await db.FailedParseJobs.ToListAsync();
        var failure = Assert.Single(failures);

        Assert.Equal(user.Id, failure.UserId);
        Assert.Equal(user.Username, failure.UserUsername);
        Assert.Equal("Nutanix", failure.Vendor);
        Assert.Equal("nutanix_software_only_pdf", failure.ParserSlug);
        Assert.Equal("fake.pdf", failure.SourceFilename);
        Assert.Equal(FailureCategory.MagicByteMismatch, failure.Category);
        Assert.Equal("upload", failure.Stage);
        Assert.True(File.Exists(failure.SourcePath));
    }
}

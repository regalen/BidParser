using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class OnCostCapabilityTests
{
    [Fact]
    public async Task Parsers_endpoint_exposes_on_cost_only_for_supporting_formats()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        var parsers = await client.GetFromJsonAsync<JsonElement[]>("/api/parsers");

        var supported = parsers!.Where(item => item.GetProperty("supportsOnCost").GetBoolean())
            .Select(item => item.GetProperty("slug").GetString()).ToList();
        supported.Should().BeEquivalentTo(ParserSlugs.ZebraAuto, ParserSlugs.ZebraPcrPdf, ParserSlugs.ZebraPcrXls,
            ParserSlugs.DatalogicQuotePdf, ParserSlugs.EpsonQuotePdf, ParserSlugs.StrikeQuotePdf);
    }

    [Fact]
    public async Task Unsupported_parser_ignores_posted_on_cost()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        var bytes = File.ReadAllBytes(TestFile("Deals_Sample_02_HPI.xlsx"));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "Deals_Sample_02_HPI.xlsx");
        form.Add(new StringContent(Vendors.Hp), "vendor");
        form.Add(new StringContent(ParserSlugs.HpBidXlsx), "parserSlug");
        form.Add(new StringContent(CrmTemplates.NoCalculation), "crmTemplate");
        form.Add(new StringContent("7.25"), "onCostPct");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var workbook = new XLWorkbook(stream);
        workbook.Worksheet(1).Cell(3, 26).IsEmpty().Should().BeTrue();
    }

    private static string TestFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BidParser.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), "samples", "inputs", name);
    }
}

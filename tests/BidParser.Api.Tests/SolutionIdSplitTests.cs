using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class SolutionIdSplitTests
{
    [Fact]
    public async Task LenovoSplit_ReturnsZipAndPersistsDownloadBehavior_WhileNormalParseStaysXlsx()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        var parserPayload = (await client.GetFromJsonAsync<JsonElement[]>("/api/parsers"))!;
        // Both Lenovo parsers split by Solution ID, so the synthesized Lenovo Auto entry
        // advertises it too; no other entry may.
        string[] splitCapableSlugs =
            [ParserSlugs.LenovoAuto, ParserSlugs.LenovoLbpeIsgXls, ParserSlugs.LenovoLbpiIsgPdf, ParserSlugs.HpServicesXlsx];
        parserPayload.Where(parser => splitCapableSlugs.Contains(parser.GetProperty("slug").GetString()))
            .Should().HaveCount(4)
            .And.OnlyContain(parser => parser.GetProperty("supportsSolutionIdSplit").GetBoolean());
        parserPayload.Where(parser => !splitCapableSlugs.Contains(parser.GetProperty("slug").GetString()))
            .Should().OnlyContain(parser => !parser.GetProperty("supportsSolutionIdSplit").GetBoolean());

        const string sourceName = "Bid_Platform_Bid_Request_Sample_03.xls";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var splitResponse = await PostParseAsync(
            client, bytes, sourceName, Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls, split: true);

        splitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        splitResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(splitResponse).Should().Be("BRDAD019200004_2_NoCalculation.zip");
        splitResponse.Headers.GetValues("X-Split-Count").Should().Equal("4");

        var archiveBytes = await splitResponse.Content.ReadAsByteArrayAsync();
        using (var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read))
        {
            archive.Entries.Select(entry => entry.FullName).Should().Equal(
                "BRDAD019200004_2_SIDX02YU2Q_NoCalculation.xlsx",
                "BRDAD019200004_2_SIDX02YU2R_NoCalculation.xlsx",
                "BRDAD019200004_2_SIDX02YU2S_NoCalculation.xlsx",
                "BRDAD019200004_2_SIDX02YU2T_NoCalculation.xlsx");
        }

        int splitJobId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ParseJobs.SingleAsync();
            job.SplitBySolutionId.Should().BeTrue();
            Path.GetExtension(job.OutputPath).Should().Be(".zip");
            splitJobId = job.Id;
        }

        using var historyDownload = await client.GetAsync($"/api/history/{splitJobId}/output");
        historyDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        historyDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(historyDownload).Should().Be("BRDAD019200004_2_NoCalculation.zip");

        using var monitoringDownload = await client.GetAsync($"/api/monitoring/jobs/{splitJobId}/output");
        monitoringDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        monitoringDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(monitoringDownload).Should().Be("BRDAD019200004_2_NoCalculation.zip");

        using var normalResponse = await PostParseAsync(
            client, bytes, sourceName, Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls, split: false);
        normalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        normalResponse.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        DownloadName(normalResponse).Should().Be("BRDAD019200004_2_NoCalculation.xlsx");
        normalResponse.Headers.Contains("X-Split-Count").Should().BeFalse();
        normalResponse.Headers.GetValues("X-Computed-Total").Should()
            .Equal(splitResponse.Headers.GetValues("X-Computed-Total"));
        normalResponse.Headers.GetValues("X-Quoted-Total").Should()
            .Equal(splitResponse.Headers.GetValues("X-Quoted-Total"));

        const string singleSourceName = "BRDAD019200001.xls";
        var singleBytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", singleSourceName));
        using var singleResponse = await PostParseAsync(
            client, singleBytes, singleSourceName, Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls, split: true);
        singleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        singleResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        singleResponse.Headers.GetValues("X-Split-Count").Should().Equal("1");
        using var singleArchive = new ZipArchive(
            new MemoryStream(await singleResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        singleArchive.Entries.Select(entry => entry.FullName).Should().Equal(
            "BRDAD019200001_1_SIDX02SDL3_NoCalculation.xlsx");
    }

    [Fact]
    public async Task UnsupportedParser_RejectsSolutionIdSplit()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string sourceName = "Deals_Sample_02_HPI.xlsx";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var response = await PostParseAsync(
            client, bytes, sourceName, Vendors.Hp, ParserSlugs.HpBidXlsx, split: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestFixture.DetailAsync(response)).Should()
            .Be("This file type does not support splitting by Solution ID.");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ParseJobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LenovoAutoPdfSplit_ResolvesParserAndReturnsZipWithSolutionWorkbooks()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string sourceName = "BRDAS019000003V1.pdf";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var response = await PostParseAsync(
            client, bytes, sourceName, Vendors.LenovoIsg, ParserSlugs.LenovoAuto, split: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Parser-Slug").Should().ContainSingle()
            .Which.Should().Be(ParserSlugs.LenovoLbpiIsgPdf);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(response).Should().Be("BRDAS019000003_1_NoCalculation.zip");
        response.Headers.GetValues("X-Split-Count").Should().Equal("2");

        var archiveBytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).Should().Equal(
            "BRDAS019000003_1_SIDX02Q2PL_NoCalculation.xlsx",
            "BRDAS019000003_1_SIDX02Q2PM_NoCalculation.xlsx");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ParseJobs.SingleAsync();
        job.ParserSlug.Should().Be(ParserSlugs.LenovoLbpiIsgPdf);
        job.SplitBySolutionId.Should().BeTrue();
        Path.GetExtension(job.OutputPath).Should().Be(".zip");
    }

    [Fact]
    public async Task LenovoSplit_Xlsx_ReturnsZipAndPersistsDownloadBehavior()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string sourceName = "Bid_Platform_Bid_Request_Sample_04.xlsx";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var response = await PostParseAsync(
            client, bytes, sourceName, Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls, split: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(response).Should().Be("BRDADTEST0001_1_NoCalculation.zip");
        response.Headers.GetValues("X-Split-Count").Should().Equal("1");

        var archiveBytes = await response.Content.ReadAsByteArrayAsync();
        using (var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read))
        {
            archive.Entries.Select(entry => entry.FullName).Should().Equal(
                "BRDADTEST0001_1_SIDTEST0001_NoCalculation.xlsx");
        }

        int splitJobId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ParseJobs.SingleAsync();
            job.ParserSlug.Should().Be(ParserSlugs.LenovoLbpeIsgXls);
            job.SplitBySolutionId.Should().BeTrue();
            Path.GetExtension(job.OutputPath).Should().Be(".zip");
            splitJobId = job.Id;
        }

        using var historyDownload = await client.GetAsync($"/api/history/{splitJobId}/output");
        historyDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        historyDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(historyDownload).Should().Be("BRDADTEST0001_1_NoCalculation.zip");

        using var monitoringDownload = await client.GetAsync($"/api/monitoring/jobs/{splitJobId}/output");
        monitoringDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        monitoringDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(monitoringDownload).Should().Be("BRDADTEST0001_1_NoCalculation.zip");
    }

    [Fact]
    public async Task LenovoSplit_ManualPartsXlsx_YieldsNoSolutionIdWorkbook()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string sourceName = "Bid_Platform_Bid_Request_Sample_07.xlsx";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var response = await PostParseAsync(
            client, bytes, sourceName, Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls, split: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(response).Should().Be("BRDADTEST0004_1_NoCalculation.zip");
        response.Headers.GetValues("X-Split-Count").Should().Equal("1");

        var archiveBytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).Should().Equal(
            "BRDADTEST0004_1_NoSolutionID_NoCalculation.xlsx");
    }

    [Fact]
    public async Task HpServicesSplit_ReturnsZipWithSevenSaidWorkbooks()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);

        const string sourceName = "CH9000000002.xlsx";
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "samples", "inputs", sourceName));
        using var response = await PostParseAsync(
            client, bytes, sourceName, Vendors.Hp, ParserSlugs.HpServicesXlsx, split: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(response).Should().Be("CH9000000002_NoCalculation.zip");
        response.Headers.GetValues("X-Split-Count").Should().Equal("7");

        var archiveBytes = await response.Content.ReadAsByteArrayAsync();
        using (var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read))
        {
            archive.Entries.Select(entry => entry.FullName).Should().Equal(
                "1073_3013_9550_NoCalculation.xlsx",
                "1073_3009_7589_NoCalculation.xlsx",
                "1073_3013_5249_NoCalculation.xlsx",
                "1073_3013_5309_NoCalculation.xlsx",
                "1073_3013_3523_NoCalculation.xlsx",
                "1073_3013_3693_NoCalculation.xlsx",
                "1073_3013_3753_NoCalculation.xlsx");
        }

        int splitJobId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ParseJobs.SingleAsync();
            job.ParserSlug.Should().Be(ParserSlugs.HpServicesXlsx);
            job.SplitBySolutionId.Should().BeTrue();
            Path.GetExtension(job.OutputPath).Should().Be(".zip");
            splitJobId = job.Id;
        }

        using var historyDownload = await client.GetAsync($"/api/history/{splitJobId}/output");
        historyDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        historyDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(historyDownload).Should().Be("CH9000000002_NoCalculation.zip");

        using var monitoringDownload = await client.GetAsync($"/api/monitoring/jobs/{splitJobId}/output");
        monitoringDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        monitoringDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        DownloadName(monitoringDownload).Should().Be("CH9000000002_NoCalculation.zip");
    }

    private static Task<HttpResponseMessage> PostParseAsync(
        HttpClient client,
        byte[] bytes,
        string filename,
        string vendor,
        string parserSlug,
        bool split)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new(Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".xls" => "application/vnd.ms-excel",
            ".pdf" => "application/pdf",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        });
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(vendor), "vendor");
        form.Add(new StringContent(parserSlug), "parserSlug");
        form.Add(new StringContent(CrmTemplates.NoCalculation), "crmTemplate");
        if (split)
        {
            form.Add(new StringContent("true"), "splitBySolutionId");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/parse") { Content = form };
        request.Headers.Add("X-Requested-With", "BidParser");
        return client.SendAsync(request);
    }

    private static string DownloadName(HttpResponseMessage response)
        => response.Content.Headers.ContentDisposition!.FileName!.Trim('"');

    private static string RepoRoot()
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

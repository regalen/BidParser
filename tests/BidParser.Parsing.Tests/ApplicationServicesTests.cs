using System.IO.Compression;
using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Domain.Constants;
using BidParser.Output;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class ApplicationServicesTests
{
    private readonly ParserRegistry registry = new();

    [Fact]
    public void Catalog_preserves_registry_order_and_inserts_auto_before_first_vendor_parser()
    {
        var catalog = new ParserCatalog(registry).GetAll();

        catalog.Should().HaveCount(registry.Parsers.Count + AutoDetectTypes.All.Count);
        catalog[0].Slug.Should().Be(ParserSlugs.NutanixAuto);
        catalog[1].Slug.Should().Be(ParserSlugs.NutanixSoftwareOnlyPdf);
        catalog.Single(item => item.Slug == ParserSlugs.LenovoLbpeIsgXls)
            .AcceptedExtensions.Should().BeEquivalentTo(".xls", ".xlsx");
        catalog.Single(item => item.Slug == ParserSlugs.TrellixQuoteXlsm)
            .AcceptedExtensions.Should().Equal(".xlsm");
    }

    [Fact]
    public void Template_capabilities_come_from_writer_profiles()
    {
        CrmWriter.GetCapabilities(CrmTemplates.ForeignUplift).Should().Be(
            new CrmTemplateCapabilities(true, true, true, true, false, false, false));
        CrmWriter.GetCapabilities(CrmTemplates.NoCalculation)!.SupportsOnCost.Should().BeTrue();
        CrmWriter.GetCapabilities(CrmTemplates.PercentOffWithUplift)!.RequiresDiscountOffMsrp.Should().BeTrue();
        CrmWriter.GetCapabilities("not-a-template").Should().BeNull();
    }

    [Fact]
    public async Task Shared_parse_and_write_services_produce_the_canonical_workbook()
    {
        var inspector = new SourceFormatInspector();
        var parser = new QuoteParseService(registry, inspector);
        var sourcePath = TestSample.Path("Strike_Quote_9203.pdf");
        var parsed = await parser.ParseAsync(new QuoteParseRequest(
            sourcePath, Vendors.Strike, ParserSlugs.StrikeQuotePdf));
        var outputPath = Path.Combine(Path.GetTempPath(), $"bidparser-application-{Guid.NewGuid():N}.xlsx");

        try
        {
            var written = await new WorkbookWriteService().WriteAsync(new WorkbookWriteRequest(
                parsed, outputPath, CrmTemplates.NoCalculation, OnCostPercent: 0.35m));

            written.OutputFilename.Should().Be("9203_1_NoCalculation.xlsx");
            written.WorkbookCount.Should().Be(1);
            using var workbook = new XLWorkbook(outputPath);
            workbook.Worksheet(CrmTemplates.NoCalculation).Cell(3, 2).GetString().Should().Be("STRIKE");
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Magic_byte_mismatch_is_a_host_neutral_input_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bidparser-magic-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "not a PDF");

        try
        {
            var action = () => new SourceFormatInspector().ValidateMagicBytesAsync(path, SourceFormatInspector.PdfMime);
            var exception = await action.Should().ThrowAsync<ParseInputException>();
            exception.Which.Kind.Should().Be(ParseInputErrorKind.MagicByteMismatch);
            exception.Which.Stage.Should().Be("upload");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Xlsm_extension_and_magic_bytes_are_accepted_only_for_the_xlsm_parser()
    {
        var inspector = new SourceFormatInspector();
        var source = TestSample.Path("Trellix_Quote_900003.xlsm");

        inspector.ResolveMime(source).Should().Be(SourceFormatInspector.XlsmMime);
        await inspector.ValidateMagicBytesAsync(source, SourceFormatInspector.XlsmMime);
        var parsed = await Service().ParseAsync(new QuoteParseRequest(
            source, Vendors.Trellix, ParserSlugs.TrellixQuoteXlsm));
        parsed.Result.LineItems.Should().HaveCount(4);

        var wrongSelection = () => Service().ParseAsync(new QuoteParseRequest(
            source, Vendors.Trellix, ParserSlugs.TrellixQuotePdf));
        (await wrongSelection.Should().ThrowAsync<ParseInputException>()).Which.Kind
            .Should().Be(ParseInputErrorKind.ExtensionMismatch);
    }

    [Theory]
    [InlineData("XQ-9100002.pdf", Vendors.Nutanix, ParserSlugs.NutanixSoftwareOnlyPdf)]
    [InlineData("XQ-9100002.xlsx", Vendors.Nutanix, ParserSlugs.NutanixSoftwareOnlyXlsx)]
    [InlineData("Deals_Sample_02_HPI.xlsx", Vendors.Hp, ParserSlugs.HpBidXlsx)]
    [InlineData("BRDAD019200001.xls", Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Zebra_PC_97000002.xls", Vendors.Zebra, ParserSlugs.ZebraPcrXls)]
    [InlineData("Zebra_PC_97000002_V2.0.pdf", Vendors.Zebra, ParserSlugs.ZebraPcrPdf)]
    [InlineData("Quote_9400000001.xls", Vendors.Cisco, ParserSlugs.CiscoCcwQuoteXls)]
    [InlineData("Epson_96000002.pdf", Vendors.Epson, ParserSlugs.EpsonQuotePdf)]
    [InlineData("Trellix_Quote_900003.xlsm", Vendors.Trellix, ParserSlugs.TrellixQuoteXlsm)]
    public async Task Representative_formats_parse_through_the_shared_service(
        string filename, string vendor, string slug)
    {
        var parsed = await Service().ParseAsync(new QuoteParseRequest(TestSample.Path(filename), vendor, slug));

        parsed.Parser.Slug.Should().Be(slug);
        parsed.WasAuto.Should().BeFalse();
        parsed.Result.LineItems.Should().NotBeEmpty();
        parsed.Result.Metadata.ParserSlug.Should().Be(slug);
    }

    [Theory]
    [InlineData("XQ-9100002.pdf", Vendors.Nutanix, ParserSlugs.NutanixAuto, ParserSlugs.NutanixSoftwareOnlyPdf)]
    [InlineData("BRDAD019200001.xls", Vendors.LenovoIsg, ParserSlugs.LenovoAuto, ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Zebra_PC_97000002_V2.0.pdf", Vendors.Zebra, ParserSlugs.ZebraAuto, ParserSlugs.ZebraPcrPdf)]
    public async Task Auto_resolves_the_concrete_parser(
        string filename, string vendor, string autoSlug, string expectedSlug)
    {
        var parsed = await Service().ParseAsync(new QuoteParseRequest(TestSample.Path(filename), vendor, autoSlug));

        parsed.Parser.Slug.Should().Be(expectedSlug);
        parsed.WasAuto.Should().BeTrue();
    }

    [Fact]
    public async Task Wrong_file_type_is_an_input_error_that_names_the_sibling_format()
    {
        var request = new QuoteParseRequest(
            TestSample.Path("XQ-9100004.pdf"), Vendors.Nutanix, ParserSlugs.NutanixSoftwareOnlyPdf);

        var exception = await ((Func<Task>)(() => Service().ParseAsync(request)))
            .Should().ThrowAsync<ParseInputException>();

        exception.Which.Kind.Should().Be(ParseInputErrorKind.WrongFileType);
        exception.Which.Stage.Should().Be("fileType");
        exception.Which.SuggestedParserName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Extension_mismatch_is_reported_before_the_file_is_read()
    {
        var action = () => new QuoteParseService(registry, new SourceFormatInspector())
            .ResolveSelection("quote.pdf", Vendors.Hp, ParserSlugs.HpBidXlsx);

        action.Should().Throw<ParseInputException>()
            .Which.Kind.Should().Be(ParseInputErrorKind.ExtensionMismatch);
    }

    [Fact]
    public async Task Describe_names_the_workbook_and_the_split_archive()
    {
        var parsed = await Service().ParseAsync(new QuoteParseRequest(
            TestSample.Path("Bid_Platform_Bid_Request_Sample_03.xls"),
            Vendors.LenovoIsg,
            ParserSlugs.LenovoLbpeIsgXls));
        var service = new WorkbookWriteService();

        service.Describe(parsed, CrmTemplates.NoCalculation, splitBySolutionId: false)
            .Should().BeEquivalentTo(new WorkbookWritePlan(
                CrmTemplates.NoCalculation,
                OutputNaming.OutputFilename(
                    parsed.SourceFilename,
                    CrmTemplates.NoCalculation,
                    parsed.Result.Metadata.BidNumber,
                    parsed.Result.Metadata.BidRevision,
                    parsed.Parser.OutputNameStyle),
                IsArchive: false));

        service.Describe(parsed, CrmTemplates.NoCalculation, splitBySolutionId: true)
            .IsArchive.Should().BeTrue();
    }

    [Fact]
    public void Describe_rejects_a_split_the_format_does_not_support()
    {
        var parsed = ParseSample("Strike_Quote_9203.pdf", Vendors.Strike, ParserSlugs.StrikeQuotePdf);

        var action = () => new WorkbookWriteService()
            .Describe(parsed, CrmTemplates.NoCalculation, splitBySolutionId: true);

        action.Should().Throw<ParseInputException>()
            .Which.Kind.Should().Be(ParseInputErrorKind.UnsupportedSplit);
    }

    [Fact]
    public async Task Split_save_writes_one_workbook_per_solution_id()
    {
        var parsed = ParseSample(
            "Bid_Platform_Bid_Request_Sample_03.xls", Vendors.LenovoIsg, ParserSlugs.LenovoLbpeIsgXls);
        var expectedGroups = SolutionOutputSplitter.Split(parsed.Result.LineItems);
        using var workspace = new TempWorkspace();
        var destination = workspace.Path("split.zip");

        var written = await new WorkbookWriteService().SaveAsync(
            new WorkbookWriteRequest(parsed, destination, CrmTemplates.NoCalculation, SplitBySolutionId: true),
            overwrite: false);

        written.WorkbookCount.Should().Be(expectedGroups.Count).And.BeGreaterThan(1);
        using var archive = ZipFile.OpenRead(destination);
        archive.Entries.Should().HaveCount(expectedGroups.Count);
        archive.Entries.Should().OnlyContain(entry => entry.Name.EndsWith(".xlsx"));
        workspace.StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Save_refuses_an_existing_destination_and_leaves_nothing_behind()
    {
        var parsed = ParseSample("Strike_Quote_9203.pdf", Vendors.Strike, ParserSlugs.StrikeQuotePdf);
        using var workspace = new TempWorkspace();
        var destination = workspace.Path("existing.xlsx");
        await File.WriteAllTextAsync(destination, "not a workbook");

        var action = () => new WorkbookWriteService().SaveAsync(
            new WorkbookWriteRequest(parsed, destination, CrmTemplates.NoCalculation), overwrite: false);

        (await action.Should().ThrowAsync<ParseInputException>())
            .Which.Kind.Should().Be(ParseInputErrorKind.DestinationExists);
        (await File.ReadAllTextAsync(destination)).Should().Be("not a workbook");
        workspace.StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Save_replaces_the_destination_when_overwrite_is_allowed()
    {
        var parsed = ParseSample("Strike_Quote_9203.pdf", Vendors.Strike, ParserSlugs.StrikeQuotePdf);
        using var workspace = new TempWorkspace();
        var destination = workspace.Path("existing.xlsx");
        await File.WriteAllTextAsync(destination, "not a workbook");

        var written = await new WorkbookWriteService().SaveAsync(
            new WorkbookWriteRequest(parsed, destination, CrmTemplates.NoCalculation), overwrite: true);

        written.OutputPath.Should().Be(Path.GetFullPath(destination));
        using var workbook = new XLWorkbook(destination);
        workbook.Worksheet(CrmTemplates.NoCalculation).Cell(3, 2).GetString().Should().Be("STRIKE");
        workspace.StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_save_writes_no_workbook_and_removes_its_staging_file()
    {
        var parsed = ParseSample("Strike_Quote_9203.pdf", Vendors.Strike, ParserSlugs.StrikeQuotePdf);
        using var workspace = new TempWorkspace();
        var destination = workspace.Path("cancelled.xlsx");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => new WorkbookWriteService().SaveAsync(
            new WorkbookWriteRequest(parsed, destination, CrmTemplates.NoCalculation),
            overwrite: false,
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(destination).Should().BeFalse();
        workspace.StagingFiles().Should().BeEmpty();
    }

    [Fact]
    public void Unique_destination_steps_past_the_names_already_taken()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.Path("9203_NoCalculation.xlsx");

        WorkbookWriteService.UniqueDestination(destination).Should().Be(destination);

        File.WriteAllText(destination, string.Empty);
        var copy = WorkbookWriteService.UniqueDestination(destination);
        copy.Should().Be(workspace.Path("9203_NoCalculation (2).xlsx"));

        File.WriteAllText(copy, string.Empty);
        WorkbookWriteService.UniqueDestination(destination)
            .Should().Be(workspace.Path("9203_NoCalculation (3).xlsx"));
    }

    private QuoteParseService Service() => new(registry, new SourceFormatInspector());

    private ParsedQuote ParseSample(string filename, string vendor, string slug) =>
        Service().ParseAsync(new QuoteParseRequest(TestSample.Path(filename), vendor, slug))
            .GetAwaiter().GetResult();

    /// <summary>A throwaway save directory, asserted empty of staging files after every write.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory =
            Directory.CreateTempSubdirectory($"bidparser-save-{Guid.NewGuid():N}");

        public string Path(string filename) => System.IO.Path.Combine(directory.FullName, filename);

        public IEnumerable<string> StagingFiles() => Directory.EnumerateFiles(directory.FullName, "bidparser-*");

        public void Dispose()
        {
            try { directory.Delete(recursive: true); } catch { }
        }
    }
}

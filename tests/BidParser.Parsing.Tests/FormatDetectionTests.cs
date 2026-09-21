using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class FormatDetectionTests
{
    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xlsm"] = "application/vnd.ms-excel.sheet.macroEnabled.12",
        [".xls"] = "application/vnd.ms-excel",
        [".json"] = "application/json"
    };

    [Theory]
    // Nutanix XLSX trio
    [InlineData("XQ-9100002.xlsx", "nutanix_software_only_xlsx")]
    [InlineData("XQ-9100003.xlsx", "nutanix_hardware_only_xlsx")]
    [InlineData("XQ-9100010.xlsx", "nutanix_renewal_xlsx")]
    // Nutanix PDF trio
    [InlineData("XQ-9100002.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100005.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100004.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100007.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100001.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100003.pdf", "nutanix_hardware_only_pdf")]
    public void Resolve_identifies_correct_nutanix_format(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);
        var ext = Path.GetExtension(inputName);
        var mime = ExtensionToMime[ext];

        var resolved = FormatDetection.Resolve(parsers, Vendors.Nutanix, mime, path);
        resolved.Should().NotBeNull();
        resolved!.Slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("Zebra_PC_97000001_V2.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000002_V2.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000003_V1.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000001.xls", "zebra_pcr_xls")]
    [InlineData("Zebra_PC_97000002.xls", "zebra_pcr_xls")]
    [InlineData("Zebra_PC_97000003.xls", "zebra_pcr_xls")]
    public void Resolve_identifies_correct_zebra_format(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);
        var mime = ExtensionToMime[Path.GetExtension(inputName)];

        var resolved = FormatDetection.Resolve(parsers, Vendors.Zebra, mime, path);
        resolved.Should().NotBeNull();
        resolved!.Slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData(Vendors.Datalogic, "Datalogic_PE930003.pdf", ParserSlugs.DatalogicQuotePdf)]
    [InlineData(Vendors.Epson, "Epson_96000002.pdf", ParserSlugs.EpsonQuotePdf)]
    [InlineData(Vendors.Strike, "Strike_Quote_9202.pdf", ParserSlugs.StrikeQuotePdf)]
    [InlineData(Vendors.Trellix, "Trellix_Quote_900001.pdf", ParserSlugs.TrellixQuotePdf)]
    public void Resolve_identifies_new_pdf_formats(string vendor, string inputName, string expectedSlug)
    {
        var path = Path.Combine(TestSample.Root, "samples", "inputs", inputName);
        FormatDetection.Resolve(new ParserRegistry().Parsers, vendor, "application/pdf", path)!.Slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("BRDAD019200001.xls", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_03.xls", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_04.xlsx", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_05.xlsx", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_06.xlsx", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_07.xlsx", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("Bid_Platform_Bid_Request_Sample_08.xlsx", ParserSlugs.LenovoLbpeIsgXls)]
    [InlineData("BRDAS019000004V1.pdf", ParserSlugs.LenovoLbpiIsgPdf)]
    [InlineData("BRDAS019000003V1.pdf", ParserSlugs.LenovoLbpiIsgPdf)]
    [InlineData("BRDAS019000002V1.pdf", ParserSlugs.LenovoLbpiIsgPdf)]
    public void Resolve_identifies_correct_lenovo_format(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);
        var mime = ExtensionToMime[Path.GetExtension(inputName)];

        var resolved = FormatDetection.Resolve(parsers, Vendors.LenovoIsg, mime, path);
        resolved.Should().NotBeNull();
        resolved!.Slug.Should().Be(expectedSlug);
    }

    [Fact]
    public void Resolve_participates_under_each_accepted_mime_for_multi_mime_parser()
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var xlsPath = Path.Combine(root, "samples", "inputs", "BRDAD019200001.xls");
        var xlsxPath = Path.Combine(root, "samples", "inputs", "Bid_Platform_Bid_Request_Sample_04.xlsx");

        var xlsResolved = FormatDetection.Resolve(parsers, Vendors.LenovoIsg, "application/vnd.ms-excel", xlsPath);
        xlsResolved.Should().NotBeNull();
        xlsResolved!.Slug.Should().Be(ParserSlugs.LenovoLbpeIsgXls);

        var xlsxResolved = FormatDetection.Resolve(
            parsers, Vendors.LenovoIsg, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsxPath);
        xlsxResolved.Should().NotBeNull();
        xlsxResolved!.Slug.Should().Be(ParserSlugs.LenovoLbpeIsgXls);
    }

    [Theory]
    [InlineData("Dell_CTO_Sample.json", ParserSlugs.DellCtoJson)]
    [InlineData("Dell_APOS_Sample.json", ParserSlugs.DellAposJson)]
    public void Resolve_identifies_dell_format_from_service_tag_number(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);

        var resolved = FormatDetection.Resolve(parsers, Vendors.Dell, ExtensionToMime[".json"], path);

        resolved.Should().NotBeNull();
        resolved!.Slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("BRDAD019200001.xls")]   // Lenovo LBP-E ISG — real OLE workbook
    [InlineData("Quote_9400000001.xls")] // Cisco CCW Quote — real OLE workbook
    public void Resolve_returns_null_for_non_zebra_xls(string inputName)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);

        FormatDetection.Resolve(parsers, Vendors.Zebra, ExtensionToMime[".xls"], path)
            .Should().BeNull();
    }

    [Theory]
    // Nutanix XLSX trio
    [InlineData("XQ-9100002.xlsx", "nutanix_software_only_xlsx")]
    [InlineData("XQ-9100003.xlsx", "nutanix_hardware_only_xlsx")]
    [InlineData("XQ-9100010.xlsx", "nutanix_renewal_xlsx")]
    // Nutanix PDF trio
    [InlineData("XQ-9100002.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100005.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100004.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100007.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100001.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100003.pdf", "nutanix_hardware_only_pdf")]
    public void Resolve_returns_null_when_expected_parser_is_excluded(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", inputName);
        var ext = Path.GetExtension(inputName);
        var mime = ExtensionToMime[ext];
        var expected = parsers.Single(p => p.Slug == expectedSlug);

        var resolved = FormatDetection.Resolve(parsers, Vendors.Nutanix, mime, path, exclude: expected);
        resolved.Should().BeNull("excluding the matching parser should result in no match");
    }

    [Fact]
    public void Resolve_returns_null_for_foreign_file()
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", "99000001.xlsx");
        var mime = ExtensionToMime[".xlsx"];

        var resolved = FormatDetection.Resolve(parsers, Vendors.Nutanix, mime, path);
        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_for_unsupported_mime()
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(root, "samples", "inputs", "Dell_CTO_Sample.json");

        var resolved = FormatDetection.Resolve(parsers, Vendors.Nutanix, "application/json", path);
        resolved.Should().BeNull();
    }

}

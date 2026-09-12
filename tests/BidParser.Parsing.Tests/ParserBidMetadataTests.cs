using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class ParserBidMetadataTests
{
    /// <summary>
    /// Every registered parser against every fixture that carries bid metadata. Formats whose
    /// source revision varies are covered on more than one fixture — a single revision-1 case
    /// would pass just as well if extraction silently fell through to <c>DefaultRevision</c>.
    /// </summary>
    public static IEnumerable<object[]> Cases()
    {
        // Revisionless formats — the stored revision is always the default.
        yield return Case(ParserSlugs.NutanixSoftwareOnlyPdf, "XQ-9100002.pdf", "XQ-9100002", "1");
        yield return Case(ParserSlugs.NutanixSoftwareOnlyXlsx, "XQ-9100002.xlsx", "XQ-9100002", "1");
        yield return Case(ParserSlugs.NutanixRenewalPdf, "XQ-9100004.pdf", "XQ-9100004", "1");
        yield return Case(ParserSlugs.NutanixRenewalXlsx, "XQ-9100010.xlsx", "XQ-9100010", "1");
        yield return Case(ParserSlugs.HpOneConfigXlsx, "99000001.xlsx", "99000001", "1");
        yield return Case(ParserSlugs.HpServicesXlsx, "CH9000000002.xlsx", "CH9000000002", "1");
        yield return Case(ParserSlugs.CiscoCcwQuoteXls, "Quote_9400000001.xls", "9400000001", "1");
        yield return Case(ParserSlugs.DatalogicQuotePdf, "Datalogic_PE930003.pdf", "PE930003", "1");
        yield return Case(ParserSlugs.EpsonQuotePdf, "Epson_96000002.pdf", "96000002", "1");
        yield return Case(ParserSlugs.StrikeQuotePdf, "Strike_Quote_9202.pdf", "9202", "1");

        // Multi-quote Nutanix Hardware files: the bid number is Quote D's parent quote, never the
        // filename — the second fixture's filename carries trailing customer text.
        yield return Case(ParserSlugs.NutanixHardwareOnlyPdf, "XQ-9100003.pdf", "XQ-9100003", "1");
        yield return Case(ParserSlugs.NutanixHardwareOnlyXlsx, "XQ-9100003.xlsx", "XQ-9100003", "1");
        yield return Case(
            ParserSlugs.NutanixHardwareOnlyPdf,
            "XQ-9100008-Hardware-Sample.pdf",
            "XQ-9100008",
            "1");

        // Formats carrying a source revision.
        yield return Case(ParserSlugs.HpBidXlsx, "Deals_Sample_01_HPI.xlsx", "99010001", "2");
        yield return Case(ParserSlugs.HpBidXlsx, "Deals_Sample_02_HPI.xlsx", "99010002", "2");
        yield return Case(ParserSlugs.HpGlobalBidXlsx, "translate_quote_98000001_v25_all.xlsx", "98000001", "25");
        yield return Case(ParserSlugs.HpeBidXlsx, "HPE_Deal_9500000001_v2.xlsx", "9500000001", "2");
        yield return Case(ParserSlugs.HpeBidXlsx, "HPE_Deal_9500000002_v1.xlsx", "9500000002", "1");

        // Lenovo LBP-E: "Version#: 3" must survive as 3, not collapse to the default.
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "BRDAD019200001.xls", "BRDAD019200001", "1");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_01.xls", "BRDAD019200002", "3");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_02.xls", "BRDAD019200003", "1");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_03.xls", "BRDAD019200004", "2");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_04.xlsx", "BRDADTEST0001", "1");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_05.xlsx", "BRDADTEST0002", "3");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_06.xlsx", "BRDADTEST0003", "2");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_07.xlsx", "BRDADTEST0004", "1");
        yield return Case(ParserSlugs.LenovoLbpeIsgXls, "Bid_Platform_Bid_Request_Sample_08.xlsx", "BRDADTEST0005", "1");

        // Lenovo LBP-I: "V1" normalises to "1".
        yield return Case(ParserSlugs.LenovoLbpiIsgPdf, "BRDAS019000001V1.pdf", "BRDAS019000001", "1");

        // Zebra: revisions are not numbers — "2.0" and "1.0" stay as written.
        yield return Case(ParserSlugs.ZebraPcrPdf, "Zebra_PC_97000001_V2.0.pdf", "97000001", "2.0");
        yield return Case(ParserSlugs.ZebraPcrXls, "Zebra_PC_97000001.xls", "97000001", "2.0");
        yield return Case(ParserSlugs.ZebraPcrPdf, "Zebra_PC_97000003_V1.0.pdf", "97000003", "1.0");
        yield return Case(ParserSlugs.ZebraPcrXls, "Zebra_PC_97000003.xls", "97000003", "1.0");
        yield return Case(ParserSlugs.ZebraPcrPdf, "Zebra_PC_97000002_V2.0.pdf", "97000002", "2.0");
        yield return Case(ParserSlugs.ZebraPcrXls, "Zebra_PC_97000002.xls", "97000002", "2.0");
        yield return Case(ParserSlugs.ZebraPcrPdf, "Zebra_PC_97000004_V1.0.pdf", "97000004", "1.0");

        // Dell: from the response's quoteNumber/quoteVersion, not the submitted quote id.
        yield return Case(ParserSlugs.DellCtoJson, "Dell_CTO_Sample.json", "9000000000002", "1");
        yield return Case(ParserSlugs.DellAposJson, "Dell_APOS_Sample.json", "9000000000001", "1");
        yield return Case(ParserSlugs.DellCtoJson, "Dell_Peripherals_Sample.json", "9000000000003", "1");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_registered_parser_extracts_expected_bid_metadata(string slug, string filename, string bidNumber, string bidRevision)
    {
        var result = Parser(slug).Parse(Path.Combine(TestSample.Root, "samples", "inputs", filename));

        result.Metadata.BidNumber.Should().Be(bidNumber);
        result.Metadata.BidRevision.Should().Be(bidRevision);
    }

    /// <summary>
    /// An unreadable revision must not discard the bid number that was read, and must not cost the
    /// user the parse: the workbook content is unaffected by bid metadata.
    /// </summary>
    [Fact]
    public void Unreadable_revision_keeps_the_number_and_still_parses()
    {
        using var temp = new TempFile(FixtureWithout("Dell_CTO_Sample.json", "quoteVersion"));

        var result = Parser(ParserSlugs.DellCtoJson).Parse(temp.Path);

        result.Metadata.BidNumber.Should().Be("9000000000002");
        result.Metadata.BidRevision.Should().Be("1");
        result.LineItems.Should().NotBeEmpty();
    }

    /// <summary>
    /// The whole point of the nullable contract: a file whose bid anchors cannot be read still
    /// produces its line items. The label degrades, the deliverable does not.
    /// </summary>
    [Fact]
    public void Unreadable_bid_number_degrades_to_null_without_failing_the_parse()
    {
        using var temp = new TempFile(FixtureWithout("Dell_CTO_Sample.json", "quoteNumber"));

        var result = Parser(ParserSlugs.DellCtoJson).Parse(temp.Path);

        result.Metadata.BidNumber.Should().BeNull();
        result.Metadata.BidRevision.Should().BeNull();
        result.LineItems.Should().NotBeEmpty();
        result.Validation.ComputedTotal.Should().BeGreaterThan(0m);
    }

    /// <summary>Lenovo LBP-I splits the bid fields while QuoteNumber stays concatenated.</summary>
    [Fact]
    public void Lenovo_lbpi_keeps_the_concatenated_quote_number_alongside_the_split_bid_fields()
    {
        var path = Path.Combine(TestSample.Root, "samples", "inputs", "BRDAS019000001V1.pdf");

        var result = Parser(ParserSlugs.LenovoLbpiIsgPdf).Parse(path);

        result.Metadata.QuoteNumber.Should().Be("BRDAS019000001V1");
        result.Metadata.BidNumber.Should().Be("BRDAS019000001");
        result.Metadata.BidRevision.Should().Be("1");
    }

    private static IParser Parser(string slug) => new ParserRegistry().Parsers.Single(p => p.Slug == slug);

    /// <summary>Renames one JSON property in a fixture so the parser cannot find it.</summary>
    private static string FixtureWithout(string filename, string property)
    {
        var source = Path.Combine(TestSample.Root, "samples", "inputs", filename);
        return File.ReadAllText(source).Replace($"\"{property}\"", $"\"suppressed_{property}\"", StringComparison.Ordinal);
    }

    private static object[] Case(string slug, string filename, string bidNumber, string bidRevision) => [slug, filename, bidNumber, bidRevision];


    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-bid-metadata-test-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
        }

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}

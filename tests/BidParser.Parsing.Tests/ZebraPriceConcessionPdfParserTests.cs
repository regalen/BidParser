using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Characterisation tests for <c>ZebraPriceConcessionPdfParser</c>.
/// Values are derived directly from the three sample PDFs.
/// </summary>
public sealed class ZebraPriceConcessionPdfParserTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string Sample(string filename) =>
        Path.Combine(RepoRoot(), "samples", "inputs", filename);

    // ── PC# 81391641 — 3 items, all active ─────────────────────────────────────

    [Fact]
    public void File1_Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        result.Metadata.QuoteNumber.Should().Be("81391641");
        result.Metadata.Supplier.Should().Be(Vendors.Zebra);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.ZebraPriceConcessionPdf);
    }

    [Fact]
    public void File1_Validation_AlwaysMatches()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void File1_ItemCount_IsThree()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        result.LineItems.Should().HaveCount(3);
    }

    [Fact]
    public void File1_FirstItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        var item = result.LineItems[0];
        item.Vpn.Should().Be("DS8178-HCBU210MS5W");
        item.Qty.Should().Be(400);
        item.MinQty.Should().Be(1);
        item.Cost.Should().Be(475.86m);
        item.Msrp.Should().Be(1830.24m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("1");
        item.Description.Should().Contain("DS8178-HC FIPS");
    }

    [Fact]
    public void File1_SecondItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        var item = result.LineItems[1];
        item.Vpn.Should().Be("ZQ61-HAXAA04-00");
        item.Qty.Should().Be(400);
        item.Cost.Should().Be(719.37m);
        item.Msrp.Should().Be(1798.42m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("2");
    }

    [Fact]
    public void File1_ThirdItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81391641.pdf"));

        var item = result.LineItems[2];
        item.Vpn.Should().Be("Z1AE-ZQ6H-3C0");
        item.Qty.Should().Be(400);
        item.Cost.Should().Be(266.13m);
        item.Msrp.Should().Be(466.89m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("3");
    }

    // ── PC# 81422095 — 8 items (two-page PDF, page-break description split) ──────

    [Fact]
    public void File3_ItemCount_IsEight()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81422095.pdf"));

        result.LineItems.Should().HaveCount(8);
    }

    [Fact]
    public void File3_PageBreakItem_DescriptionIsComplete()
    {
        // ZD4AH22-D0PE00EZ's description starts on page 1 and continues on page 2.
        // The parser must merge the leading page-1 fragment with the page-2 Part No. row.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81422095.pdf"));

        var item = result.LineItems.Single(i => i.Vpn == "ZD4AH22-D0PE00EZ");
        item.Description.Should().Contain("Direct Thermal Printer ZD411");
        item.Description.Should().Contain("Bundle");
        item.Cost.Should().Be(417.99m);
        item.Qty.Should().Be(200);
    }

    [Fact]
    public void File3_Metadata_Currency_IsAUD()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        var result = parser.Parse(Sample("Zebra_PC_81422095.pdf"));

        result.Metadata.Currency.Should().Be("AUD");
    }

    // ── Template properties ─────────────────────────────────────────────────────

    [Fact]
    public void Parser_AvailableTemplates_AreNoCalcAndUplift()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPriceConcessionPdf);
        parser.AvailableTemplates.Should().BeEquivalentTo(
            [CrmTemplates.NoCalculation, CrmTemplates.Uplift],
            opts => opts.WithStrictOrdering());
    }
}

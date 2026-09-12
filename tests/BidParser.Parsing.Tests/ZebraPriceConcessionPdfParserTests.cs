using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Pdf;
using BidParser.Parsing.Registry;
using BidParser.Parsing.Zebra.PriceConcession;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Characterisation tests for <c>ZebraPriceConcessionPdfParser</c>.
/// Values are derived directly from the three sample PDFs.
/// </summary>
public sealed class ZebraPriceConcessionPdfParserTests
{

    private static string Sample(string filename) =>
        Path.Combine(TestSample.Root, "samples", "inputs", filename);

    // ── PC# 97000001 — 3 items, all active ─────────────────────────────────────

    [Fact]
    public void File1_Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        result.Metadata.QuoteNumber.Should().Be("97000001");
        result.Metadata.Supplier.Should().Be(Vendors.Zebra);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.ZebraPcrPdf);
    }

    [Fact]
    public void File1_Validation_AlwaysMatches()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void File1_ItemCount_IsThree()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        result.LineItems.Should().HaveCount(3);
    }

    [Fact]
    public void File1_FirstItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        var item = result.LineItems[0];
        item.Vpn.Should().Be("DS8178-HCBU210MS5W");
        item.Qty.Should().Be(1);
        item.MinQty.Should().Be(1);
        item.Comments.Should().Be("Max Qty: 400");
        item.Cost.Should().Be(475.86m);
        item.Msrp.Should().Be(1830.24m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("1");
        item.Description.Should().Contain("DS8178-HC FIPS");
    }

    [Fact]
    public void File1_SecondItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        var item = result.LineItems[1];
        item.Vpn.Should().Be("ZQ61-HAXAA04-00");
        item.Qty.Should().Be(1);
        item.Comments.Should().Be("Max Qty: 400");
        item.Cost.Should().Be(719.37m);
        item.Msrp.Should().Be(1798.42m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("2");
    }

    [Fact]
    public void File1_ThirdItem_FieldsAreCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        var item = result.LineItems[2];
        item.Vpn.Should().Be("Z1AE-ZQ6H-3C0");
        item.Qty.Should().Be(1);
        item.Comments.Should().Be("Max Qty: 400");
        item.Cost.Should().Be(266.13m);
        item.Msrp.Should().Be(466.89m);
        item.IsCancelled.Should().BeFalse();
        item.LineSequence.Should().Be("3");
    }

    // ── PC# 97000003 — 8 items (two-page PDF, page-break description split) ──────

    [Fact]
    public void File3_ItemCount_IsEight()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000003_V1.0.pdf"));

        result.LineItems.Should().HaveCount(8);
    }

    [Fact]
    public void File3_PageBreakItem_DescriptionIsComplete()
    {
        // ZD4AH22-D0PE00EZ's description starts on page 1 and continues on page 2.
        // The parser must merge the leading page-1 fragment with the page-2 Part No. row.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000003_V1.0.pdf"));

        var item = result.LineItems.Single(i => i.Vpn == "ZD4AH22-D0PE00EZ");
        item.Description.Should().Contain("Direct Thermal Printer ZD411");
        item.Description.Should().Contain("Bundle");
        item.Cost.Should().Be(417.99m);
        item.Qty.Should().Be(1);
        item.Comments.Should().Be("Max Qty: 200");
    }

    [Fact]
    public void File3_CentredDescriptions_AreNotSplitAcrossItems()
    {
        // Zebra centres the Part No. row on its description block, so a three-line
        // description puts one line ABOVE the Part No. row and one BELOW it. Each line
        // must land on the item it is centred on rather than bleeding into a neighbour.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000003_V1.0.pdf"));

        result.LineItems[0].Description.Should().Be(
            "3 yr Z1C Select, ZD5H, advanced replacement, Not available in LATAM / requires "
            + "Customer owned buffer in NA / Zebra owned buffer in EMEA & APAC, purchased in 30 days, "
            + "comprehensive, includes printhead coverage (Model-ZD510)");

        result.LineItems[1].Description.Should().Be(
            "DT Printer ZD510 Wristband; ZPL II, XML, 300 dpi, UK and AUS Power Cord, USB, USB Host, "
            + "Ethernet, 802.11, BT");

        // Single-line description sandwiched between two multi-line blocks.
        result.LineItems[2].Description.Should().Be("ASSY:GOOSENECK INTELLISTAND,HC,DS4308");
    }

    [Fact]
    public void File1_ShortGlyphWords_StayInReadingOrder()
    {
        // PdfPig's glyph-tight boxes drop words with no ascender or descender ("or a", "up",
        // "power") ~3.5pt below their neighbours. Grouping rows by top edge split them into a
        // separate row that re-joined in X order, scrambling the description.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000001_V2.0.pdf"));

        result.LineItems[1].Description.Should().Be(
            "DT Printer ZQ610 Plus 2\"/48mm Healthcare; English, Trad Chinese, Korean fonts, "
            + "Wi-Fi 6 Dual Radio (802.11AX / BT5.x), Linered platen, 0.75\" core, Group A, Belt clip");
    }

    [Fact]
    public void File3_Metadata_Currency_IsAUD()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        var result = parser.Parse(Sample("Zebra_PC_97000003_V1.0.pdf"));

        result.Metadata.Currency.Should().Be("AUD");
    }

    // ── PC# 97000004 — 13 items (fused-header, top-aligned-anchor layout) ─────

    [Fact]
    public void File4_ItemCount_IsThirteen()
    {
        var result = ParseFile4();
        result.LineItems.Should().HaveCount(13);
    }

    [Fact]
    public void File4_TopAlignedAnchor_KeepsTrailingDescriptionLine()
    {
        var item = ParseFile4().LineItems.Single(i => i.Vpn == "CRD-TC2L-SE1ET-01");
        item.Description.Should().EndWith(
            "Sold Separately: Power supply (PWR-BGA12V50W0WW), DC cable (CBL-DC-388A1-01), and AC line cord.");
    }

    [Fact]
    public void File4_NextItem_DoesNotStealPreviousTrailingLine()
    {
        var item = ParseFile4().LineItems.Single(i => i.Vpn == "PWR-BGA12V50W0WW");
        item.Description.Should().StartWith("Level VI efficiency AC / DC power supply brick");
    }

    [Fact]
    public void File4_FusedPartNumber_IsSplitAtColumnBoundary()
    {
        var item = ParseFile4().LineItems.Single(i => i.Vpn == "PWR-BGA12V108W0WW");
        item.Description.Should().Contain("VAC. It delivers 12V");
    }

    [Fact]
    public void File4_DescriptionWordsNearColumnEdge_AreNotClipped()
    {
        var item = ParseFile4().LineItems.Single(i => i.Vpn == "CBL-TC5X-USBC2A-01");
        item.Description.Should().Contain("approximately 1 meter");
    }

    [Fact]
    public void File4_CancelledFlag_LandsInItsOwnColumn()
    {
        var result = ParseFile4();
        result.LineItems.Should().OnlyContain(item => !item.IsCancelled && item.Msrp.HasValue);
    }

    // ── Column-grid resolution ───────────────────────────────────────────────
    // The grid is built from three sources — the header cells, the Y/N flag columns, and repeated
    // numeric left edges — because each one degrades on its own: this document's header cells are
    // fused into single words, and a one-item table has no repetition to measure.

    [Fact]
    public void SingleItemTable_StillResolvesEveryColumn()
    {
        var (words, header) = HeaderOf("Zebra_PC_97000004_V1.0.pdf");
        var oneItem = TrimToFirstItem(words, header);

        var columns = ZebraPriceConcessionPdfParser.BuildColumns(oneItem, header);

        // Measured column left edges for this document; the Description cell must end where the
        // first data column starts, or description text is silently dropped into the gap.
        columns["Description"].Right.Should().BeApproximately(555.75, 0.01);
        columns["Min. Qty"].Left.Should().BeApproximately(591.34, 0.01);
        columns["Max. Qty"].Left.Should().BeApproximately(609.86, 0.01);
        columns["List Price"].Left.Should().BeApproximately(630.86, 0.01);
        columns["Unit Special Price"].Left.Should().BeApproximately(741.94, 0.01);
        columns["Cancelled"].Left.Should().BeApproximately(772.95, 0.01);
    }

    [Fact]
    public void UnresolvableGrid_IsRejectedRatherThanParsedAsEmptyCells()
    {
        // Without the data columns there is nothing to place the boundaries against. A missing
        // column key would read as an empty cell — a zero price, an unset cancelled flag — so the
        // parser has to refuse the table instead of returning it.
        var (words, header) = HeaderOf("Zebra_PC_97000004_V1.0.pdf");
        var descriptionOnly = words.Where(word => word.X0 < 500).ToList();

        var act = () => ZebraPriceConcessionPdfParser.BuildColumns(descriptionOnly, header);

        act.Should().Throw<ParseError>().Where(error => error.Stage == "detect");
    }

    private static (List<PdfWord> Words, PdfWord Header) HeaderOf(string filename)
    {
        var words = PdfWordCollector.CollectWords(Sample(filename))
            .Where(word => word.Text.Trim().Length > 0)
            .ToList();
        var header = words.First(word => word.Text == "Part"
            && words.Any(next => next.Text == "No."
                && next.PageIndex == word.PageIndex
                && Math.Abs(next.Top - word.Top) <= 4
                && next.X0 > word.X0));
        return (words, header);
    }

    /// <summary>Everything above the second item's Part No. row — i.e. a one-item version of the table.</summary>
    private static List<PdfWord> TrimToFirstItem(List<PdfWord> words, PdfWord header)
    {
        var descriptionX0 = words.First(word => word.Text == "Description" && word.PageIndex == header.PageIndex).X0;
        var partNumbers = words
            .Where(word => word.PageIndex == header.PageIndex
                && word.Top > header.Top + 28
                && word.X0 >= header.X0 - 3
                && word.X0 < descriptionX0)
            .OrderBy(word => word.Top)
            .ToList();
        partNumbers.Should().HaveCountGreaterThan(1, "the fixture needs a second item to trim back to one");

        var cutoff = partNumbers[1].Top;
        return words.Where(word => word.PageIndex == header.PageIndex && word.Top < cutoff).ToList();
    }

    private static BidParser.Domain.Models.ParseResult ParseFile4()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        return parser.Parse(Sample("Zebra_PC_97000004_V1.0.pdf"));
    }

    // ── Template properties ─────────────────────────────────────────────────────

    [Fact]
    public void Parser_AvailableTemplates_AreNoCalcAndUplift()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.ZebraPcrPdf);
        parser.AvailableTemplates.Should().BeEquivalentTo(
            [CrmTemplates.NoCalculation, CrmTemplates.Uplift],
            opts => opts.WithStrictOrdering());
    }
}

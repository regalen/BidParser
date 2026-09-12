using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Lenovo.LbpiIdgPdf;
using BidParser.Parsing.Pdf;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class LenovoLbpiIdgPdfParserTests
{
    private const string LaptopSample = "BRPAS019100001V1.pdf";
    private const string DesktopMonitorSample = "BRPAS019100002V1.pdf";

    /// <summary>The only IDG quote sampled so far that carries no MTM recap table.</summary>
    private const string WorkstationSample = "BRPAS019100003V1.pdf";

    private static readonly string[] AllSamples = [LaptopSample, DesktopMonitorSample, WorkstationSample];

    private static readonly Dictionary<string, ParseResult> Results = [];
    private static readonly object ResultsLock = new();

    [Fact]
    public void MetadataAndParserSurface_AreCorrect()
    {
        var parser = Parser();
        var result = Parse(LaptopSample);

        parser.DisplayName.Should().Be("LBP-I IDG Quote (PDF)");
        parser.Vendor.Should().Be(Vendors.LenovoIdg);
        parser.OutputVendorName.Should().Be(Vendors.LenovoOutput);
        parser.AcceptedMime.Should().Be("application/pdf");
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.SupportsSolutionIdSplit.Should().BeFalse();

        result.Metadata.QuoteNumber.Should().Be("BRPAS019100001V1");
        result.Metadata.BidNumber.Should().Be("BRPAS019100001");
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.Supplier.Should().Be(Vendors.LenovoIdg);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.LenovoLbpiIdgPdf);
    }

    [Theory]
    [InlineData(LaptopSample, 13, 67, 80, "1455094.20")]
    [InlineData(DesktopMonitorSample, 3, 143, 146, "41060.30")]
    [InlineData(WorkstationSample, 1, 115, 116, "90796.00")]
    public void AllFixtures_ReconcileToQuotedTotal(
        string sample, int expectedParents, int expectedChildren, int expectedItems, string expectedTotalText)
    {
        var result = Parse(sample);
        var expectedTotal = decimal.Parse(expectedTotalText, System.Globalization.CultureInfo.InvariantCulture);

        Parents(result).Should().HaveCount(expectedParents);
        Children(result).Should().HaveCount(expectedChildren);
        result.LineItems.Should().HaveCount(expectedItems);
        result.Metadata.QuotedTotal.Should().Be(expectedTotal);
        result.Validation.QuotedTotal.Should().Be(expectedTotal);
        result.Validation.ComputedTotal.Should().Be(expectedTotal);
        result.Validation.Difference.Should().Be(0m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void AllFixtures_HaveNoSolutionIdsAndNoUnexpectedNulls()
    {
        foreach (var sample in AllSamples)
        {
            var result = Parse(sample);

            result.LineItems.Should().OnlyContain(item =>
                item.SolutionId == null && item.Comments == null && item.MinQty == null
                && item.Term == null && item.Msrp == null);
            result.LineItems.Should().OnlyContain(item => item.Vpn.Length > 0 && item.Qty > 0);
        }
    }

    [Fact]
    public void LineNumbering_IsNonContiguousButLineSequenceIsSequential()
    {
        var laptopParents = Parents(Parse(LaptopSample));
        laptopParents.Select(parent => parent.Raw["Line Item"])
            .Should().Equal("1", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14");
        laptopParents.Select(parent => parent.LineSequence)
            .Should().Equal("1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13");

        var desktopParents = Parents(Parse(DesktopMonitorSample));
        desktopParents.Select(parent => parent.Raw["Line Item"]).Should().Equal("1", "3", "4");
        desktopParents.Select(parent => parent.LineSequence).Should().Equal("1", "2", "3");
    }

    // The MTM recap lists the machine type model each product line resolves to. Lenovo omits the
    // whole table when no line resolves to one — this quote's only line is an unreleased CTO
    // placeholder — so the CONFIGURATION DETAILS grid runs straight into TERMS AND CONDITIONS.
    // Before the terms heading became a terminator in its own right this file failed to parse.
    [Fact]
    public void WorkstationFixture_ParsesWithNoMtmRecapTable()
    {
        var result = Parse(WorkstationSample);
        var parents = Parents(result);

        result.Metadata.QuoteNumber.Should().Be("BRPAS019100003V1");
        result.Metadata.BidNumber.Should().Be("BRPAS019100003");
        result.Metadata.BidRevision.Should().Be("1");

        parents.Should().ContainSingle();
        parents[0].Vpn.Should().Be("30HJCTO1WW");
        parents[0].Qty.Should().Be(5);
        parents[0].Cost.Should().Be(18159.20m);
        parents[0].Description.Should().Be("Workstation TS P8_PROM21_ES_TW_R");

        var children = Children(result);
        children.Should().HaveCount(115);
        children[0].Vpn.Should().Be("Country/Region");
        children[0].Description.Should().Be("Australia");
        children[^1].Vpn.Should().Be("Warranty");
        children[^1].Description.Should().Be("3 Year On-site");

        // The terms clauses sit immediately below the grid: reading past the heading would emit
        // them as component rows rather than failing, which is the failure this bounds.
        result.LineItems.Should().NotContain(item =>
            (item.Vpn + " " + item.Description).Contains("This quote is valid", StringComparison.Ordinal));
    }

    [Fact]
    public void LaptopFixture_FirstParentCarriesAllSixtySevenChildren()
    {
        var result = Parse(LaptopSample);
        var parents = Parents(result);

        parents[0].Vpn.Should().Be("21RSCTO1WW");
        parents[0].Qty.Should().Be(5);
        parents[0].Cost.Should().Be(5649.00m);
        parents[0].Description.Should().Be("Notebook ThinkPad P16v Gen 3 21RSCTO1WW");

        ChildrenOf(result, "1").Should().HaveCount(67);
        for (var i = 2; i <= 13; i++)
        {
            ChildrenOf(result, i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().BeEmpty();
        }
    }

    [Fact]
    public void NonAsciiGlyphs_AreStrippedFromCleanedFieldsButKeptInRaw()
    {
        var result = Parse(LaptopSample);

        result.LineItems.Should().OnlyContain(item =>
            (item.Vpn + item.Description).All(ch => ch >= (char)0x20 && ch <= (char)0x7E));

        var processor = result.LineItems.Single(item => item.Vpn == "Processor");
        processor.Description.Should().Be(
            "Intel Core Ultra 9 285H vPro Processor (E-cores up to 4.50 GHz P-cores up to 5.40 GHz)");

        // Raw keeps the original characters — only the cleaned LineItem fields are stripped.
        var parent = Parents(result).Single(p => p.Vpn == "21TD0018AU");
        parent.Raw["Description"].Should().Contain("Intel®").And.Contain("Core™").And.Contain("RTX™");
    }

    [Fact]
    public void HyphenBreak_RepairsAcrossWrappedLines()
    {
        var parent = Parents(Parse(LaptopSample)).Single(p => p.Vpn == "21TD0018AU");

        parent.Description.Should().Contain("4 Cell Li-ion 90Wh");
    }

    [Fact]
    public void WrappedComponentLabel_JoinsAroundACentredDescription()
    {
        var child = Parse(LaptopSample).LineItems.Single(item => item.Vpn == "Second Storage Selection");

        child.Description.Should().Be("No Storage Selection");
    }

    [Fact]
    public void LabelLessComponentRows_EmitSpecPlaceholder()
    {
        var result = Parse(DesktopMonitorSample);
        var specChildren = result.LineItems.Where(item => item.Vpn == "SPEC").ToList();

        specChildren.Should().HaveCount(6);
        specChildren.Select(item => item.Description).Should().Equal(
            "No OB M.2 SSD RAID",
            "No AI Agent",
            "No Build Assure",
            "No OB M.2 SSD Bracket",
            "OB G5 Heat Sink",
            "SPLITV001");

        // A SPEC placeholder is a display fallback, not a source value: Raw never fabricates a
        // "Components" key for a row whose source cell was genuinely blank.
        specChildren.Should().OnlyContain(item => !item.Raw.ContainsKey("Components"));
    }

    [Fact]
    public void PrintedChildQuantities_OverrideTheDefaultOfOne()
    {
        var laptop = Parse(LaptopSample).LineItems.Single(item => item.Vpn == "S5");
        laptop.Qty.Should().Be(5);
        laptop.Description.Should().Be("WARRANTY 3Y Premier Support");

        var desktop = Parse(DesktopMonitorSample).LineItems.Single(item => item.Vpn == "5WS0U26646");
        desktop.Qty.Should().Be(1);

        Children(Parse(LaptopSample)).Where(child => child.Vpn != "S5")
            .Should().OnlyContain(child => child.Qty == 1);
    }

    // The configurator prints a literal 0 on an unselected option slot, which means what the far
    // more common blank cell means. Passing it through would put a quantity-0 line in the CRM
    // workbook, so both spellings of "not selected" land on the same default.
    [Theory]
    [InlineData("", 1)]
    [InlineData("0", 1)]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    public void ZeroAndBlankComponentQuantities_BothDefaultToOne(string qtyText, int expected)
    {
        LenovoLbpiIdgPdfParser.ComponentQty(qtyText).Should().Be(expected);
    }

    [Fact]
    public void WorkstationFixture_PrintedZeroQuantitiesDoNotReachTheOutput()
    {
        // Seven rows in this fixture print "0": HDD Bay NVMe SSD, Second HDD Bay NVMe SSD,
        // Storage, Second Storage, Second Onboard M.2 SSD, Quad AIC M.2 SSD,
        // Second Quad AIC M.2 SSD.
        var result = Parse(WorkstationSample);

        result.LineItems.Should().OnlyContain(item => item.Qty > 0);
        result.LineItems.Single(item => item.Vpn == "HDD Bay NVMe SSD").Qty.Should().Be(1);
        result.LineItems.Single(item => item.Vpn == "Quad AIC M.2 SSD").Qty.Should().Be(1);
    }

    [Fact]
    public void AllChildren_AreZeroCost()
    {
        foreach (var sample in AllSamples)
        {
            Children(Parse(sample)).Should().OnlyContain(child => child.Cost == 0m);
        }
    }

    [Fact]
    public void StructuralRows_AreNotEmitted()
    {
        foreach (var sample in AllSamples)
        {
            var items = Parse(sample).LineItems;

            items.Should().NotContain(item =>
                item.Description != null && item.Description.Contains("Grand Total", StringComparison.OrdinalIgnoreCase));
            items.Should().NotContain(item =>
                item.Vpn == "Part" || item.Vpn == "Part Number" || item.Vpn == "Components" || item.Vpn == "#");
        }
    }

    // The header-repeat filter matches the Part Number cell exactly rather than by prefix. The
    // boundary case is a value that merely begins with the header's own first word: it must be
    // read as data, not skipped as structure.
    [Fact]
    public void HeaderRepeatFilter_KeepsARealPartNumberThatBeginsWithPart()
    {
        var rows = new List<PdfRow>
        {
            ProductRow(100.0, lineItem: "#", partNumber: "Part Number", qty: "Qty"),
            ProductRow(120.0, lineItem: "1", partNumber: "Part9001AU", qty: "2",
                unitPrice: "35.00", totalExcl: "70.00"),
            ProductRow(140.0, unitPrice: "Grand Total", totalExcl: "AUD 70.00")
        };

        var (lines, quotedTotal, currency) = LenovoLbpiIdgPdfParser.ExtractProductGrid(rows);

        lines.Should().ContainSingle().Which.VpnRaw.Should().Be("Part9001AU");
        quotedTotal.Should().Be(70.00m);
        currency.Should().Be("AUD");
    }

    private static PdfRow ProductRow(
        double midline,
        string lineItem = "",
        string partNumber = "",
        string qty = "",
        string unitPrice = "",
        string totalExcl = "")
        => new(0, midline, midline, new Dictionary<string, string>
        {
            ["Line Item"] = lineItem,
            ["Part Number"] = partNumber,
            ["Description"] = string.Empty,
            ["Qty"] = qty,
            ["Unit Price"] = unitPrice,
            ["Total Excl"] = totalExcl,
            ["Total Incl"] = string.Empty
        });

    [Theory]
    [InlineData(LaptopSample)]
    [InlineData(DesktopMonitorSample)]
    [InlineData(WorkstationSample)]
    public void Detect_ScoresHighOnItsOwnFixtures(string sample)
    {
        Parser().Detect(Path.Combine(TestSample.Root, "samples", "inputs", sample)).Should().BeGreaterThanOrEqualTo(0.9);
    }

    [Fact]
    public void NutanixPdf_IsRejectedAsWrongFileType()
    {
        var path = Path.Combine(TestSample.Root, "samples", "inputs", "XQ-9100002.pdf");

        var error = FluentActions.Invoking(() => Parser().Parse(path))
            .Should().Throw<ParseError>()
            .Which;

        error.Stage.Should().Be("detect");
    }

    private static BidParser.Domain.Abstractions.IParser Parser()
        => new ParserRegistry().Parsers.Single(parser => parser.Slug == ParserSlugs.LenovoLbpiIdgPdf);

    private static ParseResult Parse(string sample)
    {
        lock (ResultsLock)
        {
            if (!Results.TryGetValue(sample, out var result))
            {
                result = Parser().Parse(Path.Combine(TestSample.Root, "samples", "inputs", sample));
                Results[sample] = result;
            }
            return result;
        }
    }

    private static List<LineItem> Parents(ParseResult result)
        => result.LineItems.Where(item => !item.LineSequence!.Contains('.')).ToList();

    private static List<LineItem> Children(ParseResult result)
        => result.LineItems.Where(item => item.LineSequence!.Contains('.')).ToList();

    private static List<LineItem> ChildrenOf(ParseResult result, string parentSequence)
        => result.LineItems
            .Where(item => item.LineSequence!.StartsWith($"{parentSequence}.", StringComparison.Ordinal))
            .ToList();

}

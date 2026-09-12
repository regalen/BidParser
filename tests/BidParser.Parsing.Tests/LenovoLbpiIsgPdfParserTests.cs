using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class LenovoLbpiIsgPdfParserTests
{
    private const string FlatSample = "BRDAS019000004V1.pdf";
    private const string FlatSingleLineSample = "BRDAS019000005V1.pdf";
    private const string SingleSolutionSample = "BRDAS019000001V1.pdf";
    private const string TwoSolutionSample = "BRDAS019000003V1.pdf";
    private const string FiveSolutionSample = "BRDAS019000002V1.pdf";

    // Quotes whose CONFIGURATION DETAILS "Components" header is centred far right of its own
    // values — the layout that used to strand every component outside the column.
    private const string DriftedConfigHeaderSample = "BRDAS019000007V1.pdf";
    private const string DriftedConfigHeaderTwoSolutionSample = "BRDAS019000006V1.pdf";

    private static readonly Dictionary<string, ParseResult> Results = [];
    private static readonly object ResultsLock = new();

    [Fact]
    public void MetadataAndParserSurface_AreCorrect()
    {
        var parser = Parser();
        var result = Parse(FlatSample);

        parser.DisplayName.Should().Be("LBP-I ISG Quote (PDF)");
        parser.Vendor.Should().Be(Vendors.LenovoIsg);
        parser.AcceptedMime.Should().Be("application/pdf");
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.SupportsSolutionIdSplit.Should().BeTrue();

        result.Metadata.QuoteNumber.Should().Be("BRDAS019000004V1");
        result.Metadata.Supplier.Should().Be(Vendors.LenovoIsg);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(77545.95m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.LenovoLbpiIsgPdf);
    }

    [Theory]
    [InlineData(FlatSample, 3, 0, 3, "77545.95")]
    [InlineData(FlatSingleLineSample, 1, 0, 1, "38896.08")]
    [InlineData(SingleSolutionSample, 1, 65, 66, "640046.00")]
    [InlineData(TwoSolutionSample, 2, 148, 150, "393231.78")]
    [InlineData(FiveSolutionSample, 5, 401, 406, "3787260.72")]
    [InlineData(DriftedConfigHeaderSample, 1, 83, 84, "95588.19")]
    [InlineData(DriftedConfigHeaderTwoSolutionSample, 2, 94, 96, "154445.77")]
    public void AllFixtures_ReconcileToQuotedTotal(
        string sample,
        int expectedParents,
        int expectedChildren,
        int expectedItems,
        string expectedTotalText)
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

    [Theory]
    [InlineData(SingleSolutionSample)]
    [InlineData(TwoSolutionSample)]
    [InlineData(FiveSolutionSample)]
    [InlineData(DriftedConfigHeaderSample)]
    [InlineData(DriftedConfigHeaderTwoSolutionSample)]
    public void SolutionFixtures_CarrySolutionIdOnEveryLineAndCommentOnParentsOnly(string sample)
    {
        var result = Parse(sample);

        result.LineItems.Should().OnlyContain(item => item.SolutionId != null);
        Parents(result).Should().OnlyContain(parent => parent.Comments == $"Solution ID: {parent.SolutionId}");
        Children(result).Should().OnlyContain(child => child.Comments == null && child.Cost == 0m);
    }

    [Theory]
    [InlineData(FlatSample)]
    [InlineData(FlatSingleLineSample)]
    public void FlatFixtures_HaveNoSolutionIdsAndEveryLineIsAPricedParent(string sample)
    {
        var result = Parse(sample);

        result.LineItems.Should().OnlyContain(item =>
            item.SolutionId == null && item.Comments == null && !item.LineSequence!.Contains('.'));
    }

    [Fact]
    public void FlatFixture_ExtractsEachLinesOwnUnitPrice()
    {
        var parents = Parents(Parse(FlatSample));

        parents.Select(parent => new { parent.LineSequence, parent.Vpn, parent.Qty, parent.Cost })
            .Should().Equal(
                new { LineSequence = (string?)"1", Vpn = "4XB7A93897", Qty = 3, Cost = 5340.85m },
                new { LineSequence = (string?)"2", Vpn = "4X77A99752", Qty = 2, Cost = 2236.62m },
                new { LineSequence = (string?)"3", Vpn = "4XB7B07604", Qty = 2, Cost = 28525.08m });

        // Wrapped description lines sit above AND below the numbered row; they must reassemble
        // in reading order.
        parents[0].Description.Should().Be("ThinkSystem 2.5\" U.2 VA 3.2TB Mixed Use NVMe PCIe 4.0 x4 HS SSD");
    }

    [Fact]
    public void SingleSolutionFixture_SpreadsTheSolutionTotalAcrossTheParentQuantity()
    {
        var result = Parse(SingleSolutionSample);
        var parent = Parents(result).Single();

        // The SID row totals 640,046.00 for qty 20: cost = solution total / qty.
        parent.Vpn.Should().Be("7DGDCTO1WW");
        parent.Qty.Should().Be(20);
        parent.Cost.Should().Be(32002.30m);
        parent.SolutionId.Should().Be("SIDX02NX1S");
        parent.Comments.Should().Be("Solution ID: SIDX02NX1S");
        parent.Description.Should().Be(
            "ThinkSystem SR650 V4-3yr Base Warranty-ICON 2RU 32c - 2x 6530P 32C - 8x 32GB - 2x 960GB M.2 NVMe - 2x 10gbE - 5YR 24x7");

        // The parent's configuration components come first...
        result.LineItems.Single(item => item.LineSequence == "1.01").Should().BeEquivalentTo(
            new { Vpn = "C3QK", Description = "ThinkSystem SR650 V4 24x2.5\" Chassis", Qty = 1, Cost = 0m });

        // ...then each later numbered grid line is demoted to a child keeping its grid quantity,
        // followed by its own configuration components.
        var demoted = Children(result).Single(child => child.Vpn == "7S0XCTO6WW");
        demoted.Qty.Should().Be(20);
        demoted.Cost.Should().Be(0m);
        demoted.Description.Should().Be("XClarity One");

        // Configuration quantities are taken as printed (e.g. "Months" rows carry the term).
        Children(result).Where(child => child.Vpn == "QA0Y" && child.Qty == 60).Should().HaveCount(2);

        // Raw carries the source Description alongside identifiers and quantity, so a
        // reconstructed description can always be compared with the cleaned field.
        parent.Raw["Description"].Should().Be(parent.Description);
        demoted.Raw["Description"].Should().Be("XClarity One");
        result.LineItems.Single(item => item.LineSequence == "1.01")
            .Raw["Description"].Should().Be("ThinkSystem SR650 V4 24x2.5\" Chassis");
    }

    [Fact]
    public void TwoSolutionFixture_DividesEachSolutionTotalByItsParentQuantity()
    {
        var result = Parse(TwoSolutionSample);
        var parents = Parents(result);

        // 356,882.96 / 8 and 36,348.82 / 2 — the SID rows are qty 1, so the division is what
        // produces the per-unit parent cost.
        parents.Select(parent => new { parent.Vpn, parent.Qty, parent.Cost, parent.SolutionId })
            .Should().Equal(
                new { Vpn = "7DG9CTO1WW", Qty = 8, Cost = 44610.37m, SolutionId = (string?)"SIDX02Q2PL" },
                new { Vpn = "7DG9CTO1WW", Qty = 2, Cost = 18174.41m, SolutionId = (string?)"SIDX02Q2PM" });

        ChildrenOf(result, "1").Should().HaveCount(73);
        ChildrenOf(result, "2").Should().HaveCount(75);
    }

    [Fact]
    public void FiveSolutionFixture_HandlesPageBreakRepeatsAndContinuations()
    {
        var result = Parse(FiveSolutionSample);
        var parents = Parents(result);

        parents.Select(parent => new { parent.Vpn, parent.Qty, parent.Cost, parent.SolutionId })
            .Should().Equal(
                new { Vpn = "7DCVCTO1WW", Qty = 1, Cost = 888519.66m, SolutionId = (string?)"SIDX02Q2PI" },
                new { Vpn = "7DCVCTO1WW", Qty = 1, Cost = 888519.66m, SolutionId = (string?)"SIDX02Q2PG" },
                new { Vpn = "7DCVCTO1WW", Qty = 1, Cost = 888519.66m, SolutionId = (string?)"SIDX02Q2PH" },
                new { Vpn = "7DCVCTO1WW", Qty = 1, Cost = 888519.66m, SolutionId = (string?)"SIDX02Q2PK" },
                new { Vpn = "7DCSCTO1WW", Qty = 2, Cost = 116591.04m, SolutionId = (string?)"SIDX02Q2PJ" });

        ChildrenOf(result, "1").Should().HaveCount(94);
        ChildrenOf(result, "2").Should().HaveCount(94);
        ChildrenOf(result, "3").Should().HaveCount(94);
        ChildrenOf(result, "4").Should().HaveCount(94);
        ChildrenOf(result, "5").Should().HaveCount(25);

        // Line 14's description starts on one page and finishes on the next, after the repeated
        // Grand Total and column-header rows.
        parents[1].Description.Should().Be(
            "Lenovo ThinkSystem DM3010H Hybrid Flash Array -3 year-Copy_Primary Storage Controller");

        // A lone wrapped hyphen reports a bounding box low enough to fall into the next visual
        // row; it must be realigned so the description keeps its reading order.
        Children(result).Where(child => child.Vpn == "BXDL")
            .Should().HaveCount(4)
            .And.OnlyContain(child => child.Description ==
                "Lenovo ThinkSystem 46.1TB (6x 7.68TB, 2.5\", Non-SED, SSD) Drive Pack for DM3010H - Complete Bundle - ONTAP: Unified");
    }

    /// <summary>
    /// Regression: this quote centres the CONFIGURATION DETAILS "Components" header 11pt right of
    /// its own values, which used to strand every component in the No. column — the grid produced
    /// no sections, the output carried only the PRODUCT AND SERVICE DETAILS lines, and because
    /// children are zero-cost the quoted total still reconciled, so nothing flagged the loss.
    /// The whole child chain is pinned in order, not just its length.
    /// </summary>
    [Fact]
    public void DriftedConfigHeaderFixture_EmitsEveryConfigurationComponentInOrder()
    {
        var result = Parse(DriftedConfigHeaderSample);
        var parent = Parents(result).Single();
        var children = ChildrenOf(result, "1");

        parent.Vpn.Should().Be("7DGDCTO1WW");
        parent.Qty.Should().Be(3);
        parent.Cost.Should().Be(31862.73m);
        parent.SolutionId.Should().Be("SIDX02YUF5");

        children.Should().HaveCount(83);
        children[0].Vpn.Should().Be("C3QL");
        children[0].Description.Should().Be("ThinkSystem SR650 V4 12x3.5\" Chassis");

        // Section 1 runs 66 components deep before the grid's second numbered line takes over.
        children[65].Should().BeEquivalentTo(new { Vpn = "A2HP", Description = "Configuration ID 01", Qty = 1 });

        // The parent's components, then each later numbered line followed by its own components.
        children.Skip(66).Select(child => new { child.Vpn, child.Qty })
            .Should().Equal(
                new { Vpn = "5374CM1", Qty = 3 },
                new { Vpn = "BM31", Qty = 1 },
                new { Vpn = "A2JX", Qty = 1 },
                new { Vpn = "A2HP", Qty = 1 },
                new { Vpn = "7S0XCTO8WW", Qty = 3 },
                new { Vpn = "SCY0", Qty = 1 },
                new { Vpn = "7S0XCTO6WW", Qty = 3 },
                new { Vpn = "SCJE", Qty = 1 },
                new { Vpn = "7Q01CTS2WW", Qty = 3 },
                new { Vpn = "QAJY", Qty = 1 },
                new { Vpn = "QA18", Qty = 1 },
                new { Vpn = "QA0Y", Qty = 60 },
                new { Vpn = "QA11", Qty = 1 },
                new { Vpn = "7Q01CTSAWW", Qty = 3 },
                new { Vpn = "QAJY", Qty = 1 },
                new { Vpn = "QAK6", Qty = 1 },
                new { Vpn = "QA0Y", Qty = 60 });
    }

    [Fact]
    public void DriftedConfigHeaderTwoSolutionFixture_KeepsBothSolutionsComponentChains()
    {
        var result = Parse(DriftedConfigHeaderTwoSolutionSample);

        // The near-miss case: the old boundary landed 0.23pt right of the component codes.
        Parents(result).Select(parent => new { parent.Vpn, parent.Qty, parent.Cost, parent.SolutionId })
            .Should().Equal(
                new { Vpn = "7DG9CTO1WW", Qty = 2, Cost = 46888.53m, SolutionId = (string?)"SIDX02YUEQ" },
                new { Vpn = "7DCACTO1WW", Qty = 1, Cost = 60668.71m, SolutionId = (string?)"SIDX02YUER" });

        ChildrenOf(result, "1").Should().HaveCount(70);
        ChildrenOf(result, "2").Should().HaveCount(24);

        // An all-digit component code is a component, never a section number: only a code with a
        // line number in front of it opens a section.
        Children(result).Select(child => child.Vpn)
            .Should().Contain(["5977", "6400", "6201"]);
    }

    [Fact]
    public void StructuralRows_AreNotEmitted()
    {
        foreach (var sample in new[]
                 {
                     FlatSample, FlatSingleLineSample, SingleSolutionSample,
                     TwoSolutionSample, FiveSolutionSample,
                     DriftedConfigHeaderSample, DriftedConfigHeaderTwoSolutionSample
                 })
        {
            var items = Parse(sample).LineItems;

            items.Should().NotContain(item => item.Vpn.StartsWith("SID", StringComparison.Ordinal));
            items.Should().NotContain(item =>
                item.Description != null && item.Description.Contains("Grand Total", StringComparison.OrdinalIgnoreCase));
            items.Should().NotContain(item => item.Vpn == "Part" || item.Vpn == "Part Number" || item.Vpn == "Components");
            items.Should().OnlyContain(item => item.Vpn.Length > 0 && item.Qty > 0);
        }
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
        => new ParserRegistry().Parsers.Single(parser => parser.Slug == ParserSlugs.LenovoLbpiIsgPdf);

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

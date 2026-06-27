using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class HpeBidXlsxParserTests
{
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

    private static BidParser.Domain.Models.ParseResult Parse(string inputName)
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpeBidXlsx);
        return parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
    }

    // ── File 1: 1601962887 — 66 items (Bundle × 3, BundleDetails × 63) ──

    [Fact]
    public void File1_Metadata_IsCorrect()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        result.Metadata.QuoteNumber.Should().Be("1601962887");
        result.Metadata.Supplier.Should().Be(Vendors.Hpe);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpeBidXlsx);
    }

    [Fact]
    public void File1_ValidationMatchesWithNullTotal()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        // BundleDetails components contribute 0 (their price lives on the Bundle parent),
        // so the total is the sum of the three Bundle costs × qty (all qty 1).
        result.Validation.ComputedTotal.Should().Be(131713.36m);
    }

    [Fact]
    public void File1_TotalItemCount()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        result.LineItems.Should().HaveCount(66);
    }

    [Fact]
    public void File1_Bundle_TakesVpnFromBundleId_PricedFromListPrcEstAndOffering()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        // Item 1 is the first Bundle. VPN comes from BundleID (not ProductNumber); msrp from
        // ListPrcEst, cost from Offering, comment from MaxDealQty, qty from Quantity.
        var bundle = result.LineItems.First(i => i.LineSequence == "1");
        bundle.Vpn.Should().Be("52080474");
        bundle.Msrp.Should().Be(87995.00m);
        bundle.Cost.Should().Be(26632.75m);
        bundle.Qty.Should().Be(1);
        bundle.MinQty.Should().Be(1);
        bundle.Comments.Should().Be("Max Qty: 1");
    }

    [Fact]
    public void File1_BundleDetail_SequenceAndSentinelPricing()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        // First child of Bundle 1: VPN from ComponentID; msrp/cost dropped to 0 (the writer
        // emits the 0.0001 sentinel); qty from Quantity; no comment.
        var child1 = result.LineItems.First(i => i.LineSequence == "1.01");
        child1.Vpn.Should().Be("AK379B");
        child1.Msrp.Should().Be(0m);
        child1.Cost.Should().Be(0m);
        child1.Qty.Should().Be(1);
        child1.Comments.Should().BeNull();

        // Second child takes its qty from the Quantity column (2, not 1).
        var child2 = result.LineItems.First(i => i.LineSequence == "1.02");
        child2.Vpn.Should().Be("R6Q75A");
        child2.Qty.Should().Be(2);
    }

    [Fact]
    public void File1_BundlesOpenChildGroups_SequencesNest()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        // Three Bundle parents, sequenced 1, 2, 3.
        result.LineItems
            .Where(i => !i.LineSequence!.Contains('.'))
            .Select(i => (i.LineSequence, i.Vpn))
            .Should()
            .Equal(("1", "52080474"), ("2", "52079278"), ("3", "52079277"));

        // Every child sub-sequences under a parent (parent.NN).
        result.LineItems
            .Where(i => i.LineSequence!.Contains('.'))
            .Should()
            .OnlyContain(i => i.LineSequence!.StartsWith("1.")
                || i.LineSequence!.StartsWith("2.")
                || i.LineSequence!.StartsWith("3."));
    }

    [Fact]
    public void File1_OptionCodeIsNotConcatenatedIntoVpn()
    {
        var result = Parse("HPE_Deal_1601962887_v2.xlsx");

        // Unlike HP Bid, HPE VPNs are the base ID only — OptionCode is never appended.
        result.LineItems.Should().OnlyContain(i => !i.Vpn.Contains('#'));
    }

    [Fact]
    public void File1_AvailableTemplates()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpeBidXlsx);

        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.Vendor.Should().Be(Vendors.Hpe);
        parser.DisplayName.Should().Be("HPE Bid (XLSX)");
    }

    // ── File 2: 1602186424 — 4 Part Number rows only ──

    [Fact]
    public void File2_Metadata_IsCorrect()
    {
        var result = Parse("HPE_Deal_1602186424_v1.xlsx");

        result.Metadata.QuoteNumber.Should().Be("1602186424");
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
    }

    [Fact]
    public void File2_AllFourPartNumberItems()
    {
        var result = Parse("HPE_Deal_1602186424_v1.xlsx");

        result.LineItems.Should().HaveCount(4);

        // VPN from ProductNumber; msrp from ListPrcEst; cost from Offering; qty from Quantity;
        // comment from MaxDealQty. The third line has ListPrcEst/Offering of 0 (kept as 0 in
        // the model; the writer emits the sentinel on export).
        result.LineItems
            .Select(i => (i.LineSequence, i.Vpn, i.Msrp, i.Cost, i.Qty, i.MinQty, i.Comments))
            .Should()
            .Equal(
                ("1", "R8Q70A", (decimal?)20514.00m, 5128.50m, 5, 1, "Max Qty: 5"),
                ("2", "JL087A", (decimal?)2552.00m,   638.00m, 5, 1, "Max Qty: 5"),
                ("3", "JL087A", (decimal?)0m,           0m,    5, 1, "Max Qty: 5"),
                ("4", "JL669B", (decimal?)1106.00m,   276.50m, 5, 1, "Max Qty: 5"));
    }

    [Fact]
    public void File2_ComputedTotal_UsesQuantityColumn()
    {
        var result = Parse("HPE_Deal_1602186424_v1.xlsx");

        // qty derives from the Quantity column (5 for every line), so the total is
        // (5128.50 + 638.00 + 0 + 276.50) × 5 = 30215.00.
        result.Validation.ComputedTotal.Should().Be(30215.00m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void File2_NoBundleChildren_SequenceIsWholeNumbers()
    {
        var result = Parse("HPE_Deal_1602186424_v1.xlsx");

        result.LineItems.Should().OnlyContain(i => !i.LineSequence!.Contains('.'));
    }
}

using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class HpBidXlsxParserTests
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

    // ── File 1: 034809 — 479 items (Part Number × 3, Bundle × 14, Bundle Detail × 462) ──

    [Fact]
    public void File1_Metadata_IsCorrect()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("48035102");
        result.Metadata.Supplier.Should().Be(Vendors.Hp);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpBidXlsx);
    }

    [Fact]
    public void File1_ValidationMatchesWithNullTotal()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        // Bundle Detail components contribute 0 (their price lives on the Bundle parent),
        // so the computed total counts only Part Number and Bundle lines.
        result.Validation.ComputedTotal.Should().Be(14788828.99m);
    }

    [Fact]
    public void File1_TotalItemCount()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        result.LineItems.Should().HaveCount(479);
    }

    [Fact]
    public void File1_FirstThreeItems_ArePartNumbers()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        result.LineItems
            .Take(3)
            .Select(i => (i.LineSequence, i.Vpn, i.Cost, i.Qty, i.MinQty))
            .Should()
            .Equal(
                ("1", "9D9L6UT", 213.92m, 2012, 1),
                ("2", "9D9L6A9", 184.78m, 2012, 1),
                ("3", "9D9V7AA", 240m,    2012, 1));
    }

    [Fact]
    public void File1_FirstBundle_HasCorrectSequence()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        // Item 4 is the first Bundle
        var bundle = result.LineItems.First(i => i.LineSequence == "4");
        bundle.Vpn.Should().Be("55623728");
        bundle.Cost.Should().Be(2387.94m);
        bundle.Qty.Should().Be(880);
        bundle.MinQty.Should().Be(1);
    }

    [Fact]
    public void File1_BundleDetail_SequencePattern()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        // The first child of Bundle 4. Bundle Detail components carry no price (the
        // Bundle parent holds the total), so Cost is dropped to 0 — the writer then
        // emits the 0.000001 sentinel on export.
        var child1 = result.LineItems.First(i => i.LineSequence == "4.01");
        child1.Vpn.Should().Be("C89FGAV");
        child1.Cost.Should().Be(0m);
        child1.Qty.Should().Be(1);
        child1.MinQty.Should().Be(1);

        // The 6th child — has Option Code so vpn gets #-concatenated
        var child6 = result.LineItems.First(i => i.LineSequence == "4.06");
        child6.Vpn.Should().Be("4SS11AV#ABG");
    }

    [Fact]
    public void File1_OptionCode_ConcatenatedWithHash()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        // At least one item must have a # in its VPN (from Option Code concatenation)
        result.LineItems.Should().Contain(i => i.Vpn != null && i.Vpn.Contains('#'));
    }

    [Fact]
    public void File1_MinQty_ZeroSubstitutedWithOne()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        // All raw Min Order Qty values in this file are 0; after substitution they must all be 1
        result.LineItems.Should().OnlyContain(i => i.MinQty >= 1);
        result.LineItems.Should().NotContain(i => i.MinQty == 0);
    }

    [Fact]
    public void File1_MsrpIsAlwaysNull()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T034809_HPI.xlsx"));

        result.LineItems.Should().OnlyContain(i => i.Msrp == null);
    }

    [Fact]
    public void File1_AvailableTemplates()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
    }

    // ── File 2: 043243 — 5 Part Number rows only ──

    [Fact]
    public void File2_Metadata_IsCorrect()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("48034525");
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
    }

    [Fact]
    public void File2_AllFivePartNumberItems()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        result.LineItems.Should().HaveCount(5);

        result.LineItems
            .Select(i => (i.LineSequence, i.Vpn, i.Cost, i.Qty, i.MinQty))
            .Should()
            .Equal(
                ("1", "5TW10AA",  165.83m,  100, 1),
                ("2", "9D9S0UT",  336.70m,  100, 1),
                ("3", "BV2Q6PT", 3393.88m,  100, 1),
                ("4", "BQ4E3PT", 2258.18m,  500, 1),
                ("5", "BV8B6PT", 2160.00m,  250, 1));
    }

    [Fact]
    public void File2_ComputedTotal()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        result.Validation.ComputedTotal.Should().Be(2058731.00m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void File2_NoBundleChildren_SequenceIsWholeNumbers()
    {
        var root = RepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "Deals20260518T043243_HPI.xlsx"));

        // No dots in any LineSequence when there are no Bundle Detail rows
        result.LineItems.Should().OnlyContain(i => !i.LineSequence!.Contains('.'));
    }
}

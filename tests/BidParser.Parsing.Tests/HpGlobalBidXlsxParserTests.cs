using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class HpGlobalBidXlsxParserTests
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

    private static string SamplePath() =>
        Path.Combine(RepoRoot(), "samples", "inputs", "translate_quote_47500427_v25_all.xlsx");

    [Fact]
    public void Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.Metadata.QuoteNumber.Should().Be("47500427");
        result.Metadata.Supplier.Should().Be(Vendors.Hp);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpGlobalBidXlsx);
    }

    [Fact]
    public void Validation_MatchesTrueWithNullTotal()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.Difference.Should().Be(0m);
        result.Validation.ComputedTotal.Should().Be(34746055.00m);
    }

    [Fact]
    public void TotalItemCount_Is24()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.LineItems.Should().HaveCount(24);
    }

    [Fact]
    public void FirstFiveItems_HaveCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.LineItems
            .Take(5)
            .Select(i => (i.Vpn, i.Cost, i.Qty))
            .Should()
            .Equal(
                ("D95A8UC", 1900.95m, 2500),
                ("9E0G5AA",  347.56m, 1000),
                ("8X223AA",  161.51m,  100),
                ("9D9V7AA",  269.54m, 1000),
                ("A4LZ8AA", 5325.12m,   25));
    }

    [Fact]
    public void Comments_RemainingOnly_WhenNoTerm()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        // D95A8UC has no term — comments should be just the remaining qty
        var item = result.LineItems.First(i => i.Vpn == "D95A8UC");
        item.Comments.Should().Be("620 Remaining");
    }

    [Fact]
    public void Comments_CombinesTermAndRemaining_WhenTermPresent()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        // 8X223AA has Full term (Months) = 24 and Remaining qty = 20
        var item = result.LineItems.First(i => i.Vpn == "8X223AA");
        item.Comments.Should().Be("24 Months | 20 Remaining");
    }

    [Fact]
    public void ItemsWithTerm_AllHavePipeSeparatedComments()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        var termItems = result.LineItems.Where(i => i.Comments != null && i.Comments.Contains("Months")).ToList();
        termItems.Should().HaveCount(7);
        termItems.Should().OnlyContain(i => i.Comments!.Contains(" | ") && i.Comments.Contains("Remaining"));
    }

    [Fact]
    public void AvailableTemplates_AreNoCalculationAndUplift()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
    }

    [Fact]
    public void AllItems_HaveMsrpNull()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.LineItems.Should().OnlyContain(i => i.Msrp == null);
    }

    [Fact]
    public void AllItems_HaveNonEmptyVpnAndDescription()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpGlobalBidXlsx);

        var result = parser.Parse(SamplePath());

        result.LineItems.Should().OnlyContain(i => i.Vpn.Length > 0);
        result.LineItems.Should().OnlyContain(i => i.Description != null && i.Description.Length > 0);
    }
}

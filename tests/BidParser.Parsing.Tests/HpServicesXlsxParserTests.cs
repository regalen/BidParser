using BidParser.Domain.Constants;
using BidParser.Output;
using BidParser.Parsing.Hp.ServicesXlsx;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class HpServicesXlsxParserTests
{

    [Fact]
    public void CH9000000001_Extracts_Expected_LineItems_And_Totals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000001.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("CH9000000001");
        result.Metadata.BidNumber.Should().Be("CH9000000001");
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.Supplier.Should().Be(Vendors.Hp);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(11928.95m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpServicesXlsx);

        result.LineItems.Should().HaveCount(13);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().Be(11928.95m);
        result.Validation.ComputedTotal.Should().Be(11928.95m);
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void CH9000000002_Extracts_Expected_LineItems_And_Totals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000002.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("CH9000000002");
        result.Metadata.BidNumber.Should().Be("CH9000000002");
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.Supplier.Should().Be(Vendors.Hp);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(6432.00m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpServicesXlsx);

        result.LineItems.Should().HaveCount(17);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().Be(6432.00m);
        result.Validation.ComputedTotal.Should().Be(6432.00m);
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void CH9000000001_Tuple_Equality_Over_First_Few_Lines()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000001.xlsx"));

        result.LineItems.Take(3)
            .Select(i => (i.LineSequence, i.Vpn, i.Description, i.Cost, i.Qty, i.SerialNumber, i.StartDate, i.EndDate, i.SolutionId))
            .Should()
            .Equal(
                ("1", "HA151AC", "HP Hardware Maintenance Onsite Support - 8M0K5EC HP EB630G10 i5-1345U 13 16GB/512 PC (Warranty End Date: 26/07/2025)", 1008m, 1, "0PQ976P3PP", new DateOnly(2026, 4, 20), new DateOnly(2029, 4, 19), "1073 3019 2253"),
                ("2", "HA151AC", "HP Hardware Maintenance Onsite Support - 8M0K5EC HP EB630G10 i5-1345U 13 16GB/512 PC (Warranty End Date: 26/07/2025)", 1008m, 1, "0PQ976P3PQ", new DateOnly(2026, 4, 20), new DateOnly(2029, 4, 19), "1073 3019 2253"),
                ("3", "HA151AC", "HP Hardware Maintenance Onsite Support - 8M0K5EC HP EB630G10 i5-1345U 13 16GB/512 PC (Warranty End Date: 26/07/2025)", 1008m, 1, "0PQ976P3PS", new DateOnly(2026, 4, 20), new DateOnly(2029, 4, 19), "1073 3019 2253")
            );
    }

    [Fact]
    public void SkipRule_Drops_Summary_Rows()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result1 = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000001.xlsx"));
        var result2 = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000002.xlsx"));

        // No emitted line has a blank VPN or zero cost without hardware/serial
        result1.LineItems.Should().NotContain(i => string.IsNullOrEmpty(i.Vpn) && i.Cost == 0m);
        result2.LineItems.Should().NotContain(i => string.IsNullOrEmpty(i.Vpn) && i.Cost == 0m);

        // All 13 items in CH9000000001 and 17 items in CH9000000002 have non-empty VPN and non-zero Cost
        result1.LineItems.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Vpn) && i.Cost > 0m);
        result2.LineItems.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Vpn) && i.Cost > 0m);
    }

    [Fact]
    public void Priced_Service_Row_Is_Kept()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000001.xlsx"));
        var lastItem = result.LineItems.Last();

        lastItem.Vpn.Should().Be("UJ561AC");
        lastItem.Cost.Should().Be(2316.95m);
        lastItem.SerialNumber.Should().BeNull();
        lastItem.Description.Should().Be("HP PC & Notebook Return to HW Supp");
        lastItem.StartDate.Should().Be(new DateOnly(2026, 4, 20));
        lastItem.EndDate.Should().Be(new DateOnly(2026, 5, 19));
    }

    [Fact]
    public void Blank_Warranty_End_Date_Degrades_Gracefully()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000002.xlsx"));
        var a71Item = result.LineItems.Single(i => i.Description is not null && i.Description.Contains("A71DPPT"));

        a71Item.Description.Should().Be("HP Hardware Maintenance Onsite Support - A71DPPT HP ZBPG11 U7-155H 16 16GB/512 PC");
        a71Item.Description.Should().EndWith("512 PC");
        a71Item.Description.Should().NotContain("(Warranty End Date:");
    }

    [Fact]
    public void Split_Yields_Expected_Groups_And_Renumbered_Sequences()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpServicesXlsx);

        var result1 = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000001.xlsx"));
        var groups1 = SolutionOutputSplitter.Split(result1.LineItems);
        groups1.Should().HaveCount(1);
        groups1[0].SolutionId.Should().Be("1073 3019 2253");
        groups1[0].Items.Should().HaveCount(13);
        groups1[0].Items.Select(i => i.LineSequence).Should().Equal(
            Enumerable.Range(1, 13).Select(n => n.ToString())
        );

        var result2 = parser.Parse(Path.Combine(root, "samples", "inputs", "CH9000000002.xlsx"));
        var groups2 = SolutionOutputSplitter.Split(result2.LineItems);
        groups2.Should().HaveCount(7);
        groups2.Select(g => g.SolutionId).Should().Equal(
            "1073 3013 9550",
            "1073 3009 7589",
            "1073 3013 5249",
            "1073 3013 5309",
            "1073 3013 3523",
            "1073 3013 3693",
            "1073 3013 3753"
        );
        groups2.Select(g => g.Items.Count).Should().Equal(3, 1, 2, 7, 1, 1, 2);

        foreach (var g in groups2)
        {
            g.Items.Select(i => i.LineSequence).Should().Equal(
                Enumerable.Range(1, g.Items.Count).Select(n => n.ToString())
            );
        }
    }

    [Theory]
    // Day-first: this is an Australian HP export. "05/07/2026" is 5 July, never 7 May.
    [InlineData("05/07/2026", 2026, 7, 5)]
    [InlineData("26/07/2025", 2025, 7, 26)]
    [InlineData("5/7/2026", 2026, 7, 5)]
    [InlineData("2026-07-05", 2026, 7, 5)]
    public void ParseDayFirst_ReadsTextDatesDayFirst(string text, int year, int month, int day)
    {
        HpServicesXlsxParser.ParseDayFirst(text).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    // A month-first value that cannot also be read day-first must fail rather than
    // silently binding to the wrong month.
    [InlineData("07/26/2025")]
    public void ParseDayFirst_ReturnsNullForUnrecognisedText(string text)
    {
        HpServicesXlsxParser.ParseDayFirst(text).Should().BeNull();
    }
}

using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Parsing.Trellix.QuoteXlsm;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class TrellixQuoteXlsmParserTests
{
    private readonly TrellixQuoteXlsmParser parser = new();

    [Theory]
    [InlineData("Trellix_Quote_900003.xlsm", "Q-900003", 4, 99.99)]
    [InlineData("Trellix_Quote_900004.xlsm", "Q-900004", 1, 36.25)]
    public void Parses_synthetic_quote_and_validates_summed_line_totals(
        string filename, string quote, int lines, decimal total)
    {
        var result = parser.Parse(TestSample.Path(filename));

        parser.Slug.Should().Be(ParserSlugs.TrellixQuoteXlsm);
        parser.Vendor.Should().Be(Vendors.Trellix);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        ((IParser)parser).SupportsOnCost.Should().BeFalse();
        result.Metadata.QuoteNumber.Should().Be(quote);
        result.Metadata.BidNumber.Should().Be(quote);
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(total);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.TrellixQuoteXlsm);
        result.LineItems.Should().HaveCount(lines);
        result.LineItems.Select(item => item.LineSequence)
            .Should().Equal(Enumerable.Range(1, lines).Select(number => number.ToString()));
        result.Validation.Matches.Should().BeTrue();
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void Uses_displayed_dates_and_total_msrp_and_skips_placeholder()
    {
        var lines = parser.Parse(TestSample.Path("Trellix_Quote_900003.xlsm")).LineItems;

        lines[0].Vpn.Should().Be("TRX-SYN-001");
        lines[0].Qty.Should().Be(4);
        lines[0].Cost.Should().Be(12.50m);
        lines[0].Msrp.Should().Be(20m);
        lines[0].StartDate.Should().Be(new DateOnly(2026, 1, 15));
        lines[0].EndDate.Should().Be(new DateOnly(2026, 12, 31));
        lines[0].Comments.Should().Be("Subscription | 1.000 | GRANT-SYN-001");
        lines[0].Term.Should().BeNull();
        lines[0].Raw.Should().ContainKey("Channel SKU");

        // The source's second Start Date column has a date on these rows, but the displayed
        // first Start Date column is blank. The CRM must match the visible/PDF output.
        lines[1].StartDate.Should().BeNull();
        lines[1].Msrp.Should().Be(30m);
        lines[1].Comments.Should().Be("Renewal | 1.333");
        lines[2].StartDate.Should().BeNull();
        lines[2].EndDate.Should().BeNull();
        lines[2].Cost.Should().Be(0m);
        lines[2].Msrp.Should().Be(0m);
        lines[3].Qty.Should().Be(3);
        lines[3].SerialNumber.Should().Be("SN-SYN-004");
    }

    [Fact]
    public void Detect_requires_trellix_quote_table()
    {
        parser.Detect(TestSample.Path("Trellix_Quote_900003.xlsm")).Should().BeGreaterThan(0.7);
        parser.Detect(TestSample.Path("Trellix_Quote_900004.xlsm")).Should().BeGreaterThan(0.7);
        parser.Detect(TestSample.Path("XQ-9100002.xlsx")).Should().Be(0);
    }
}

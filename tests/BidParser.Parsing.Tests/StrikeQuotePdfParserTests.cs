using BidParser.Parsing.Strike.QuotePdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class StrikeQuotePdfParserTests
{
    private readonly StrikeQuotePdfParser parser = new();

    [Theory]
    [InlineData("Strike_Quote_9202.pdf", "9202", 4, 63585.00)]
    [InlineData("Strike_Quote_9201.pdf", "9201", 19, 61064.57)]
    [InlineData("Strike_Quote_9203.pdf", "9203", 2, 28820.70)]
    public void Parses_fixture(string filename, string quote, int count, decimal total)
    {
        var result = parser.Parse(TestSample.Path(filename));
        result.Metadata.QuoteNumber.Should().Be(quote);
        result.Metadata.BidNumber.Should().Be(quote);
        result.Metadata.BidRevision.Should().Be("1");
        result.LineItems.Should().HaveCount(count).And.OnlyContain(item => item.MinQty == 1 && item.Msrp == 0m);
        result.Validation.Should().BeEquivalentTo(new { ComputedTotal = total, QuotedTotal = (decimal?)total, Matches = true, Difference = 0m });
    }

    [Fact]
    public void Primary_fixture_joins_wrapped_codes()
    {
        var items = parser.Parse(TestSample.Path("Strike_Quote_9202.pdf")).LineItems;
        items.Select(item => item.Vpn).Should().Equal("CAS-STKPTA5P", "CAS-STKHTA5P", "ACC-STKTTA5P", "ACC-STKATTA5P");
        items.Select(item => item.Cost).Should().Equal(47.93m, 38.03m, 18.86m, 22.37m);
        items.Should().OnlyContain(item => item.Qty == 500);
        items[1].Raw["Product Code"].Should().Be("CAS-STKHTA5P");
    }

    [Fact]
    public void Real_spaces_in_product_code_are_preserved()
    {
        parser.Parse(TestSample.Path("Strike_Quote_9201.pdf")).LineItems[1].Vpn.Should().Be("CAS-STK APP IPAD AIR 11 2024 RGD HSL");
    }

    [Fact]
    public void Wrapped_price_is_concatenated()
    {
        parser.Parse(TestSample.Path("Strike_Quote_9203.pdf")).LineItems[0].Cost.Should().Be(319.50m);
    }

    [Fact]
    public void Primary_fixture_resolves_measured_median_column_boundaries()
    {
        var words = PdfWordCollector.CollectWords(TestSample.Path("Strike_Quote_9202.pdf"))
            .Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        var headerIndex = PdfTableHelpers.FindSequence(words, ["Product", "Code"])!.Value;
        var stopIndex = words.FindIndex(headerIndex, word => word.Text == "Delivery");
        var bodyIndex = words.FindIndex(headerIndex + 1, word => word.Text == "500");

        var columns = StrikeQuotePdfParser.BuildColumns(
            words, headerIndex, bodyIndex, stopIndex, words[headerIndex].PageWidth);

        columns.Keys.Should().Equal("Product Code", "Description", "QTY", "Price", "Value");
        columns["Description"].Left.Should().BeApproximately(125.70, 0.01);
        columns["QTY"].Left.Should().BeApproximately(413.35, 0.01);
        columns["Price"].Left.Should().BeApproximately(448.91, 0.01);
        columns["Value"].Left.Should().BeApproximately(490.44, 0.01);
    }
}

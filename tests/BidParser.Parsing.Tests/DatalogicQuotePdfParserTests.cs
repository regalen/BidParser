using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Datalogic.QuotePdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class DatalogicQuotePdfParserTests
{
    private readonly DatalogicQuotePdfParser parser = new();

    [Theory]
    [InlineData("Datalogic_PE930003.pdf", "PE930003", "9300000003", 8, 1, 60595.50)]
    [InlineData("Datalogic_PE930001.pdf", "PE930001", "9300000001", 3, 10, 209397.00)]
    [InlineData("Datalogic_PE930002.pdf", "PE930002", "9300000002", 1, 1, 2625.45)]
    public void Parses_fixture(string filename, string bid, string quote, int count, int minQty, decimal total)
    {
        var result = parser.Parse(TestSample.Path(filename));
        result.Metadata.BidNumber.Should().Be(bid);
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.QuoteNumber.Should().Be(quote);
        result.Metadata.Currency.Should().Be("AUD");
        result.LineItems.Should().HaveCount(count).And.OnlyContain(item => item.MinQty == minQty);
        result.Validation.Should().BeEquivalentTo(new { ComputedTotal = total, QuotedTotal = (decimal?)total, Matches = true, Difference = 0m });
    }

    [Fact]
    public void Primary_fixture_extracts_every_field()
    {
        var items = parser.Parse(TestSample.Path("Datalogic_PE930003.pdf")).LineItems;
        items[0].Should().BeEquivalentTo(new { Vpn = "944950003", Description = "MEMOR 17 051AC65AW2FT2AN GMS", Qty = 50, MinQty = (int?)1, Msrp = (decimal?)2033m, Cost = 731.88m, LineSequence = "1" });
        items[1].Description.Should().Be("M3X, M12-17,SSD, Wired - Charge Only");
        items[2].Description.Should().Be("SKORPIO X5 CABLE USB A - USB TYPE-C");
        items[^1].Should().BeEquivalentTo(new { Vpn = "ZSCFMEM1731", Description = "MEMOR 17, FLEXI, 3 YEARS, COMPREHENSIVE", Qty = 50, Msrp = (decimal?)305m, Cost = 183m, LineSequence = "8" });
    }

    [Fact]
    public void Wrapped_part_numbers_are_concatenated()
    {
        var items = parser.Parse(TestSample.Path("Datalogic_PE930001.pdf")).LineItems;
        items[1].Vpn.Should().Be("GD4690-BKK1B-HP");
        items[2].Vpn.Should().Be("GD4620-BKK1B-HD");
        var single = parser.Parse(TestSample.Path("Datalogic_PE930002.pdf")).LineItems.Single();
        single.Vpn.Should().Be("MG1501-10211-0200");
        single.Raw["Part Number"].Should().Be("MG1501-10211-0200");
    }

    [Fact]
    public void Parse_rejects_non_aud_currency_from_the_document_header()
    {
        var path = TestSample.Path("Datalogic_PE930003.pdf");
        var words = PdfWordCollector.CollectWords(path).ToList();
        var currencyLabelIndex = words.FindIndex(word => word.Text == "Currency:");
        var currencyIndex = words.FindIndex(currencyLabelIndex + 1, word => word.Text == "AUD");
        currencyLabelIndex.Should().BeGreaterThanOrEqualTo(0, "the fixture must exercise the Currency header regex");
        currencyIndex.Should().BeGreaterThan(currencyLabelIndex);
        words[currencyIndex] = words[currencyIndex] with { Text = "USD" };
        var parserWithUsdDocument = new DatalogicQuotePdfParser(_ => words);

        var action = () => parserWithUsdDocument.Parse(path);

        action.Should().Throw<ParseError>().Which.Stage.Should().Be("currency");
    }

    [Fact]
    public void Parse_reports_missing_numeric_cells_as_typed_extraction_errors()
    {
        var path = TestSample.Path("Datalogic_PE930003.pdf");
        var words = PdfWordCollector.CollectWords(path).ToList();
        var priceIndex = words.FindIndex(word => word.Text == "$731.88");
        priceIndex.Should().BeGreaterThanOrEqualTo(0);
        words[priceIndex] = words[priceIndex] with { Text = "unreadable" };
        var parserWithUnreadablePrice = new DatalogicQuotePdfParser(_ => words);

        var action = () => parserWithUnreadablePrice.Parse(path);

        action.Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    [Fact]
    public void Primary_fixture_resolves_measured_column_boundaries()
    {
        var words = PdfWordCollector.CollectWords(TestSample.Path("Datalogic_PE930003.pdf"))
            .Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        var headerIndex = PdfTableHelpers.FindSequence(words, ["ID", "#", "Part", "Number"])!.Value;
        var stopIndex = words.FindIndex(headerIndex, word => word.Text == "Grand");
        var bodyIndex = words.FindIndex(headerIndex + 4,
            word => word.Top > words[headerIndex].Top && int.TryParse(word.Text, out _));

        var columns = DatalogicQuotePdfParser.BuildColumns(
            words, headerIndex, bodyIndex, stopIndex, words[headerIndex].PageWidth);

        columns["Part Number"].Left.Should().BeApproximately(93.60, 0.01);
        columns["Description"].Left.Should().BeApproximately(159.86, 0.01);
        columns["Qty"].Left.Should().BeApproximately(281.54, 0.01);
        columns["List Price"].Left.Should().BeApproximately(315.75, 0.01);
        columns["Std. Disc."].Left.Should().BeApproximately(368.90, 0.01);
        columns["Target Disc."].Left.Should().BeApproximately(407.90, 0.01);
        columns["Target Unit Price"].Left.Should().BeApproximately(443.35, 0.01);
        columns["Total Value"].Left.Should().BeApproximately(493.48, 0.01);
    }
}

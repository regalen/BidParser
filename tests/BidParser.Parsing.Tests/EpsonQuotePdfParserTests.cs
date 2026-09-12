using BidParser.Parsing.Epson.QuotePdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class EpsonQuotePdfParserTests
{
    private readonly EpsonQuotePdfParser parser = new();

    [Theory]
    [InlineData("Epson_96000002.pdf", "96000002", 2, 450)]
    [InlineData("Epson_96000001.pdf", "96000001", 1, 200)]
    [InlineData("Epson_96000003.pdf", "96000003", 1, 2500)]
    public void Parses_fixture(string filename, string contract, int count, int qty)
    {
        var result = parser.Parse(TestSample.Path(filename));
        result.Metadata.QuoteNumber.Should().Be(contract);
        result.Metadata.BidNumber.Should().Be(contract);
        result.Metadata.BidRevision.Should().Be("1");
        result.LineItems.Should().HaveCount(count).And.OnlyContain(item => item.Qty == qty && item.MinQty == 1 && item.Msrp == 0m);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void Primary_fixture_keeps_model_raw_and_maps_description()
    {
        var items = parser.Parse(TestSample.Path("Epson_96000002.pdf")).LineItems;
        items[0].Should().BeEquivalentTo(new { Vpn = "C31CK50202", Description = "USB/Ethernet Thermal Printer - Black", Cost = 275m, Qty = 450 });
        items[0].Raw["Model"].Should().Be("TM-M30III-202");
        items[1].Description.Should().Be("Built-in USB, Parallel, Front facing receipt printer");
        items[1].Raw["Product Description"].Should().Be("Built-in USB, Parallel, Front facing receipt printer");
    }

    [Fact]
    public void Split_currency_token_and_thousands_quantity_are_parsed()
    {
        var item = parser.Parse(TestSample.Path("Epson_96000003.pdf")).LineItems.Single();
        item.Qty.Should().Be(2500);
        item.Cost.Should().Be(231.42m);
    }

    [Theory]
    [InlineData("B12B12345")]
    [InlineData("S01512345")]
    public void Parse_starts_a_new_item_for_any_valid_Epson_product_code(string productCode)
    {
        var path = TestSample.Path("Epson_96000002.pdf");
        var words = PdfWordCollector.CollectWords(path).ToList();
        var secondCode = parser.Parse(path).LineItems[1].Vpn;
        var (codeIndex, codeWordCount) = FindWordSequence(words, secondCode);
        var lastCodeWord = words[codeIndex + codeWordCount - 1];
        words[codeIndex] = words[codeIndex] with { Text = productCode, X1 = lastCodeWord.X1 };
        for (var i = 1; i < codeWordCount; i++)
            words[codeIndex + i] = words[codeIndex + i] with { Text = string.Empty };
        var parserWithAlternateSku = new EpsonQuotePdfParser(_ => words);

        var items = parserWithAlternateSku.Parse(path).LineItems;

        items.Should().HaveCount(2);
        items[1].Vpn.Should().Be(productCode);
        items[1].Cost.Should().Be(275m);
    }

    [Fact]
    public void Primary_fixture_resolves_measured_column_boundaries()
    {
        var words = PdfWordCollector.CollectWords(TestSample.Path("Epson_96000002.pdf"))
            .Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        var headerIndex = PdfTableHelpers.FindSequence(words, ["Epson", "Product", "Code"])!.Value;
        var stopIndex = words.FindIndex(headerIndex, word => word.Text.StartsWith("Terms", StringComparison.OrdinalIgnoreCase));
        var bodyIndex = words.FindIndex(headerIndex + 1, word => word.Text == "C31CK50202");

        var columns = EpsonQuotePdfParser.BuildColumns(
            words, headerIndex, bodyIndex, stopIndex, words[headerIndex].PageWidth);

        columns["Model"].Left.Should().BeInRange(160, 180);
        columns["Product Description"].Left.Should().BeInRange(245, 270);
        columns["Price per unit ($AUD ex GST)"].Left.Should().BeInRange(460, 490);
    }

    private static (int Start, int Count) FindWordSequence(IReadOnlyList<PdfWord> words, string value)
    {
        for (var start = 0; start < words.Count; start++)
        {
            var joined = string.Empty;
            for (var end = start; end < words.Count && end - start < value.Length; end++)
            {
                if (words[end].PageIndex != words[start].PageIndex || Math.Abs(words[end].Top - words[start].Top) > 3.5)
                    break;

                joined += words[end].Text;
                if (joined == value) return (start, end - start + 1);
                if (!value.StartsWith(joined, StringComparison.Ordinal)) break;
            }
        }

        throw new InvalidOperationException($"Could not find PDF word sequence for '{value}'.");
    }
}

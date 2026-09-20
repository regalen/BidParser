using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Pdf;
using BidParser.Parsing.Trellix.QuotePdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class TrellixQuotePdfParserTests
{
    private readonly TrellixQuotePdfParser parser = new();

    [Theory]
    [InlineData("Trellix_Quote_900001.pdf", "Q-900001", 9, 1546.05)]
    [InlineData("Trellix_Quote_900002.pdf", "Q-900002", 2, 50.00)]
    public void Parses_synthetic_report_and_validates_distribution_total(
        string filename, string quote, int lines, decimal total)
    {
        var result = parser.Parse(TestSample.Path(filename));

        parser.Slug.Should().Be(ParserSlugs.TrellixQuotePdf);
        parser.Vendor.Should().Be(Vendors.Trellix);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        ((IParser)parser).SupportsOnCost.Should().BeFalse();
        result.Metadata.Should().BeEquivalentTo(new
        {
            QuoteNumber = quote, BidNumber = quote, BidRevision = "1", Supplier = Vendors.Trellix,
            Currency = "AUD", QuotedTotal = (decimal?)total, SourceFilename = filename,
            ParserSlug = ParserSlugs.TrellixQuotePdf
        });
        result.LineItems.Should().HaveCount(lines);
        result.LineItems.Select(item => item.LineSequence)
            .Should().Equal(Enumerable.Range(1, lines).Select(number => number.ToString()));
        result.Validation.Should().BeEquivalentTo(new
        {
            ComputedTotal = total, QuotedTotal = (decimal?)total, Matches = true, Difference = 0m
        });
    }

    [Fact]
    public void Primary_fixture_maps_wrapped_fields_and_unit_msrp()
    {
        var items = parser.Parse(TestSample.Path("Trellix_Quote_900001.pdf")).LineItems;
        items[0].Should().BeEquivalentTo(new
        {
            Vpn = "SYNTH001", Description = "Test Cloud Plan TE", Qty = 10,
            Cost = 12.34m, Msrp = (decimal?)20m,
            StartDate = (DateOnly?)new DateOnly(2026, 10, 1),
            EndDate = (DateOnly?)new DateOnly(2027, 9, 30),
            SerialNumber = (string?)null, Term = (int?)null,
            Comments = "Support Renewal | 1.000 | 9000001-NAI"
        });
        items[0].Raw["Channel SKU"].Should().Contain("SYNTH").And.Contain("001");
        items[0].Raw["Product Description"].Should().Be("Test Cloud Plan TE");
        items[0].Raw["Terms Length"].Should().Be("1.000");
        items[0].Raw["QTY Software of support (Nodes)"].Should().Be("10");
        items[0].Raw["QTY Hardware"].Should().Be("0");
        items[0].Raw["Cost Per Unit"].Should().Be("12.34");
        items[0].Raw["Total MSRP"].Should().Be("200.00");
        items[0].Raw["Grant #s"].Should().Contain("9000001-").And.Contain("NAI");
        items[0].Raw["Program Type"].Should().Be("Support Renewal");
        items[7].Should().BeEquivalentTo(new { Cost = 1080.40m, Msrp = (decimal?)0m, Qty = 1,
            Comments = "Upgrade/Crossgrade | 1.121" });
        items[8].Should().BeEquivalentTo(new { Vpn = "SYNTH009", Cost = 0m, Msrp = (decimal?)0m,
            Qty = 3, LineSequence = "9", Comments = "New | 1.121" });
    }

    [Fact]
    public void Alternate_layout_uses_hardware_quantity_and_optional_fields()
    {
        var items = parser.Parse(TestSample.Path("Trellix_Quote_900002.pdf")).LineItems;
        items[0].Should().BeEquivalentTo(new
        {
            Vpn = "HW100", Description = "Test Device Hardware", Qty = 3,
            Msrp = (decimal?)25m, Cost = 10m, SerialNumber = "SN-02",
            StartDate = (DateOnly?)new DateOnly(2026, 3, 8), EndDate = (DateOnly?)null,
            Comments = "9000002-NAI"
        });
        items[0].Raw["Channel SKU"].Should().Be("HW 100");
        items[0].Raw["Latest Serial Number"].Should().Be("SN-02");
        items[0].Raw["QTY Hardware"].Should().Be("3");
        items[1].Should().BeEquivalentTo(new
        {
            Vpn = "SW200", Qty = 4, Msrp = (decimal?)30m, Cost = 5m,
            StartDate = (DateOnly?)new DateOnly(2027, 3, 8),
            EndDate = (DateOnly?)new DateOnly(2029, 3, 7),
            SerialNumber = (string?)null, Comments = "Support Renewal | 2.000"
        });
    }

    [Fact]
    public void Channel_sku_removes_leading_trailing_and_internal_whitespace()
    {
        var changed = ChangeAlternate(word => word.Text == "HW", "  HW  X  ");
        changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"))
            .LineItems[0].Vpn.Should().Be("HWX100");
    }

    [Fact]
    public void Both_positive_quantity_fields_fail()
    {
        var changed = ChangeAlternate(word => word.PageIndex == 1 && word.Text == "0"
            && word.X0 > 480 && word.X0 < 520 && word.Top > 200 && word.Top < 250, "4");
        var action = () => changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        action.Should().Throw<ParseError>().Which.Should().BeEquivalentTo(new { Stage = "extract" });
        action.Should().Throw<ParseError>().Which.Hint.Should().Contain("both Trellix quantity fields");
    }

    [Fact]
    public void No_positive_quantity_fails()
    {
        var changed = ChangeAlternate(word => word.PageIndex == 1 && word.Text == "3"
            && word.X0 > 440 && word.X0 < 480 && word.Top > 200 && word.Top < 250, "0");
        var action = () => changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        action.Should().Throw<ParseError>().Which.Hint.Should().Contain("no valid Trellix quantity");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("unreadable")]
    public void Invalid_quantities_fail_as_typed_extraction_errors(string replacement)
    {
        var changed = ChangeAlternate(word => word.PageIndex == 1 && word.Text == "3"
            && word.X0 > 440 && word.X0 < 480 && word.Top > 200 && word.Top < 250, replacement);
        var action = () => changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        action.Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    [Fact]
    public void All_missing_comment_parts_leave_comments_null()
    {
        var path = TestSample.Path("Trellix_Quote_900002.pdf");
        var words = PdfWordCollector.CollectWords(path).ToList();
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].PageIndex == 1 && words[index].Top > 200 && words[index].Top < 260
                && words[index].X0 is > 560 and < 606)
                words[index] = words[index] with { Text = string.Empty };
        }
        var result = new TrellixQuotePdfParser(_ => words).Parse(path);
        result.LineItems[0].Comments.Should().BeNull();
    }

    [Theory]
    [InlineData("50.01", true, 0.01)]
    [InlineData("50.02", false, 0.02)]
    public void Distribution_total_uses_shared_tolerance(string quoted, bool matches, decimal difference)
    {
        var changed = ChangeAlternate(word => word.Text == "50.00", quoted);
        var result = changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        result.Metadata.QuotedTotal.Should().Be(decimal.Parse(quoted, System.Globalization.CultureInfo.InvariantCulture));
        result.Validation.Matches.Should().Be(matches);
        result.Validation.Difference.Should().Be(-difference);
    }

    [Fact]
    public void Currency_is_validated_and_quote_number_is_best_effort()
    {
        var path = TestSample.Path("Trellix_Quote_900002.pdf");
        var changedCurrency = ChangeAlternate(word => word.Text == "AUD", "USD");
        var action = () => changedCurrency.Parse(path);
        action.Should().Throw<ParseError>().Which.Stage.Should().Be("currency");

        var missingCurrency = ChangeAlternate(word => word.Text == "AUD", string.Empty);
        Action missingAction = () => missingCurrency.Parse(path);
        missingAction.Should().Throw<ParseError>().Which.Stage.Should().Be("currency");

        var words = PdfWordCollector.CollectWords(path)
            .Select(word => word.Text == "Q-900002" ? word with { Text = "?" } : word)
            .ToList();
        var withoutQuote = new TrellixQuotePdfParser(_ => words).Parse(path);
        withoutQuote.Metadata.QuoteNumber.Should().BeEmpty();
        withoutQuote.Metadata.BidNumber.Should().BeNull();
        withoutQuote.Metadata.BidRevision.Should().BeNull();
    }

    [Fact]
    public void Missing_distribution_total_is_a_typed_totals_error()
    {
        var changed = ChangeAlternate(word => word.Text == "50.00", "unreadable");
        var action = () => changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        action.Should().Throw<ParseError>().Which.Stage.Should().Be("totals");
    }

    [Fact]
    public void Malformed_cost_is_a_typed_extraction_error()
    {
        var changed = ChangeAlternate(word => word.PageIndex == 1 && word.Text == "10.00"
            && word.X0 > 750 && word.X0 < 785 && word.Top > 200 && word.Top < 250, "unreadable");
        var action = () => changed.Parse(TestSample.Path("Trellix_Quote_900002.pdf"));
        action.Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    [Theory]
    [InlineData("Trellix_Quote_900001.pdf", 238, 314, 598)]
    [InlineData("Trellix_Quote_900002.pdf", 238, 314, 560)]
    public void Header_geometry_resolves_source_columns(string filename, double terms, double start, double grant)
    {
        var words = PdfWordCollector.CollectWords(TestSample.Path(filename)).ToList();
        var page = words.Where(word => word.PageIndex == 1).ToList();
        var header = page.Single(word => word.Text == "Channel" && word.Top > 100 && word.Top < 200);
        var columns = TrellixQuotePdfParser.BuildColumns(page, header);
        columns["Terms Length"].Left.Should().BeApproximately(terms, 1);
        columns["Start Date"].Left.Should().BeApproximately(start, 1);
        columns["Grant #s"].Left.Should().BeApproximately(grant, 1);
        columns["Product Description"].Right.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Detect_requires_trellix_identity_and_table_signature()
    {
        parser.Detect(TestSample.Path("Trellix_Quote_900001.pdf")).Should().BeGreaterThan(0.7);
        parser.Detect(TestSample.Path("Trellix_Quote_900002.pdf")).Should().BeGreaterThan(0.7);
        parser.Detect(TestSample.Path("Datalogic_PE930003.pdf")).Should().Be(0);
    }

    private static TrellixQuotePdfParser ChangeAlternate(Func<PdfWord, bool> predicate, string replacement)
    {
        var words = PdfWordCollector.CollectWords(TestSample.Path("Trellix_Quote_900002.pdf")).ToList();
        var index = words.FindIndex(word => predicate(word));
        index.Should().BeGreaterThanOrEqualTo(0, "the synthetic fixture must retain the target PDF token");
        words[index] = words[index] with { Text = replacement };
        return new TrellixQuotePdfParser(_ => words);
    }
}

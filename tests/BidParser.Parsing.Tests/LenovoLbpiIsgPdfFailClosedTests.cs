using BidParser.Domain.Models;
using BidParser.Parsing.Lenovo.LbpiIsgPdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Fail-closed behavior on malformed or truncated documents, exercised at the internal seams
/// with synthetic words/rows: a recognizable table that cannot be safely bounded or mapped
/// must raise a staged ParseError, never silently drop content or leak an unstaged exception.
/// </summary>
public sealed class LenovoLbpiIsgPdfFailClosedTests
{
    private static PdfWord Word(string text, double x0, double top, int page = 0)
        => new(text, x0, x0 + Math.Max(4, text.Length * 5), top, top + 6.5, page, 595.0);

    private static PdfRow Row(double midline, params (string Key, string Value)[] cells)
        => new(0, midline - 3, midline,
            cells.ToDictionary(cell => cell.Key, cell => cell.Value));

    [Fact]
    public void ProductGrid_WithoutAnyTerminator_FailsClosed()
    {
        var words = new List<PdfWord>
        {
            Word("Line", 42, 100), Word("Item", 62, 100), Word("Part", 85, 100), Word("Number", 105, 100),
            Word("1", 59, 120), Word("7XTEST0AWW", 85, 120)
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ResolveProductEnd(words, 0, configIndex: null))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("PRODUCT AND SERVICE DETAILS");
    }

    [Fact]
    public void ConfigSection_WithUnresolvableHeader_FailsClosedInsteadOfDroppingComponents()
    {
        var words = new List<PdfWord>
        {
            Word("CONFIGURATION", 40, 100), Word("DETAILS", 133, 100),
            // A tokenization drift: "No" without its period never matches the header sequence.
            Word("No", 42, 120), Word("Components", 65, 120), Word("Description", 307, 120), Word("Qty", 538, 120),
            Word("Please", 40, 300), Word("transmit", 70, 300), Word("this", 110, 300), Word("quote", 130, 300)
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ExtractConfigSections(words, 0))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("CONFIGURATION DETAILS table header");
    }

    [Fact]
    public void ConfigSection_WithoutTerminator_FailsClosed()
    {
        var words = new List<PdfWord>
        {
            Word("CONFIGURATION", 40, 100), Word("DETAILS", 133, 100),
            Word("No.", 42, 120), Word("Components", 65, 120), Word("Description", 307, 120), Word("Qty", 538, 120),
            Word("1", 42, 140), Word("7XTEST0AWW", 60, 140), Word("Echo", 130, 140), Word("1", 538, 140)
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ExtractConfigSections(words, 0))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("end of the CONFIGURATION DETAILS");
    }

    /// <summary>
    /// The heading and header both resolved, so a grid that yields nothing has failed to read its
    /// own columns. Children are zero-cost, so returning an empty map would let the quote validate
    /// and ship with every component missing — the defect BRDAS019000007V1 exposed in production.
    /// </summary>
    [Fact]
    public void ConfigSection_YieldingNoSections_FailsClosedInsteadOfDroppingEveryComponent()
    {
        var words = new List<PdfWord>
        {
            Word("CONFIGURATION", 40, 100), Word("DETAILS", 133, 100),
            Word("No.", 42, 120), Word("Components", 65, 120), Word("Description", 307, 120), Word("Qty", 538, 120),
            // A row whose leading cell carries no line number: nothing opens a section.
            Word("CODE", 60, 140), Word("Echo", 130, 140), Word("1", 538, 140),
            Word("Please", 40, 300), Word("transmit", 70, 300), Word("this", 110, 300), Word("quote", 130, 300)
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ExtractConfigSections(words, 0))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("no configuration sections");
    }

    [Fact]
    public void ConfigSection_WithoutComponents_FailsClosed()
    {
        var words = new List<PdfWord>
        {
            Word("CONFIGURATION", 40, 100), Word("DETAILS", 133, 100),
            Word("No.", 42, 120), Word("Components", 65, 120), Word("Description", 307, 120), Word("Qty", 538, 120),
            // A section row with nothing under it: Lenovo never lists a component-less section.
            Word("7", 42, 140), Word("7XTEST0AWW", 60, 140), Word("Echo", 130, 140), Word("1", 538, 140),
            Word("Please", 40, 300), Word("transmit", 70, 300), Word("this", 110, 300), Word("quote", 130, 300)
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ExtractConfigSections(words, 0))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("7");
    }

    /// <summary>
    /// The No. and Components sub-columns are read as one cell, so telling a section row from a
    /// component row is a token question — and a component code can itself be all digits.
    /// </summary>
    [Theory]
    [InlineData("1 7DGDCTO1WW", 1, "7DGDCTO1WW")]
    [InlineData("12 7DCACTO1WW", 12, "7DCACTO1WW")]
    // A long VPN cut by the Description boundary still leads with its line number.
    [InlineData("1 7DGDCTO1", 1, "7DGDCTO1")]
    [InlineData("5977", null, "5977")]
    [InlineData("6400", null, "6400")]
    [InlineData("C3QL", null, "C3QL")]
    public void SplitLeadingCell_TellsSectionRowsFromComponentRows(string cell, int? number, string vpn)
    {
        LenovoLbpiIsgPdfParser.SplitLeadingCell(cell).Should().Be((number, vpn));
    }

    [Fact]
    public void ConfigSection_KeyedToNoProductLine_FailsClosedInsteadOfDroppingIt()
    {
        var line = new LenovoLbpiIsgPdfParser.ProductLine(1, "7XTEST0AWW", [], 2, "100.00", "200.00");
        var sections = new Dictionary<int, List<LenovoLbpiIsgPdfParser.ConfigComponent>>
        {
            [2] = [new LenovoLbpiIsgPdfParser.ConfigComponent("CODE", [], 1)]
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.AssembleItems([], [line], sections))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("2");
    }

    [Fact]
    public void SolutionParent_WithBlankQuantity_RaisesStagedErrorNotFormatException()
    {
        var rows = new List<PdfRow>
        {
            Row(100, ("Line Item", ""), ("Part Number", "SIDX0TEST1"), ("Total Price", "100.00")),
            Row(115, ("Line Item", "1"), ("Part Number", "7XTEST0AWW"), ("Qty", ""),
                ("Unit Price", "-"), ("Total Price", "-"))
        };

        var (solutions, flatLines, _) = LenovoLbpiIsgPdfParser.ExtractProductGrid(rows);
        solutions.Single().Lines.Single().Qty.Should().Be(0);

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.AssembleItems(
                solutions, flatLines, new Dictionary<int, List<LenovoLbpiIsgPdfParser.ConfigComponent>>()))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("quantity");
    }

    [Fact]
    public void SolutionRow_WithoutTotalPrice_FailsClosed()
    {
        var rows = new List<PdfRow>
        {
            Row(100, ("Line Item", ""), ("Part Number", "SIDX0TEST1"), ("Total Price", "-")),
            Row(115, ("Line Item", "1"), ("Part Number", "7XTEST0AWW"), ("Qty", "2"))
        };

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.ExtractProductGrid(rows))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("total price");
    }

    [Fact]
    public void SolutionRow_WithoutAnyNumberedLine_FailsClosed()
    {
        var rows = new List<PdfRow>
        {
            Row(100, ("Line Item", ""), ("Part Number", "SIDX0TEST1"), ("Total Price", "100.00"))
        };

        var (solutions, flatLines, _) = LenovoLbpiIsgPdfParser.ExtractProductGrid(rows);

        var error = FluentActions
            .Invoking(() => LenovoLbpiIsgPdfParser.AssembleItems(
                solutions, flatLines, new Dictionary<int, List<LenovoLbpiIsgPdfParser.ConfigComponent>>()))
            .Should().Throw<ParseError>().Which;

        error.Stage.Should().Be("extract");
        error.Hint.Should().Contain("no line items");
    }

    [Fact]
    public void ProductGrid_WithoutGrandTotalRow_ReportsNoQuotedTotal()
    {
        // Parse() turns the null into ParseError("totals", …); this pins the seam.
        var rows = new List<PdfRow>
        {
            Row(100, ("Line Item", "1"), ("Part Number", "7XTEST0AWW"), ("Qty", "2"),
                ("Unit Price", "10.00"), ("Total Price", "20.00"))
        };

        var (_, _, quotedTotal) = LenovoLbpiIsgPdfParser.ExtractProductGrid(rows);

        quotedTotal.Should().BeNull();
    }
}

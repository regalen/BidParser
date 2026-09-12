using BidParser.Domain.Models;
using BidParser.Parsing.Lenovo.LbpiIdgPdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Unlike LBP-I ISG, LBP-I IDG's Part Number/Description and Line Item#/Components pairs are
/// genuinely separate ruled columns whose shared boundary still has to be recovered from centred
/// header text, because the printed cell widths vary between quotes. These tests pin the
/// <see cref="PdfTableHelpers.CentredColumnRanges"/> recurrence against coordinates measured from
/// the two real fixtures, and the <see cref="LenovoLbpiIdgPdfParser.GroupLogicalRows"/> wrap rule
/// against synthetic rows.
/// </summary>
public sealed class LenovoLbpiIdgPdfGeometryTests
{
    /// <summary>Product grid header, coordinates measured from BRPAS019100001V1.pdf.</summary>
    private static List<PdfWord> ProductHeaderWords() =>
    [
        new PdfWord("#", 44.5, 49.5, 287.6, 294.2, 0, 595.0),
        new PdfWord("Part", 61.3, 77.8, 287.8, 294.3, 0, 595.0),
        new PdfWord("Number", 80.3, 112.3, 287.8, 294.3, 0, 595.0),
        new PdfWord("Description", 179.1, 224.1, 287.8, 294.3, 0, 595.0),
        new PdfWord("Qty", 284.6, 298.7, 287.6, 294.2, 0, 595.0),
        new PdfWord("Unit", 301.6, 317.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("price", 320.1, 339.7, 282.7, 289.1, 0, 595.0),
        new PdfWord("excl.", 342.2, 360.7, 282.7, 289.1, 0, 595.0),
        new PdfWord("GST", 363.2, 381.7, 282.4, 289.0, 0, 595.0),
        new PdfWord("(AUD)", 329.1, 354.1, 292.8, 299.3, 0, 595.0),
        new PdfWord("Total", 384.6, 404.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("price", 407.1, 426.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("excl.", 429.1, 447.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("GST", 450.1, 468.6, 282.4, 289.0, 0, 595.0),
        new PdfWord("(AUD)", 414.1, 439.1, 292.8, 299.3, 0, 595.0),
        new PdfWord("Total", 471.6, 491.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("price", 494.1, 513.6, 282.7, 289.1, 0, 595.0),
        new PdfWord("incl.", 516.1, 532.1, 282.7, 289.1, 0, 595.0),
        new PdfWord("GST", 534.6, 553.1, 282.4, 289.0, 0, 595.0),
        new PdfWord("(AUD)", 499.8, 524.8, 292.8, 299.3, 0, 595.0)
    ];

    /// <summary>Config grid header + a section row's line number, coordinates from BRPAS019100002V1.pdf.</summary>
    private static List<PdfWord> ConfigHeaderWords() =>
    [
        new PdfWord("Line", 53.5, 70.5, 576.2, 582.6, 0, 595.0),
        new PdfWord("Item#", 73.0, 95.5, 576.0, 582.5, 0, 595.0),
        new PdfWord("Components", 161.4, 212.4, 576.0, 582.5, 0, 595.0),
        new PdfWord("Description", 376.1, 421.2, 576.2, 582.6, 0, 595.0),
        new PdfWord("Qty", 532.5, 546.5, 575.9, 582.5, 0, 595.0),
        // The leftmost real body content — a section-row line number — anchors the seam.
        new PdfWord("1", 45.8, 50.8, 589.5, 595.9, 0, 595.0)
    ];

    [Fact]
    public void BuildProductColumns_RecoversRealCellEdges()
    {
        var words = ProductHeaderWords();

        var columns = LenovoLbpiIdgPdfParser.BuildProductColumns(words, headerIndex: 0);

        columns["Line Item"].Left.Should().Be(0);
        columns["Line Item"].Right.Should().BeApproximately(54.1, 1.0);
        columns["Part Number"].Left.Should().BeApproximately(54.1, 1.0);
        columns["Part Number"].Right.Should().BeApproximately(119.5, 1.0);
        columns["Description"].Right.Should().BeApproximately(283.7, 1.0);
        columns["Qty"].Right.Should().BeApproximately(299.6, 1.0);
        columns["Unit Price"].Right.Should().BeApproximately(383.7, 1.0);
        columns["Total Excl"].Right.Should().BeApproximately(469.5, 1.0);
        columns["Total Incl"].Right.Should().Be(595.0);
    }

    [Fact]
    public void BuildProductColumns_ThrowsWhenAHeaderTokenIsMissing()
    {
        var words = ProductHeaderWords().Where(word => word.Text != "Qty").ToList();

        FluentActions.Invoking(() => LenovoLbpiIdgPdfParser.BuildProductColumns(words, headerIndex: 0))
            .Should().Throw<ParseError>().Which.Stage.Should().Be("detect");
    }

    [Fact]
    public void BuildConfigColumns_AnchorsTheSeamOnBodyContentNotTheHeaderWord()
    {
        var words = ConfigHeaderWords();

        var columns = LenovoLbpiIdgPdfParser.BuildConfigColumns(words, headerIndex: 0, configEnd: words.Count);

        // "Line" itself starts at 53.5 — using that as the seam (instead of the body's 45.8) would
        // put every boundary ~8pt too far right, which is exactly the regression this seam choice
        // guards against (BuildConfigColumns's doc comment explains why).
        columns["Line Item#"].Right.Should().BeApproximately(104.2, 1.0);
        columns["Components"].Right.Should().BeApproximately(269.6, 1.0);
        columns["Description"].Right.Should().BeApproximately(527.7, 1.0);
        columns["Qty"].Right.Should().Be(595.0);
    }

    [Fact]
    public void BuildConfigColumns_ThrowsWhenAHeaderTokenIsMissing()
    {
        var words = ConfigHeaderWords().Where(word => word.Text != "Components").ToList();

        FluentActions.Invoking(() => LenovoLbpiIdgPdfParser.BuildConfigColumns(words, headerIndex: 0, configEnd: words.Count))
            .Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    [Fact]
    public void BuildConfigColumns_IgnoresContentPrintedLeftOfTheLineItemColumn()
    {
        // This template already prints a marketing paragraph at x=39.8, just above CONFIGURATION
        // DETAILS. Were such text to fall below the heading instead, a bare minimum over the
        // region would seam on it and shift every boundary left — Components would open at 110.2
        // and swallow the labels it is supposed to bound.
        var words = ConfigHeaderWords();
        words.Add(new PdfWord("Did", 39.8, 52.0, 700.0, 706.5, 0, 595.0));

        var columns = LenovoLbpiIdgPdfParser.BuildConfigColumns(words, headerIndex: 0, configEnd: words.Count);

        columns["Line Item#"].Right.Should().BeApproximately(104.2, 1.0);
        columns["Components"].Right.Should().BeApproximately(269.6, 1.0);
    }

    [Fact]
    public void BuildConfigColumns_ThrowsWhenNoSectionLineNumberAnchorsTheSeam()
    {
        var words = ConfigHeaderWords().Where(word => word.Text != "1").ToList();

        FluentActions.Invoking(() => LenovoLbpiIdgPdfParser.BuildConfigColumns(words, headerIndex: 0, configEnd: words.Count))
            .Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    [Fact]
    public void BuildConfigColumns_ThrowsRatherThanCrashingOnAnInvertedRegion()
    {
        // A terminator resolving ahead of the header leaves nothing to scan; that must surface as
        // a ParseError, not an InvalidOperationException out of an empty Min().
        var words = ConfigHeaderWords();

        FluentActions.Invoking(() => LenovoLbpiIdgPdfParser.BuildConfigColumns(words, headerIndex: 0, configEnd: 0))
            .Should().Throw<ParseError>().Which.Stage.Should().Be("extract");
    }

    private static PdfRow Row(int pageIndex, double midline, string description, string? marker = null)
        => new(pageIndex, midline, midline, new Dictionary<string, string>
        {
            ["Description"] = description,
            ["Marker"] = marker ?? string.Empty
        });

    private static bool IsAnchor(PdfRow row) => PdfTableHelpers.Cell(row.Cells, "Marker").Length > 0;

    [Fact]
    public void GroupLogicalRows_JoinsRowsUnderBlockGap()
    {
        // Measured intra-block pitch: 5.2pt.
        var rows = new List<PdfRow> { Row(0, 100.0, "Second Storage"), Row(0, 105.2, "Selection") };

        var logical = LenovoLbpiIdgPdfParser.GroupLogicalRows(rows, IsAnchor);

        logical.Should().ContainSingle();
        logical[0].Cells["Description"].Should().Be("Second Storage Selection");
    }

    [Fact]
    public void GroupLogicalRows_SplitsAtOrAboveBlockGap()
    {
        // Measured inter-block pitch: 13.4pt.
        var rows = new List<PdfRow> { Row(0, 100.0, "Country/Region"), Row(0, 113.4, "Preload Type") };

        var logical = LenovoLbpiIdgPdfParser.GroupLogicalRows(rows, IsAnchor);

        logical.Should().HaveCount(2);
        logical[0].Cells["Description"].Should().Be("Country/Region");
        logical[1].Cells["Description"].Should().Be("Preload Type");
    }

    [Fact]
    public void GroupLogicalRows_CarriesOverANonAnchorRowAcrossAPageBreak()
    {
        var rows = new List<PdfRow>
        {
            Row(0, 700.0, "21TD0018AU wraps", marker: "6"),
            Row(1, 50.0, "onto the next page")
        };

        var logical = LenovoLbpiIdgPdfParser.GroupLogicalRows(rows, IsAnchor);

        logical.Should().ContainSingle();
        logical[0].Cells["Description"].Should().Be("21TD0018AU wraps onto the next page");
    }

    [Fact]
    public void GroupLogicalRows_SplitsOnAnAnchorRowAcrossAPageBreak()
    {
        var rows = new List<PdfRow>
        {
            Row(0, 700.0, "last line of page one", marker: "6"),
            Row(1, 50.0, "first anchored line of page two", marker: "7")
        };

        var logical = LenovoLbpiIdgPdfParser.GroupLogicalRows(rows, IsAnchor);

        logical.Should().HaveCount(2);
        logical[1].Cells["Description"].Should().Be("first anchored line of page two");
    }

    /// <summary>
    /// A word stream ending in the requested terminators, one token per line at 13.4pt pitch.
    /// Only the ordering of the anchors matters to <c>FindGridEnd</c>.
    /// </summary>
    private static List<PdfWord> TerminatorWords(params string[] texts)
        => [.. texts.Select((text, index) =>
            new PdfWord(text, 45.0, 90.0, 100.0 + (13.4 * index), 106.6 + (13.4 * index), 0, 595.0))];

    [Fact]
    public void FindGridEnd_ResolvesTheMtmRecapWhenPresent()
    {
        var words = TerminatorWords("Warranty", "MTM", "Line", "Item#", "TERMS", "AND", "CONDITIONS");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 0).Should().Be(1);
    }

    // Lenovo omits the MTM recap entirely when no product line resolves to a machine type model
    // (BRPAS019100003V1.pdf). The grid then runs straight into the terms heading, which has to
    // bound it on its own — this is the case that used to raise ParseError("extract", …).
    [Fact]
    public void FindGridEnd_FallsBackToTheTermsHeadingWhenTheMtmRecapIsAbsent()
    {
        var words = TerminatorWords("Warranty", "TERMS", "AND", "CONDITIONS");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 0).Should().Be(1);
    }

    // The minimum, not a null-coalescing fallback: an "MTM Line Item#" run below the terms heading
    // would otherwise reopen the region across the clauses and emit terms prose as components.
    [Fact]
    public void FindGridEnd_TakesTheEarlierTerminatorRatherThanPreferringMtm()
    {
        var words = TerminatorWords("Warranty", "TERMS", "AND", "CONDITIONS", "MTM", "Line", "Item#");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 0).Should().Be(1);
    }

    [Fact]
    public void FindGridEnd_SearchesOnlyFromTheGivenStartIndex()
    {
        // A terminator above the caller's grid must not bound the grid below it.
        var words = TerminatorWords("MTM", "Line", "Item#", "Warranty", "TERMS", "AND", "CONDITIONS");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 3).Should().Be(4);
    }

    [Fact]
    public void FindGridEnd_ReturnsNullWhenNeitherTerminatorIsPresent()
    {
        // Parse turns this into ParseError("extract", …) — the grid stays fail-closed rather than
        // reading on to the end of the document.
        var words = TerminatorWords("Warranty", "3", "Year", "On-site");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 0).Should().BeNull();
    }

    [Fact]
    public void FindGridEnd_IgnoresTheLowerCaseTermsPhraseInTheClauseBody()
    {
        // The clauses themselves say "the Lenovo Terms and Conditions"; FindSequence is ordinal,
        // so only the uppercase heading can terminate a grid.
        var words = TerminatorWords("Lenovo", "Terms", "and", "Conditions");

        LenovoLbpiIdgPdfParser.FindGridEnd(words, startIndex: 0).Should().BeNull();
    }
}

using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class PdfTableGeometryTests
{
    private static readonly IReadOnlyDictionary<string, (double Left, double Right)> TwoColumns =
        new Dictionary<string, (double Left, double Right)>
        {
            ["Left"] = (0, 15),
            ["Right"] = (15, 40)
        };

    [Fact]
    public void RowsBetween_groups_zero_height_words_by_shared_baseline()
    {
        var rows = PdfTableHelpers.RowsBetween(
        [
            Word("first", 0, 10, 100, 100, lineY: 105),
            Word("second", 20, 30, 111, 111, lineY: 105),
            Word("next", 0, 10, 120, 120, lineY: 125)
        ], 0, 0, TwoColumns, stopToken: "never");

        rows.Should().HaveCount(2);
        rows[0].Cells["Left"].Should().Be("first");
        rows[0].Cells["Right"].Should().Be("second");
        rows[0].Midline.Should().Be(105);
    }

    [Fact]
    public void RemovePageFooters_removes_complete_edge_footer_before_column_splitting()
    {
        var words = new[]
        {
            Word("SKU-1", 0, 30, 100, 106, lineY: 105),
            Word("Body", 0, 30, 182, 189, lineY: 186.5),
            Word("Strike Group Australia", 0, 90, 188, 195, lineY: 192.38),
            Word("Page", 100, 120, 188, 195, lineY: 192),
            Word(" ", 120, 122, 188, 195, lineY: 192),
            Word("10", 122, 130, 188, 195, lineY: 192),
            Word(" ", 130, 132, 188, 195, lineY: 192),
            Word("of", 132, 138, 188, 195, lineY: 192),
            Word(" ", 138, 140, 188, 195, lineY: 192),
            Word("12", 140, 148, 188, 195, lineY: 192)
        };

        PdfTableHelpers.RemovePageFooters(words).Select(word => word.Text).Should().Equal("SKU-1", "Body");
    }

    [Fact]
    public void RowsBetween_does_not_split_a_continuous_baseline_word_at_a_column_boundary()
    {
        var word = Word("AB", 0, 20, 100, 100, lineY: 105,
            [new PdfLetter("A", 0, 10), new PdfLetter("B", 10, 20)]);

        var row = PdfTableHelpers.RowsBetween([word], 0, 0, TwoColumns, stopToken: "never").Single();

        row.Cells["Left"].Should().Be("AB");
        row.Cells["Right"].Should().BeEmpty();
    }

    [Fact]
    public void RowsBetween_splits_a_word_at_a_real_letter_gap_on_a_column_boundary()
    {
        var word = Word("AB", 0, 30, 100, 108, lineY: 105,
            [new PdfLetter("A", 0, 10), new PdfLetter("B", 20, 30)]);

        var row = PdfTableHelpers.RowsBetween([word], 0, 0, TwoColumns, stopToken: "never").Single();

        row.Cells["Left"].Should().Be("A");
        row.Cells["Right"].Should().Be("B");
    }

    [Fact]
    public void RowsBetween_retains_short_glyph_repair_when_tight_geometry_is_trustworthy()
    {
        var rows = PdfTableHelpers.RowsBetween(
        [
            Word("left", 0, 10, 100, 110),
            Word("-", 12, 14, 116, 118),
            Word("next", 0, 10, 130, 140)
        ], 0, 0, new Dictionary<string, (double Left, double Right)> { ["Text"] = (0, 40) }, stopToken: "never");

        rows.Should().HaveCount(2);
        rows[0].Cells["Text"].Should().Be("left -");
    }

    [Fact]
    public void RowsBetween_skips_short_glyph_repair_only_on_pages_marked_for_baseline_geometry()
    {
        var rows = PdfTableHelpers.RowsBetween(
        [
            Word("baseline", 0, 10, 100, 108, lineY: 104, pageIndex: 0, usesBaselineGeometry: true),
            Word("-", 12, 14, 115, 115, lineY: 115, pageIndex: 0, usesBaselineGeometry: true),
            Word("next", 0, 10, 130, 138, lineY: 134, pageIndex: 0, usesBaselineGeometry: true),
            Word("tight", 0, 10, 100, 108, pageIndex: 1),
            Word("-", 12, 14, 115, 117, pageIndex: 1),
            Word("next", 0, 10, 130, 138, pageIndex: 1)
        ], 0, 0, new Dictionary<string, (double Left, double Right)> { ["Text"] = (0, 40) }, stopToken: "never");

        rows.Where(row => row.PageIndex == 0).Select(row => row.Cells["Text"])
            .Should().Equal("baseline", "-", "next");
        rows.Where(row => row.PageIndex == 1).Select(row => row.Cells["Text"])
            .Should().Equal("tight -", "next");
    }

    [Fact]
    public void PdfWordCollector_falls_back_to_tight_geometry_for_rotated_text()
    {
        var lineY = PdfWordCollector.GetLineY(
            pageUsesBaselineGeometry: true,
            pageHeight: 200,
            [(StartY: 100, EndY: 102)]);

        lineY.Should().BeNull();
    }

    private static PdfWord Word(
        string text,
        double x0,
        double x1,
        double top,
        double bottom,
        double? lineY = null,
        IReadOnlyList<PdfLetter>? letters = null,
        int pageIndex = 0,
        bool usesBaselineGeometry = false)
        => new(text, x0, x1, top, bottom, pageIndex, 200, letters, PageHeight: 200, LineY: lineY,
            PageUsesBaselineGeometry: usesBaselineGeometry);
}

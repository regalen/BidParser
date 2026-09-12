using BidParser.Parsing.Lenovo.LbpiIsgPdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// The product grid centres both its header text and its values in every column, so a value
/// wider than its header spills evenly past both header edges. These tests pin the consequence:
/// the Part Number column has to be wide enough for a VPN longer than any yet quoted, and it
/// must not be bounded against Description at all — the widest VPN on five of the seven sample
/// quotes already overruns the "Number" header's right edge.
/// </summary>
public sealed class LenovoLbpiIsgPdfColumnGeometryTests
{
    /// <summary>Header coordinates measured from BRDAS019000007V1 — the widest of the samples.</summary>
    private static readonly (string Text, double X0, double X1)[] HeaderRow =
    [
        ("Line", 42.5, 60.1), ("Item", 62.5, 82.3),
        ("Part", 91.2, 108.7), ("Number", 111.0, 146.2),
        ("Description", 232.8, 281.3),
        ("Qty", 385.0, 400.0), ("Unit", 430.0, 448.0), ("Total", 500.0, 518.0)
    ];

    private static IReadOnlyDictionary<string, (double Left, double Right)> Columns()
    {
        var words = HeaderRow
            .Select(cell => new PdfWord(cell.Text, cell.X0, cell.X1, 100, 108, 0, 595.0))
            .ToList();

        return LenovoLbpiIsgPdfParser.BuildProductColumns(words, words[0]);
    }

    [Fact]
    public void PartNumberColumn_RunsAllTheWayToQty_WithNoDescriptionBoundaryToCutAVpn()
    {
        var columns = Columns();

        columns.Should().NotContainKey("Description",
            "Part Number and Description are read as one cell and split by token afterwards");
        columns["Part Number"].Right.Should().Be(columns["Qty"].Left);
    }

    /// <summary>
    /// Lenovo VPNs run to ten characters today (`7DGDCTO1WW`, `5PS7C20566`), which already
    /// overhangs the header by ~5pt on each side. A twelve-character one must still land whole
    /// inside the column rather than losing its first or last glyph to a neighbour.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void PartNumberColumn_HoldsAVpnWiderThanItsHeader(int characters)
    {
        // Glyph pitch measured from BRDAS019000007V1: "7DGDCTO1WW" spans 86.49 → 150.92.
        const double pitch = 6.443;
        const double headerCentre = (91.2 + 146.2) / 2;

        var half = characters * pitch / 2;
        var column = Columns()["Part Number"];

        (headerCentre - half).Should().BeGreaterThan(column.Left);
        (headerCentre + half).Should().BeLessThan(column.Right);
    }

    [Fact]
    public void LineItemColumn_StillClearsAThreeDigitLineNumber()
    {
        // Two-digit line numbers reach x1 67.1 on BRDAS019000002V1; a third digit adds ~5.1pt.
        Columns()["Line Item"].Right.Should().BeGreaterThan(72.2);
    }

    [Theory]
    // A numbered grid line: the leading token is the part number, the rest is the description.
    [InlineData("1", "7DGDCTO1WW ThinkSystem SR650 V4", "7DGDCTO1WW", "ThinkSystem SR650 V4")]
    // A Solution ID row carries a part number with no line number and no description.
    [InlineData("", "SIDX02YUF5", "SIDX02YUF5", "")]
    // A wrapped description fragment carries no part number, however it starts.
    [InlineData("", "TCE_Flexi Host", "", "TCE_Flexi Host")]
    [InlineData("", "2U12 chassis", "", "2U12 chassis")]
    // The page-break header repeat is description-only, so the Line Item cell still names it.
    [InlineData("Line Item", "Part Number Description", "", "Part Number Description")]
    public void SplitPartNumberColumn_UsesTheLineItemCellToDecideWhatLeadsTheRow(
        string lineItem, string merged, string expectedPartNumber, string expectedDescription)
    {
        var row = new PdfRow(0, 100, 104, new Dictionary<string, string>
        {
            ["Line Item"] = lineItem,
            ["Part Number"] = merged
        });

        var cells = LenovoLbpiIsgPdfParser.SplitPartNumberColumn([row]).Single().Cells;

        cells["Part Number"].Should().Be(expectedPartNumber);
        cells["Description"].Should().Be(expectedDescription);
    }
}

using BidParser.Parsing.Lenovo.LbpiIsgPdf;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

/// <summary>
/// Geometry-only tests for wrapped-description ownership. RowBlocks' cross-page rule reaches
/// only the row immediately adjacent to an anchor; the parser-local repair passes in
/// <see cref="LenovoLbpiIsgPdfParser.AssignDescriptionOwners"/> extend that to blocks whose
/// wrap spans multiple rows across a page break. Pitches mirror the real fixtures: ~6pt
/// between wrapped lines, ~15pt inter-block padding.
/// </summary>
public sealed class LenovoLbpiIsgPdfOwnerAssignmentTests
{
    private static PdfRow Row(int page, double midline)
        => new(page, midline - 3, midline, new Dictionary<string, string> { ["Description"] = "x" });

    [Fact]
    public void SamePage_CentredBlocks_SplitAtTheLargestGap()
    {
        // desc | A | desc … desc | B | desc — the 545504 shape.
        var rows = new List<PdfRow>
        {
            Row(0, 426.81), Row(0, 432.94), Row(0, 439.07),
            Row(0, 454.33), Row(0, 460.46), Row(0, 466.59)
        };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [1, 4]);

        owners.Should().Equal(1, 1, 1, 4, 4, 4);
    }

    [Fact]
    public void SingleTrailingLineAtPageTop_PaddedFromTheNextAnchor_StaysWithPreviousAnchor()
    {
        // The real BRDAS019000002V1 line-14 shape: one continuation resumes at the top of the
        // next page, a full inter-block padding above the next anchor.
        var rows = new List<PdfRow> { Row(0, 700.00), Row(1, 76.53), Row(1, 91.78) };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [0, 2]);

        owners.Should().Equal(0, 0, 2);
    }

    [Fact]
    public void TwoTrailingLinesAtPageTop_StayWithThePreviousAnchor()
    {
        // The reviewed defect: RowBlocks alone assigns the second continuation to the next
        // anchor because only the adjacent row gets a finite cross-page distance.
        var rows = new List<PdfRow> { Row(0, 700.00), Row(1, 49.01), Row(1, 55.14), Row(1, 70.40) };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [0, 3]);

        owners.Should().Equal(0, 0, 0, 3);
    }

    [Fact]
    public void ThreeTrailingLinesAtPageTop_ChainOntoThePreviousAnchor()
    {
        var rows = new List<PdfRow>
        {
            Row(0, 700.00), Row(1, 49.01), Row(1, 55.14), Row(1, 61.27), Row(1, 76.53)
        };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [0, 4]);

        owners.Should().Equal(0, 0, 0, 0, 4);
    }

    [Fact]
    public void TrailingPairAtPageBottom_StaysWithThePreviousAnchor()
    {
        // Both wrap lines below their anchor at the bottom of a page, the next anchor opening
        // the following page: the pitch-consistent chain keeps them with their block.
        var rows = new List<PdfRow> { Row(0, 600.00), Row(0, 606.13), Row(0, 612.26), Row(1, 50.00) };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [0, 3]);

        owners.Should().Equal(0, 0, 0, 3);
    }

    [Fact]
    public void LeadingPairAtPageBottom_GoesToTheNextAnchor()
    {
        // A block that starts at the bottom of one page with its anchor on the next — the
        // Zebra ZD4AH22 shape, but with two leading lines. The padding above the pair marks
        // them as the next anchor's wrap.
        var rows = new List<PdfRow> { Row(0, 500.00), Row(0, 590.00), Row(0, 596.13), Row(1, 50.00) };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [0, 3]);

        owners.Should().Equal(0, 3, 3, 3);
    }

    [Fact]
    public void RowsOutsideTheOutermostAnchors_GoToTheNearestAnchor()
    {
        var rows = new List<PdfRow> { Row(0, 100.00), Row(0, 106.13), Row(0, 200.00), Row(0, 206.13) };

        var owners = LenovoLbpiIsgPdfParser.AssignDescriptionOwners(rows, [1, 2]);

        owners.Should().Equal(1, 1, 2, 2);
    }
}

/// <summary>
/// Pins the shared helper's own cross-page contract (Zebra also consumes it): only the row
/// immediately adjacent to an anchor gets a finite cross-page distance. The LBP-I parser
/// compensates with its repair passes — if this behavior ever changes, that compensation
/// must be revisited deliberately.
/// </summary>
public sealed class RowBlocksTests
{
    private static PdfRow Row(int page, double midline)
        => new(page, midline - 3, midline, new Dictionary<string, string>());

    [Fact]
    public void SamePage_ContinuationRowsSplitAtTheLargestGap()
    {
        var rows = new List<PdfRow>
        {
            Row(0, 426.81), Row(0, 432.94), Row(0, 439.07),
            Row(0, 454.33), Row(0, 460.46), Row(0, 466.59)
        };
        var blocks = new RowBlocks(rows, [1, 4]);

        blocks.OwnerOf(0).Should().Be(1);
        blocks.OwnerOf(2).Should().Be(1);
        blocks.OwnerOf(3).Should().Be(4);
        blocks.OwnerOf(5).Should().Be(4);
    }

    [Fact]
    public void CrossPage_AdjacentTrailingLine_StaysWithThePreviousAnchor()
    {
        var rows = new List<PdfRow> { Row(0, 700.00), Row(1, 76.53), Row(1, 91.78) };
        var blocks = new RowBlocks(rows, [0, 2]);

        blocks.OwnerOf(1).Should().Be(0);
    }

    [Fact]
    public void CrossPage_SecondContinuationRow_IsBeyondTheAdjacencyRule()
    {
        // Documented limitation, not desired extraction behavior: the second row after the
        // page break is out of the adjacency window and falls to the next anchor.
        var rows = new List<PdfRow> { Row(0, 700.00), Row(1, 49.01), Row(1, 55.14), Row(1, 70.40) };
        var blocks = new RowBlocks(rows, [0, 3]);

        blocks.OwnerOf(1).Should().Be(0);
        blocks.OwnerOf(2).Should().Be(3);
    }
}

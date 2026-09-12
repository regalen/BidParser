using BidParser.Domain.Models;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class PdfContentGeometryTests
{
    [Fact]
    public void ContentColumnRanges_places_boundaries_in_content_corridors()
    {
        var headers = new List<(string Name, double Centre)> { ("Code", 40), ("Description", 130), ("Qty", 230) };
        var words = new List<PdfWord>
        {
            new("ABC", 20, 60, 100, 110, 0, 300),
            new("A description", 90, 175, 100, 110, 0, 300),
            new("10", 220, 240, 100, 110, 0, 300)
        };

        var columns = PdfTableHelpers.ContentColumnRanges(headers, words, minGutter: 11, pageWidth: 300);

        columns["Code"].Right.Should().Be(75);
        columns["Description"].Right.Should().Be(197.5);
    }

    [Fact]
    public void ContentColumnRanges_coalesces_a_narrow_intra_column_gap()
    {
        var headers = new List<(string Name, double Centre)> { ("Code", 40), ("Description", 130), ("Qty", 230) };
        var words = new List<PdfWord>
        {
            new("ABC", 20, 60, 100, 110, 0, 300),
            new("A", 90, 125, 100, 110, 0, 300),
            new("description", 128, 175, 100, 110, 0, 300),
            new("10", 220, 240, 100, 110, 0, 300)
        };

        var columns = PdfTableHelpers.ContentColumnRanges(headers, words, minGutter: 1, pageWidth: 300);

        columns["Code"].Right.Should().Be(75);
        columns["Description"].Right.Should().Be(197.5);
    }

    [Fact]
    public void ContentColumnRanges_fails_closed_when_a_column_band_is_missing()
    {
        var headers = new List<(string Name, double Centre)> { ("Code", 40), ("Description", 130), ("Qty", 230) };
        var words = new List<PdfWord> { new("ABC", 20, 60, 100, 110, 0, 300), new("10", 220, 240, 100, 110, 0, 300) };

        FluentActions.Invoking(() => PdfTableHelpers.ContentColumnRanges(headers, words, 11, 300))
            .Should().Throw<ParseError>().Which.Stage.Should().Be("detect");
    }

    [Fact]
    public void RowsBetween_excludes_the_complete_stop_anchor_row()
    {
        var columns = new Dictionary<string, (double Left, double Right)> { ["Value"] = (0, 300) };
        var words = new List<PdfWord>
        {
            new("Item", 20, 50, 10, 12, 0, 300),
            new("&", 5, 10, 20, 22, 0, 300),
            new("Terms", 20, 50, 20, 22, 0, 300),
            new("Conditions", 55, 100, 20, 22, 0, 300),
            new("After", 20, 50, 30, 32, 0, 300)
        };

        var rows = PdfTableHelpers.RowsBetween(words, startTop: 0, startPage: 0, columns, "Terms");

        rows.Should().ContainSingle();
        rows[0].Cells["Value"].Should().Be("Item");
    }

    [Fact]
    public void GroupByAnchor_attaches_continuations_to_the_preceding_anchor()
    {
        static PdfRow Row(string id, string text) => new(0, 0, 0, new Dictionary<string, string> { ["Id"] = id, ["Text"] = text });
        var groups = PdfTableHelpers.GroupByAnchor([Row("1", "first"), Row("", "continued"), Row("2", "second")], row => row.Cells["Id"].Length > 0);

        groups.Should().HaveCount(2);
        groups[0].Select(row => row.Cells["Text"]).Should().Equal("first", "continued");
    }
}

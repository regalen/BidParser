using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class SolutionOutputSplitterTests
{
    [Fact]
    public void FourSolutionFixture_GroupsInFirstAppearanceOrderAndRestartsSequences()
    {
        var groups = Split("Bid_Platform_Bid_Request_Sample_03.xls");

        groups.Select(group => group.SolutionId).Should().Equal(
            "SIDX02YU2Q", "SIDX02YU2R", "SIDX02YU2S", "SIDX02YU2T");
        groups.Select(group => group.Items.Count).Should().Equal(51, 40, 57, 40);

        foreach (var group in groups)
        {
            group.Items[0].LineSequence.Should().Be("1");
            group.Items.Skip(1).Select(item => item.LineSequence).Should().Equal(
                Enumerable.Range(1, group.Items.Count - 1).Select(index => $"1.{index:D2}"));
        }
    }

    [Fact]
    public void SingleSolutionFixtures_PreserveItemsAndRenumberEveryParent()
    {
        var primary = Split("BRDAD019200001.xls").Should().ContainSingle().Subject;
        primary.SolutionId.Should().Be("SIDX02SDL3");
        primary.Items.Should().HaveCount(62);
        ParentSequences(primary).Should().Equal("1", "2");

        var standalone = Split("Bid_Platform_Bid_Request_Sample_01.xls")
            .Should().ContainSingle().Subject;
        standalone.Items.Should().HaveCount(153);
        ParentSequences(standalone).Should().Equal(
            Enumerable.Range(1, 13).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Split_PreservesRelativeItemOrderAndCreatesANullGroup()
    {
        var items = new[]
        {
            Item("NULL-PARENT", null, "1"),
            Item("A-PARENT", "SID-A", "2"),
            Item("NULL-CHILD", null, "1.01"),
            Item("A-CHILD", "SID-A", "2.01"),
            Item("B-PARENT", "SID-B", "3"),
            Item("ORPHAN-CHILD", "SID-ORPHAN", "4.01"),
        };

        var groups = SolutionOutputSplitter.Split(items);

        groups.Select(group => group.SolutionId).Should().Equal(null, "SID-A", "SID-B", "SID-ORPHAN");
        groups[0].Items.Select(item => item.Vpn).Should().Equal("NULL-PARENT", "NULL-CHILD");
        groups[1].Items.Select(item => item.Vpn).Should().Equal("A-PARENT", "A-CHILD");
        groups[2].Items.Select(item => item.Vpn).Should().Equal("B-PARENT");
        groups[3].Items.Should().ContainSingle().Which.LineSequence.Should().Be("1.01");
    }

    [Fact]
    public void ManualPartsFixture_YieldsOneNullSolutionIdGroupAndResequencesAtOne()
    {
        var group = Split("Bid_Platform_Bid_Request_Sample_07.xlsx").Should().ContainSingle().Subject;
        group.SolutionId.Should().BeNull();
        group.Items.Should().ContainSingle();
        group.Items[0].LineSequence.Should().Be("1");
        group.Items[0].Vpn.Should().Be("4X67A84824");
    }

    [Fact]
    public void NoLineItems_StillYieldsOneEmptyGroup()
    {
        // A split must always produce at least one workbook, so the archive is never empty
        // and X-Split-Count is never 0.
        var groups = SolutionOutputSplitter.Split([]);

        groups.Should().ContainSingle();
        groups[0].SolutionId.Should().BeNull();
        groups[0].Items.Should().BeEmpty();
    }

    private static IReadOnlyList<SolutionGroup> Split(string sample)
    {
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == ParserSlugs.LenovoLbpeIsgXls);
        var result = parser.Parse(Path.Combine(TestSample.Root, "samples", "inputs", sample));
        return SolutionOutputSplitter.Split(result.LineItems);
    }

    private static IEnumerable<string?> ParentSequences(SolutionGroup group)
        => group.Items.Where(item => item.LineSequence is null || !item.LineSequence.Contains('.'))
            .Select(item => item.LineSequence);

    private static LineItem Item(string vpn, string? solutionId, string lineSequence) => new()
    {
        Vpn = vpn,
        Cost = 1m,
        Qty = 1,
        SolutionId = solutionId,
        LineSequence = lineSequence,
    };

}

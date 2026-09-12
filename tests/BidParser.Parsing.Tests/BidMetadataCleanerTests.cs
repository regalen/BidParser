using BidParser.Parsing.Cleaning;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class BidMetadataCleanerTests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData("V1", "1")]
    [InlineData("v1", "1")]
    [InlineData("v.25", "25")]
    [InlineData("2.0", "2.0")]
    [InlineData("  V  .  25  ", "25")]
    public void Normalizes_revision(string source, string expected)
    {
        BidMetadataCleaner.Clean("XQ-1", source).BidRevision.Should().Be(expected);
    }

    /// <summary>
    /// The number is the load-bearing half — a revision on its own is not displayable, and the
    /// frontend needs both to render a bid cell.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_bid_number_yields_no_bid_metadata(string? number)
    {
        var bid = BidMetadataCleaner.Clean(number, "2");

        bid.BidNumber.Should().BeNull();
        bid.BidRevision.Should().BeNull();
    }

    /// <summary>
    /// Revision anchors are newer and less proven than number anchors, so an unreadable revision
    /// must not discard a bid number we did successfully read.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_revision_keeps_the_number_and_defaults(string? revision)
    {
        var bid = BidMetadataCleaner.Clean("99010001", revision);

        bid.BidNumber.Should().Be("99010001");
        bid.BidRevision.Should().Be(BidMetadataCleaner.DefaultRevision);
    }

    [Fact]
    public void Revisionless_formats_store_the_default_revision()
    {
        var bid = BidMetadataCleaner.CleanRevisionless("XQ-9100002");

        bid.BidNumber.Should().Be("XQ-9100002");
        bid.BidRevision.Should().Be(BidMetadataCleaner.DefaultRevision);
    }

    [Fact]
    public void Revisionless_formats_with_no_number_yield_nothing()
    {
        BidMetadataCleaner.CleanRevisionless(null).BidNumber.Should().BeNull();
    }
}

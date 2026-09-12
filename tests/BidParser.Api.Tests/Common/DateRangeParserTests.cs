using BidParser.Api.Common;
using Xunit;
using FluentAssertions;

namespace BidParser.Api.Tests.Common;

public class DateRangeParserTests
{
    [Fact]
    public void Parse_WhenRangeIsAll_ReturnsAllWithNoDates()
    {
        var result = DateRangeParser.Parse("all", null, null, out var parsed);

        result.Should().BeNull();
        parsed.IsAll.Should().BeTrue();
        parsed.FromStr.Should().BeNull();
        parsed.ToStr.Should().BeNull();
        parsed.FromUtc.Should().BeNull();
        parsed.ToUtc.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenFromAndToAreMissing_DefaultsToLast30Days()
    {
        var before = DateTime.Today;
        var result = DateRangeParser.Parse(null, null, null, out var parsed);
        var after = DateTime.Today;

        result.Should().BeNull();
        parsed.IsAll.Should().BeFalse();
        var toDate = DateTime.ParseExact(parsed.ToStr!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        toDate.Should().BeOneOf(before, after);
        parsed.FromStr.Should().Be(toDate.AddDays(-29).ToString("yyyy-MM-dd"));
        parsed.FromUtc.Should().Be(toDate.AddDays(-29).ToUniversalTime());
        parsed.ToUtc.Should().Be(toDate.AddDays(1).ToUniversalTime());
    }

    [Fact]
    public void Parse_WhenFromIsInvalid_ReturnsError()
    {
        var result = DateRangeParser.Parse(null, "invalid", "2026-01-01", out _);

        result.Should().NotBeNull();
    }

    [Fact]
    public void Parse_WhenToIsInvalid_ReturnsError()
    {
        var result = DateRangeParser.Parse(null, "2026-01-01", "invalid", out _);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, "2026-01-01", null)]
    [InlineData(null, null, "2026-01-01")]
    [InlineData(null, "2026-01-02", "2026-01-01")]
    [InlineData("unknown", null, null)]
    [InlineData("all", "2026-01-01", null)]
    [InlineData("all", null, "2026-01-01")]
    public void Parse_WhenParametersConflict_ReturnsTypedError(string? range, string? from, string? to)
    {
        DateRangeParser.Parse(range, from, to, out _).Should().NotBeNull();
    }
}

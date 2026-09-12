using BidParser.Domain;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class DellQuoteIdTests
{
    [Theory]
    [InlineData("9000000000003.1", "9000000000003", "1")]
    [InlineData("9000000000003.0", "9000000000003", "0")]
    [InlineData("9000000000003.99", "9000000000003", "99")]
    public void TryParse_accepts_canonical_id(string value, string number, string version)
    {
        DellQuoteId.TryParse(value, out var parsedNumber, out var parsedVersion).Should().BeTrue();
        parsedNumber.Should().Be(number);
        parsedVersion.Should().Be(version);
    }

    [Theory]
    [InlineData("9000000000003")]
    [InlineData("370002813141.1")]
    [InlineData("90000000000037.1")]
    [InlineData("9000000000003.100")]
    [InlineData("9000000000003.")]
    [InlineData(".1")]
    [InlineData("9000000000003-1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9000000000003.1 ; DROP TABLE")]
    public void TryParse_rejects_noncanonical_id(string value)
    {
        DellQuoteId.TryParse(value, out _, out _).Should().BeFalse();
    }
}

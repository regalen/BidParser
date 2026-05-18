using BidParser.Parsing.Cleaning;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class CleaningTests
{
    [Fact]
    public void Clean_collapses_whitespace_and_handles_null()
    {
        TextCleaner.Clean(null).Should().BeEmpty();
        TextCleaner.Clean("  alpha \n beta\t\tgamma  ").Should().Be("alpha beta gamma");
    }

    [Fact]
    public void JoinSpaced_preserves_wrapped_hyphenated_values()
    {
        TextCleaner.JoinSpaced(["NX-1175S-G10-", "6517P-CM"]).Should().Be("NX-1175S-G10-6517P-CM");
        TextCleaner.JoinSpaced(["Support", "Term in months"]).Should().Be("Support Term in months");
    }

    [Fact]
    public void JoinUnspaced_concatenates_clean_fragments()
    {
        TextCleaner.JoinUnspaced(["24SW000351227,", " LIC-02472987 "]).Should().Be("24SW000351227,LIC-02472987");
    }

    [Theory]
    [InlineData("USD 2,275.00", "2275.00")]
    [InlineData("$1,625,358.51", "1625358.51")]
    [InlineData(" 54.41 ", "54.41")]
    public void ParseDecimal_strips_currency_noise(string input, string expected)
    {
        DecimalCleaner.Parse(input).Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ParseDecimal_can_default_empty_values_to_zero()
    {
        DecimalCleaner.Parse("", defaultZero: true).Should().Be(0m);
        FluentActions.Invoking(() => DecimalCleaner.Parse("")).Should().Throw<FormatException>();
    }

    [Fact]
    public void ParseInt_and_optional_int_match_python_helpers()
    {
        DecimalCleaner.ParseInt("2,096").Should().Be(2096);
        DecimalCleaner.ParseInt("60.0").Should().Be(60);
        DecimalCleaner.ParseOptionalInt("").Should().BeNull();
        DecimalCleaner.ParseOptionalInt(" 60 ").Should().Be(60);
    }

    [Fact]
    public void ParseMmDdYyyy_returns_date_only()
    {
        DateCleaner.ParseMmDdYyyy("07/13/2026").Should().Be(new DateOnly(2026, 7, 13));
    }
}

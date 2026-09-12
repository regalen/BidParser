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
    [InlineData("CAS-|STKPTA5P", "CAS-STKPTA5P")]
    [InlineData("ACC-|STKATTA5|P", "ACC-STKATTA5P")]
    [InlineData("CAS-STK APP IPAD|AIR 11 2024 RGD|HSL", "CAS-STK APP IPAD AIR 11 2024 RGD HSL")]
    [InlineData("$319.5|0", "$319.50")]
    public void JoinWrapped_distinguishes_word_and_token_wraps(string joined, string expected)
    {
        TextCleaner.JoinWrapped(joined.Split('|')).Should().Be(expected);
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

        // A currency label that bleeds into an integer cell (a List "USD" landing one column
        // left, in Term/Quantity) must be stripped, not throw. See XQ-9100012.
        DecimalCleaner.ParseInt("USD 128").Should().Be(128);
        DecimalCleaner.ParseOptionalInt("36 USD").Should().Be(36);
        DecimalCleaner.ParseOptionalInt("USD").Should().BeNull();
    }

    [Fact]
    public void ParseMmDdYyyy_returns_date_only()
    {
        DateCleaner.ParseMmDdYyyy("07/13/2026").Should().Be(new DateOnly(2026, 7, 13));
    }

    [Theory]
    // The HP Bid bundle case: the id is repeated in front of its own description.
    [InlineData("55623728-HP EliteBook 8 G2a 14", "55623728", "HP EliteBook 8 G2a 14")]
    // Only the leading prefix goes — a dirty tail (one real bundle ends in ".xlsx") survives.
    [InlineData("55636494-HP EliteBook 6 G1a 14 16GB/256GB.xlsx", "55636494", "HP EliteBook 6 G1a 14 16GB/256GB.xlsx")]
    // No prefix at all.
    [InlineData("HP S5 Pro 524pf FHD MNTR", "9D9L6UT", "HP S5 Pro 524pf FHD MNTR")]
    // The code appears, but not at the start.
    [InlineData("HP Dock 55623728-x", "55623728", "HP Dock 55623728-x")]
    // The code leads but the delimiter is not a hyphen.
    [InlineData("55623728 HP EliteBook", "55623728", "55623728 HP EliteBook")]
    // A blank Product Number/ID cell must not eat the description.
    [InlineData("-HP EliteBook", "", "-HP EliteBook")]
    // Stripping everything would leave a blank description — keep the original instead.
    [InlineData("55623728-", "55623728", "55623728-")]
    // A repeated prefix loses only the first occurrence.
    [InlineData("55623728-55623728-HP EliteBook", "55623728", "55623728-HP EliteBook")]
    public void StripLeadingCode_removes_only_an_exact_leading_code_hyphen(string description, string code, string expected)
    {
        DescriptionCleaner.StripLeadingCode(description, code).Should().Be(expected);
    }
}

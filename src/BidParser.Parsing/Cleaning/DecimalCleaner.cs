using System.Globalization;
using System.Text.RegularExpressions;

namespace BidParser.Parsing.Cleaning;

/// <summary>
/// Parses money/quantity cells, stripping currency noise ("$", "USD", "AUD", thousands separators,
/// whitespace) before conversion. The Optional / <c>defaultZero</c> variants treat a cell that is
/// empty once noise is stripped as absent rather than a parse error.
/// </summary>
public static partial class DecimalCleaner
{
    /// <summary>Parses a decimal; empty input returns 0 when <paramref name="defaultZero"/> is set, else throws.</summary>
    public static decimal Parse(object? value, bool defaultZero = false)
    {
        var text = TextCleaner.Clean(value);
        if (text.Length == 0)
        {
            if (defaultZero)
            {
                return 0m;
            }

            throw new FormatException("Expected decimal value, got empty string.");
        }

        var cleaned = CurrencyNoise().Replace(text, string.Empty);
        if (cleaned.Length == 0)
        {
            if (defaultZero)
            {
                return 0m;
            }

            throw new FormatException($"Could not parse decimal from '{value}'.");
        }

        return decimal.Parse(cleaned, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public static int ParseInt(object? value)
    {
        // Strip the same currency noise the decimal path strips, so a stray "USD" label that
        // bleeds into an integer cell (e.g. a List "USD" landing in the Term column) does not
        // trip decimal.Parse. Integer cells never legitimately contain currency symbols.
        var cleaned = CurrencyNoise().Replace(TextCleaner.Clean(value), string.Empty);
        return (int)decimal.Parse(cleaned, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public static int? ParseOptionalInt(object? value)
    {
        // Treat a cell that has no digits once currency noise is stripped (e.g. a bare "USD")
        // as absent rather than a parse failure.
        var cleaned = CurrencyNoise().Replace(TextCleaner.Clean(value), string.Empty);
        return cleaned.Length == 0 ? null : ParseInt(cleaned);
    }

    [GeneratedRegex(@"AUD|USD|\$|,|\s")]
    private static partial Regex CurrencyNoise();
}

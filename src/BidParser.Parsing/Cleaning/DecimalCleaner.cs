using System.Globalization;
using System.Text.RegularExpressions;

namespace BidParser.Parsing.Cleaning;

public static partial class DecimalCleaner
{
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
        var text = TextCleaner.Clean(value).Replace(",", string.Empty, StringComparison.Ordinal);
        return (int)decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public static int? ParseOptionalInt(object? value)
    {
        var text = TextCleaner.Clean(value);
        return text.Length == 0 ? null : ParseInt(text);
    }

    [GeneratedRegex(@"AUD|USD|\$|,|\s")]
    private static partial Regex CurrencyNoise();
}

namespace BidParser.Parsing.Cleaning;

/// <summary>
/// Removes every character outside printable ASCII (0x20-0x7E) from a value, then re-collapses
/// whitespace. Opt-in per parser — do not fold this into <see cref="TextCleaner.Clean"/>, which
/// would change golden output for every other vendor.
/// </summary>
public static class AsciiCleaner
{
    public static string StripNonAscii(object? value)
    {
        var text = TextCleaner.Clean(value);
        var stripped = new string(text.Where(c => c is >= (char)0x20 and <= (char)0x7E).ToArray());
        return TextCleaner.Clean(stripped);
    }
}

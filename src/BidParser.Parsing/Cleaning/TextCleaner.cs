using System.Text.RegularExpressions;

namespace BidParser.Parsing.Cleaning;

public static partial class TextCleaner
{
    public static string Clean(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var text = value switch
        {
            string s => s,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };

        return Whitespace().Replace(text.Trim(), " ");
    }

    public static string JoinSpaced(IEnumerable<object?> parts)
    {
        var text = Clean(string.Join(' ', parts.Select(Clean).Where(part => part.Length > 0)));
        return HyphenBreak().Replace(text, "-");
    }

    public static string JoinUnspaced(IEnumerable<object?> parts)
    {
        return Clean(string.Concat(parts.Select(Clean).Where(part => part.Length > 0)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=\w)-\s+(?=\w)")]
    private static partial Regex HyphenBreak();
}

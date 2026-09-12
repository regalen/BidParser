using System.Text.RegularExpressions;

namespace BidParser.Parsing.Cleaning;

/// <summary>
/// Normalises raw cell/word text: trims and collapses internal whitespace to single spaces, and
/// joins multi-part values. <see cref="JoinSpaced"/> also repairs hyphenated line-breaks
/// ("Enter- prise" → "Enterprise"); <see cref="JoinUnspaced"/> concatenates with no separator.
/// </summary>
public static partial class TextCleaner
{
    /// <summary>Trims and collapses runs of whitespace in a single value to single spaces.</summary>
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

    /// <summary>
    /// Joins PDF line-wrap fragments without corrupting identifiers. A fragment containing spaces
    /// ended at a word boundary, while a single token was split mid-token.
    /// </summary>
    public static string JoinWrapped(IEnumerable<object?> parts)
    {
        var cleaned = parts.Select(Clean).Where(part => part.Length > 0).ToList();
        if (cleaned.Count == 0) return string.Empty;

        var result = cleaned[0];
        for (var i = 1; i < cleaned.Count; i++)
        {
            result += (cleaned[i - 1].Contains(' ') ? " " : string.Empty) + cleaned[i];
        }
        return Clean(result);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=\w)-\s+(?=\w)")]
    private static partial Regex HyphenBreak();
}

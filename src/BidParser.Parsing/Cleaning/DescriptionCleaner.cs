namespace BidParser.Parsing.Cleaning;

/// <summary>
/// Removes a redundant leading "{code}-" from a description when the source repeats the product
/// code at the front of its own description text. Opt-in per parser — do not fold this into
/// <see cref="TextCleaner.Clean"/>, which would change golden output for every other vendor.
/// </summary>
public static class DescriptionCleaner
{
    /// <summary>
    /// Strips an exact leading "<paramref name="code"/>-" from <paramref name="description"/>.
    /// A blank code, an absent prefix, or a strip that would leave nothing behind all return the
    /// description unchanged — the rule never costs the line its description.
    /// </summary>
    public static string StripLeadingCode(string description, string code)
    {
        if (code.Length == 0)
        {
            return description;
        }

        var prefix = code + '-';
        if (!description.StartsWith(prefix, StringComparison.Ordinal))
        {
            return description;
        }

        var stripped = description[prefix.Length..].Trim();
        return stripped.Length > 0 ? stripped : description;
    }
}

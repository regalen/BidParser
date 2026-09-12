using System.Text.RegularExpressions;

namespace BidParser.Domain;

/// <summary>Parses Dell's canonical quote identifier into slash-separated API path segments.</summary>
public static class DellQuoteId
{
    private static readonly Regex Pattern = new(
        @"^(\d{13})\.(\d{1,2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out string quoteNumber, out string quoteVersion)
    {
        var match = value is null ? Match.Empty : Pattern.Match(value);
        if (!match.Success)
        {
            quoteNumber = string.Empty;
            quoteVersion = string.Empty;
            return false;
        }

        quoteNumber = match.Groups[1].Value;
        quoteVersion = match.Groups[2].Value;
        return true;
    }
}

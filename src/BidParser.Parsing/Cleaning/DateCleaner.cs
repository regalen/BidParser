using System.Globalization;

namespace BidParser.Parsing.Cleaning;

/// <summary>Parses vendor date cells in the US "MM/dd/yyyy" format into a <see cref="DateOnly"/>.</summary>
public static class DateCleaner
{
    /// <summary>Parses a "MM/dd/yyyy" value; throws if it does not match that exact format.</summary>
    public static DateOnly ParseMmDdYyyy(object? value)
    {
        return DateOnly.ParseExact(TextCleaner.Clean(value), "MM/dd/yyyy", CultureInfo.InvariantCulture);
    }
}

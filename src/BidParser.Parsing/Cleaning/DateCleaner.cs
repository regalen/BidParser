using System.Globalization;

namespace BidParser.Parsing.Cleaning;

public static class DateCleaner
{
    public static DateOnly ParseMmDdYyyy(object? value)
    {
        return DateOnly.ParseExact(TextCleaner.Clean(value), "MM/dd/yyyy", CultureInfo.InvariantCulture);
    }
}

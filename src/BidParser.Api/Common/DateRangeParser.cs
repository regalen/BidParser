using System.Globalization;
using BidParser.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace BidParser.Api.Common;

public sealed record ParsedDateRange(bool IsAll, DateTime? FromUtc, DateTime? ToUtc, string? FromStr, string? ToStr);

public static class DateRangeParser
{
    public static IResult? Parse(string? range, string? from, string? to, out ParsedDateRange parsedRange)
    {
        parsedRange = default!;

        if (range == "all")
        {
            if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
            {
                return Results.BadRequest(new ApiError("range=all cannot be combined with 'from' or 'to' dates."));
            }
            parsedRange = new ParsedDateRange(true, null, null, null, null);
            return null;
        }

        if (!string.IsNullOrEmpty(range) && range != "all")
        {
            return Results.BadRequest(new ApiError("Unknown 'range' parameter."));
        }

        if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to))
        {
            var defaultTo = DateTime.Today;
            var defaultFrom = defaultTo.AddDays(-29);
            parsedRange = new ParsedDateRange(false, defaultFrom.ToUniversalTime(), defaultTo.AddDays(1).ToUniversalTime(), defaultFrom.ToString("yyyy-MM-dd"), defaultTo.ToString("yyyy-MM-dd"));
            return null;
        }

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return Results.BadRequest(new ApiError("Both 'from' and 'to' dates are required for a custom range."));
        }

        if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
        {
            return Results.BadRequest(new ApiError("Invalid 'from' date — expected yyyy-MM-dd."));
        }

        if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            return Results.BadRequest(new ApiError("Invalid 'to' date — expected yyyy-MM-dd."));
        }

        if (fromDate > toDate)
        {
            return Results.BadRequest(new ApiError("'from' date cannot be after 'to' date."));
        }

        parsedRange = new ParsedDateRange(false, fromDate.ToUniversalTime(), toDate.AddDays(1).ToUniversalTime(), fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));
        return null;
    }
}

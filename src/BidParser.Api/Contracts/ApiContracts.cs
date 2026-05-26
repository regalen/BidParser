namespace BidParser.Api.Contracts;

public sealed record ApiError(string Detail);

public sealed record OkResponse(bool Ok = true);

public sealed record ParseErrorDetail(string Stage, string Hint, string Message);

public sealed record ParseErrorResponse(ParseErrorDetail Detail);

public sealed record PasswordValidationError(IReadOnlyList<string> Detail);

public sealed record MetricsKpis(
    int TotalParses,
    int ActiveUsers,
    int ActiveVendors,
    string MismatchRate
);

public sealed record MetricsByUser(
    int? UserId,
    string Username,
    string? Name,
    int Count
);

public sealed record MetricsByVendor(
    string Vendor,
    int Count
);

public sealed record MetricsByParser(
    string ParserSlug,
    string DisplayName,
    int Count
);

public sealed record MetricsTimeSeries(
    string Date,
    int Count
);

public sealed record MetricsSummaryResponse(
    MetricsDateRange Range,
    MetricsKpis Kpis,
    IReadOnlyList<MetricsByUser> ByUser,
    IReadOnlyList<MetricsByVendor> ByVendor,
    IReadOnlyList<MetricsByParser> ByParser,
    IReadOnlyList<MetricsTimeSeries> TimeSeries
);

public sealed record MetricsDateRange(
    string From,
    string To
);

public sealed record FailedParseJobItem(
    int Id,
    DateTime CreatedAt,
    int? UserId,
    string Username,
    string? Name,
    string Vendor,
    string ParserSlug,
    string ParserDisplayName,
    string SourceFilename,
    string Category,
    string? Stage,
    string? Hint,
    string? Message,
    string ErrorDetail,
    bool SourceAvailable,
    // Populated only for validation_mismatch entries; null for exception categories.
    string? ComputedTotal,
    string? QuotedTotal
);

public sealed record FailedParseJobListResponse(
    int Total,
    IReadOnlyList<FailedParseJobItem> Items
);

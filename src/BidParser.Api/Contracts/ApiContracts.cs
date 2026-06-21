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

public sealed record MonitoringRunItem(
    // "job" → download via /jobs/{id}/source|output; "failure" → /failures/{id}/source.
    string Kind,
    int Id,
    // "success", "validation_mismatch", "magic_byte_mismatch", "parser_error", "unhandled_exception".
    string Status,
    DateTime CreatedAt,
    int? UserId,
    string Username,
    string? Name,
    string Vendor,
    string ParserSlug,
    string ParserDisplayName,
    string SourceFilename,
    bool SourceAvailable,
    bool OutputAvailable,
    string? ComputedTotal,
    string? QuotedTotal,
    // Populated for failure rows only; null for successful/mismatch jobs.
    string? Stage,
    string? Hint,
    string? Message,
    string? ErrorDetail
);

public sealed record MonitoringRunsResponse(
    int Total,
    IReadOnlyList<MonitoringRunItem> Items
);

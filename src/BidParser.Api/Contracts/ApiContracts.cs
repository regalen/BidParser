using System.Text.Json.Serialization;
using BidParser.Api.Serialization;

namespace BidParser.Api.Contracts;

// Typed response records for the API (serialised camelCase by the ASP.NET Core Web defaults — there is no
// global naming policy, and no per-property [JsonPropertyName]). There are three distinct error body shapes
// and the SPA branches on shape: ApiError (a single "detail" string), ParseErrorResponse (a nested
// {stage, hint, message} object), and PasswordValidationError (a list of messages under "detail").

/// <summary>Generic error body: a single machine-readable detail string (e.g. "notAuthenticated").</summary>
public sealed record ApiError(string Detail);

public sealed record OkResponse(bool Ok = true);

public sealed record ParseErrorDetail(string Stage, string Hint, string Message);

public sealed record ParseErrorResponse(ParseErrorDetail Detail);

public sealed record PasswordValidationError(IReadOnlyList<string> Detail);

public sealed record DellApiSettingsResponse(
    string TokenUrl,
    string ClientId,
    bool ClientSecretConfigured,
    string QuoteUrlTemplate,
    string DefaultLocale,
    string ClientIdHeader,
    string ApiVersion,
    bool UseBasicAuthForToken,
    DateTime? UpdatedAt);

public sealed record DellApiSettingsUpdateRequest(
    string TokenUrl,
    string ClientId,
    string? ClientSecret,
    string QuoteUrlTemplate,
    string DefaultLocale,
    string ClientIdHeader,
    string ApiVersion,
    bool UseBasicAuthForToken);

public sealed record RuntimeConfigUpdateRequest(string Json);

public sealed record RuntimeConfigDocumentResponse(string Json, DateTime? UpdatedAt, string? ValidationError);

public sealed record RuntimeConfigParserReferenceResponse(string Slug, string DisplayName, string Vendor);

public sealed record RuntimeConfigFieldResponse(string Key, string Label, int Scale);

public sealed record RuntimeConfigurationAdminResponse(
    RuntimeConfigDocumentResponse GuidanceMessages,
    RuntimeConfigDocumentResponse VendorDefaults,
    IReadOnlyList<RuntimeConfigParserReferenceResponse> Parsers,
    IReadOnlyList<string> Vendors,
    IReadOnlyList<RuntimeConfigFieldResponse> SupportedFields,
    IReadOnlyList<string> HtmlAllowlist);

public sealed record RuntimeVendorDefaultsResponse(
    [property: JsonConverter(typeof(FxRateConverter))] decimal? FxRate,
    [property: JsonConverter(typeof(PercentageConverter))] decimal? Margin,
    [property: JsonConverter(typeof(PercentageConverter))] decimal? ImPercent,
    [property: JsonConverter(typeof(PercentageConverter))] decimal? OnCostPct);

public sealed record ParseUiConfigResponse(
    IReadOnlyDictionary<string, RuntimeVendorDefaultsResponse> VendorDefaults,
    IReadOnlyDictionary<string, string> GuidanceByParserSlug);

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

public sealed record MetricsByImportType(
    string? ImportType,
    int Count
);

public sealed record MetricsTimeSeries(
    string Date,
    int Count
);

public sealed record MetricsSummaryResponse(
    MetricsDateRange Range,
    string TimeSeriesGranularity,
    MetricsKpis Kpis,
    IReadOnlyList<MetricsByUser> ByUser,
    IReadOnlyList<MetricsByVendor> ByVendor,
    IReadOnlyList<MetricsByParser> ByParser,
    IReadOnlyList<MetricsByImportType> ByImportType,
    IReadOnlyList<MetricsTimeSeries> TimeSeries
);

public sealed record MetricsDateRange(
    string Mode,
    string? From,
    string? To
);

public sealed record MonitoringRunItem(
    // "job" → download via /jobs/{id}/source|output; "failure" → /failures/{id}/source.
    string Kind,
    int Id,
    // "success", "validationMismatch", "magicByteMismatch", "parserError", "unhandledException".
    string Status,
    DateTime CreatedAt,
    int? UserId,
    string Username,
    string? Name,
    string Vendor,
    string ParserSlug,
    string ParserDisplayName,
    string? ImportType,
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

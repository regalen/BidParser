using BidParser.Api.Auth;
using BidParser.Application.Parsing;

namespace BidParser.Api.Endpoints;

/// <summary>
/// GET /api/parsers (active users): lists every registered parser with its slug, display name, vendor,
/// MIME and CRM templates. Also synthesizes auto-detect entries for eligible vendors.
/// This feeds the SPA's file-type dropdown — dropdowns auto-populate from here.
/// </summary>
public static class ParsersEndpoints
{
    public static IEndpointRouteBuilder MapParsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parsers", ListParsers)
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private static IResult ListParsers(ParserCatalog catalog)
    {
        return Results.Ok(catalog.GetAll().Select(parser => new ParserInfo(
            parser.Slug,
            parser.DisplayName,
            parser.Vendor,
            parser.AcceptedMime,
            parser.AcceptedMimes,
            parser.CrmTemplate,
            parser.AvailableTemplates,
            parser.SupportsSubComponentDetail,
            parser.SupportsSolutionIdSplit,
            parser.SupportsOnCost,
            parser.SolutionSplitLabel)));
    }

    /// <summary>Per-parser info surfaced to the SPA.</summary>
    private sealed record ParserInfo(
        string Slug,
        string DisplayName,
        string Vendor,
        string AcceptedMime,
        IReadOnlyList<string> AcceptedMimes,
        string CrmTemplate,
        IReadOnlyList<string> AvailableTemplates,
        bool SupportsSubComponentDetail,
        bool SupportsSolutionIdSplit,
        bool SupportsOnCost,
        string SolutionSplitLabel);
}

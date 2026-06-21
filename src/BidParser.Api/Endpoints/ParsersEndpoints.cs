using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;

namespace BidParser.Api.Endpoints;

public static class ParsersEndpoints
{
    public static IEndpointRouteBuilder MapParsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parsers", ListParsers)
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private static IResult ListParsers(IParserRegistry registry)
    {
        // Report type per parser slug comes from the hardcoded ReportTypes map
        // (shared with the desktop app). Unmapped slugs render no guidance.
        var parsers = registry.Parsers
            .Select(p => new ParserInfo(
                p.Slug,
                p.DisplayName,
                p.Vendor,
                p.AcceptedMime,
                p.CrmTemplate,
                p.AvailableTemplates.ToList(),
                ReportTypes.For(p.Slug)))
            .ToList();

        return Results.Ok(parsers);
    }

    private sealed record ParserInfo(
        string Slug,
        string DisplayName,
        string Vendor,
        string AcceptedMime,
        string CrmTemplate,
        IReadOnlyList<string> AvailableTemplates,
        string? ReportType);
}

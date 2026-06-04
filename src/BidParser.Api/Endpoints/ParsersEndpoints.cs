using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class ParsersEndpoints
{
    public static IEndpointRouteBuilder MapParsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parsers", ListParsersAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private static async Task<IResult> ListParsersAsync(
        IParserRegistry registry,
        AppDbContext db,
        CancellationToken ct)
    {
        // Admin-configured report type per parser slug. Absent slugs render no
        // report-type guidance in the user's parse-result popup.
        var reportTypes = await db.ReportTypeConfigs
            .ToDictionaryAsync(c => c.ParserSlug, c => c.ReportType, ct);

        var parsers = registry.Parsers
            .Select(p => new ParserInfo(
                p.Slug,
                p.DisplayName,
                p.Vendor,
                p.AcceptedMime,
                p.CrmTemplate,
                p.AvailableTemplates.ToList(),
                reportTypes.GetValueOrDefault(p.Slug)))
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

using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;

namespace BidParser.Api.Endpoints;

public static class ParsersEndpoints
{
    public static IEndpointRouteBuilder MapParsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parsers", (IParserRegistry registry) =>
            Results.Ok(registry.Parsers
                .Select(p => new ParserInfo(p.Slug, p.DisplayName, p.Vendor, p.AcceptedMime, p.CrmTemplate))
                .ToList()))
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private sealed record ParserInfo(string Slug, string DisplayName, string Vendor, string AcceptedMime, string CrmTemplate);
}

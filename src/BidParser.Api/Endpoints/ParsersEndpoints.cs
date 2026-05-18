using BidParser.Api.Auth;

namespace BidParser.Api.Endpoints;

public static class ParsersEndpoints
{
    public static IEndpointRouteBuilder MapParsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parsers", () => Results.Ok(Array.Empty<ParserInfo>()))
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private sealed record ParserInfo(string Slug, string DisplayName, string Vendor, string AcceptedMime, string CrmTemplate);
}

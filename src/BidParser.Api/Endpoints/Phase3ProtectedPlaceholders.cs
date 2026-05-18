using BidParser.Api.Auth;

namespace BidParser.Api.Endpoints;

public static class Phase3ProtectedPlaceholders
{
    public static IEndpointRouteBuilder MapPhase3ProtectedPlaceholders(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/parse", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>();
        app.MapGet("/api/history", () => Results.Ok(new { rows = Array.Empty<object>(), total = 0 }))
            .RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }
}

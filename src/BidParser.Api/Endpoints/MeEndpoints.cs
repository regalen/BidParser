using BidParser.Api.Auth;
using BidParser.Infrastructure.Persistence;

namespace BidParser.Api.Endpoints;

public static class MeEndpoints
{
    private static readonly HashSet<string> KnownVendors = new(StringComparer.Ordinal) { "Nutanix" };

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", MeAsync).RequireAuthorization(AuthPolicies.LoggedIn);
        app.MapPatch("/api/me/settings", UpdateSettingsAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    private static async Task<IResult> MeAsync(HttpContext context, AppDbContext db)
    {
        var user = await EndpointHelpers.CurrentUserAsync(context, db);
        return user is null
            ? Results.Json(new { detail = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(UserPublic.FromEntity(user));
    }

    private static async Task<IResult> UpdateSettingsAsync(HttpContext context, HttpRequest request, AppDbContext db)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<SettingsUpdateRequest>(request);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db);
        if (user is null)
        {
            return Results.Json(new { detail = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (body.Value.DefaultVendor is not null)
        {
            var vendor = body.Value.DefaultVendor.Trim();
            if (!KnownVendors.Contains(vendor))
            {
                return Results.Json(new { detail = "Unknown vendor." }, statusCode: StatusCodes.Status400BadRequest);
            }

            user.DefaultVendor = vendor;
        }

        if (body.Value.FxRate is not null)
        {
            if (body.Value.FxRate < 0)
            {
                return EndpointHelpers.ValidationProblem("Input should be greater than or equal to 0.");
            }
            user.FxRate = decimal.Round(body.Value.FxRate.Value, 4, MidpointRounding.AwayFromZero);
        }

        if (body.Value.Margin is not null)
        {
            if (body.Value.Margin < 0)
            {
                return EndpointHelpers.ValidationProblem("Input should be greater than or equal to 0.");
            }
            user.Margin = decimal.Round(body.Value.Margin.Value, 2, MidpointRounding.AwayFromZero);
        }

        await db.SaveChangesAsync();
        return Results.Ok(UserPublic.FromEntity(user));
    }

    private sealed record SettingsUpdateRequest(string? DefaultVendor, decimal? FxRate, decimal? Margin);
}

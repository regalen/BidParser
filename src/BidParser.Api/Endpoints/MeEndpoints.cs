using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Persistence;

namespace BidParser.Api.Endpoints;

public static class MeEndpoints
{
    private static readonly HashSet<string> KnownVendors = new(StringComparer.Ordinal) { Vendors.Nutanix, Vendors.Hp, Vendors.Lenovo };

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", MeAsync).RequireAuthorization(AuthPolicies.LoggedIn);
        app.MapPatch("/api/me/settings", UpdateSettingsAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    private static async Task<IResult> MeAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        return user is null
            ? Results.Json(new ApiError("not_authenticated"), statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(UserPublic.FromEntity(user));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        HttpContext context,
        HttpRequest request,
        AppDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<SettingsUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("not_authenticated"), statusCode: StatusCodes.Status401Unauthorized);
        }

        if (body.Value.DefaultVendor is not null)
        {
            var vendor = body.Value.DefaultVendor.Trim();
            if (!KnownVendors.Contains(vendor))
            {
                return Results.Json(new ApiError("Unknown vendor."), statusCode: StatusCodes.Status400BadRequest);
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

        if (body.Value.ImPercent is not null)
        {
            if (body.Value.ImPercent < 0)
            {
                return EndpointHelpers.ValidationProblem("Input should be greater than or equal to 0.");
            }
            user.ImPercent = decimal.Round(body.Value.ImPercent.Value, 2, MidpointRounding.AwayFromZero);
        }

        await db.SaveChangesAsync(ct);
        loggerFactory.CreateLogger(nameof(MeEndpoints)).LogInformation(
            "Settings update user={UserId} vendor_set={VendorSet} fx_set={FxSet} margin_set={MarginSet} im_percent_set={ImPercentSet}",
            user.Id,
            body.Value.DefaultVendor is not null,
            body.Value.FxRate is not null,
            body.Value.Margin is not null,
            body.Value.ImPercent is not null);
        return Results.Ok(UserPublic.FromEntity(user));
    }

    private sealed record SettingsUpdateRequest(string? DefaultVendor, decimal? FxRate, decimal? Margin, decimal? ImPercent);
}

using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Infrastructure.Dell;

namespace BidParser.Api.Endpoints;

/// <summary>Admin-only Dell Quote API configuration; the client secret is write-only.</summary>
public static class DellSettingsEndpoints
{
    public static IEndpointRouteBuilder MapDellSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dell-settings")
            .RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync).AddEndpointFilter<RequireCsrfHeader>();
        group.MapPost("/test", TestConnectionAsync).AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    private static async Task<IResult> GetAsync(
        DellApiSettingsService settingsService,
        CancellationToken ct)
    {
        var view = await settingsService.GetForAdminAsync(ct);
        return Results.Ok(ToResponse(view));
    }

    private static async Task<IResult> UpdateAsync(
        HttpRequest request,
        DellApiSettingsService settingsService,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<DellApiSettingsUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        try
        {
            await settingsService.SaveAsync(new DellApiSettingsUpdate(
                body.Value.TokenUrl,
                body.Value.ClientId,
                body.Value.ClientSecret,
                body.Value.QuoteUrlTemplate,
                body.Value.DefaultLocale,
                body.Value.ClientIdHeader,
                body.Value.ApiVersion,
                body.Value.UseBasicAuthForToken), ct);
        }
        catch (DellApiSettingsValidationException ex)
        {
            return Results.Json(new ApiError(ex.Detail), statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(ToResponse(await settingsService.GetForAdminAsync(ct)));
    }

    private static async Task<IResult> TestConnectionAsync(
        DellApiSettingsService settingsService,
        DellAuthTokenProvider tokenProvider,
        ILogger<DellApiSettingsService> logger,
        CancellationToken ct)
    {
        var config = await settingsService.GetAsync(ct);
        if (config is null)
        {
            return Results.Json(new ApiError(DellApiMessages.NotConfigured), statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        try
        {
            tokenProvider.Invalidate();
            await tokenProvider.GetTokenAsync(config, ct);
            return Results.Ok(new OkResponse());
        }
        catch (DellApiException ex)
        {
            logger.LogWarning(ex,
                "Dell API connection test failed kind={Kind} status={Status}",
                ex.Kind,
                ex.HttpStatus);
            return Results.Json(new ApiError(ex.UserMessage), statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static DellApiSettingsResponse ToResponse(DellApiSettingsView view) => new(
        view.TokenUrl,
        view.ClientId,
        view.ClientSecretConfigured,
        view.QuoteUrlTemplate,
        view.DefaultLocale,
        view.ClientIdHeader,
        view.ApiVersion,
        view.UseBasicAuthForToken,
        view.UpdatedAt);
}

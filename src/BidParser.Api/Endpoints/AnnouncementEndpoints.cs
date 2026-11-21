using System.Globalization;
using System.Security.Claims;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Infrastructure.Services;

namespace BidParser.Api.Endpoints;

/// <summary>The site announcement banner: an active-user read for the current session and the admin editor.</summary>
public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/announcement", GetActiveAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser);

        var admin = app.MapGroup("/api/admin/announcement")
            .RequireAuthorization(AuthPolicies.Admin);
        admin.MapGet("", GetAdminAsync);
        admin.MapPut("", UpdateAsync)
            .AddEndpointFilter<RequireCsrfHeader>();
        return app;
    }

    private static async Task<IResult> GetActiveAsync(HttpContext context, AnnouncementService service, CancellationToken ct)
    {
        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId)
            || !long.TryParse(context.User.FindFirstValue(SessionCookieAuthHandler.SessionIssuedAtClaim), CultureInfo.InvariantCulture, out var issuedAt))
        {
            return Results.Ok(new ActiveAnnouncementResponse(null, null));
        }

        var active = await service.GetActiveAsync(userId, issuedAt, ct);
        return Results.Ok(new ActiveAnnouncementResponse(active?.Html, active?.DismissalKey));
    }

    private static async Task<IResult> GetAdminAsync(AnnouncementService service, CancellationToken ct) =>
        Results.Ok(ToResponse(await service.GetDocumentAsync(ct)));

    private static async Task<IResult> UpdateAsync(HttpRequest request, AnnouncementService service, CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<AnnouncementUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        try
        {
            return Results.Ok(ToResponse(await service.SaveAsync(body.Value.Enabled, body.Value.Html, ct)));
        }
        catch (RuntimeConfigurationValidationException exception)
        {
            return EndpointHelpers.ValidationProblem(exception.Detail);
        }
    }

    private static AnnouncementAdminResponse ToResponse(AnnouncementDocument document) =>
        new(document.Enabled, document.Html, document.UpdatedAt, document.ValidationError, AnnouncementService.HtmlAllowlist);
}

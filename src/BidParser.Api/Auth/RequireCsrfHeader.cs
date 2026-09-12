using BidParser.Api.Contracts;

namespace BidParser.Api.Auth;

/// <summary>
/// CSRF guard endpoint filter: rejects any non-GET request lacking the custom "X-Requested-With: BidParser"
/// header (403 csrfRequired). A browser cannot set that custom header on a cross-origin form post, so its
/// presence proves the request came from the SPA.
/// </summary>
public sealed class RequireCsrfHeader : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            && context.HttpContext.Request.Headers["X-Requested-With"] != "BidParser")
        {
            return Results.Json(new ApiError("csrfRequired"), statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

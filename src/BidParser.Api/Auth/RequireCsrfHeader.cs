using BidParser.Api.Contracts;

namespace BidParser.Api.Auth;

public sealed class RequireCsrfHeader : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            && context.HttpContext.Request.Headers["X-Requested-With"] != "BidParser")
        {
            return Results.Json(new ApiError("csrf_required"), statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

using System.Security.Claims;
using System.Globalization;
using System.Text.Json;
using BidParser.Api.Contracts;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

internal static class EndpointHelpers
{
    public static async Task<BodyReadResult<T>> ReadJsonBodyAsync<T>(
        HttpRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var payload = await request.ReadFromJsonAsync<T>(cancellationToken: ct);
            return payload is null
                ? BodyReadResult<T>.Failure("Invalid request body.")
                : BodyReadResult<T>.Success(payload);
        }
        catch (JsonException)
        {
            return BodyReadResult<T>.Failure("Invalid request body.");
        }
        catch (BadHttpRequestException)
        {
            return BodyReadResult<T>.Failure("Invalid request body.");
        }
    }

    public static async Task<User?> CurrentUserAsync(
        HttpContext context,
        AppDbContext db,
        CancellationToken ct = default)
    {
        var idValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, CultureInfo.InvariantCulture, out var userId))
        {
            return null;
        }

        return await db.Users.SingleOrDefaultAsync(user => user.Id == userId, ct);
    }

    public static IResult ValidationProblem(string detail)
    {
        return Results.Json(new ApiError(detail), statusCode: StatusCodes.Status400BadRequest);
    }
}

internal sealed record BodyReadResult<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static BodyReadResult<T> Success(T value) => new(value, null);
    public static BodyReadResult<T> Failure(string error) => new(default, error);
}

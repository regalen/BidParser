using System.Text.Json.Serialization;
using BidParser.Api.Serialization;
using BidParser.Infrastructure.Entities;

namespace BidParser.Api.Endpoints;

/// <summary>Safe user projection returned by the API (no password hash); rates/margins serialise as fixed-scale strings.</summary>
public sealed record UserPublic(
    int Id,
    string Username,
    string? Name,
    string Role,
    bool MustChangePassword,
    string? DefaultVendor,
    [property: JsonConverter(typeof(FxRateConverter))] decimal? FxRate,
    [property: JsonConverter(typeof(PercentageConverter))] decimal? Margin,
    [property: JsonConverter(typeof(PercentageConverter))] decimal? ImPercent,
    DateTime? CreatedAt)
{
    public static UserPublic FromEntity(User user)
    {
        return new UserPublic(
            user.Id,
            user.Username,
            user.Name,
            user.Role.ToString().ToLowerInvariant(),
            user.MustChangePassword,
            user.DefaultVendor,
            user.FxRate,
            user.Margin,
            user.ImPercent,
            user.CreatedAt);
    }
}

public sealed record LoginResponse(UserPublic User);

// Returned from user create / reset. TempPassword is the one-time generated
// password (shown once so the admin can hand it over); null when no password
// was set/reset by the request.
public sealed record UserWithTempPassword(UserPublic User, string? TempPassword);

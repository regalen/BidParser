using System.Text.Json.Serialization;
using BidParser.Api.Serialization;
using BidParser.Infrastructure.Entities;

namespace BidParser.Api.Endpoints;

public sealed record UserPublic(
    int Id,
    string Username,
    string? Name,
    string Role,
    bool MustChangePassword,
    string? DefaultVendor,
    [property: JsonConverter(typeof(FxRateConverter))] decimal? FxRate,
    [property: JsonConverter(typeof(MarginConverter))] decimal? Margin,
    [property: JsonConverter(typeof(MarginConverter))] decimal? ImPercent,
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

public sealed class FxRateConverter : NullableJsonStringDecimalConverter
{
    public FxRateConverter() : base(4)
    {
    }
}

public sealed class MarginConverter : NullableJsonStringDecimalConverter
{
    public MarginConverter() : base(2)
    {
    }
}

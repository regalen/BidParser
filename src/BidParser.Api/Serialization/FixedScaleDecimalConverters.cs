using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BidParser.Api.Serialization;

/// <summary>Serialises nullable FX rates as fixed-scale strings with four decimal places.</summary>
public sealed class FxRateConverter : NullableJsonStringDecimalConverter
{
    public FxRateConverter() : base(4)
    {
    }
}

/// <summary>Serialises nullable percentage values as fixed-scale strings with two decimal places.</summary>
public sealed class PercentageConverter : NullableJsonStringDecimalConverter
{
    public PercentageConverter() : base(2)
    {
    }
}

/// <summary>Serialises non-nullable money/percentage values as fixed-scale strings with two decimal places.</summary>
public sealed class MoneyConverter : JsonStringDecimalConverter
{
    public MoneyConverter() : base(2)
    {
    }
}

/// <summary>Serialises decimals as invariant unscaled strings so unrounded source prices preserve full precision.</summary>
public sealed class UnscaledDecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numberValue))
        {
            return numberValue;
        }

        throw new JsonException("Invalid decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Nullable counterpart of <see cref="UnscaledDecimalStringConverter"/>.</summary>
public sealed class NullableUnscaledDecimalStringConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numberValue))
        {
            return numberValue;
        }

        throw new JsonException("Invalid decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BidParser.Api.Serialization;

/// <summary>
/// Serialises a decimal as a fixed-scale JSON string (e.g. "5.00") so money/rate values keep their scale
/// and never suffer float rounding on the wire; reads back from either a JSON string or number. Applied
/// per-field via [JsonConverter] (e.g. fxRate at 4 dp, margin/totals at 2 dp).
/// </summary>
public class JsonStringDecimalConverter : JsonConverter<decimal>
{
    private readonly int _scale;

    public JsonStringDecimalConverter(int scale)
    {
        _scale = scale;
    }

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
        writer.WriteStringValue(value.ToString($"F{_scale}", CultureInfo.InvariantCulture));
    }
}

/// <summary>Nullable counterpart of <see cref="JsonStringDecimalConverter"/>: writes null as JSON null, otherwise a fixed-scale string.</summary>
public class NullableJsonStringDecimalConverter : JsonConverter<decimal?>
{
    private readonly int _scale;

    public NullableJsonStringDecimalConverter(int scale)
    {
        _scale = scale;
    }

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

        writer.WriteStringValue(value.Value.ToString($"F{_scale}", CultureInfo.InvariantCulture));
    }
}

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

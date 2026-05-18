namespace BidParser.Domain.Models;

public static class ParseValidation
{
    private static readonly decimal Tolerance = 0.01m;

    public static ValidationResult Validate(IReadOnlyList<LineItem> lineItems, decimal? quotedTotal)
    {
        var computed = lineItems
            .Sum(item => item.Cost * item.Qty);
        computed = decimal.Round(computed, 2, MidpointRounding.AwayFromZero);

        var warnings = new List<string>();
        var difference = 0m;
        var matches = true;

        if (quotedTotal is null)
        {
            matches = false;
            warnings.Add("Quoted total not found.");
        }
        else
        {
            difference = decimal.Round(computed - quotedTotal.Value, 2, MidpointRounding.AwayFromZero);
            matches = Math.Abs(difference) <= Tolerance;
            if (!matches)
            {
                warnings.Add($"Computed total {computed:F2} differs from quoted total {quotedTotal.Value:F2}.");
            }
        }

        return new ValidationResult
        {
            ComputedTotal = computed,
            QuotedTotal = quotedTotal,
            Matches = matches,
            Difference = difference,
            Warnings = warnings
        };
    }
}

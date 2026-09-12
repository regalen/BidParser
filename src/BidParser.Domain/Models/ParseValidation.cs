namespace BidParser.Domain.Models;

/// <summary>
/// Shared total-validation, identical across formats: computed = Σ(cost × qty), compared to the
/// quoted total within a 0.01 tolerance. IMPORTANT: for formats with no quoted total, do NOT call
/// this (a null total yields Matches = false and trips the frontend mismatch modal on every parse) —
/// build the <see cref="ValidationResult"/> directly with Matches = true. See AGENTS.md.
/// </summary>
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
            // No total to compare against: report a non-match with a warning rather than silently
            // passing. Formats that legitimately lack a total must bypass this method (see summary).
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

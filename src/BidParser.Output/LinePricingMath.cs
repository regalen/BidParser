using BidParser.Domain.Models;

namespace BidParser.Output;

/// <summary>Totals produced by <see cref="LinePricingMath.Summarise"/>.</summary>
public sealed record LinePricingTotals(
    decimal TotalCost,
    decimal TotalPrice,
    decimal MarginAmount,
    decimal? MarginPercent);

/// <summary>
/// Single source of truth for per-line pricing calculations (discount off MSRP, effective unit
/// cost, sale price preview, and quote totals). All rounding is 2 decimal places, midpoint away
/// from zero.
/// </summary>
public static class LinePricingMath
{
    public static decimal Round2(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal CostFromMsrp(decimal msrp, decimal imPercent) =>
        Round2(msrp * (1m - imPercent / 100m));

    public static decimal? DerivedImPercent(decimal sourceCost, decimal? msrp) =>
        msrp is > 0m ? Round2((1m - sourceCost / msrp.Value) * 100m) : null;

    public static decimal EffectiveUnitCost(LineItem item, bool calculatesCostFromMsrp, decimal? headerImPercent = null)
    {
        if (calculatesCostFromMsrp)
        {
            var msrp = item.Msrp ?? throw new ArgumentException("MSRP is required to calculate cost.", nameof(item));
            // A zero MSRP (e.g. an Iris "incl." component) costs zero whatever the discount, so it
            // needs no IM% — the review popups never offer one for it.
            if (msrp == 0m)
            {
                return 0m;
            }

            var im = item.ImPercent ?? headerImPercent
                ?? throw new ArgumentException("IM% is required to calculate cost from MSRP.", nameof(item));
            return CostFromMsrp(msrp, im);
        }

        if (item.ImPercent is not null && item.Msrp is > 0m)
        {
            return CostFromMsrp(item.Msrp.Value, item.ImPercent.Value);
        }

        return item.Cost;
    }

    public static decimal ExtCost(int qty, decimal unitCost) =>
        Round2(qty * unitCost);

    public static decimal UnitPrice(decimal unitCost, decimal margin)
    {
        if (margin < 0m || margin >= 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(margin), "Uplift must be at least 0 and less than 100.");
        }

        return Round2(unitCost / (1m - margin / 100m));
    }

    public static decimal ExtPrice(int qty, decimal unitPrice) =>
        Round2(qty * unitPrice);

    public static LinePricingTotals Summarise(
        IEnumerable<LineItem> items,
        bool calculatesCostFromMsrp,
        decimal headerMargin = 0m,
        decimal? headerImPercent = null)
    {
        var totalCost = 0m;
        var totalPrice = 0m;

        foreach (var item in items)
        {
            if (item.IsCancelled)
            {
                continue;
            }

            var unitCost = EffectiveUnitCost(item, calculatesCostFromMsrp, headerImPercent);
            var margin = item.Margin ?? headerMargin;
            var unitPrice = UnitPrice(unitCost, margin);

            totalCost += ExtCost(item.Qty, unitCost);
            totalPrice += ExtPrice(item.Qty, unitPrice);
        }

        var marginAmount = totalPrice - totalCost;
        decimal? marginPercent = totalPrice > 0m
            ? Round2(marginAmount / totalPrice * 100m)
            : null;

        return new LinePricingTotals(totalCost, totalPrice, marginAmount, marginPercent);
    }
}

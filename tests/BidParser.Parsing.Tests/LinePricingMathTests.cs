using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public class LinePricingMathTests
{
    [Fact]
    public void ReferenceCases_R1_And_R2_CostFromMsrp()
    {
        // R1: Iris MSRP 427, IM 15 -> unitCost 362.95
        LinePricingMath.CostFromMsrp(427m, 15m).Should().Be(362.95m);

        // R2: Iris MSRP 427, IM 27.53 -> unitCost 309.45
        LinePricingMath.CostFromMsrp(427m, 27.53m).Should().Be(309.45m);
    }

    [Fact]
    public void ReferenceCase_R3_UnitPrice()
    {
        // R3: unitCost 100, uplift 4.76 -> unitPrice 105.00
        LinePricingMath.UnitPrice(100m, 4.76m).Should().Be(105.00m);
    }

    [Fact]
    public void ReferenceCase_R4_And_R5_LineAndSummaryTotals()
    {
        // R4: Iris MSRP 427, qty 2, IM 15, uplift 4.76 -> extCost 725.90, unitPrice 381.09, extPrice 762.18
        var line1 = new LineItem
        {
            Vpn = "EX4400-48MP",
            Qty = 2,
            Cost = 0m,
            Msrp = 427m,
            ImPercent = 15m,
            Margin = 4.76m,
        };

        var uc1 = LinePricingMath.EffectiveUnitCost(line1, calculatesCostFromMsrp: true);
        var ec1 = LinePricingMath.ExtCost(line1.Qty, uc1);
        var up1 = LinePricingMath.UnitPrice(uc1, line1.Margin!.Value);
        var ep1 = LinePricingMath.ExtPrice(line1.Qty, up1);

        uc1.Should().Be(362.95m);
        ec1.Should().Be(725.90m);
        up1.Should().Be(381.09m);
        ep1.Should().Be(762.18m);

        // R5: R4 + (Iris MSRP 427, qty 1, IM 27.53, uplift 10)
        var line2 = new LineItem
        {
            Vpn = "SUB-EX-48-A1-3",
            Qty = 1,
            Cost = 0m,
            Msrp = 427m,
            ImPercent = 27.53m,
            Margin = 10m,
        };

        var uc2 = LinePricingMath.EffectiveUnitCost(line2, calculatesCostFromMsrp: true);
        var up2 = LinePricingMath.UnitPrice(uc2, line2.Margin!.Value);
        uc2.Should().Be(309.45m);
        up2.Should().Be(343.83m);

        var totals = LinePricingMath.Summarise([line1, line2], calculatesCostFromMsrp: true);
        totals.TotalCost.Should().Be(1035.35m);
        totals.TotalPrice.Should().Be(1106.01m);
        totals.MarginAmount.Should().Be(70.66m);
        totals.MarginPercent.Should().Be(6.39m);
    }

    [Fact]
    public void ReferenceCases_R6_And_R7_CpqUntouchedVsOverridden()
    {
        var cpqUntouched = new LineItem
        {
            Vpn = "EX4400-48MP",
            Qty = 1,
            Cost = 362.9512m,
            Msrp = 427m,
            ImPercent = null,
            Margin = 4.76m,
        };

        // R6: derivedIm 15.00, unitCost displayed 362.95, effective unitCost unrounded 362.9512
        LinePricingMath.DerivedImPercent(cpqUntouched.Cost, cpqUntouched.Msrp).Should().Be(15.00m);
        var effectiveUntouched = LinePricingMath.EffectiveUnitCost(cpqUntouched, calculatesCostFromMsrp: false);
        effectiveUntouched.Should().Be(362.9512m);
        LinePricingMath.Round2(effectiveUntouched).Should().Be(362.95m);

        // R7: IM overridden to 20 -> unitCost 341.60
        var cpqOverridden = cpqUntouched with { ImPercent = 20m };
        LinePricingMath.EffectiveUnitCost(cpqOverridden, calculatesCostFromMsrp: false).Should().Be(341.60m);
    }

    [Fact]
    public void ReferenceCases_R8_And_R9_UpliftBoundaries()
    {
        // R8: uplift 0 -> unitPrice = unitCost
        LinePricingMath.UnitPrice(362.95m, 0m).Should().Be(362.95m);

        // R9: uplift 100 -> rejected
        var act100 = () => LinePricingMath.UnitPrice(100m, 100m);
        act100.Should().Throw<ArgumentOutOfRangeException>();

        var actNeg = () => LinePricingMath.UnitPrice(100m, -0.01m);
        actNeg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReferenceCase_R10_IrisZeroMsrp_ProducesZeroUnitCostAndWriterSentinel()
    {
        var item = new LineItem
        {
            Vpn = "ZERO-SKU",
            Qty = 1,
            Cost = 0m,
            Msrp = 0m,
            ImPercent = 15m,
            Margin = 5m,
        };

        var unitCost = LinePricingMath.EffectiveUnitCost(item, calculatesCostFromMsrp: true);
        unitCost.Should().Be(0m);

        var tempPath = Path.Combine(Path.GetTempPath(), $"r10-{Guid.NewGuid():N}.xlsx");
        try
        {
            CrmWriter.Write(
                [item],
                tempPath,
                CrmTemplates.ForeignUplift,
                new CrmWriterOptions(VendorName: "JUNIPER", CalculatesCostFromMsrp: true));

            using var wb = new XLWorkbook(tempPath);
            var ws = wb.Worksheet(CrmTemplates.ForeignUplift);
            ws.Cell(3, 20).GetValue<decimal>().Should().Be(0.0001m);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Iris_zero_msrp_needs_no_im_but_positive_msrp_still_does()
    {
        // An Iris "incl." line has MSRP 0 and the review popups never offer it an IM%.
        var included = new LineItem { Vpn = "INCL-SKU", Qty = 4, Cost = 0m, Msrp = 0m };
        LinePricingMath.EffectiveUnitCost(included, calculatesCostFromMsrp: true).Should().Be(0m);

        var priced = included with { Msrp = 100m };
        ((Action)(() => LinePricingMath.EffectiveUnitCost(priced, calculatesCostFromMsrp: true)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MidpointRounding_DerivedIm_And_ZeroTotalPrice()
    {
        // Midpoint away from zero: 1.005 -> 1.01
        LinePricingMath.Round2(1.005m).Should().Be(1.01m);
        LinePricingMath.Round2(-1.005m).Should().Be(-1.01m);

        // DerivedImPercent with msrp 0 or null -> null
        LinePricingMath.DerivedImPercent(100m, 0m).Should().BeNull();
        LinePricingMath.DerivedImPercent(100m, null).Should().BeNull();

        // DerivedImPercent can be negative when sourceCost > msrp
        LinePricingMath.DerivedImPercent(110m, 100m).Should().Be(-10.00m);

        // MarginPercent is null when TotalPrice == 0
        var zeroItem = new LineItem { Vpn = "FREE", Qty = 2, Cost = 0m, Msrp = 0m, Margin = 10m };
        var totals = LinePricingMath.Summarise([zeroItem], calculatesCostFromMsrp: false);
        totals.TotalCost.Should().Be(0m);
        totals.TotalPrice.Should().Be(0m);
        totals.MarginAmount.Should().Be(0m);
        totals.MarginPercent.Should().BeNull();
    }
}

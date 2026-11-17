using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public class LinePricingWriterTests
{
    [Fact]
    public void Write_Iris_UsesPerLineMarginAndImPercent_AndLeavesColXBlank()
    {
        var items = new[]
        {
            new LineItem { Vpn = "EX4400-48MP", Qty = 2, Cost = 0m, Msrp = 427m, ImPercent = 15m, Margin = 4.76m },
            new LineItem { Vpn = "SUB-EX-48-A1-3", Qty = 1, Cost = 0m, Msrp = 427m, ImPercent = 27.53m, Margin = 10m },
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"iris-writer-{Guid.NewGuid():N}.xlsx");
        try
        {
            CrmWriter.Write(
                items,
                tempPath,
                CrmTemplates.ForeignUplift,
                new CrmWriterOptions(VendorName: "JUNIPER", FxRate: 0.6520m, CalculatesCostFromMsrp: true));

            using var wb = new XLWorkbook(tempPath);
            var ws = wb.Worksheet(CrmTemplates.ForeignUplift);

            // Row 3 (Line 1)
            ws.Cell(3, 11).GetValue<decimal>().Should().Be(4.76m); // Col K Margin
            ws.Cell(3, 20).GetValue<decimal>().Should().Be(362.95m); // Col T Foreign Cost
            ws.Cell(3, 21).GetValue<decimal>().Should().Be(427m); // Col U Foreign MSRP
            ws.Cell(3, 22).GetValue<decimal>().Should().Be(0.6520m); // Col V FX Rate
            ws.Cell(3, 24).IsEmpty().Should().BeTrue(); // Col X IM% blank

            // Row 4 (Line 2)
            ws.Cell(4, 11).GetValue<decimal>().Should().Be(10m); // Col K Margin
            ws.Cell(4, 20).GetValue<decimal>().Should().Be(309.45m); // Col T Foreign Cost
            ws.Cell(4, 24).IsEmpty().Should().BeTrue(); // Col X IM% blank
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Write_Cpq_And_Indirect_UntouchedUsesUnroundedSourceCost_OverriddenCalculatesFromMsrp()
    {
        var items = new[]
        {
            // Untouched CPQ line: unrounded source cost
            new LineItem { Vpn = "CPQ-UNTOUCHED", Qty = 1, Cost = 362.9512m, Msrp = 427m, ImPercent = null, Margin = 4.76m },
            // Overridden CPQ line: calculated 2-dp cost
            new LineItem { Vpn = "CPQ-OVERRIDDEN", Qty = 1, Cost = 362.9512m, Msrp = 427m, ImPercent = 20m, Margin = 8m },
            // Indirect line overridden
            new LineItem { Vpn = "IND-OVERRIDDEN", Qty = 3, Cost = 100m, Msrp = 200m, ImPercent = 25m, Margin = 12.5m },
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"cpq-writer-{Guid.NewGuid():N}.xlsx");
        try
        {
            CrmWriter.Write(
                items,
                tempPath,
                CrmTemplates.ForeignUplift,
                new CrmWriterOptions(VendorName: "JUNIPER", FxRate: 0.6500m, CalculatesCostFromMsrp: false));

            using var wb = new XLWorkbook(tempPath);
            var ws = wb.Worksheet(CrmTemplates.ForeignUplift);

            ws.Cell(3, 11).GetValue<decimal>().Should().Be(4.76m);
            ws.Cell(3, 20).GetValue<decimal>().Should().Be(362.9512m);
            ws.Cell(3, 24).IsEmpty().Should().BeTrue();

            ws.Cell(4, 11).GetValue<decimal>().Should().Be(8m);
            ws.Cell(4, 20).GetValue<decimal>().Should().Be(341.60m);
            ws.Cell(4, 24).IsEmpty().Should().BeTrue();

            ws.Cell(5, 11).GetValue<decimal>().Should().Be(12.5m);
            ws.Cell(5, 20).GetValue<decimal>().Should().Be(150.00m);
            ws.Cell(5, 24).IsEmpty().Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Write_Iris_ThrowsWhenRowAndHeaderImPercentAreBothMissing()
    {
        var items = new[]
        {
            new LineItem { Vpn = "EX4400-48MP", Qty = 1, Cost = 0m, Msrp = 427m, ImPercent = 15m, Margin = 5m },
            new LineItem { Vpn = "MISSING-IM", Qty = 1, Cost = 0m, Msrp = 427m, ImPercent = null, Margin = 5m },
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"iris-missing-{Guid.NewGuid():N}.xlsx");
        try
        {
            var act = () => CrmWriter.Write(
                items,
                tempPath,
                CrmTemplates.ForeignUplift,
                new CrmWriterOptions(VendorName: "JUNIPER", ImPercent: null, CalculatesCostFromMsrp: true));

            act.Should().Throw<ArgumentException>().WithMessage("*IM% is required*");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

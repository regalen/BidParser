using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public static class PercentOffWithUpliftWriter
{
    public static string Write(
        IEnumerable<LineItem> items,
        string outputPath,
        decimal margin,
        decimal im,
        string vendorName = "HP")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("% Off RRP with Uplift");

        sheet.Cell(1, 12).Value = "(Optional for Software and/or Services)";
        for (var index = 0; index < TemplateLayout.Headers.Length; index++)
        {
            sheet.Cell(2, index + 1).Value = TemplateLayout.Headers[index];
        }

        var rowNumber = 3;
        foreach (var item in items)
        {
            // Col A — Item
            sheet.Cell(rowNumber, 1).Value = item.LineSequence;

            // Col B — Vendor Name
            sheet.Cell(rowNumber, 2).Value = vendorName;

            // Col D — Vendor Part Number
            sheet.Cell(rowNumber, 4).Value = item.Vpn;

            // Col E — Description
            if (item.Description is not null)
            {
                sheet.Cell(rowNumber, 5).Value = item.Description;
            }

            // Col F — Qty.
            sheet.Cell(rowNumber, 6).Value = item.Qty;

            // Col H — MSRP: parent carries the real price; children use the zero-price sentinel.
            // (Col I Cost is intentionally blank for this template.)
            sheet.Cell(rowNumber, 8).Value = NonZeroPrice(item.Msrp ?? 0m);

            // Col K — Margin (always written for this template)
            sheet.Cell(rowNumber, 11).Value = margin;

            // Col X — IM% (column 24)
            sheet.Cell(rowNumber, 24).Value = im;

            rowNumber++;
        }

        // End-loop sentinel row
        sheet.Cell(rowNumber, 2).Value = "*";
        sheet.Cell(rowNumber, 4).Value = ForeignUpliftWriter.EndLoopWarning;

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    // Downstream import rejects a literal 0; the sentinel rounds back to 0 on import.
    private static decimal NonZeroPrice(decimal value) => value == 0m ? TemplateLayout.ZeroPriceSentinel : value;
}

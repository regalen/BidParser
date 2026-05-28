using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public static class AnzGenericWriter
{
    /// <summary>
    /// Writes an HP (ANZ-GENERIC) parsed output workbook.
    /// </summary>
    /// <param name="items">Line items from the HP parser.</param>
    /// <param name="outputPath">Destination file path.</param>
    /// <param name="sheetName">Worksheet name — "No Calculation" or "Uplift".</param>
    /// <param name="includeMargin">When true, writes <paramref name="margin"/> to column K (Margin).</param>
    /// <param name="margin">Margin percentage written to col K when <paramref name="includeMargin"/> is true.</param>
    /// <param name="vendorName">Vendor label written to col B (default "HP").</param>
    public static string Write(
        IEnumerable<LineItem> items,
        string outputPath,
        string sheetName,
        bool includeMargin,
        decimal margin = 5.00m,
        string vendorName = "HP")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);

        sheet.Cell(1, 12).Value = "(Optional for Software and/or Services)";
        for (var index = 0; index < TemplateLayout.Headers.Length; index++)
        {
            sheet.Cell(2, index + 1).Value = TemplateLayout.Headers[index];
        }

        var rowNumber = 3;
        var fallbackIndex = 1; // used only when LineSequence is null

        foreach (var item in items)
        {
            // Col A — Item (line sequence string, or a running integer fallback)
            sheet.Cell(rowNumber, 1).Value = item.LineSequence ?? fallbackIndex.ToString();

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

            // Col H — MSRP: intentionally blank for HP

            // Col I — Cost (0 → sentinel; Bundle Detail components carry no price and the
            // downstream import rejects a literal 0, rounding the sentinel back to 0).
            sheet.Cell(rowNumber, 9).Value = NonZeroPrice(item.Cost);

            // Col K — Margin (Uplift template only)
            if (includeMargin)
            {
                sheet.Cell(rowNumber, 11).Value = margin;
            }

            // Col W — Min Order Qty
            if (item.MinQty is not null)
            {
                sheet.Cell(rowNumber, 23).Value = item.MinQty.Value;
            }

            rowNumber++;
            fallbackIndex++;
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

    // Downstream import treats a literal 0 as an invalid price; the sentinel rounds back to 0 on import.
    private static decimal NonZeroPrice(decimal value) => value == 0m ? TemplateLayout.ZeroPriceSentinel : value;
}

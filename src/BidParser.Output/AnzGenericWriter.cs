using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public static class AnzGenericWriter
{
    /// <summary>
    /// Writes an ANZ-GENERIC parsed output workbook (HP, Lenovo, Zebra).
    /// </summary>
    /// <param name="items">Line items from the parser.</param>
    /// <param name="outputPath">Destination file path.</param>
    /// <param name="sheetName">Worksheet name — "No Calculation" or "Uplift".</param>
    /// <param name="includeMargin">When true, writes <paramref name="margin"/> to column K (Margin).</param>
    /// <param name="margin">Margin percentage written to col K when <paramref name="includeMargin"/> is true.</param>
    /// <param name="vendorName">Vendor label written to col B (default "HP").</param>
    /// <param name="onCost">
    /// Optional On Cost % written to col Z for each non-cancelled line item.
    /// When <see langword="null"/> col Z is left blank (existing HP/Lenovo behaviour preserved).
    /// </param>
    public static string Write(
        IEnumerable<LineItem> items,
        string outputPath,
        string sheetName,
        bool includeMargin,
        decimal margin = 5.00m,
        string vendorName = "HP",
        decimal? onCost = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);

        TemplateLayout.WriteHeaders(sheet);

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

            if (item.IsCancelled)
            {
                // Cancelled lines: downstream system pulls standard pricing from SAP.
                // Col F — Qty: always 1 (ignore source qty).
                sheet.Cell(rowNumber, 6).Value = 1;
                // Col H (MSRP), Col I (Cost), Col W (Min Order Qty), Col Z (On Cost %)
                // are intentionally left blank — no zero-dollar sentinel for these.
                // Col R — Comments
                if (item.Comments is not null)
                {
                    sheet.Cell(rowNumber, 18).Value = item.Comments;
                }
            }
            else
            {
                // Col F — Qty.
                sheet.Cell(rowNumber, 6).Value = item.Qty;

                // Col H — MSRP (written when the parser populates it; blank for HP/Lenovo).
                if (item.Msrp is not null)
                {
                    sheet.Cell(rowNumber, 8).Value = item.Msrp.Value;
                }

                // Col I — Cost (0 → sentinel; Bundle Detail components carry no price and the
                // downstream import rejects a literal 0, rounding the sentinel back to 0).
                sheet.Cell(rowNumber, 9).Value = TemplateLayout.NonZeroPrice(item.Cost);

                // Col K — Margin (Uplift template only)
                if (includeMargin)
                {
                    sheet.Cell(rowNumber, 11).Value = margin;
                }

                // Col R — Comments
                if (item.Comments is not null)
                {
                    sheet.Cell(rowNumber, 18).Value = item.Comments;
                }

                // Col W — Min Order Qty
                if (item.MinQty is not null)
                {
                    sheet.Cell(rowNumber, 23).Value = item.MinQty.Value;
                }

                // Col Z — On Cost % (written only when the caller supplies a value;
                // null preserves existing HP/Lenovo behaviour — col Z stays blank).
                if (onCost is not null)
                {
                    sheet.Cell(rowNumber, 26).Value = onCost.Value;
                }
            }

            rowNumber++;
            fallbackIndex++;
        }

        // End-loop sentinel row
        sheet.Cell(rowNumber, 2).Value = "*";
        sheet.Cell(rowNumber, 4).Value = TemplateLayout.EndLoopWarning;

        return TemplateLayout.Save(workbook, outputPath);
    }
}

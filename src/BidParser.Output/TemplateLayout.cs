using ClosedXML.Excel;

namespace BidParser.Output;

internal static class TemplateLayout
{
    // Zero-dollar lines export as this sentinel because the downstream import rejects a literal 0
    // and rounds the sentinel back to 0. Applies to every writer's price columns.
    internal const decimal ZeroPriceSentinel = 0.0001m;

    internal const string EndLoopWarning = "DO NOT DELETE THIS LINE. Indicate * on column B to mark the end loop. Add / remove lines above as necessary.";

    internal static readonly string[] Headers =
    [
        "Item",
        "Vendor Name",
        "IMTH SKU\n(Optional)",
        "Vendor Part Number",
        "Description",
        "Qty.",
        "Unit Price",
        "MSRP",
        "Cost",
        "Discount",
        "Margin",
        "Product Part Number \n(for Warranty/Renewal)",
        "Serial Number",
        "Warranty / Duration (months)",
        "Vendor Ref",
        "Start Date",
        "End Date",
        "Comments",
        "Foreign Currency",
        "Foreign Cost",
        "Foreign MSRP",
        "Foreign Exchange Rate",
        "Min Order Qty",
        "IM%",
        "Diff%",
        "On Cost %",
        "Retail Bump %"
    ];

    internal static decimal NonZeroPrice(decimal value) => value == 0m ? ZeroPriceSentinel : value;

    internal static void WriteHeaders(IXLWorksheet sheet)
    {
        sheet.Cell(1, 12).Value = "(Optional for Software and/or Services)";
        for (var index = 0; index < Headers.Length; index++)
        {
            sheet.Cell(2, index + 1).Value = Headers[index];
        }
    }

    internal static string Save(XLWorkbook workbook, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        workbook.SaveAs(outputPath);
        return outputPath;
    }
}

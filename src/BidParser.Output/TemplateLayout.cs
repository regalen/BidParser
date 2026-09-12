using ClosedXML.Excel;

namespace BidParser.Output;

/// <summary>
/// Shared layout for every output writer: the canonical 27-column header row, the zero-price
/// sentinel, date formatting, and the header/save helpers. The single source of truth for column
/// order — writers reference columns by the positions defined here, so changing a header's index
/// here shifts it for all templates at once.
/// </summary>
internal static class TemplateLayout
{
    // ── The two zero prices ───────────────────────────────────────────────────────────────
    // CRM does not accept a literal 0 as a price. That single fact gives the output two
    // distinct, deliberate ways to express "zero", and picking the wrong one silently changes
    // what the customer is quoted. Never write a bare 0m to a price column — say which one.

    /// <summary>
    /// "This line really is free." Exported instead of a literal 0 for a genuinely zero-dollar
    /// price, because CRM would reject the 0; it rounds the sentinel back to 0.00 on import, so
    /// the line lands at $0.00 as intended. This is the default for a zero price.
    /// </summary>
    internal const decimal ZeroPriceSentinel = 0.0001m;

    /// <summary>
    /// "This line carries no bid price." A literal 0, written on purpose: CRM does not recognise
    /// it as a price and therefore falls back to the line's standard price held in SAP. Use it
    /// only for lines that belong on the quote but that we are deliberately not bid-pricing
    /// (e.g. Zebra cancelled rows) — never as a stand-in for a genuine $0.00.
    /// </summary>
    internal const decimal NoBidPrice = 0m;

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

    internal static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = "DD/MM/YYYY";
    }

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

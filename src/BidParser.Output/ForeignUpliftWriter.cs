using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public static class ForeignUpliftWriter
{
    public const string EndLoopWarning = "DO NOT DELETE THIS LINE. Indicate * on column B to mark the end loop. Add / remove lines above as necessary.";

    private static readonly string[] Headers =
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

    public static string WriteForeignUplift(
        IEnumerable<LineItem> lineItems,
        string outputPath,
        decimal margin = 5.00m,
        decimal fxRate = 1.000m,
        string vendorName = "NUTANIX",
        string currency = "USD")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Foreign Uplift");

        sheet.Cell(1, 12).Value = "(Optional for Software and/or Services)";
        for (var index = 0; index < Headers.Length; index++)
        {
            sheet.Cell(2, index + 1).Value = Headers[index];
        }

        var rowNumber = 3;
        var itemIndex = 1;
        foreach (var item in lineItems)
        {
            sheet.Cell(rowNumber, 1).Value = itemIndex;
            sheet.Cell(rowNumber, 2).Value = vendorName;
            sheet.Cell(rowNumber, 4).Value = item.Vpn;
            if (item.Description is not null)
            {
                sheet.Cell(rowNumber, 5).Value = item.Description;
            }

            sheet.Cell(rowNumber, 6).Value = item.Qty;
            SetNumber(sheet.Cell(rowNumber, 11), margin);
            if (item.Term is >= 1)
            {
                sheet.Cell(rowNumber, 14).Value = item.Term.Value;
            }

            if (item.StartDate is not null)
            {
                SetDate(sheet.Cell(rowNumber, 16), item.StartDate.Value);
            }

            if (item.EndDate is not null)
            {
                SetDate(sheet.Cell(rowNumber, 17), item.EndDate.Value);
            }

            if (item.SerialNumber is not null)
            {
                sheet.Cell(rowNumber, 18).Value = item.SerialNumber;
            }

            sheet.Cell(rowNumber, 19).Value = currency;
            SetNumber(sheet.Cell(rowNumber, 20), item.Cost);
            SetNumber(sheet.Cell(rowNumber, 21), item.Msrp ?? 0m);
            SetNumber(sheet.Cell(rowNumber, 22), fxRate);

            rowNumber++;
            itemIndex++;
        }

        sheet.Cell(rowNumber, 2).Value = "*";
        sheet.Cell(rowNumber, 4).Value = EndLoopWarning;

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    private static void SetNumber(IXLCell cell, decimal value)
    {
        cell.Value = value == decimal.Truncate(value) ? decimal.ToInt32(value) : Convert.ToDouble(value);
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = "DD/MM/YYYY";
    }
}

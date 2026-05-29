using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using ClosedXML.Excel;

namespace BidParser.Output;

public static class ForeignUpliftWriter
{
    public static string WriteForeignUplift(
        IEnumerable<LineItem> lineItems,
        string outputPath,
        decimal margin = 5.00m,
        decimal fxRate = 1.000m,
        string vendorName = "NUTANIX",
        string currency = "USD",
        string? parserSlug = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(CrmTemplates.ForeignUplift);

        TemplateLayout.WriteHeaders(sheet);

        var termAsComment = parserSlug is "nutanix_software_only_pdf" or "nutanix_software_only_xlsx";

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
            sheet.Cell(rowNumber, 11).Value = margin;
            if (item.Term is >= 1)
            {
                if (termAsComment)
                {
                    sheet.Cell(rowNumber, 18).Value = $"{item.Term.Value} Months";
                }
                else
                {
                    sheet.Cell(rowNumber, 14).Value = item.Term.Value;
                }
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
            sheet.Cell(rowNumber, 20).Value = TemplateLayout.NonZeroPrice(item.Cost);
            sheet.Cell(rowNumber, 21).Value = TemplateLayout.NonZeroPrice(item.Msrp ?? 0m);
            sheet.Cell(rowNumber, 22).Value = fxRate;

            rowNumber++;
            itemIndex++;
        }

        sheet.Cell(rowNumber, 2).Value = "*";
        sheet.Cell(rowNumber, 4).Value = TemplateLayout.EndLoopWarning;

        return TemplateLayout.Save(workbook, outputPath);
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = "DD/MM/YYYY";
    }
}

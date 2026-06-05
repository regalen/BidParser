using System.Globalization;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Nutanix.SoftwareOnlyXlsx;

public sealed class NutanixSoftwareOnlyXlsxParser : IParser
{
    public string Slug => ParserSlugs.NutanixSoftwareOnlyXlsx;
    public string DisplayName => "Software Only (XLSX)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    // Signature: the "Quote Number" grid carrying Term (Months) + List/Sale Price, and
    // NOT under a "Quote D" banner (Hardware Only) nor carrying the renewal price columns.
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.First();
            if (WorkbookReader.FindCell(sheet, NutanixXlsxSignatures.QuoteDBanner) is not null)
            {
                return 0.0;
            }

            var headerCell = WorkbookReader.FindCell(sheet, "Quote Number");
            if (headerCell is null)
            {
                return 0.0;
            }

            var columns = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber).Columns;
            if (columns.ContainsKey("Net Unit Price") || columns.ContainsKey("Serial Number"))
            {
                return 0.0;
            }

            return columns.ContainsKey("Term (Months)")
                && columns.ContainsKey("List Price")
                && columns.ContainsKey("Sale Price")
                ? 0.85
                : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var sheet = workbook.Worksheets.First();
        var headerCell = WorkbookReader.FindCell(sheet, "Quote Number")
            ?? throw new ParseError("detect", "Could not find the Quote Number table header.", "Could not find Quote Number header");

        var headerMap = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber);
        WorkbookReader.RequireLabels(
            headerMap,
            "Product Code",
            "Product Description",
            "Term (Months)",
            "List Price",
            "Sale Price",
            "Quantity");

        var items = new List<LineItem>();
        decimal? quotedTotal = null;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerMap.RowNumber;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var row = headerMap.RowNumber + 1; row <= lastRow; row++)
        {
            var totalText = WorkbookReader.FindTotalTextInRow(sheet, row, lastColumn);
            if (totalText.Length > 0)
            {
                quotedTotal = WorkbookReader.ParseTotalText(totalText);
                break;
            }

            if (WorkbookReader.RowIsEmpty(sheet, row))
            {
                break;
            }

            var vpn = Text(sheet, row, headerMap, "Product Code");
            if (vpn.Length == 0)
            {
                continue;
            }

            if (vpn == "Term-Months")
            {
                var termValue = DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, "Term (Months)"));
                items.Add(new LineItem
                {
                    Vpn = "Term-Months",
                    Description = "Term in months",
                    Term = termValue,
                    Qty = termValue ?? 0,
                    Cost = 0m,
                    Msrp = 0m,
                    Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
                });
                continue;
            }

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = Text(sheet, row, headerMap, "Product Description"),
                Term = DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, "Term (Months)")),
                Msrp = DecimalCleaner.Parse(Text(sheet, row, headerMap, "List Price"), defaultZero: true),
                Cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Sale Price"), defaultZero: true),
                Qty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Quantity")),
                StartDate = ReadOptionalDate(sheet, row, headerMap, "Selected Start Date"),
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
            });
        }

        quotedTotal ??= FindTotalAfterHeader(sheet, headerMap.RowNumber, lastRow, lastColumn);
        var validation = ParseValidation.Validate(items, quotedTotal);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = Path.GetFileNameWithoutExtension(path),
                Supplier = Vendor,
                Currency = "USD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };

    }

    private static decimal? FindTotalAfterHeader(
        ClosedXML.Excel.IXLWorksheet sheet,
        int headerRow,
        int lastRow,
        int lastColumn)
    {
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            for (var column = 1; column <= lastColumn; column++)
            {
                var text = WorkbookReader.CellText(sheet.Cell(row, column));
                if (text.StartsWith("TOTAL ", StringComparison.Ordinal))
                {
                    return WorkbookReader.ParseTotalText(text);
                }
            }
        }

        return null;
    }

    private static string Text(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static DateOnly? ReadOptionalDate(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        if (!headerMap.Columns.TryGetValue(label, out var column))
        {
            return null;
        }

        var cell = sheet.Cell(row, column);
        if (cell.Value.IsDateTime)
        {
            return DateOnly.FromDateTime(cell.Value.GetDateTime());
        }

        var text = WorkbookReader.CellText(cell);
        if (text.Length == 0)
        {
            return null;
        }

        string[] formats = ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "dd/MM/yyyy"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

}

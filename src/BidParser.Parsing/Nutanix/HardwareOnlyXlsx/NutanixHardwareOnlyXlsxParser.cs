using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Nutanix.HardwareOnlyXlsx;

public sealed class NutanixHardwareOnlyXlsxParser : IParser
{
    private const string QuoteDBanner = "Quote D For distributor to quote to the reseller only";

    public string Slug => "nutanix_hardware_only_xlsx";
    public string DisplayName => "Hardware Only (XLSX)";
    public string Vendor => "Nutanix";
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => "Foreign Uplift";

    public ParseResult Parse(string path)
    {
        var sheet = WorkbookReader.ActiveSheet(path);
        var bannerCell = WorkbookReader.FindCell(sheet, QuoteDBanner)
            ?? throw new ParseError("detect", "Could not find the Quote D section banner.", "Could not find Quote D banner");
        var headerCell = FindCellAfterRow(sheet, "Product Code", bannerCell.Address.RowNumber)
            ?? throw new ParseError("detect", "Could not find the Product Code table header.", "Could not find Product Code header");

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
            var totalText = FindTotalTextInRow(sheet, row, lastColumn);
            if (totalText.Length > 0)
            {
                quotedTotal = WorkbookReader.ParseTotalText(totalText);
                break;
            }

            var vpn = Text(sheet, row, headerMap, "Product Code");
            if (vpn.Length == 0)
            {
                continue;
            }

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = Text(sheet, row, headerMap, "Product Description"),
                Term = DecimalCleaner.ParseOptionalInt(Value(sheet, row, headerMap, "Term (Months)")),
                Msrp = DecimalCleaner.Parse(Value(sheet, row, headerMap, "List Price"), defaultZero: true),
                Cost = DecimalCleaner.Parse(Value(sheet, row, headerMap, "Sale Price"), defaultZero: true),
                Qty = DecimalCleaner.ParseInt(Value(sheet, row, headerMap, "Quantity")),
                Raw = RawDict(sheet, row, headerMap)
            });
        }

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

    private static IXLCell? FindCellAfterRow(IXLWorksheet sheet, string expected, int minRow)
    {
        return sheet
            .RangeUsed()?
            .CellsUsed()
            .Where(cell => cell.Address.RowNumber > minRow)
            .OrderBy(cell => cell.Address.RowNumber)
            .ThenBy(cell => cell.Address.ColumnNumber)
            .FirstOrDefault(cell => WorkbookReader.CellText(cell) == expected);
    }

    private static string FindTotalTextInRow(IXLWorksheet sheet, int row, int columnCount)
    {
        for (var column = 1; column <= columnCount; column++)
        {
            var text = WorkbookReader.CellText(sheet.Cell(row, column));
            if (text.StartsWith("TOTAL ", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string Text(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static object? Value(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellValue(sheet.Cell(row, headerMap.Require(label)));
    }

    private static IReadOnlyDictionary<string, string> RawDict(
        IXLWorksheet sheet,
        int row,
        HeaderMap headerMap)
    {
        return headerMap.Columns
            .Select(pair => (pair.Key, Value: WorkbookReader.CellText(sheet.Cell(row, pair.Value))))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}

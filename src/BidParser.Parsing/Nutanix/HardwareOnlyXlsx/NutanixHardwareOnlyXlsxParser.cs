using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Nutanix.HardwareOnlyXlsx;

public sealed class NutanixHardwareOnlyXlsxParser : IParser
{
    private const string QuoteDBanner = NutanixXlsxSignatures.QuoteDBanner;

    public string Slug => ParserSlugs.NutanixHardwareOnlyXlsx;
    public string DisplayName => "Hardware Only (XLSX)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    // Signature: the "Quote D" banner with a "Product Code" header below it.
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.First();
            var banner = WorkbookReader.FindCell(sheet, QuoteDBanner);
            if (banner is null)
            {
                return 0.0;
            }

            return WorkbookReader.FindCellAfter(sheet, "Product Code", banner.Address.RowNumber) is not null
                ? 0.9
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
        var bannerCell = WorkbookReader.FindCell(sheet, QuoteDBanner)
            ?? throw new ParseError("detect", "Could not find the Quote D section banner.", "Could not find Quote D banner");
        var headerCell = WorkbookReader.FindCellAfter(sheet, "Product Code", bannerCell.Address.RowNumber)
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
            var totalText = WorkbookReader.FindTotalTextInRow(sheet, row, lastColumn);
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
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
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

    private static string Text(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static object? Value(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellValue(sheet.Cell(row, headerMap.Require(label)));
    }

}

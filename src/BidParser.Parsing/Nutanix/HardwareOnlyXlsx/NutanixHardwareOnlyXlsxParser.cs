using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Nutanix.HardwareOnlyXlsx;

/// <summary>
/// Parses Nutanix "Hardware Only" quote spreadsheets (XLSX) — extracts only the "Quote D" section,
/// anchored on the Quote D banner with the "Product Code" header below it. Foreign-currency →
/// "Foreign Uplift" output. See docs/nutanix_hardware_only_xlsx.md.
/// </summary>
public sealed class NutanixHardwareOnlyXlsxParser : IParser
{
    private const string QuoteDBanner = NutanixXlsxSignatures.QuoteDBanner;

    public string Slug => ParserSlugs.NutanixHardwareOnlyXlsx;
    public string DisplayName => "Hardware Only (XLSX)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    /// <summary>
    /// Wrong-file-type hint (not routing): 0.9 when the "Quote D" banner is present with a
    /// "Product Code" header below it.
    /// </summary>
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
        // Quote D's "Parent Quote Name" is the only place this workbook states the quote identity —
        // the section banner carries no number. The column is read optionally: when it is absent the
        // bid label is simply unavailable, which is not a reason to reject an otherwise valid file.
        var bidNumber = string.Empty;
        decimal? quotedTotal = null;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerMap.RowNumber;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var row = headerMap.RowNumber + 1; row <= lastRow; row++)
        {
            if (bidNumber.Length == 0)
            {
                bidNumber = TextOptional(sheet, row, headerMap, "Parent Quote Name");
            }
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
                Vpn = NutanixVpnNormalizer.Normalize(vpn),
                Description = Text(sheet, row, headerMap, "Product Description"),
                Term = DecimalCleaner.ParseOptionalInt(Value(sheet, row, headerMap, "Term (Months)")),
                Msrp = DecimalCleaner.Parse(Value(sheet, row, headerMap, "List Price"), defaultZero: true),
                Cost = DecimalCleaner.Parse(Value(sheet, row, headerMap, "Sale Price"), defaultZero: true),
                Qty = DecimalCleaner.ParseInt(Value(sheet, row, headerMap, "Quantity")),
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
            });
        }

        var validation = ParseValidation.Validate(items, quotedTotal);
        var bid = BidMetadataCleaner.CleanRevisionless(bidNumber);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = Path.GetFileNameWithoutExtension(path),
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
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

    /// <summary>Reads a column that may be absent, without the "detect"-stage throw of <c>Require</c>.</summary>
    private static string TextOptional(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return headerMap.Columns.TryGetValue(label, out var column)
            ? WorkbookReader.CellText(sheet.Cell(row, column))
            : string.Empty;
    }

    private static object? Value(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellValue(sheet.Cell(row, headerMap.Require(label)));
    }

}

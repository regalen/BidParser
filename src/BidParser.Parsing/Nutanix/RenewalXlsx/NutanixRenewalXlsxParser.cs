using System.Globalization;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Nutanix.RenewalXlsx;

public sealed class NutanixRenewalXlsxParser : IParser
{
    public string Slug => ParserSlugs.NutanixRenewalXlsx;
    public string DisplayName => "Renewal (XLSX)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    // Signature: the "Quote Number" grid carrying the renewal price/serial columns
    // (Net Unit Price + Serial Number + Term Adjusted List Unit Price).
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
            return columns.ContainsKey("Net Unit Price")
                && columns.ContainsKey("Serial Number")
                && columns.ContainsKey("Term Adjusted List Unit Price")
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
        var headerCell = WorkbookReader.FindCell(sheet, "Quote Number")
            ?? throw new ParseError("detect", "Could not find the Quote Number table header.", "Could not find Quote Number header");

        var headerMap = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber);
        WorkbookReader.RequireLabels(
            headerMap,
            "Product Code",
            "Serial Number",
            "Start Date",
            "End Date",
            "Term Adjusted List Unit Price",
            "Net Unit Price",
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

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = BuildDescription(
                    OptionalText(sheet, row, headerMap, "Product Description"),
                    OptionalText(sheet, row, headerMap, "Platform")),
                SerialNumber = CleanSerial(OptionalText(sheet, row, headerMap, "Serial Number")),
                StartDate = ReadOptionalDate(sheet, row, headerMap, "Start Date"),
                EndDate = ReadOptionalDate(sheet, row, headerMap, "End Date"),
                Msrp = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Term Adjusted List Unit Price"), defaultZero: true),
                Cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Net Unit Price"), defaultZero: true),
                Qty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Quantity")),
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
            });
        }

        // The quoted total sits on a "TOTAL $…" row below the line items, but a blank
        // row can separate it from the last item (ending the loop above early). Fall back
        // to scanning the full sheet for the first "TOTAL " cell, mirroring the Software
        // Only (XLSX) parser.
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

    // Output Description combines the rich "Product Description" with the hardware
    // "Platform" identifier when present, e.g. "<desc> (Platform: NX-3060-G7-AF)".
    // Software-subscription rows leave Platform blank → Description is the bare description.
    private static string? BuildDescription(string description, string platform)
    {
        if (description.Length == 0)
        {
            return platform.Length > 0 ? $"Platform: {platform}" : null;
        }

        return platform.Length > 0 ? $"{description} (Platform: {platform})" : description;
    }

    // Serial cells carry the embedded license joined with a comma (e.g.
    // "26SW000487027, LIC-02574676"). Strip internal whitespace to match the
    // Renewal (PDF) convention of a single comma-joined value with no spaces.
    private static string? CleanSerial(string serial)
    {
        if (serial.Length == 0)
        {
            return null;
        }

        return serial.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string Text(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static string OptionalText(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return headerMap.Columns.TryGetValue(label, out var column)
            ? WorkbookReader.CellText(sheet.Cell(row, column))
            : string.Empty;
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

using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Trellix.QuoteXlsm;

/// <summary>Parses the macro-enabled Trellix Distributor Quote workbook without running its macros.</summary>
public sealed class TrellixQuoteXlsmParser : IParser
{
    private const string ChannelSku = "Channel SKU";
    private const string HardwareQty = "QTY Hardware";
    private const string SoftwareQty = "QTY Software or Support (Nodes)";

    public string Slug => ParserSlugs.TrellixQuoteXlsm;
    public string DisplayName => "Quote (XLSM)";
    public string Vendor => Vendors.Trellix;
    public string AcceptedMime => "application/vnd.ms-excel.sheet.macroEnabled.12";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = FindQuoteSheet(workbook);
            if (sheet is null || !HasTrellixIdentity(sheet)) return 0.0;
            _ = BuildHeaderMap(sheet);
            return 0.9;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var sheet = FindQuoteSheet(workbook);
        if (sheet is null || !HasTrellixIdentity(sheet))
            throw new ParseError("detect", "This is not a Trellix Distributor Quote workbook.",
                "Missing Trellix identity or Channel SKU table.");

        var header = BuildHeaderMap(sheet);
        var currency = WorkbookReader.ValueRightOf(sheet, "Quote Currency");
        if (!currency.Equals("AUD", StringComparison.OrdinalIgnoreCase))
            throw new ParseError("currency",
                $"This Trellix quote declares {(currency.Length == 0 ? "an unknown currency" : currency)}; AUD is required.",
                "Unsupported or unreadable Trellix quote currency.");

        var items = new List<LineItem>();
        var quotedTotal = 0m;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? header.RowNumber;
        for (var row = header.RowNumber + 1; row <= lastRow; row++)
        {
            var sku = RemoveWhitespace(Text(sheet, row, header, ChannelSku));
            if (sku.Length == 0) continue; // The exported workbook includes a non-item placeholder row.

            var sequence = items.Count + 1;
            var hardware = Quantity(Text(sheet, row, header, HardwareQty), HardwareQty, sequence);
            var software = Quantity(Text(sheet, row, header, SoftwareQty), SoftwareQty, sequence);
            if ((hardware > 0) == (software > 0))
                throw new ParseError("extract", $"Line {sequence} must have exactly one positive Trellix quantity.",
                    "Both or neither Trellix quantity fields are positive.");
            var qty = Math.Max(hardware, software);

            var cost = Money(Text(sheet, row, header, "Cost Per Unit"), "Cost Per Unit", sequence);
            var totalMsrp = Money(Text(sheet, row, header, "Total MSRP"), "Total MSRP", sequence);
            quotedTotal += Money(Text(sheet, row, header, "Final Disti Cost"), "Final Disti Cost", sequence);
            var commentParts = new[]
            {
                Text(sheet, row, header, "Program Type"),
                Text(sheet, row, header, "Selling Term"),
                RemoveWhitespace(Text(sheet, row, header, "Grant #'s"))
            }.Where(value => value.Length > 0).ToList();
            var serial = Text(sheet, row, header, "Latest Serial Number");

            items.Add(new LineItem
            {
                Vpn = sku,
                Description = Text(sheet, row, header, "Product Description"),
                Qty = qty,
                Cost = cost,
                Msrp = totalMsrp / qty,
                SerialNumber = serial.Length == 0 ? null : serial,
                StartDate = Date(sheet, row, header, "Start Date", sequence),
                EndDate = Date(sheet, row, header, "End Date", sequence),
                Comments = commentParts.Count == 0 ? null : string.Join(" | ", commentParts),
                LineSequence = sequence.ToString(CultureInfo.InvariantCulture),
                Raw = WorkbookReader.BuildRawDict(sheet, row, header)
            });
        }

        if (items.Count == 0)
            throw new ParseError("extract", "Could not find Trellix quote lines.",
                "No Channel SKU product row found in the Trellix table.");

        var quoteNumber = WorkbookReader.ValueRightOf(sheet, "Quote ID");
        var (bidNumber, bidRevision) = BidMetadataCleaner.CleanRevisionless(quoteNumber);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = ParseValidation.Validate(items, quotedTotal)
        };
    }

    private static IXLWorksheet? FindQuoteSheet(XLWorkbook workbook) => workbook.Worksheets
        .Where(sheet => sheet.Visibility == XLWorksheetVisibility.Visible)
        .FirstOrDefault(sheet => WorkbookReader.FindCell(sheet, ChannelSku) is not null
            && WorkbookReader.FindCell(sheet, "Quote ID") is not null
            && WorkbookReader.FindCell(sheet, "Quote Currency") is not null);

    private static bool HasTrellixIdentity(IXLWorksheet sheet) => sheet.RangeUsed()?.CellsUsed()
        .Any(cell => WorkbookReader.CellText(cell).Contains("Trellix", StringComparison.OrdinalIgnoreCase)) == true;

    private static HeaderMap BuildHeaderMap(IXLWorksheet sheet)
    {
        var anchor = WorkbookReader.FindCell(sheet, ChannelSku)
            ?? throw new ParseError("detect", "Could not find the Trellix quote table.", "Missing Channel SKU header.");
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(anchor.Address.RowNumber).CellsUsed())
        {
            var label = WorkbookReader.CellText(cell);
            if (label.Length > 0) columns.TryAdd(label, cell.Address.ColumnNumber);
        }

        // Start Date and End Date appear twice. The first pair is what the PDF displays; the
        // second pair is internal source data and may be populated when the displayed date is blank.
        var header = new HeaderMap(anchor.Address.RowNumber, columns);
        WorkbookReader.RequireLabels(header, "Product Description", "Program Type", "Selling Term",
            "Start Date", "End Date", HardwareQty, SoftwareQty, ChannelSku, "Grant #'s",
            "Latest Serial Number", "Total MSRP", "Cost Per Unit", "Final Disti Cost");
        return header;
    }

    private static string Text(IXLWorksheet sheet, int row, HeaderMap header, string label) =>
        WorkbookReader.CellText(sheet.Cell(row, header.Require(label)));

    private static int Quantity(string value, string column, int sequence)
    {
        if (value.Length == 0) return 0;
        try
        {
            var number = DecimalCleaner.Parse(value);
            if (number < 0 || number > int.MaxValue || number != decimal.Truncate(number))
                throw new FormatException();
            return (int)number;
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new ParseError("extract", $"Line {sequence} has an invalid {column} value.",
                $"Invalid Trellix {column} quantity.");
        }
    }

    private static decimal Money(string value, string column, int sequence)
    {
        try
        {
            return DecimalCleaner.Parse(value);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new ParseError("extract", $"Could not read Trellix {column} on line {sequence}.",
                $"Missing or invalid Trellix {column} amount.");
        }
    }

    private static DateOnly? Date(IXLWorksheet sheet, int row, HeaderMap header, string label, int sequence)
    {
        var cell = sheet.Cell(row, header.Require(label));
        var visible = WorkbookReader.CellText(cell);
        if (visible.Length == 0) return null;
        if (cell.Value.IsDateTime) return DateOnly.FromDateTime(cell.Value.GetDateTime());
        string[] formats = ["M/d/yyyy", "M/d/yy", "M-d-yyyy", "M-d-yy", "yyyy-MM-dd"];
        if (DateOnly.TryParseExact(visible, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)) return date;
        throw new ParseError("extract", $"Could not read Trellix {label} on line {sequence}.",
            $"Invalid Trellix {label} date.");
    }

    private static string RemoveWhitespace(string value) => Regex.Replace(value, @"\s+", string.Empty);
}

using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Juniper.IrisXlsx;

/// <summary>Undiscounted Juniper USD Iris BOM, located entirely through source labels.</summary>
public sealed class JuniperIrisXlsxParser : IParser
{
    private static readonly string[] RequiredHeaders =
        ["Part Number", "Description", "Quantity", "Unit Price", "Manufacturer", "Discount", "Total", "Price List"];

    public string Slug => ParserSlugs.JuniperIrisXlsx;
    public string DisplayName => "Iris (XLSX)";
    public string Vendor => Vendors.Juniper;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;
    public bool CalculatesCostFromMsrp => true;

    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            return FindTable(workbook) is null ? 0.0 : 0.9;
        }
        catch { return 0.0; }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var (sheet, header) = FindTable(workbook)
            ?? throw new ParseError("detect", "Could not find the Iris quote table.", "Missing required Iris headers.");
        var items = new List<LineItem>();
        decimal? quotedTotal = null;
        for (var row = header.RowNumber + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? header.RowNumber); row++)
        {
            string Text(string label) => WorkbookReader.CellText(sheet.Cell(row, header.Require(label)));
            decimal Number(string label)
            {
                try
                {
                    var text = Text(label);
                    if (label == "Discount") text = text.TrimEnd('%').Trim();
                    if (text.Length == 0) throw new FormatException();
                    var value = DecimalCleaner.Parse(text);
                    if (value < 0m) throw new FormatException();
                    return value;
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new ParseError("extract", $"Invalid Iris {label} on row {row}.", "Missing, negative, or unreadable numeric value.");
                }
            }
            if (sheet.Row(row).CellsUsed().Any(cell =>
                string.Equals(WorkbookReader.CellText(cell), "Quote Total", StringComparison.OrdinalIgnoreCase)))
            {
                quotedTotal = Number("Total");
                break;
            }
            if (RequiredHeaders.All(label => Text(label).Length == 0)) continue;
            if (Text("Part Number").Length == 0 || Text("Description").Length == 0)
                throw new ParseError("extract", $"Missing Iris part or description on row {row}.", "Incomplete Iris product row.");
            if (!string.Equals(Text("Manufacturer"), "Juniper Networks", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Text("Manufacturer"), Vendors.Juniper, StringComparison.OrdinalIgnoreCase))
                throw new ParseError("extract", "Iris supports Juniper products only. Mixed-vendor files are not supported.", "Unsupported Iris manufacturer.");
            if (!Text("Price List").EndsWith("(USD)", StringComparison.OrdinalIgnoreCase))
                throw new ParseError("currency", "Iris requires a USD price list on every product row.", "Unsupported Iris currency.");
            if (Number("Discount") != 0m)
                throw new ParseError("extract", "Iris source discounts must be zero. Enter IM% separately.", "Discounted Iris source pricing is unsupported.");
            var qty = Number("Quantity");
            if (qty < 1m || qty > int.MaxValue || qty != decimal.Truncate(qty))
                throw new ParseError("extract", $"Invalid Iris Quantity on row {row}.", "Quantity must be a positive integer.");
            _ = Number("Total");
            items.Add(new LineItem
            {
                Vpn = Text("Part Number"), Description = Text("Description"), Qty = (int)qty,
                Msrp = Number("Unit Price"), Cost = 0m,
                Raw = WorkbookReader.BuildRawDict(sheet, row, header)
            });
        }
        if (quotedTotal is null || items.Count == 0)
            throw new ParseError("extract", "Iris requires product rows and a Quote Total.", "Missing Iris products or total.");
        var computed = items.Sum(item => item.Msrp!.Value * item.Qty);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = Path.GetFileNameWithoutExtension(path), BidNumber = null, BidRevision = null,
                Supplier = Vendor, Currency = "USD", QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path), ParserSlug = Slug
            },
            LineItems = items,
            Validation = new ValidationResult
            {
                ComputedTotal = computed, QuotedTotal = quotedTotal, Difference = computed - quotedTotal.Value,
                Matches = Math.Abs(computed - quotedTotal.Value) <= 0.01m
            }
        };
    }

    private static (IXLWorksheet Sheet, HeaderMap Header)? FindTable(XLWorkbook workbook)
    {
        foreach (var sheet in workbook.Worksheets.Where(sheet => sheet.Visibility == XLWorksheetVisibility.Visible))
        {
            if (sheet.RangeUsed() is not { } range) continue;
            foreach (var row in range.RowsUsed())
            {
                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in row.CellsUsed())
                {
                    var label = WorkbookReader.CellText(cell);
                    if (label.Length > 0) columns.TryAdd(label, cell.Address.ColumnNumber);
                }
                if (RequiredHeaders.All(columns.ContainsKey))
                    return (sheet, new HeaderMap(row.RowNumber(), columns));
            }
        }
        return null;
    }
}

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

    /// <summary>Optional source line number ("1.0", "1.2", "1.2.1") that drives output sequencing.</summary>
    private const string LineNumberHeader = "Line#";

    public string Slug => ParserSlugs.JuniperIrisXlsx;
    public string DisplayName => "Iris (XLSX)";
    public string Vendor => Vendors.Juniper;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;
    public bool CalculatesCostFromMsrp => true;
    public bool SupportsLinePricing => true;

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
        var sequencer = new LineSequencer();
        header.Columns.TryGetValue(LineNumberHeader, out var lineNumberColumn);
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
            // An included component ("incl.") is priced on its parent line, so it carries no price,
            // price list, discount, or total of its own. It is emitted as a genuine zero-price
            // line: the writer turns the zero MSRP and cost into the zero-price sentinel, no IM%
            // is required, and it adds nothing to the MSRP validation total.
            var included = IsIncluded(Text("Unit Price"), Text("Price List"));
            if (!included)
            {
                if (!Text("Price List").EndsWith("(USD)", StringComparison.OrdinalIgnoreCase))
                    throw new ParseError("currency", "Iris requires a USD price list on every priced product row.", "Unsupported Iris currency.");
                if (Number("Discount") != 0m)
                    throw new ParseError("extract", "Iris source discounts must be zero. Enter IM% separately.", "Discounted Iris source pricing is unsupported.");
            }
            var qty = Number("Quantity");
            if (qty < 1m || qty > int.MaxValue || qty != decimal.Truncate(qty))
                throw new ParseError("extract", $"Invalid Iris Quantity on row {row}.", "Quantity must be a positive integer.");
            if (!included) _ = Number("Total");
            items.Add(new LineItem
            {
                Vpn = Text("Part Number"), Description = Text("Description"), Qty = (int)qty,
                Msrp = included ? 0m : Number("Unit Price"), Cost = 0m,
                LineSequence = lineNumberColumn > 0
                    ? sequencer.Next(WorkbookReader.CellText(sheet.Cell(row, lineNumberColumn)))
                    : null,
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

    /// <summary>
    /// "incl." (any case, dot optional) marks a component included in its parent's price. A row
    /// with neither a Unit Price nor a Price List is treated the same way.
    /// </summary>
    private static bool IsIncluded(string unitPrice, string priceList)
    {
        var marker = unitPrice.Trim().TrimEnd('.');
        return string.Equals(marker, "incl", StringComparison.OrdinalIgnoreCase)
            || (unitPrice.Length == 0 && priceList.Length == 0);
    }

    /// <summary>
    /// Flattens Iris's up-to-three-level Line# ("1.0" → "1.2" → "1.2.1") into the two-level
    /// parent/child sequence the CRM templates use ("1", "1.01", "1.02", …). A row whose Line# has
    /// no non-zero component after the first ("1", "1.0") opens a parent; every deeper row
    /// becomes the next child of the current parent, whatever its depth, so 1.2.1 and 1.2.2
    /// follow 1.2 as 1.03 and 1.04. A child whose top-level number differs from the open parent's,
    /// or an unreadable Line#, opens a new parent rather than attaching to the wrong group.
    /// Parents are numbered by a running counter.
    /// </summary>
    private sealed class LineSequencer
    {
        private int parent;
        private int child;
        private string? group;

        public string Next(string lineNumber)
        {
            var segments = lineNumber.Trim().Split('.', StringSplitOptions.TrimEntries);
            var valid = segments.Length > 0 && segments.All(segment => segment.Length > 0 && segment.All(char.IsAsciiDigit));
            var isParent = !valid || segments.Skip(1).All(segment => segment.TrimStart('0').Length == 0);
            var top = valid ? segments[0].TrimStart('0') : null;

            if (isParent || group is null || top != group)
            {
                parent++;
                child = 0;
                group = top;
                return parent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            child++;
            return $"{parent}.{child:D2}";
        }
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

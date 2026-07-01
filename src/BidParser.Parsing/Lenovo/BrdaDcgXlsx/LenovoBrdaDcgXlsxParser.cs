using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using ExcelDataReader;

namespace BidParser.Parsing.Lenovo.BrdaDcgXlsx;

public sealed partial class LenovoBrdaDcgXlsxParser : IParser
{
    static LenovoBrdaDcgXlsxParser()
    {
        // ExcelDataReader needs legacy code-page support to decode .xls strings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string Slug => ParserSlugs.LenovoBrdaDcgXlsx;
    public string DisplayName => "BRDA DCG (XLS)";
    public string Vendor => Vendors.Lenovo;
    public string AcceptedMime => "application/vnd.ms-excel";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public ParseResult Parse(string path)
    {
        var rows = ReadSheet(path);

        var headerRowIndex = FindHeaderRow(rows)
            ?? throw new ParseError("detect", "Could not find the line-item header row.", "Missing 'PN' / 'Description' / 'Requested Quantity' header.");

        var columns = MapColumns(rows[headerRowIndex]);
        var (items, quotedTotal) = ExtractRows(rows, headerRowIndex + 1, columns);
        var quoteNumber = ExtractQuoteNumber(rows, path);
        var validation = ParseValidation.Validate(items, quotedTotal);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Sheet reading

    private static List<object?[]> ReadSheet(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateBinaryReader(stream);

        // First (and in the Lenovo template, only) sheet.
        var rows = new List<object?[]>();
        if (!reader.Read()) // advance past nothing; first Read positions on first row
        {
            return rows;
        }

        do
        {
            var row = new object?[reader.FieldCount];
            for (var col = 0; col < reader.FieldCount; col++)
            {
                row[col] = reader.GetValue(col);
            }
            rows.Add(row);
        } while (reader.Read());

        return rows;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Header location

    private static int? FindHeaderRow(IReadOnlyList<object?[]> rows)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var hasPn = false;
            var hasDescription = false;
            var hasQty = false;
            foreach (var cell in row)
            {
                var text = TextCleaner.Clean(cell);
                if (string.Equals(text, "PN", StringComparison.OrdinalIgnoreCase)) hasPn = true;
                else if (string.Equals(text, "Description", StringComparison.OrdinalIgnoreCase)) hasDescription = true;
                else if (string.Equals(text, "Requested Quantity", StringComparison.OrdinalIgnoreCase)) hasQty = true;
            }
            if (hasPn && hasDescription && hasQty)
            {
                return rowIndex;
            }
        }
        return null;
    }

    private sealed record ColumnMap(int Pn, int Description, int Qty, int UnitPrice, int ExtendedPrice);

    private static ColumnMap MapColumns(object?[] header)
    {
        int? pn = null, desc = null, qty = null, unit = null, extended = null;
        for (var c = 0; c < header.Length; c++)
        {
            var text = TextCleaner.Clean(header[c]);
            if (string.Equals(text, "PN", StringComparison.OrdinalIgnoreCase)) pn = c;
            else if (string.Equals(text, "Description", StringComparison.OrdinalIgnoreCase)) desc = c;
            else if (string.Equals(text, "Requested Quantity", StringComparison.OrdinalIgnoreCase)) qty = c;
            else if (text.StartsWith("Adjusted Buy Price", StringComparison.OrdinalIgnoreCase))
            {
                // The header has two "Adjusted Buy Price (AUD)" cells side by side
                // (per unit, then qty × unit). Take them in order.
                if (unit is null) unit = c;
                else extended ??= c;
            }
        }

        if (pn is null || desc is null || qty is null || unit is null || extended is null)
        {
            throw new ParseError(
                "detect",
                "Header row is missing one of: PN, Description, Requested Quantity, Adjusted Buy Price (×2).",
                "Incomplete BRDA DCG (XLS) header row.");
        }

        return new ColumnMap(pn.Value, desc.Value, qty.Value, unit.Value, extended.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Row classification + extraction

    private static (List<LineItem> Items, decimal QuotedTotal) ExtractRows(
        IReadOnlyList<object?[]> rows,
        int firstBodyRow,
        ColumnMap cols)
    {
        var items = new List<LineItem>();
        // Every emitted line — parent and child alike — takes the next number in a single
        // running sequence: 1, 2, 3, … Children are no longer sub-numbered as parent.NN.
        // `sawParent` still gates orphan children that appear before any parent.
        var lineSeq = 0;
        var sawParent = false;
        decimal? quotedTotal = null;

        for (var rowIndex = firstBodyRow; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];

            var pn = Cell(row, cols.Pn);
            var description = Cell(row, cols.Description);
            var qtyText = Cell(row, cols.Qty);
            var unitPriceCell = Get(row, cols.UnitPrice);
            var extendedCell = Get(row, cols.ExtendedPrice);
            var unitPriceText = TextCleaner.Clean(unitPriceCell);
            var extendedText = TextCleaner.Clean(extendedCell);

            // Total: row — terminator. Quoted total sits in the extended-price column.
            if (string.Equals(unitPriceText, "Total:", StringComparison.OrdinalIgnoreCase))
            {
                var rawTotal = DecimalCleaner.Parse(extendedCell);
                quotedTotal = decimal.Round(rawTotal, 2, MidpointRounding.AwayFromZero);
                break;
            }

            if (IsBlank(pn) && IsBlank(description) && IsBlank(qtyText) && IsBlank(unitPriceText) && IsBlank(extendedText))
            {
                continue;
            }

            // Config marker rows (e.g. "Set from Configurator(Config 1) SIDX02SDL3")
            if (pn.StartsWith("Set from Configurator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Per-config "<PN>  Subtotal  <price>" rows.
            if (string.Equals(description, "Subtotal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Repeated child-table header rows.
            if (string.Equals(pn, "Feature Code", StringComparison.OrdinalIgnoreCase)
                && string.Equals(description, "Description", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsBlank(pn))
            {
                continue;
            }

            var qty = ParseQty(qtyText);

            // A populated, non-zero unit-price cell marks a PARENT (a billable top-level
            // item). Children carry the part number + description + qty; some children
            // (e.g. "software1 Configuration Instruction") have an explicit 0.0 in the
            // price column, so a populated cell alone is not enough — only price > 0
            // promotes a row to PARENT.
            decimal? unitPrice = null;
            if (unitPriceCell is not null && !IsBlank(unitPriceText))
            {
                unitPrice = DecimalCleaner.Parse(unitPriceCell);
            }
            var isParent = unitPrice is > 0m;

            string lineSequence;
            decimal cost;
            if (isParent)
            {
                sawParent = true;
                lineSeq++;
                lineSequence = lineSeq.ToString(CultureInfo.InvariantCulture);
                cost = unitPrice!.Value;
            }
            else
            {
                if (!sawParent)
                {
                    // Orphan child before any parent — skip rather than crash.
                    continue;
                }
                lineSeq++;
                lineSequence = lineSeq.ToString(CultureInfo.InvariantCulture);
                cost = 0m;
            }

            items.Add(new LineItem
            {
                Vpn = pn,
                Description = description.Length > 0 ? description : null,
                Cost = cost,
                Qty = qty,
                LineSequence = lineSequence,
                Raw = BuildRaw(pn, description, qtyText, unitPriceText, extendedText)
            });
        }

        if (quotedTotal is null)
        {
            throw new ParseError(
                "totals",
                "Could not locate the 'Total:' row in the BRDA DCG (XLS) workbook.",
                "Missing 'Total:' row.");
        }

        return (items, quotedTotal.Value);
    }

    private static int ParseQty(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }
        return DecimalCleaner.ParseInt(text);
    }

    private static IReadOnlyDictionary<string, string> BuildRaw(
        string pn, string description, string qty, string unitPrice, string extended)
    {
        var raw = new Dictionary<string, string>();
        if (pn.Length > 0) raw["PN"] = pn;
        if (description.Length > 0) raw["Description"] = description;
        if (qty.Length > 0) raw["Requested Quantity"] = qty;
        if (unitPrice.Length > 0) raw["Adjusted Buy Price (AUD) (per unit)"] = unitPrice;
        if (extended.Length > 0) raw["Adjusted Buy Price (AUD) (qty x unit price)"] = extended;
        return raw;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Quote metadata

    private static string ExtractQuoteNumber(IReadOnlyList<object?[]> rows, string path)
    {
        foreach (var row in rows)
        {
            foreach (var cell in row)
            {
                var text = TextCleaner.Clean(cell);
                var match = BidRequestNumber().Match(text);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    [GeneratedRegex(@"Bid Request Number:\s*([A-Za-z0-9_-]+)")]
    private static partial Regex BidRequestNumber();

    // ──────────────────────────────────────────────────────────────────────────────
    // Cell helpers

    private static object? Get(object?[] row, int index)
        => index < row.Length ? row[index] : null;

    private static string Cell(object?[] row, int index)
        => TextCleaner.Clean(Get(row, index));

    private static bool IsBlank(string text) => text.Length == 0;
}

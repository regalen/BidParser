using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using ExcelDataReader;

namespace BidParser.Parsing.Lenovo.LbpeIsgXls;

/// <summary>
/// Parses Lenovo LBP-E ISG Quote Excel workbooks (legacy OLE .xls and OOXML .xlsx). Configuration parents take
/// their cost from the preceding Subtotal row; included components are emitted as zero-cost children.
/// See docs/lenovo_lbpe_isg_xls.md.
/// </summary>
public sealed partial class LenovoLbpeIsgXlsParser : IParser
{
    static LenovoLbpeIsgXlsParser()
    {
        // ExcelDataReader needs legacy code-page support to decode .xls strings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string Slug => ParserSlugs.LenovoLbpeIsgXls;
    public string DisplayName => "LBP-E ISG Quote (XLSX)";
    public string Vendor => Vendors.LenovoIsg;
    public string OutputVendorName => Vendors.LenovoOutput;
    public string AcceptedMime => "application/vnd.ms-excel";
    public IReadOnlyList<string> AcceptedMimes =>
    [
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    ];
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsSolutionIdSplit => true;

    /// <summary>
    /// Recognition signature for the Lenovo Auto entry: an Excel workbook (.xls or .xlsx) carrying the
    /// PN / Description / Requested Quantity header row and a valid price header pair (Adjusted Buy Price or Estimated Price).
    /// An HTML-disguised or unrecognised file fails reader creation or header mapping and scores 0.0.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            var rows = ReadSheet(path);
            var headerRowIndex = FindHeaderRow(rows);
            if (headerRowIndex is null)
            {
                return 0.0;
            }

            MapColumns(rows[headerRowIndex.Value]);
            return 0.9;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        var rows = ReadSheet(path);
        var headerRowIndex = FindHeaderRow(rows)
            ?? throw new ParseError(
                "detect",
                "Could not find the line-item header row.",
                "Missing 'PN' / 'Description' / 'Requested Quantity' header.");

        var columns = MapColumns(rows[headerRowIndex]);
        var (items, quotedTotal) = ExtractRows(rows, headerRowIndex + 1, columns);

        var bid = ExtractBidMetadata(rows);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = ExtractQuoteNumber(rows, path),
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
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

    private static List<object?[]> ReadSheet(string path)
    {
        using var stream = File.OpenRead(path);

        // Magic-byte validation accepts OLE or HTML for application/vnd.ms-excel because Zebra
        // exports styled HTML as .xls. Surface a non-Excel selection as a wrong-file-type error.
        IExcelDataReader reader;
        try
        {
            reader = ExcelReaderFactory.CreateReader(stream);
        }
        catch (Exception ex) when (ex is not ParseError)
        {
            throw new ParseError(
                "detect",
                "File is not a supported Excel (.xls / .xlsx) workbook.",
                "Could not open the file as an Excel workbook.");
        }

        using var _ = reader;
        var rows = new List<object?[]>();
        if (!reader.Read())
        {
            return rows;
        }

        do
        {
            var row = new object?[reader.FieldCount];
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[column] = reader.GetValue(column);
            }
            rows.Add(row);
        } while (reader.Read());

        return rows;
    }

    private static int? FindHeaderRow(IReadOnlyList<object?[]> rows)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var hasPn = false;
            var hasDescription = false;
            var hasQty = false;

            foreach (var cell in rows[rowIndex])
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

    private sealed record ColumnMap(
        int Pn,
        int Description,
        int Qty,
        int UnitPrice,
        int ExtendedPrice,
        string PriceHeaderName);

    private static ColumnMap MapColumns(object?[] header)
    {
        int? pn = null, description = null, qty = null;
        int? unitPrice = null, extendedPrice = null;
        string? recognizedFamily = null;
        string? priceHeaderName = null;

        for (var column = 0; column < header.Length; column++)
        {
            var text = TextCleaner.Clean(header[column]);
            if (string.Equals(text, "PN", StringComparison.OrdinalIgnoreCase))
            {
                pn = column;
            }
            else if (string.Equals(text, "Description", StringComparison.OrdinalIgnoreCase))
            {
                description = column;
            }
            else if (string.Equals(text, "Requested Quantity", StringComparison.OrdinalIgnoreCase))
            {
                qty = column;
            }
            else if (text.StartsWith("Adjusted Buy Price", StringComparison.OrdinalIgnoreCase))
            {
                if (recognizedFamily is null)
                {
                    recognizedFamily = "Adjusted Buy Price";
                    priceHeaderName = text;
                    unitPrice = column;
                }
                else if (recognizedFamily == "Adjusted Buy Price")
                {
                    extendedPrice ??= column;
                }
                else
                {
                    throw new ParseError(
                        "detect",
                        "Header row contains mixed price header types.",
                        "Incomplete LBP-E ISG Quote header row.");
                }
            }
            else if (text.StartsWith("Estimated Price", StringComparison.OrdinalIgnoreCase))
            {
                if (recognizedFamily is null)
                {
                    recognizedFamily = "Estimated Price";
                    priceHeaderName = text;
                    unitPrice = column;
                }
                else if (recognizedFamily == "Estimated Price")
                {
                    extendedPrice ??= column;
                }
                else
                {
                    throw new ParseError(
                        "detect",
                        "Header row contains mixed price header types.",
                        "Incomplete LBP-E ISG Quote header row.");
                }
            }
        }

        if (pn is null || description is null || qty is null || unitPrice is null || extendedPrice is null || priceHeaderName is null)
        {
            throw new ParseError(
                "detect",
                "Header row is missing one of: PN, Description, Requested Quantity, or a matching pair of Adjusted Buy Price / Estimated Price columns.",
                "Incomplete LBP-E ISG Quote header row.");
        }

        return new ColumnMap(pn.Value, description.Value, qty.Value, unitPrice.Value, extendedPrice.Value, priceHeaderName);
    }

    private static (List<LineItem> Items, decimal QuotedTotal) ExtractRows(
        IReadOnlyList<object?[]> rows,
        int firstBodyRow,
        ColumnMap columns)
    {
        var items = new List<LineItem>();
        string? currentSolutionId = null;
        var parentIndex = 0;
        var childIndex = 0;
        decimal? blockSubtotal = null;
        var blockSubtotalText = "";
        var blockAccrued = 0m;
        var pendingParent = false;
        var blockClosed = false;
        decimal? quotedTotal = null;

        for (var rowIndex = firstBodyRow; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var pn = Cell(row, columns.Pn);
            var description = Cell(row, columns.Description);
            var qtyText = Cell(row, columns.Qty);
            var unitPriceCell = Get(row, columns.UnitPrice);
            var extendedPriceCell = Get(row, columns.ExtendedPrice);
            var unitPriceText = TextCleaner.Clean(unitPriceCell);
            var extendedPriceText = TextCleaner.Clean(extendedPriceCell);

            if (string.Equals(unitPriceText, "Total:", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsBlank(extendedPriceText))
                {
                    quotedTotal = RoundMoney(DecimalCleaner.Parse(extendedPriceCell));
                }
                break;
            }

            if (IsBlank(pn) && IsBlank(description) && IsBlank(qtyText)
                && IsBlank(unitPriceText) && IsBlank(extendedPriceText))
            {
                continue;
            }

            if (pn.StartsWith("Set from Configurator", StringComparison.OrdinalIgnoreCase))
            {
                var match = SolutionId().Match(pn);
                currentSolutionId = match.Success ? match.Value : null;
                continue;
            }

            if (string.Equals(description, "Subtotal", StringComparison.OrdinalIgnoreCase))
            {
                blockSubtotal = RoundMoney(DecimalCleaner.Parse(unitPriceCell));
                blockSubtotalText = unitPriceText;
                blockAccrued = 0m;
                pendingParent = true;
                blockClosed = false;
                continue;
            }

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
            var unitPrice = ParseOptionalMoney(unitPriceCell, unitPriceText);

            if (pendingParent)
            {
                parentIndex++;
                childIndex = 0;
                items.Add(CreateItem(
                    pn, description, qty, blockSubtotal!.Value,
                    parentIndex.ToString(CultureInfo.InvariantCulture),
                    currentSolutionId, writeSolutionComment: true,
                    qtyText, unitPriceText, extendedPriceText, blockSubtotalText,
                    columns.PriceHeaderName));

                pendingParent = false;
                blockAccrued += unitPrice ?? 0m;
                blockClosed = HasReconciled(blockAccrued, blockSubtotal.Value);
                continue;
            }

            // A priced row that sits outside any open configuration block is a standalone
            // line of its own — the warranty/licence options Lenovo lists between blocks,
            // or an item quoted before the first Subtotal. Its price is not covered by any
            // subtotal, so it carries its own cost rather than the zero-cost sentinel.
            if ((blockSubtotal is null || blockClosed) && unitPrice is > 0m)
            {
                parentIndex++;
                childIndex = 0;
                items.Add(CreateItem(
                    pn, description, qty, unitPrice.Value,
                    parentIndex.ToString(CultureInfo.InvariantCulture),
                    currentSolutionId, writeSolutionComment: true,
                    qtyText, unitPriceText, extendedPriceText, null,
                    columns.PriceHeaderName));

                // The standalone line owns no subtotal, so no block is open after it.
                blockSubtotal = null;
                blockSubtotalText = "";
                blockAccrued = 0m;
                blockClosed = false;
                continue;
            }

            if (parentIndex == 0)
            {
                // Unpriced row before the first parent — there is nothing to attach it to.
                continue;
            }

            childIndex++;
            items.Add(CreateItem(
                pn, description, qty, 0m,
                $"{parentIndex}.{childIndex:D2}",
                currentSolutionId, writeSolutionComment: false,
                qtyText, unitPriceText, extendedPriceText, null,
                columns.PriceHeaderName));

            if (!blockClosed && blockSubtotal is not null)
            {
                blockAccrued += unitPrice ?? 0m;
                blockClosed = HasReconciled(blockAccrued, blockSubtotal.Value);
            }
        }

        if (quotedTotal is null)
        {
            throw new ParseError(
                "totals",
                "Could not locate the 'Total:' row.",
                "Missing 'Total:' row.");
        }

        return (items, quotedTotal.Value);
    }

    private static LineItem CreateItem(
        string pn,
        string description,
        int qty,
        decimal cost,
        string lineSequence,
        string? solutionId,
        bool writeSolutionComment,
        string qtyText,
        string unitPriceText,
        string extendedPriceText,
        string? subtotalText,
        string priceHeaderName)
        => new()
        {
            Vpn = pn,
            Description = IsBlank(description) ? null : description,
            Cost = cost,
            Qty = qty,
            SolutionId = solutionId,
            Comments = writeSolutionComment && solutionId is not null ? $"Solution ID: {solutionId}" : null,
            LineSequence = lineSequence,
            Raw = BuildRaw(pn, description, qtyText, unitPriceText, extendedPriceText, subtotalText, priceHeaderName)
        };

    private static decimal? ParseOptionalMoney(object? value, string text)
        => IsBlank(text) ? null : RoundMoney(DecimalCleaner.Parse(value));

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    // The block closes once its rows have added up to the subtotal. Deliberately ">=" rather
    // than an equality test: if an accrual ever overshoots without landing exactly on the
    // subtotal, an equality test would leave the block open forever and silently absorb every
    // later priced row as a zero-cost child, dropping quoted value.
    private static bool HasReconciled(decimal accrued, decimal subtotal)
        => accrued >= subtotal - 0.01m;

    private static int ParseQty(string text)
        => IsBlank(text) ? 0 : DecimalCleaner.ParseInt(text);

    private static IReadOnlyDictionary<string, string> BuildRaw(
        string pn,
        string description,
        string qty,
        string unitPrice,
        string extendedPrice,
        string? subtotal,
        string priceHeaderName)
    {
        var raw = new Dictionary<string, string>();
        if (!IsBlank(pn)) raw["PN"] = pn;
        if (!IsBlank(description)) raw["Description"] = description;
        if (!IsBlank(qty)) raw["Requested Quantity"] = qty;
        if (!IsBlank(unitPrice)) raw[$"{priceHeaderName} (per unit)"] = unitPrice;
        if (!IsBlank(extendedPrice)) raw[$"{priceHeaderName} (qty x unit price)"] = extendedPrice;
        if (!IsBlank(subtotal ?? "")) raw["Subtotal (AUD) (per unit)"] = subtotal!;
        return raw;
    }

    private static string ExtractQuoteNumber(IReadOnlyList<object?[]> rows, string path)
    {
        foreach (var row in rows)
        {
            foreach (var cell in row)
            {
                var match = BidRequestNumber().Match(TextCleaner.Clean(cell));
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static (string? BidNumber, string? BidRevision) ExtractBidMetadata(IReadOnlyList<object?[]> rows)
    {
        var text = string.Join(" ", rows.SelectMany(row => row).Select(TextCleaner.Clean));
        var number = BidRequestNumber().Match(text).Groups[1].Value;
        var revision = VersionNumber().Match(text).Groups[1].Value;
        return BidMetadataCleaner.Clean(number, revision);
    }

    [GeneratedRegex(@"Bid Request Number:\s*([A-Za-z0-9_-]+)")]
    private static partial Regex BidRequestNumber();

    [GeneratedRegex(@"Version#:\s*([^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionNumber();

    [GeneratedRegex(@"\bSID[A-Za-z0-9]+")]
    private static partial Regex SolutionId();

    private static object? Get(object?[] row, int index)
        => index < row.Length ? row[index] : null;

    private static string Cell(object?[] row, int index)
        => TextCleaner.Clean(Get(row, index));

    private static bool IsBlank(string text) => text.Length == 0;
}

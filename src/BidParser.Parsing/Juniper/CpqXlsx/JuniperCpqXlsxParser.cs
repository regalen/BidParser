using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Juniper.CpqXlsx;

/// <summary>
/// Parses Juniper CPQ Bill of Materials workbooks (XLSX).
/// Anchor-based on the CPQ line-item table headers ("SKU", "Description", "Service Duration",
/// "List Price Per", "Quantity", "Extended Net Price"), the "Transaction #:" metadata label,
/// and the "Totals:" summary row. Structural option summary rows (carrying a SKU and aggregate
/// totals without a description or unit list price) are excluded by content.
/// See docs/juniper_cpq_xlsx.md.
/// </summary>
public sealed class JuniperCpqXlsxParser : IParser
{
    private const string SkuHeader = "SKU";
    private const string DescriptionHeader = "Description";
    private const string ServiceDurationHeader = "Service Duration";
    private const string ListPricePerHeader = "List Price Per";
    private const string QuantityHeader = "Quantity";
    private const string ExtendedNetPriceHeader = "Extended Net Price";
    private const string TransactionNumberLabel = "Transaction #:";
    private const string TotalsLabel = "Totals:";

    private static readonly string[] RequiredHeaders =
    [
        SkuHeader,
        DescriptionHeader,
        ServiceDurationHeader,
        ListPricePerHeader,
        QuantityHeader,
        ExtendedNetPriceHeader
    ];

    public string Slug => ParserSlugs.JuniperCpqXlsx;
    public string DisplayName => "CPQ (XLSX)";
    public string Vendor => Vendors.Juniper;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.ForeignUplift, CrmTemplates.ForeignNoCalculation];
    public bool TermRendersAsComment => true;

    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            foreach (var sheet in workbook.Worksheets.Where(sheet => sheet.Visibility == XLWorksheetVisibility.Visible))
            {
                if (FindCandidateHeaders(sheet).Any(HasAllRequiredHeaders))
                {
                    return 0.9;
                }
            }

            return 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var (sheet, header) = FindQuoteTable(workbook)
            ?? throw new ParseError(
                "detect",
                "Could not find the Juniper CPQ quote table.",
                "Missing Juniper CPQ header row.");

        WorkbookReader.RequireLabels(header, RequiredHeaders);

        var items = new List<LineItem>();
        decimal? quotedTotal = null;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? header.RowNumber;

        for (var row = header.RowNumber + 1; row <= lastRow; row++)
        {
            if (RowContainsLabel(sheet, row, TotalsLabel))
            {
                var totalText = Text(sheet, row, header, ExtendedNetPriceHeader);
                if (!TryParseNonNegativeDecimal(totalText, out var parsedTotal))
                {
                    throw new ParseError(
                        "extract",
                        "Could not read the Juniper CPQ Totals Extended Net Price.",
                        "Missing or invalid Extended Net Price on the Juniper CPQ Totals: row.");
                }

                quotedTotal = parsedTotal;
                break;
            }

            var sku = Text(sheet, row, header, SkuHeader);
            var description = Text(sheet, row, header, DescriptionHeader);
            var serviceDuration = Text(sheet, row, header, ServiceDurationHeader);
            var listPricePer = Text(sheet, row, header, ListPricePerHeader);
            var quantity = Text(sheet, row, header, QuantityHeader);
            var extendedNetPrice = Text(sheet, row, header, ExtendedNetPriceHeader);

            if (sku.Length == 0
                && description.Length == 0
                && serviceDuration.Length == 0
                && listPricePer.Length == 0
                && quantity.Length == 0
                && extendedNetPrice.Length == 0)
            {
                continue;
            }

            if (IsStructuralOptionSummary(sku, description, serviceDuration, listPricePer, quantity, extendedNetPrice))
            {
                continue;
            }

            var sequence = items.Count + 1;
            if (sku.Length == 0)
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} is missing a SKU.",
                    "Blank Juniper CPQ SKU on product row.");
            }

            if (description.Length == 0)
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) is missing a Description.",
                    "Blank Juniper CPQ Description on product row.");
            }

            if (!TryParsePositiveInt(quantity, out var qty))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid Quantity.",
                    "Missing, nonpositive, or fractional Juniper CPQ Quantity.");
            }

            if (!TryParseNonNegativeDecimal(listPricePer, out var msrp))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid List Price Per.",
                    "Missing or invalid Juniper CPQ List Price Per.");
            }

            if (!TryParseNonNegativeDecimal(extendedNetPrice, out var extendedNet))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid Extended Net Price.",
                    "Missing or invalid Juniper CPQ Extended Net Price.");
            }

            int? term = null;
            if (serviceDuration.Length > 0)
            {
                if (!TryParsePositiveInt(serviceDuration, out var parsedTerm))
                {
                    throw new ParseError(
                        "extract",
                        $"Line {sequence} ({sku}) has an invalid Service Duration.",
                        "Nonpositive, fractional, or unreadable Juniper CPQ Service Duration.");
                }

                term = parsedTerm;
            }

            items.Add(new LineItem
            {
                Vpn = sku,
                Description = description,
                Qty = qty,
                Cost = extendedNet / qty,
                Msrp = msrp,
                Term = term,
                Raw = WorkbookReader.BuildRawDict(sheet, row, header)
            });
        }

        if (quotedTotal is null)
        {
            throw new ParseError(
                "extract",
                "Could not find the Juniper CPQ Totals row.",
                "Missing Totals: row in Juniper CPQ table.");
        }

        if (items.Count == 0)
        {
            throw new ParseError(
                "extract",
                "Could not find any Juniper CPQ product lines.",
                "No product rows found in Juniper CPQ table.");
        }

        var transactionNumber = ReadMetadataValue(sheet, TransactionNumberLabel);
        var (bidNumber, bidRevision) = BidMetadataCleaner.CleanRevisionless(transactionNumber);
        var quoteNumber = bidNumber ?? Path.GetFileNameWithoutExtension(path);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "USD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = ParseValidation.Validate(items, quotedTotal)
        };
    }

    private static (IXLWorksheet Sheet, HeaderMap Header)? FindQuoteTable(XLWorkbook workbook)
    {
        (IXLWorksheet Sheet, HeaderMap Header, int MatchCount)? bestCandidate = null;

        foreach (var sheet in workbook.Worksheets.Where(sheet => sheet.Visibility == XLWorksheetVisibility.Visible))
        {
            foreach (var header in FindCandidateHeaders(sheet))
            {
                if (HasAllRequiredHeaders(header))
                {
                    return (sheet, header);
                }

                var matchCount = RequiredHeaders.Count(header.Columns.ContainsKey);
                if (bestCandidate is null || matchCount > bestCandidate.Value.MatchCount)
                {
                    bestCandidate = (sheet, header, matchCount);
                }
            }
        }

        return bestCandidate is null ? null : (bestCandidate.Value.Sheet, bestCandidate.Value.Header);
    }

    private static IEnumerable<HeaderMap> FindCandidateHeaders(IXLWorksheet sheet)
    {
        var range = sheet.RangeUsed();
        if (range is null)
        {
            yield break;
        }

        foreach (var row in range.RowsUsed())
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in row.CellsUsed())
            {
                var label = WorkbookReader.CellText(cell);
                if (label.Length > 0)
                {
                    columns.TryAdd(label, cell.Address.ColumnNumber);
                }
            }

            if (RequiredHeaders.Count(columns.ContainsKey) >= 2)
            {
                yield return new HeaderMap(row.RowNumber(), columns);
            }
        }
    }

    private static bool HasAllRequiredHeaders(HeaderMap header) =>
        RequiredHeaders.All(header.Columns.ContainsKey);

    private static bool RowContainsLabel(IXLWorksheet sheet, int rowNumber, string expectedLabel) =>
        sheet.Row(rowNumber).CellsUsed()
            .Any(cell => string.Equals(WorkbookReader.CellText(cell), expectedLabel, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// CPQ option summaries carry a SKU and aggregate quantity/net totals for a configuration group,
    /// while leaving product description, service duration, and unit list price blank.
    /// </summary>
    private static bool IsStructuralOptionSummary(
        string sku,
        string description,
        string serviceDuration,
        string listPricePer,
        string quantity,
        string extendedNetPrice) =>
        sku.Length > 0
        && description.Length == 0
        && serviceDuration.Length == 0
        && listPricePer.Length == 0
        && TryParsePositiveInt(quantity, out _)
        && TryParseNonNegativeDecimal(extendedNetPrice, out _);

    private static string ReadMetadataValue(IXLWorksheet sheet, string label)
    {
        var labelCell = sheet.RangeUsed()?.CellsUsed()
            .FirstOrDefault(cell => string.Equals(WorkbookReader.CellText(cell), label, StringComparison.OrdinalIgnoreCase));
        if (labelCell is null)
        {
            return string.Empty;
        }

        var row = labelCell.Address.RowNumber;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? labelCell.Address.ColumnNumber;
        for (var col = labelCell.Address.ColumnNumber + 1; col <= lastColumn; col++)
        {
            var value = WorkbookReader.CellText(sheet.Cell(row, col));
            if (value.Length == 0)
            {
                continue;
            }

            return value.EndsWith(':') ? string.Empty : value;
        }

        return string.Empty;
    }

    private static string Text(IXLWorksheet sheet, int row, HeaderMap header, string label) =>
        WorkbookReader.CellText(sheet.Cell(row, header.Require(label)));

    private static bool TryParsePositiveInt(string value, out int result)
    {
        result = 0;
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            var number = DecimalCleaner.Parse(value);
            if (number < 1m || number > int.MaxValue || number != decimal.Truncate(number))
            {
                return false;
            }

            result = (int)number;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseNonNegativeDecimal(string value, out decimal result)
    {
        result = 0m;
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            var number = DecimalCleaner.Parse(value);
            if (number < 0m)
            {
                return false;
            }

            result = number;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }
    }
}

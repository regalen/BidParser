using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Juniper.IndirectXlsx;

/// <summary>
/// Parses Juniper Indirect Quotation workbooks (XLSX).
/// Anchor-based on the Indirect line-item headers ("SKU", "SKU DESCRIPTION", "QUANTITY",
/// "UNIT NET PRICE", "LIST PRICE $", "EXTENDED NET TOTAL", "SERVICE DURATION", "CURRENCY"),
/// with optional bid metadata columns ("SHIP AND DEBIT ID", "CONFIG INSTANCE") read from the
/// first extracted product line. Validates each row's Cost × Qty against EXTENDED NET TOTAL
/// while keeping QuotedTotal null.
/// See docs/juniper_indirect_xlsx.md.
/// </summary>
public sealed class JuniperIndirectXlsxParser : IParser
{
    private const string SkuHeader = "SKU";
    private const string SkuDescriptionHeader = "SKU DESCRIPTION";
    private const string QuantityHeader = "QUANTITY";
    private const string UnitNetPriceHeader = "UNIT NET PRICE";
    private const string ListPriceHeader = "LIST PRICE $";
    private const string ExtendedNetTotalHeader = "EXTENDED NET TOTAL";
    private const string ServiceDurationHeader = "SERVICE DURATION";
    private const string CurrencyHeader = "CURRENCY";
    private const string ShipAndDebitIdHeader = "SHIP AND DEBIT ID";
    private const string ConfigInstanceHeader = "CONFIG INSTANCE";
    private const decimal RowValidationTolerance = 0.01m;

    private static readonly string[] RequiredHeaders =
    [
        SkuHeader,
        SkuDescriptionHeader,
        QuantityHeader,
        UnitNetPriceHeader,
        ListPriceHeader,
        ExtendedNetTotalHeader,
        ServiceDurationHeader,
        CurrencyHeader
    ];

    public string Slug => ParserSlugs.JuniperIndirectXlsx;
    public string DisplayName => "Indirect (XLSX)";
    public string Vendor => Vendors.Juniper;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.ForeignUplift;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.ForeignUplift];
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
                "Could not find the Juniper Indirect quote table.",
                "Missing Juniper Indirect header row.");

        WorkbookReader.RequireLabels(header, RequiredHeaders);

        var items = new List<LineItem>();
        var warnings = new List<string>();
        var totalDifference = 0m;
        var shipAndDebitId = string.Empty;
        var configInstance = string.Empty;
        var capturedFirstRowMetadata = false;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? header.RowNumber;

        for (var row = header.RowNumber + 1; row <= lastRow; row++)
        {
            var sku = Text(sheet, row, header, SkuHeader);
            var description = Text(sheet, row, header, SkuDescriptionHeader);
            var quantity = Text(sheet, row, header, QuantityHeader);
            var unitNetPrice = Text(sheet, row, header, UnitNetPriceHeader);
            var listPrice = Text(sheet, row, header, ListPriceHeader);
            var extendedNetTotal = Text(sheet, row, header, ExtendedNetTotalHeader);
            var serviceDuration = Text(sheet, row, header, ServiceDurationHeader);
            var currency = Text(sheet, row, header, CurrencyHeader);

            if (sku.Length == 0
                && description.Length == 0
                && quantity.Length == 0
                && unitNetPrice.Length == 0
                && listPrice.Length == 0
                && extendedNetTotal.Length == 0
                && serviceDuration.Length == 0
                && currency.Length == 0)
            {
                continue;
            }

            var sequence = items.Count + 1;

            if (!string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                throw new ParseError(
                    "currency",
                    $"This Juniper Indirect quote declares {(currency.Length == 0 ? "an unknown currency" : currency)} on line {sequence}; USD is required.",
                    "Unsupported or unreadable Juniper Indirect quote currency.");
            }

            if (sku.Length == 0)
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} is missing a SKU.",
                    "Blank Juniper Indirect SKU on product row.");
            }

            if (description.Length == 0)
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) is missing a SKU DESCRIPTION.",
                    "Blank Juniper Indirect SKU DESCRIPTION on product row.");
            }

            if (!TryParsePositiveInt(quantity, out var qty))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid QUANTITY.",
                    "Missing, nonpositive, or fractional Juniper Indirect QUANTITY.");
            }

            if (!TryParseNonNegativeDecimal(unitNetPrice, out var cost))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid UNIT NET PRICE.",
                    "Missing or invalid Juniper Indirect UNIT NET PRICE.");
            }

            if (!TryParseNonNegativeDecimal(listPrice, out var msrp))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid LIST PRICE $.",
                    "Missing or invalid Juniper Indirect LIST PRICE $.");
            }

            if (!TryParseNonNegativeDecimal(extendedNetTotal, out var rowExtendedNet))
            {
                throw new ParseError(
                    "extract",
                    $"Line {sequence} ({sku}) has an invalid EXTENDED NET TOTAL.",
                    "Missing or invalid Juniper Indirect EXTENDED NET TOTAL.");
            }

            int? term = null;
            if (serviceDuration.Length > 0)
            {
                if (!TryParsePositiveInt(serviceDuration, out var parsedTerm))
                {
                    throw new ParseError(
                        "extract",
                        $"Line {sequence} ({sku}) has an invalid SERVICE DURATION.",
                        "Nonpositive, fractional, or unreadable Juniper Indirect SERVICE DURATION.");
                }

                term = parsedTerm;
            }

            if (!capturedFirstRowMetadata)
            {
                shipAndDebitId = OptionalText(sheet, row, header, ShipAndDebitIdHeader);
                configInstance = OptionalText(sheet, row, header, ConfigInstanceHeader);
                capturedFirstRowMetadata = true;
            }

            var lineComputed = decimal.Round(cost * qty, 2, MidpointRounding.AwayFromZero);
            var lineExtended = decimal.Round(rowExtendedNet, 2, MidpointRounding.AwayFromZero);
            var lineDifference = decimal.Round((cost * qty) - rowExtendedNet, 2, MidpointRounding.AwayFromZero);
            totalDifference += (cost * qty) - rowExtendedNet;
            if (Math.Abs(lineDifference) > RowValidationTolerance)
            {
                warnings.Add(
                    $"Line {sequence} ({sku}) computed total {lineComputed:F2} differs from EXTENDED NET TOTAL {lineExtended:F2}.");
            }

            items.Add(new LineItem
            {
                Vpn = sku,
                Description = description,
                Qty = qty,
                Cost = cost,
                Msrp = msrp,
                Term = term,
                Raw = WorkbookReader.BuildRawDict(sheet, row, header)
            });
        }

        if (items.Count == 0)
        {
            throw new ParseError(
                "extract",
                "Could not find any Juniper Indirect product lines.",
                "No product rows found in Juniper Indirect table.");
        }

        var (bidNumber, bidRevision) = BidMetadataCleaner.Clean(shipAndDebitId, configInstance);
        var quoteNumber = bidNumber ?? Path.GetFileNameWithoutExtension(path);

        var computedTotal = decimal.Round(items.Sum(item => item.Cost * item.Qty), 2, MidpointRounding.AwayFromZero);
        var matches = warnings.Count == 0;
        var validation = new ValidationResult
        {
            ComputedTotal = computedTotal,
            QuotedTotal = null,
            Matches = matches,
            Difference = matches ? 0m : decimal.Round(totalDifference, 2, MidpointRounding.AwayFromZero),
            Warnings = warnings
        };

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "USD",
                QuotedTotal = null,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
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

    private static string Text(IXLWorksheet sheet, int row, HeaderMap header, string label) =>
        WorkbookReader.CellText(sheet.Cell(row, header.Require(label)));

    private static string OptionalText(IXLWorksheet sheet, int row, HeaderMap header, string label) =>
        header.Columns.TryGetValue(label, out var column)
            ? WorkbookReader.CellText(sheet.Cell(row, column))
            : string.Empty;

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

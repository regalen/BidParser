using System.Globalization;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Hp.ServicesXlsx;

/// <summary>
/// Parses HP "Services (XLSX)" quote spreadsheets into <see cref="LineItem"/>s.
/// Handles HP hardware warranty renewal / extension exports, mapping each covered
/// device and service line into flat line items.
/// Supports splitting by Service Agreement ID (SAID) via <see cref="IParser.SupportsSolutionIdSplit"/>.
/// </summary>
public sealed class HpServicesXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpServicesXlsx;
    public string DisplayName => "Services (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsSolutionIdSplit => true;
    public string SolutionSplitLabel => "SAID";
    public OutputNameStyle OutputNameStyle => OutputNameStyle.SolutionScoped;

    /// <summary>
    /// Soft recognition score 0.0–1.0: returns 0.9 if the first sheet has Contract ID,
    /// Service Agreement ID, Service Product Number, and Line Item Total Net Price columns.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.First();
            var anchor = WorkbookReader.FindCell(sheet, "Contract ID");
            if (anchor is null)
            {
                return 0.0;
            }

            var headers = WorkbookReader.HeaderMap(sheet, anchor.Address.RowNumber);
            if (headers.Columns.ContainsKey("Service Agreement ID") &&
                headers.Columns.ContainsKey("Service Product Number") &&
                headers.Columns.ContainsKey("Line Item Total Net Price"))
            {
                return 0.9;
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
        var sheet = workbook.Worksheets.First();

        var anchor = WorkbookReader.FindCell(sheet, "Contract ID")
            ?? throw new ParseError("detect",
                "Could not find the Contract ID table header.",
                "Could not find Contract ID header");

        var headers = WorkbookReader.HeaderMap(sheet, anchor.Address.RowNumber);
        WorkbookReader.RequireLabels(headers,
            "Contract ID", "Service Agreement ID", "Coverage Start", "Coverage End",
            "Total Net Price", "Product Number", "Product Description", "Quantity",
            "Service Product Number", "Service Product Description", "Warranty End Date",
            "Serial Number", "Line Item Total Net Price");

        var items = new List<LineItem>();
        var saidTotals = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        string contractId = string.Empty;
        var counter = 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headers.RowNumber;

        for (var row = headers.RowNumber + 1; row <= lastRow; row++)
        {
            if (WorkbookReader.RowIsEmpty(sheet, row))
            {
                break;
            }

            if (contractId.Length == 0)
            {
                contractId = Text(sheet, row, headers, "Contract ID");
            }

            var said = Text(sheet, row, headers, "Service Agreement ID");
            var serial = Text(sheet, row, headers, "Serial Number");
            var partNo = Text(sheet, row, headers, "Product Number");
            var cost = DecimalCleaner.Parse(Text(sheet, row, headers, "Line Item Total Net Price"), defaultZero: true);

            // AND skip rule. A zero price counts as "no value", which is exactly what drops the
            // per-SAID summary rows while keeping a priced service charge that carries no
            // Product Number or Serial Number.
            if (serial.Length == 0 && partNo.Length == 0 && cost == 0m)
            {
                continue;
            }

            // First occurrence of each SAID contributes its quoted total.
            if (said.Length > 0 && !saidTotals.ContainsKey(said))
            {
                saidTotals[said] = ReadOptionalDecimal(sheet, row, headers, "Total Net Price");
            }

            var qty = DecimalCleaner.ParseOptionalInt(Text(sheet, row, headers, "Quantity")) ?? 0;

            items.Add(new LineItem
            {
                LineSequence = (++counter).ToString(CultureInfo.InvariantCulture),
                Vpn = Text(sheet, row, headers, "Service Product Number"),
                Description = BuildDescription(sheet, row, headers),
                Cost = cost,
                Qty = qty <= 0 ? 1 : qty,
                SerialNumber = serial.Length == 0 ? null : serial,
                StartDate = ReadOptionalDate(sheet, row, headers, "Coverage Start"),
                EndDate = ReadOptionalDate(sheet, row, headers, "Coverage End"),
                SolutionId = said.Length == 0 ? null : said,
                Raw = WorkbookReader.BuildRawDict(sheet, row, headers),
            });
        }

        var totals = saidTotals.Values.Where(v => v is not null).Select(v => v!.Value).ToList();
        decimal? quotedTotal = totals.Count > 0 ? totals.Sum() : null;

        var validation = quotedTotal is null
            ? new ValidationResult
            {
                ComputedTotal = decimal.Round(items.Sum(i => i.Cost * i.Qty), 2, MidpointRounding.AwayFromZero),
                QuotedTotal = null,
                Matches = true,
                Difference = 0m
            }
            : ParseValidation.Validate(items, quotedTotal);

        var bid = BidMetadataCleaner.CleanRevisionless(contractId);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = contractId.Length > 0 ? contractId : Path.GetFileNameWithoutExtension(path),
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug,
            },
            LineItems = items,
            Validation = validation,
        };
    }

    private static string BuildDescription(IXLWorksheet sheet, int row, HeaderMap headers)
    {
        var text = Text(sheet, row, headers, "Service Product Description");

        var partNumber = Text(sheet, row, headers, "Product Number");
        var productDesc = Text(sheet, row, headers, "Product Description");
        var hardware = string.Join(' ',
            new[] { partNumber, productDesc }.Where(part => part.Length > 0));
        if (hardware.Length > 0)
        {
            text = $"{text} - {hardware}";
        }

        var warrantyEnd = ReadOptionalDate(sheet, row, headers, "Warranty End Date");
        if (warrantyEnd is not null)
        {
            text = $"{text} (Warranty End Date: {warrantyEnd.Value:dd/MM/yyyy})";
        }

        return text;
    }

    private static string Text(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static DateOnly? ReadOptionalDate(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
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

        return ParseDayFirst(WorkbookReader.CellText(cell));
    }

    /// <summary>
    /// Parses a text date cell day-first. This format is an Australian HP export, so an
    /// ambiguous value such as "05/07/2026" is 5 July, never 7 May. US month-first patterns are
    /// deliberately absent: including them would let an ambiguous date bind silently to the wrong
    /// month, which is worse than failing to parse. Returns null for anything unrecognised —
    /// coverage dates are best-effort and must not cost the user a workbook.
    /// </summary>
    internal static DateOnly? ParseDayFirst(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ReadOptionalDecimal(IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        if (!headerMap.Columns.TryGetValue(label, out var column))
        {
            return null;
        }

        var text = WorkbookReader.CellText(sheet.Cell(row, column));
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return DecimalCleaner.Parse(text);
        }
        catch
        {
            return null;
        }
    }
}

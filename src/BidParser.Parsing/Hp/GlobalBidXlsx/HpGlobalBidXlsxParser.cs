using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Hp.GlobalBidXlsx;

/// <summary>
/// Parses HP "Global Bid" quote spreadsheets (XLSX). Anchor-based on the "Product number" header in the
/// "Product numbers" sheet. Requires AUD denomination (the cost column is "Converted net price [AUD]"),
/// throwing a "currency" error otherwise. Emits ANZ-GENERIC output. See docs/hp_global_bid_xlsx.md.
/// </summary>
public sealed class HpGlobalBidXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpGlobalBidXlsx;
    public string DisplayName => "Global Bid (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    /// <summary>
    /// Wrong-file-type hint (not routing): 0.9 when a "Product number" header exists on the
    /// "Product numbers" sheet — unique to the HP Global Bid export.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name == "Product numbers")
                ?? workbook.Worksheets.First();
            return WorkbookReader.FindCell(sheet, "Product number") is not null ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);

        // ── Locate header row via "Product number" anchor ──────────────────────
        var productNumberSheet = workbook.Worksheets
            .FirstOrDefault(ws => ws.Name == "Product numbers")
            ?? workbook.Worksheets.First();

        var headerCell = WorkbookReader.FindCell(productNumberSheet, "Product number")
            ?? throw new ParseError("detect", "Could not find Product number header.", "Could not find 'Product number' header in the Product numbers sheet.");

        var headerMap = WorkbookReader.HeaderMap(productNumberSheet, headerCell.Address.RowNumber);

        // AUD validation: the cost column header normalises to "Converted net price [AUD]"
        // (ClosedXML + TextCleaner collapse the embedded newline to a space).
        if (!headerMap.Columns.ContainsKey("Converted net price [AUD]"))
        {
            throw new ParseError(
                "currency",
                "Quote is not denominated in AUD.",
                "This file does not contain a 'Converted net price [AUD]' column. Only AUD-denominated Global Bid quotes are supported.");
        }

        WorkbookReader.RequireLabels(
            headerMap,
            "Product number",
            "Description",
            "Converted net price [AUD]",
            "Remaining qty");

        var items = new List<LineItem>();
        var lastRow = productNumberSheet.LastRowUsed()?.RowNumber() ?? headerMap.RowNumber;

        for (var row = headerMap.RowNumber + 1; row <= lastRow; row++)
        {
            if (WorkbookReader.RowIsEmpty(productNumberSheet, row))
            {
                break;
            }

            var vpn = CellText(productNumberSheet, row, headerMap, "Product number");
            if (vpn.Length == 0)
            {
                continue;
            }

            var costText = CellText(productNumberSheet, row, headerMap, "Converted net price [AUD]");
            var cost = DecimalCleaner.Parse(costText, defaultZero: true);

            var remainingQtyText = CellText(productNumberSheet, row, headerMap, "Remaining qty");
            var remainingQty = DecimalCleaner.ParseOptionalInt(remainingQtyText) ?? 0;

            var comments = $"{remainingQty} Remaining";

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = CellText(productNumberSheet, row, headerMap, "Description"),
                Cost = cost,
                // Qty is always 1; "Aggregated item quantity" is intentionally not used.
                Qty = 1,
                Comments = comments,
                Raw = WorkbookReader.BuildRawDict(productNumberSheet, row, headerMap)
            });
        }

        // ── Extract deal number from "About this deal" sheet ───────────────────
        var (quoteNumber, bidNumber, bidRevision) = ExtractDealMetadata(workbook, path);

        var computed = items.Sum(item => item.Cost * item.Qty);
        computed = decimal.Round(computed, 2, MidpointRounding.AwayFromZero);
        var validation = new ValidationResult
        {
            ComputedTotal = computed,
            QuotedTotal = null,
            Matches = true,
            Difference = 0m
        };

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = null,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

    private static (string QuoteNumber, string? BidNumber, string? BidRevision) ExtractDealMetadata(ClosedXML.Excel.IXLWorkbook workbook, string path)
    {
        var aboutSheet = workbook.Worksheets
            .FirstOrDefault(ws => ws.Name == "About this deal");

        var dealNumber = aboutSheet is null ? string.Empty : WorkbookReader.ValueRightOf(aboutSheet, "Deal Number");
        var dealVersion = aboutSheet is null ? string.Empty : WorkbookReader.ValueRightOf(aboutSheet, "Deal Version");

        // QuoteNumber keeps its filename fallback. The bid fields have no fallback but are also
        // never fatal: a missing sheet or value leaves them null and the parse still succeeds.
        var quoteNumber = dealNumber.Length > 0 ? dealNumber : Path.GetFileNameWithoutExtension(path);
        var bid = BidMetadataCleaner.Clean(dealNumber, dealVersion);
        return (quoteNumber, bid.BidNumber, bid.BidRevision);
    }

    private static string CellText(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }
}

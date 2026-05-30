using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Hp.GlobalBidXlsx;

public sealed class HpGlobalBidXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpGlobalBidXlsx;
    public string DisplayName => "Global Bid (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

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
            "Remaining qty",
            "Aggregated item quantity");

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

            var qty = DecimalCleaner.ParseOptionalInt(
                CellText(productNumberSheet, row, headerMap, "Aggregated item quantity")) ?? 0;

            var remainingQtyText = CellText(productNumberSheet, row, headerMap, "Remaining qty");
            var remainingQty = DecimalCleaner.ParseOptionalInt(remainingQtyText) ?? 0;

            var termText = headerMap.Columns.ContainsKey("Full term (Months)")
                ? CellText(productNumberSheet, row, headerMap, "Full term (Months)")
                : string.Empty;
            var term = DecimalCleaner.ParseOptionalInt(termText);

            var comments = term is > 0
                ? $"{term} Months | {remainingQty} Remaining"
                : $"{remainingQty} Remaining";

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = CellText(productNumberSheet, row, headerMap, "Description"),
                Cost = cost,
                Qty = qty,
                Comments = comments,
                Raw = WorkbookReader.BuildRawDict(productNumberSheet, row, headerMap)
            });
        }

        // ── Extract deal number from "About this deal" sheet ───────────────────
        var quoteNumber = ExtractDealNumber(workbook, path);

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

    private static string ExtractDealNumber(ClosedXML.Excel.IXLWorkbook workbook, string path)
    {
        var aboutSheet = workbook.Worksheets
            .FirstOrDefault(ws => ws.Name == "About this deal");

        if (aboutSheet is not null)
        {
            var dealCell = WorkbookReader.FindCell(aboutSheet, "Deal Number");
            if (dealCell is not null)
            {
                var value = WorkbookReader.CellText(
                    aboutSheet.Cell(dealCell.Address.RowNumber, dealCell.Address.ColumnNumber + 1));
                if (value.Length > 0)
                {
                    return value;
                }
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static string CellText(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }
}

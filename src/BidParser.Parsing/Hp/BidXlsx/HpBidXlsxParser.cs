using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Hp.BidXlsx;

public sealed class HpBidXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpBidXlsx;
    public string DisplayName => "HP Bid (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var sheet = workbook.Worksheets.First();

        var headerCell = WorkbookReader.FindCell(sheet, "Line Type")
            ?? throw new ParseError("detect", "Could not find the Line Type table header.", "Could not find Line Type header");

        var headerMap = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber);
        WorkbookReader.RequireLabels(
            headerMap,
            "Line Type",
            "Product Number/ID",
            "Option Code",
            "Product Description",
            "Price",
            "Max Deal Qty",
            "Bundle Detail Qty",
            "Min Order Qty");

        var items = new List<LineItem>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerMap.RowNumber;

        var lineCounter = 0;
        var bundleChildCounter = 0;

        for (var row = headerMap.RowNumber + 1; row <= lastRow; row++)
        {
            if (WorkbookReader.RowIsEmpty(sheet, row))
            {
                break;
            }

            var lineType = Text(sheet, row, headerMap, "Line Type");
            if (lineType.Length == 0)
            {
                continue;
            }

            string lineSequence;
            int qty;
            int rawMinQty;

            switch (lineType)
            {
                case "Part Number":
                case "Bundle":
                    lineCounter++;
                    bundleChildCounter = 0;
                    lineSequence = lineCounter.ToString();
                    qty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Max Deal Qty"));
                    rawMinQty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Min Order Qty"));
                    break;

                case "Bundle Detail":
                    bundleChildCounter++;
                    lineSequence = $"{lineCounter}.{bundleChildCounter:D2}";
                    qty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Bundle Detail Qty"));
                    rawMinQty = DecimalCleaner.ParseInt(Text(sheet, row, headerMap, "Bundle Detail Qty"));
                    break;

                default:
                    // Unknown line type — skip
                    continue;
            }

            var minQty = rawMinQty == 0 ? 1 : rawMinQty;

            var code = Text(sheet, row, headerMap, "Product Number/ID");
            var opt = Text(sheet, row, headerMap, "Option Code");
            var vpn = opt.Length > 0 ? $"{code}#{opt}" : code;

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = Text(sheet, row, headerMap, "Product Description"),
                Cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Price"), defaultZero: true),
                Msrp = null,
                Qty = qty,
                MinQty = minQty,
                LineSequence = lineSequence,
                Raw = RawDict(sheet, row, headerMap)
            });
        }

        // Locate Deal Number from metadata block above the header
        var dealNumberCell = WorkbookReader.FindCell(sheet, "Deal Number");
        var quoteNumber = dealNumberCell is not null
            ? WorkbookReader.CellText(sheet.Cell(dealNumberCell.Address.RowNumber, dealNumberCell.Address.ColumnNumber + 1))
            : string.Empty;
        if (quoteNumber.Length == 0)
        {
            quoteNumber = Path.GetFileNameWithoutExtension(path);
        }

        // HP files carry no quoted total. Build the ValidationResult directly so the
        // absence of a total is treated as "neutral / not a mismatch" rather than a
        // warning — avoiding a blocking modal on every HP parse.
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

    private static string Text(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        return WorkbookReader.CellText(sheet.Cell(row, headerMap.Require(label)));
    }

    private static IReadOnlyDictionary<string, string> RawDict(
        ClosedXML.Excel.IXLWorksheet sheet,
        int row,
        HeaderMap headerMap)
    {
        return headerMap.Columns
            .Select(pair => (pair.Key, Value: WorkbookReader.CellText(sheet.Cell(row, pair.Value))))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}

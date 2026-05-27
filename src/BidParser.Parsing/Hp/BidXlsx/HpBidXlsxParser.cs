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
        var bundleParentSeq = 0;
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
            decimal cost;

            switch (lineType)
            {
                case "Part Number":
                case "Bundle":
                    lineCounter++;
                    lineSequence = lineCounter.ToString();
                    qty = Int(sheet, row, headerMap, "Max Deal Qty");
                    rawMinQty = Int(sheet, row, headerMap, "Min Order Qty");
                    cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Price"), defaultZero: true);
                    if (lineType == "Bundle")
                    {
                        // A Bundle opens a child group: subsequent Bundle Detail rows
                        // sub-sequence under it (4.01, 4.02, …). A plain Part Number is
                        // never a parent, so it leaves the child counter untouched.
                        bundleParentSeq = lineCounter;
                        bundleChildCounter = 0;
                    }
                    break;

                case "Bundle Detail":
                    bundleChildCounter++;
                    lineSequence = $"{bundleParentSeq}.{bundleChildCounter:D2}";
                    qty = Int(sheet, row, headerMap, "Bundle Detail Qty");
                    rawMinQty = qty;
                    // A Bundle Detail is a component of its Bundle; the Bundle line carries
                    // the total price, so the component's own Price is dropped to avoid
                    // double-counting. The writer emits the 0.000001 sentinel (the
                    // downstream import rejects a literal 0). The source Price is still
                    // captured in Raw["Price"].
                    cost = 0m;
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
                Cost = cost,
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

    private static int Int(ClosedXML.Excel.IXLWorksheet sheet, int row, HeaderMap headerMap, string label)
    {
        // A blank qty cell defaults to 0 rather than throwing. For min_qty the 0→1 rule
        // then promotes a blank Min Order Qty to 1, matching the "0 means 1" intent.
        return DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, label)) ?? 0;
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

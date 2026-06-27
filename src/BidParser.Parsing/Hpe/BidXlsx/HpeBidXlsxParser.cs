using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Hpe.BidXlsx;

public sealed class HpeBidXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpeBidXlsx;
    public string DisplayName => "HPE Bid (XLSX)";
    public string Vendor => Vendors.Hpe;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var sheet = workbook.Worksheets.First();

        // The HPE deal export uses "LineType" (one word) as its table anchor — distinct
        // from the HP Bid export's "Line Type".
        var headerCell = WorkbookReader.FindCell(sheet, "LineType")
            ?? throw new ParseError("detect", "Could not find the LineType table header.", "Could not find LineType header");

        var headerMap = WorkbookReader.HeaderMap(sheet, headerCell.Address.RowNumber);
        WorkbookReader.RequireLabels(
            headerMap,
            "LineType",
            "ProductNumber",
            "BundleID",
            "ComponentID",
            "Quantity",
            "ProductDescription",
            "ListPrcEst",
            "Offering",
            "MinOrderQty",
            "MaxDealQty");

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

            var lineType = Text(sheet, row, headerMap, "LineType");
            if (lineType.Length == 0)
            {
                continue;
            }

            string lineSequence;
            string vpn;
            decimal msrp;
            decimal cost;
            string? comments = null;

            switch (lineType)
            {
                case "Part Number":
                case "Bundle":
                    lineCounter++;
                    lineSequence = lineCounter.ToString();
                    // A Part Number takes its VPN from ProductNumber; a Bundle (the header
                    // line that carries the bundle's pricing) takes it from BundleID. The
                    // OptionCode column is intentionally ignored for the VPN (it stays in Raw).
                    vpn = lineType == "Bundle"
                        ? Text(sheet, row, headerMap, "BundleID")
                        : Text(sheet, row, headerMap, "ProductNumber");
                    msrp = DecimalCleaner.Parse(Text(sheet, row, headerMap, "ListPrcEst"), defaultZero: true);
                    cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Offering"), defaultZero: true);
                    var maxDealQty = DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, "MaxDealQty"));
                    if (maxDealQty is not null)
                    {
                        comments = $"Max Qty: {maxDealQty}";
                    }
                    if (lineType == "Bundle")
                    {
                        // A Bundle opens a child group: subsequent BundleDetails rows
                        // sub-sequence under it (1.01, 1.02, …).
                        bundleParentSeq = lineCounter;
                        bundleChildCounter = 0;
                    }
                    break;

                case "BundleDetails":
                    bundleChildCounter++;
                    lineSequence = $"{bundleParentSeq}.{bundleChildCounter:D2}";
                    vpn = Text(sheet, row, headerMap, "ComponentID");
                    // A BundleDetails line is a component of its Bundle; the Bundle line
                    // carries the total price, so the component's own msrp/cost are dropped to
                    // avoid double-counting. The writer emits the 0.0001 sentinel (the
                    // downstream import rejects a literal 0). The source values are still
                    // captured in Raw. BundleDetails rows carry no comment.
                    msrp = 0m;
                    cost = 0m;
                    break;

                default:
                    // Unknown line type — skip
                    continue;
            }

            // qty derives from the Quantity column for every line type. Min Order Qty is
            // surfaced separately in the output's Min Order Qty column (0 → 1).
            var qty = Int(sheet, row, headerMap, "Quantity");
            qty = qty == 0 ? 1 : qty;
            var rawMinQty = Int(sheet, row, headerMap, "MinOrderQty");
            var minQty = rawMinQty == 0 ? 1 : rawMinQty;

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = Text(sheet, row, headerMap, "ProductDescription"),
                Cost = cost,
                Msrp = msrp,
                Qty = qty,
                MinQty = minQty,
                LineSequence = lineSequence,
                Comments = comments,
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
            });
        }

        // Locate Deal Number from the metadata block above the header (key in this column,
        // value in the next).
        var dealNumberCell = WorkbookReader.FindCell(sheet, "DealNumber");
        var quoteNumber = dealNumberCell is not null
            ? WorkbookReader.CellText(sheet.Cell(dealNumberCell.Address.RowNumber, dealNumberCell.Address.ColumnNumber + 1))
            : string.Empty;
        if (quoteNumber.Length == 0)
        {
            quoteNumber = Path.GetFileNameWithoutExtension(path);
        }

        // HPE files carry no quoted total. Build the ValidationResult directly so the
        // absence of a total is treated as "neutral / not a mismatch" rather than a
        // warning — avoiding a blocking modal on every HPE parse.
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
        // A blank qty cell defaults to 0 rather than throwing; callers promote 0 → 1.
        return DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, label)) ?? 0;
    }
}

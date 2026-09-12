using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;

namespace BidParser.Parsing.Hp.BidXlsx;

/// <summary>
/// Parses HP "Bid" quote spreadsheets (XLSX) into <see cref="LineItem"/>s.
///
/// Anchor-based: the line-item table is found by the unique "Line Type" header
/// (never a fixed row) and every column is resolved by label via <see cref="HeaderMap"/>.
/// Three line types are handled — "Part Number" and "Bundle" are top-level lines
/// (sequenced 1, 2, 3…), while "Bundle Detail" rows are components that sub-sequence
/// under the most recent Bundle (4.01, 4.02…) and carry no price of their own.
///
/// Emits the ANZ-GENERIC output (No Calculation / Uplift). HP quotes carry no quoted
/// total, so validation is built as a neutral match. See docs/hp_bid_xlsx.md.
/// </summary>
public sealed class HpBidXlsxParser : IParser
{
    // IParser identity + capabilities: slug, UI label, vendor, accepted MIME, and the
    // CRM templates this parser can target (the first is the default).
    public string Slug => ParserSlugs.HpBidXlsx;
    public string DisplayName => "HP Bid (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    /// <summary>
    /// Wrong-file-type hint (not routing): scores 0.9 when the "Line Type" header —
    /// the signature unique to the HP Bid export — is present, else 0.0. Consumed only
    /// by the wrong-file-type flow to name the likely-correct type.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.First();
            return WorkbookReader.FindCell(sheet, "Line Type") is not null ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Locates the table by the "Line Type" anchor, requires the expected columns, then
    /// walks each row (stopping at the first empty row) building one <see cref="LineItem"/>
    /// per recognised line. Unknown line types are skipped.
    /// </summary>
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
            string? comments = null;

            switch (lineType)
            {
                case "Part Number":
                case "Bundle":
                    lineCounter++;
                    lineSequence = lineCounter.ToString();
                    // Qty derives from Min Order Qty (0 → 1); Max Deal Qty is not
                    // the quantity — it is surfaced in the Comments column instead.
                    rawMinQty = Int(sheet, row, headerMap, "Min Order Qty");
                    qty = rawMinQty == 0 ? 1 : rawMinQty;
                    cost = DecimalCleaner.Parse(Text(sheet, row, headerMap, "Price"), defaultZero: true);
                    var maxDealQty = DecimalCleaner.ParseOptionalInt(Text(sheet, row, headerMap, "Max Deal Qty"));
                    if (maxDealQty is not null)
                    {
                        comments = $"Max Qty: {maxDealQty}";
                    }
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
                    // double-counting. The writer emits the 0.0001 sentinel (the
                    // downstream import rejects a literal 0). The source Price is still
                    // captured in Raw["Price"]. Bundle Detail rows carry no Max Deal Qty,
                    // so Comments stays blank.
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

            // HP repeats a bundle's own Product Number/ID at the front of its Product
            // Description ("55623728-HP EliteBook 8 G2a 14 …"). The VPN column already
            // carries that id, so the duplicated prefix is dropped. Only Bundle rows carry
            // it — Part Number and Bundle Detail descriptions are verbatim.
            var description = Text(sheet, row, headerMap, "Product Description");
            if (lineType == "Bundle")
            {
                description = DescriptionCleaner.StripLeadingCode(description, code);
            }

            items.Add(new LineItem
            {
                Vpn = vpn,
                Description = description,
                Cost = cost,
                Msrp = null,
                Qty = qty,
                MinQty = minQty,
                LineSequence = lineSequence,
                Comments = comments,
                Raw = WorkbookReader.BuildRawDict(sheet, row, headerMap)
            });
        }

        // Locate Deal Number / Deal Version from the metadata block above the header.
        var dealNumber = WorkbookReader.ValueRightOf(sheet, "Deal Number");
        var dealVersion = WorkbookReader.ValueRightOf(sheet, "Deal Version");

        // QuoteNumber keeps its filename fallback. The bid fields have no fallback but are also
        // never fatal: a missing label leaves them null and the parse still succeeds.
        var quoteNumber = dealNumber.Length > 0 ? dealNumber : Path.GetFileNameWithoutExtension(path);
        var bid = BidMetadataCleaner.Clean(dealNumber, dealVersion);

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
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
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

}

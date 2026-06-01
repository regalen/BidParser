using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using HtmlAgilityPack;

namespace BidParser.Parsing.Zebra.PriceConcession;

/// <summary>
/// Parses Zebra PartnerConnect "Price Concession" XLS exports.
///
/// Despite the .xls extension these files are styled HTML tables, not OLE binary
/// workbooks.  They cannot be read by ExcelDataReader or ClosedXML.  The file is
/// parsed with HtmlAgilityPack which handles the HTML table structure natively.
///
/// The HTML contains two tables:
///   • Table 0 — details block (Account, Reseller, Currency, dates, etc.)
///   • Table 1 — items table with the same 10-column layout as the PDF version.
///
/// NOTE: <see cref="ParseService.ValidateMagicBytesAsync"/> was deliberately relaxed
/// to accept both OLE-compound-doc and HTML signatures for application/vnd.ms-excel,
/// because Zebra's portal exports HTML under a .xls filename.  Existing real-OLE .xls
/// files (Lenovo BRDA DCG) continue to pass the OLE check unchanged.
/// </summary>
public sealed class ZebraPriceConcessionXlsParser : IParser
{
    public string Slug => ParserSlugs.ZebraPriceConcessionXls;
    public string DisplayName => "Price Concession (XLS)";
    public string Vendor => Vendors.Zebra;
    public string AcceptedMime => "application/vnd.ms-excel";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public ParseResult Parse(string path)
    {
        var html = File.ReadAllText(path);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables is null || tables.Count < 2)
        {
            throw new ParseError(
                "detect",
                "Could not find the expected two HTML tables in the XLS export.",
                "Missing Price Concession Items table.");
        }

        // ── Details block (first table) ─────────────────────────────────────────
        var detailsTable = tables[0];
        var currency = ExtractCurrencyFromDetails(detailsTable);

        // ── Items table (second table) ─────────────────────────────────────────
        var itemsTable = tables[1];
        var (headerMap, dataRows) = ParseItemsTable(itemsTable);

        // ── Build normalised ItemRow list ──────────────────────────────────────
        var itemRows = BuildItemRows(dataRows, headerMap);

        var quoteNumber = ZebraPriceConcessionExtractor.QuoteNumberFromFilename(path);

        return ZebraPriceConcessionExtractor.Build(
            itemRows,
            currency,
            quoteNumber,
            Path.GetFileName(path),
            Slug);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Details table parsing

    private static string ExtractCurrencyFromDetails(HtmlNode detailsTable)
    {
        foreach (var row in detailsTable.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = CellTexts(row);
            if (cells.Count >= 3
                && string.Equals(cells[0], "Currency", StringComparison.OrdinalIgnoreCase))
            {
                var value = cells[2];
                if (value is "AUD" or "USD" or "EUR" or "GBP" or "NZD") return value;
            }
        }
        return "AUD";
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Items table parsing

    private const string ColPartNo          = "Part No.";
    private const string ColDescription     = "Description";
    private const string ColMinQty          = "Min. Qty";
    private const string ColMaxQty          = "Max. Qty";
    private const string ColListPrice       = "List Price";
    private const string ColUnitSpecialPrice = "Unit Special Price";
    private const string ColCancelled       = "Cancelled";

    private sealed record HeaderMap(
        int PartNo, int Description, int MinQty, int MaxQty,
        int ListPrice, int UnitSpecialPrice, int Cancelled);

    private static (HeaderMap Map, List<List<string>> DataRows) ParseItemsTable(HtmlNode table)
    {
        var allRows = table.SelectNodes(".//tr")?.ToList() ?? [];
        if (allRows.Count == 0)
        {
            throw new ParseError("detect", "Items table has no rows.", "Empty items table.");
        }

        // Find the header row — it contains "Part No." and "Unit Special Price".
        var headerRowIndex = -1;
        List<string> headerCells = [];
        for (var i = 0; i < allRows.Count; i++)
        {
            var cells = CellTexts(allRows[i]);
            var hasPartNo = cells.Any(c => c.Contains("Part No.", StringComparison.OrdinalIgnoreCase));
            var hasUnitPrice = cells.Any(c => c.Contains("Unit Special Price", StringComparison.OrdinalIgnoreCase));
            if (hasPartNo && hasUnitPrice)
            {
                headerRowIndex = i;
                headerCells = cells;
                break;
            }
        }

        if (headerRowIndex < 0)
        {
            throw new ParseError(
                "detect",
                "Could not find the 'Part No.' / 'Unit Special Price' header row in the items table.",
                "Missing items table header.");
        }

        var map = MapHeaderColumns(headerCells);

        var dataRows = allRows
            .Skip(headerRowIndex + 1)
            .Select(r => CellTexts(r))
            .Where(cells => cells.Count > 0 && cells.Any(c => c.Length > 0))
            .ToList();

        return (map, dataRows);
    }

    private static HeaderMap MapHeaderColumns(List<string> headerCells)
    {
        int? partNo = null, desc = null, minQty = null, maxQty = null,
             listPrice = null, unitPrice = null, cancelled = null;

        for (var i = 0; i < headerCells.Count; i++)
        {
            var cell = headerCells[i];
            if (cell.Contains("Part No.", StringComparison.OrdinalIgnoreCase)) partNo = i;
            else if (cell.Contains("Description", StringComparison.OrdinalIgnoreCase)) desc = i;
            else if (cell.Contains("Min. Qty", StringComparison.OrdinalIgnoreCase)) minQty = i;
            else if (cell.Contains("Max. Qty", StringComparison.OrdinalIgnoreCase)) maxQty = i;
            else if (cell.Contains("List Price", StringComparison.OrdinalIgnoreCase)) listPrice = i;
            else if (cell.Contains("Unit Special Price", StringComparison.OrdinalIgnoreCase)) unitPrice = i;
            else if (cell.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)) cancelled = i;
        }

        if (partNo is null || desc is null || maxQty is null || unitPrice is null || cancelled is null)
        {
            throw new ParseError(
                "detect",
                "Items table header is missing required columns: Part No., Description, Max. Qty, Unit Special Price, or Cancelled.",
                "Incomplete items table header.");
        }

        return new HeaderMap(
            partNo.Value, desc.Value,
            minQty ?? -1,
            maxQty.Value,
            listPrice ?? -1,
            unitPrice.Value,
            cancelled.Value);
    }

    private static IReadOnlyList<ZebraPriceConcessionExtractor.ItemRow> BuildItemRows(
        List<List<string>> dataRows, HeaderMap map)
    {
        var result = new List<ZebraPriceConcessionExtractor.ItemRow>();
        foreach (var cells in dataRows)
        {
            var partNo = Get(cells, map.PartNo);
            if (partNo.Length == 0) continue; // skip empty/blank rows

            result.Add(new ZebraPriceConcessionExtractor.ItemRow(
                PartNo: partNo,
                Description: Get(cells, map.Description),
                MinQty: map.MinQty >= 0 ? Get(cells, map.MinQty) : string.Empty,
                MaxQty: Get(cells, map.MaxQty),
                ListPrice: map.ListPrice >= 0 ? Get(cells, map.ListPrice) : string.Empty,
                UnitSpecialPrice: Get(cells, map.UnitSpecialPrice),
                Cancelled: Get(cells, map.Cancelled)));
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HTML helpers

    private static List<string> CellTexts(HtmlNode row)
    {
        var cells = new List<string>();
        foreach (var cell in row.SelectNodes(".//td|.//th") ?? Enumerable.Empty<HtmlNode>())
        {
            cells.Add(TextCleaner.Clean(HtmlEntity.DeEntitize(cell.InnerText)));
        }
        return cells;
    }

    private static string Get(List<string> cells, int index)
        => index >= 0 && index < cells.Count ? cells[index] : string.Empty;
}

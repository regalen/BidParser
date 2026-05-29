using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Nutanix.RenewalPdf;

public sealed class NutanixRenewalPdfParser : IParser
{
    public string Slug => ParserSlugs.NutanixRenewalPdf;
    public string DisplayName => "Renewal (PDF)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    public ParseResult Parse(string path)
    {
        var words = PdfWordCollector.CollectWords(path);
        var fused = PdfTableHelpers.FuseCurrencyTokens(words);
        var header = FindHeader(fused);
        var columns = BuildColumns(fused, header);
        var rows = PdfTableHelpers.RowsBetween(fused, header.Top + 6, header.PageIndex, columns);
        var quotedTotal = PdfTableHelpers.TotalFromWords(fused);

        var items = new List<LineItem>();
        CurrentItem? current = null;

        foreach (var row in rows)
        {
            var cells = row.Cells.ToDictionary(pair => pair.Key, pair => TextCleaner.Clean(pair.Value));
            var number = PdfTableHelpers.Cell(cells,"No");
            var productCode = PdfTableHelpers.Cell(cells,"Product Code");

            if (int.TryParse(number, out var parsedNumber) && parsedNumber > 0 && productCode.Length > 0)
            {
                if (current is not null)
                {
                    items.Add(BuildItem(current));
                }

                current = new CurrentItem(
                    Parts(PdfTableHelpers.Cell(cells,"Platform")),
                    Parts(PdfTableHelpers.Cell(cells,"Product Code")),
                    Parts(PdfTableHelpers.Cell(cells,"Serial Number")),
                    cells);
            }
            else if (current is not null && cells.Values.Any(value => value.Length > 0))
            {
                AddIfPresent(current.PlatformParts, PdfTableHelpers.Cell(cells,"Platform"));
                AddIfPresent(current.ProductCodeParts, PdfTableHelpers.Cell(cells,"Product Code"));
                AddIfPresent(current.SerialParts, PdfTableHelpers.Cell(cells,"Serial Number"));

                foreach (var (key, value) in cells)
                {
                    if (value.Length > 0
                        && (!current.Cells.TryGetValue(key, out var existingValue) || existingValue.Length == 0))
                    {
                        current.Cells[key] = value;
                    }
                }
            }
        }

        if (current is not null)
        {
            items.Add(BuildItem(current));
        }

        var validation = ParseValidation.Validate(items, quotedTotal);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = Path.GetFileNameWithoutExtension(path),
                Supplier = Vendor,
                Currency = "USD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

    private static PdfWord FindHeader(IReadOnlyList<PdfWord> words)
    {
        foreach (var word in words)
        {
            if (word.Text != "No")
            {
                continue;
            }

            var headerWords = HeaderWords(words, word);
            if (headerWords.Any(candidate => candidate.Text == "Serial"))
            {
                return word;
            }
        }

        throw new ParseError("detect", "Could not find the Renewal table header.", "Could not find Renewal header");
    }

    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(IReadOnlyList<PdfWord> words, PdfWord header)
    {
        var headerWords = HeaderWords(words, header);
        var discountWord = headerWords.FirstOrDefault(word => word.Text == "Discount");
        var totalWord = headerWords
            .Where(word => word.Text == "Total" && word.X0 > 500)
            .OrderBy(word => word.X0)
            .FirstOrDefault();
        var platformWord = headerWords.FirstOrDefault(word => word.Text == "Platform");
        var qtyX0 = RequiredX0(headerWords, "Qty");

        // The "Net Unit Price" header cluster (Net / Unit / Price) can render in different
        // left-to-right orders across PDF variants. Take the minimum X0 of the cluster and
        // subtract 8 so the column boundary sits left of wherever the data actually lands —
        // the same pattern used for "Term Adjusted List Unit Price" above.
        var netUnitPriceX0 = headerWords
            .Where(word => word.Text is "Net" or "Unit" or "Price"
                && word.X0 > (discountWord?.X0 ?? 395.0)
                && word.X0 < qtyX0)
            .Select(word => word.X0)
            .DefaultIfEmpty(456.0)
            .Min() - 8;

        var headers = new List<(string Name, double X0)>
        {
            ("No", header.X0),
            ("Product Code", RequiredX0(headerWords, "Product")),
            ("Serial Number", RequiredX0(headerWords, "Serial")),
            ("Start Date", RequiredX0(headerWords, "Start")),
            ("End Date", RequiredX0(headerWords, "End")),
            ("Term Adjusted List Unit Price", headerWords
                .Where(word => word.Text is "Adjusted" or "List")
                .Select(word => word.X0)
                .DefaultIfEmpty(348.0)
                .Min() - 8),
            ("Total Discount", discountWord?.X0 ?? 395.0),
            ("Net Unit Price", netUnitPriceX0),
            ("Qty", qtyX0),
            ("Total Net Price", totalWord?.X0 ?? 522.0)
        };

        // Platform column is optional — only present in the variant that includes it.
        // When present it sits between No and Product Code; adding it narrows the No
        // range so that the row number and the platform value land in separate buckets.
        if (platformWord is not null)
        {
            headers.Add(("Platform", platformWord.X0));
        }

        return PdfTableHelpers.ColumnRanges(headers, header.PageWidth);
    }

    private static IReadOnlyList<PdfWord> HeaderWords(IReadOnlyList<PdfWord> words, PdfWord header)
    {
        return words
            .Where(word => word.PageIndex == header.PageIndex && word.Top >= header.Top - 20 && word.Top <= header.Top + 22)
            .ToList();
    }

    private static double RequiredX0(IReadOnlyList<PdfWord> words, string text)
    {
        return words.First(word => word.Text == text).X0;
    }

    private static LineItem BuildItem(CurrentItem item)
    {
        var cells = item.Cells;
        var platform = TextCleaner.JoinUnspaced(item.PlatformParts);
        return new LineItem
        {
            Vpn = TextCleaner.JoinUnspaced(item.ProductCodeParts),
            Description = platform.Length > 0 ? $"Platform: {platform}" : null,
            SerialNumber = TextCleaner.JoinUnspaced(item.SerialParts),
            StartDate = DateCleaner.ParseMmDdYyyy(PdfTableHelpers.Cell(cells,"Start Date")),
            EndDate = DateCleaner.ParseMmDdYyyy(PdfTableHelpers.Cell(cells,"End Date")),
            Msrp = DecimalCleaner.Parse(PdfTableHelpers.Cell(cells,"Term Adjusted List Unit Price")),
            Cost = DecimalCleaner.Parse(PdfTableHelpers.Cell(cells,"Net Unit Price")),
            Qty = DecimalCleaner.ParseInt(PdfTableHelpers.Cell(cells,"Qty")),
            Raw = PdfTableHelpers.RawDict(cells)
        };
    }

    private static List<string> Parts(string value) =>
        value.Length > 0 ? [value] : [];

    private static void AddIfPresent(List<string> parts, string value)
    {
        if (value.Length > 0)
        {
            parts.Add(value);
        }
    }

    private sealed record CurrentItem(
        List<string> PlatformParts,
        List<string> ProductCodeParts,
        List<string> SerialParts,
        Dictionary<string, string> Cells);

}

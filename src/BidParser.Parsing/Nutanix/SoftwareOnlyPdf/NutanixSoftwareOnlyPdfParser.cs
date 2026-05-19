using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Nutanix.SoftwareOnlyPdf;

public sealed partial class NutanixSoftwareOnlyPdfParser : IParser
{
    public string Slug => ParserSlugs.NutanixSoftwareOnlyPdf;
    public string DisplayName => "Software Only (PDF)";
    public string Vendor => Vendors.Nutanix;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.ForeignUplift;

    public ParseResult Parse(string path)
    {
        var words = PdfWordCollector.CollectWords(path);
        var header = FindHeader(words);
        var columns = BuildColumns(words, header);
        var rows = PdfTableHelpers.RowsBetween(words, header.Top + 6, header.PageIndex, columns);
        var quotedTotal = PdfTableHelpers.TotalFromWords(words);

        var items = new List<LineItem>();
        CurrentItem? current = null;

        foreach (var row in rows)
        {
            var cells = row.Cells;
            var productCode = Cell(cells, "Product Code");
            var product = Cell(cells, "Product");

            if (productCode == "Term-Months" || product == "Term in months")
            {
                continue;
            }

            if (ProductCodePattern().IsMatch(productCode))
            {
                if (current is not null)
                {
                    items.Add(BuildItem(current));
                }

                current = new CurrentItem(
                    [productCode],
                    [product],
                    cells);
            }
            else if (current is not null && productCode.Length == 0 && product.Length > 0)
            {
                current.DescriptionParts.Add(product);
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
        for (var i = 0; i < words.Count; i++)
        {
            var first = words[i];
            if (first.Text != "Product")
            {
                continue;
            }

            var code = words
                .Skip(i + 1)
                .Take(8)
                .FirstOrDefault(word =>
                    word.Text == "Code"
                    && word.PageIndex == first.PageIndex
                    && Math.Abs(word.Top - first.Top) <= 4
                    && word.X0 > first.X0);
            if (code is not null)
            {
                return first;
            }
        }

        throw new ParseError("detect", "Could not find the Product Code table header.", "Could not find Product Code header");
    }

    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(IReadOnlyList<PdfWord> words, PdfWord header)
    {
        var headerWords = words
            .Where(word => word.PageIndex == header.PageIndex && word.Top >= header.Top - 16 && word.Top <= header.Top + 16)
            .ToList();

        var productWords = headerWords
            .Where(word => word.Text == "Product")
            .OrderBy(word => word.X0)
            .ToList();
        if (productWords.Count < 2)
        {
            throw new ParseError("detect", "Could not find the Product Code table header.", "Could not resolve Product Code columns");
        }

        var discountWords = headerWords
            .Where(word => word.Text == "Discount")
            .OrderBy(word => word.X0)
            .ToList();
        var discountX0 = discountWords.FirstOrDefault()?.X0 ?? 348.0;
        var netWord = headerWords
            .Where(word => word.Text == "Net" && word.X0 > discountX0)
            .OrderBy(word => word.X0)
            .FirstOrDefault();
        var totalWord = headerWords
            .Where(word => word.Text == "Total" && word.X0 > 450)
            .OrderBy(word => word.X0)
            .FirstOrDefault();

        var headers = new List<(string Name, double X0)>
        {
            ("Product Code", productWords[0].X0),
            ("Product", productWords[1].X0),
            ("Term (Months)", headerWords
                .Where(word => (word.Text == "Term" || word.Text == "(Months)") && word.X0 > productWords[1].X0)
                .Select(word => word.X0)
                .DefaultIfEmpty(220.0)
                .Min()),
            ("List Unit Price", headerWords
                .Where(word => (word.Text == "List" || word.Text == "Unit" || word.Text == "Price") && word.X0 >= 260 && word.X0 <= 340)
                .Select(word => word.X0)
                .DefaultIfEmpty(280.0)
                .Min()),
            ("Total Discount", discountX0),
            ("Net Unit Price", netWord is null ? 396.0 : netWord.X0 - 22),
            ("Quantity", headerWords.FirstOrDefault(word => word.Text == "Quantity")?.X0 ?? 460.0),
            ("Total Net Price", totalWord?.X0 ?? 514.0)
        };

        return PdfTableHelpers.ColumnRanges(headers, header.PageWidth);
    }

    private static LineItem BuildItem(CurrentItem item)
    {
        var cells = item.Cells;
        return new LineItem
        {
            Vpn = TextCleaner.JoinSpaced(item.CodeParts),
            Description = TextCleaner.JoinSpaced(item.DescriptionParts),
            Term = DecimalCleaner.ParseInt(Cell(cells, "Term (Months)")),
            Msrp = DecimalCleaner.Parse(Cell(cells, "List Unit Price")),
            Cost = DecimalCleaner.Parse(Cell(cells, "Net Unit Price")),
            Qty = DecimalCleaner.ParseInt(Cell(cells, "Quantity")),
            Raw = RawDict(cells)
        };
    }

    private static string Cell(IReadOnlyDictionary<string, string> cells, string key)
    {
        return cells.TryGetValue(key, out var value) ? TextCleaner.Clean(value) : string.Empty;
    }

    private static IReadOnlyDictionary<string, string> RawDict(IReadOnlyDictionary<string, string> cells)
    {
        return cells
            .Select(pair => (pair.Key, Value: TextCleaner.Clean(pair.Value)))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    [GeneratedRegex(@"^[A-Z0-9-]+$")]
    private static partial Regex ProductCodePattern();

    private sealed record CurrentItem(
        List<string> CodeParts,
        List<string> DescriptionParts,
        IReadOnlyDictionary<string, string> Cells);
}

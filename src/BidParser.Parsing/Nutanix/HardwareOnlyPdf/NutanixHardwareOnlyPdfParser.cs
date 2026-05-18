using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Nutanix.HardwareOnlyPdf;

public sealed class NutanixHardwareOnlyPdfParser : IParser
{
    public string Slug => "nutanix_hardware_only_pdf";
    public string DisplayName => "Hardware Only (PDF)";
    public string Vendor => "Nutanix";
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => "Foreign Uplift";

    public ParseResult Parse(string path)
    {
        var words = PdfWordCollector.CollectWords(path);
        var bannerIndex = FindQuoteDBanner(words);
        var header = FindHeader(words, bannerIndex);
        var columns = BuildColumns(words, header);
        var rows = PdfTableHelpers.RowsBetween(words, header.Top + 6, header.PageIndex, columns);
        var quotedTotal = PdfTableHelpers.TotalFromWords(words, bannerIndex);

        var items = new List<LineItem>();
        CurrentItem? current = null;

        foreach (var row in rows)
        {
            var cells = row.Cells.ToDictionary(pair => pair.Key, pair => TextCleaner.Clean(pair.Value));
            var productCode = Cell(cells, "Product Code");
            var isWrappedCode = productCode.Length > 0
                && current is not null
                && !HasAny(cells, "Term (Months)", "List Unit Price", "Net Unit Price", "Quantity", "Total Net Price");

            if (productCode.Length > 0 && !isWrappedCode)
            {
                if (current is not null)
                {
                    items.Add(BuildItem(current));
                }

                current = new CurrentItem(
                    [productCode],
                    [Cell(cells, "Product")],
                    cells);
            }
            else if (current is not null && cells.Values.Any(value => value.Length > 0))
            {
                if (productCode.Length > 0)
                {
                    current.CodeParts.Add(productCode);
                }

                var product = Cell(cells, "Product");
                if (product.Length > 0)
                {
                    current.DescriptionParts.Add(product);
                }

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

    private static int FindQuoteDBanner(IReadOnlyList<PdfWord> words)
    {
        for (var i = 0; i < words.Count; i++)
        {
            if (!string.Equals(words[i].Text, "Quote", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nearby = words
                .Skip(i)
                .Take(28)
                .Where(word => word.PageIndex == words[i].PageIndex)
                .Select(word => word.Text)
                .ToList();
            var text = string.Join(' ', nearby);
            if (nearby.Any(word => string.Equals(word, "D", StringComparison.OrdinalIgnoreCase))
                && text.Contains("distributor", StringComparison.OrdinalIgnoreCase)
                && text.Contains("reseller", StringComparison.OrdinalIgnoreCase)
                && text.Contains("only", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ParseError("detect", "Could not find the Quote D section banner.", "Could not find Quote D banner");
    }

    private static PdfWord FindHeader(IReadOnlyList<PdfWord> words, int startIndex)
    {
        for (var i = startIndex; i < words.Count; i++)
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
            Vpn = JoinProductCode(item.CodeParts),
            Description = TextCleaner.JoinSpaced(item.DescriptionParts),
            Term = DecimalCleaner.ParseOptionalInt(Cell(cells, "Term (Months)")),
            Msrp = DecimalCleaner.Parse(Cell(cells, "List Unit Price"), defaultZero: true),
            Cost = DecimalCleaner.Parse(Cell(cells, "Net Unit Price"), defaultZero: true),
            Qty = DecimalCleaner.ParseInt(Cell(cells, "Quantity")),
            Raw = RawDict(cells)
        };
    }

    private static string JoinProductCode(IEnumerable<string> parts)
    {
        var cleaned = parts.Select(TextCleaner.Clean).Where(part => part.Length > 0).ToList();
        if (cleaned.Count == 0)
        {
            return string.Empty;
        }

        var result = cleaned[0];
        foreach (var part in cleaned.Skip(1))
        {
            if (result.EndsWith("-", StringComparison.Ordinal) || part is "CM" or "AB1A-CM" or "6517P-CM")
            {
                result += part;
            }
            else
            {
                result += $" {part}";
            }
        }

        return result.Trim();
    }

    private static bool HasAny(IReadOnlyDictionary<string, string> cells, params string[] keys)
    {
        return keys.Any(key => Cell(cells, key).Length > 0);
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

    private sealed record CurrentItem(
        List<string> CodeParts,
        List<string> DescriptionParts,
        Dictionary<string, string> Cells);
}

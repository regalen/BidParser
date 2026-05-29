using System.Globalization;
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
            var cells = row.Cells.ToDictionary(pair => pair.Key, pair => TextCleaner.Clean(pair.Value));
            var productCode = PdfTableHelpers.Cell(cells,"Product Code");
            var product = PdfTableHelpers.Cell(cells,"Product");

            // Anchor signal is restricted to Quantity (and Term as a backup) because the supplier
            // template puts the "USD" label and the Quantity on the anchor row while the numeric
            // List/Net/Total values land one visual line below (continuation row). Treating List
            // or Net values as an anchor cue would mis-classify continuation rows as new items
            // whenever the Product Code wraps onto multiple lines.
            var hasAnchorSignal = PdfTableHelpers.HasAny(cells, "Quantity", "Term (Months)");

            // Page-footer skip. The supplier template prints "Page X of Y" between body rows
            // on every page; without skipping, its words land in column cells and pollute the
            // last line item on the previous page.
            var rowText = string.Join(" ", cells.Values.Where(value => value.Length > 0));
            if (PageFooterPattern().IsMatch(rowText))
            {
                continue;
            }

            // Term-Months row — KEEP as a line item with sentinel-zero pricing.
            if (product == "Term in months")
            {
                if (current is not null)
                {
                    items.Add(BuildItem(current));
                    current = null;
                }

                var termValue = DecimalCleaner.ParseOptionalInt(PdfTableHelpers.Cell(cells,"Term (Months)"));
                items.Add(new LineItem
                {
                    Vpn = "Term-Months",
                    Description = "Term in months",
                    Term = termValue,
                    Qty = termValue ?? 0,
                    Cost = 0m,
                    Msrp = 0m,
                    Raw = PdfTableHelpers.RawDict(cells)
                });
                continue;
            }

            // Narrow-layout continuation row ("Months" alone after "Term-") — already
            // canonicalised above, drop it so it doesn't append to the next real anchor.
            if (hasAnchorSignal && productCode.Length > 0 && !ProductCodePattern().IsMatch(productCode))
            {
                continue;
            }

            var isAnchor = hasAnchorSignal
                && productCode.Length > 0
                && ProductCodePattern().IsMatch(productCode);

            if (isAnchor)
            {
                if (current is not null)
                {
                    items.Add(BuildItem(current));
                }

                current = new CurrentItem(
                    [productCode],
                    product.Length > 0 ? [product] : [],
                    new Dictionary<string, string>(cells, StringComparer.Ordinal));
            }
            else if (current is not null)
            {
                if (productCode.Length > 0 && ProductCodePattern().IsMatch(productCode))
                {
                    current.CodeParts.Add(productCode);
                }

                if (product.Length > 0)
                {
                    current.DescriptionParts.Add(product);
                }

                // Merge any non-Product cells (numeric values) from continuation rows. The
                // supplier template often puts "USD" on the anchor row and the numeric tail on
                // the next visual row, so List/Net/Total values arrive across two rows; append
                // so DecimalCleaner can strip "USD" and parse the combined value.
                foreach (var (key, value) in cells)
                {
                    if (key == "Product Code" || key == "Product" || value.Length == 0)
                    {
                        continue;
                    }

                    current.Cells[key] = current.Cells.TryGetValue(key, out var existing) && existing.Length > 0
                        ? $"{existing} {value}"
                        : value;
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

    private static LineItem BuildItem(CurrentItem item)
    {
        var cells = item.Cells;
        return new LineItem
        {
            Vpn = TextCleaner.JoinUnspaced(item.CodeParts),
            Description = TextCleaner.JoinSpaced(item.DescriptionParts),
            Term = DecimalCleaner.ParseOptionalInt(PdfTableHelpers.Cell(cells,"Term (Months)")),
            Msrp = DecimalCleaner.Parse(PdfTableHelpers.Cell(cells,"List Unit Price"), defaultZero: true),
            Cost = DecimalCleaner.Parse(PdfTableHelpers.Cell(cells,"Net Unit Price"), defaultZero: true),
            Qty = DecimalCleaner.ParseInt(PdfTableHelpers.Cell(cells,"Quantity")),
            StartDate = ParseStartDate(PdfTableHelpers.Cell(cells,"Selected Start Date")),
            Raw = PdfTableHelpers.RawDict(cells)
        };
    }

    private static DateOnly? ParseStartDate(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        return DateOnly.TryParseExact(value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
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
                .Take(12)
                .FirstOrDefault(word =>
                    word.Text == "Code"
                    && word.PageIndex == first.PageIndex
                    && IsAdjacentCode(first, word));
            if (code is not null)
            {
                return first;
            }
        }

        throw new ParseError("detect", "Could not find the Product Code table header.", "Could not find Product Code header");
    }

    // "Code" can sit either to the right of "Product" on the same baseline (wide-column layouts)
    // or directly beneath it (narrow-column layouts where the cell wraps onto two lines).
    private static bool IsAdjacentCode(PdfWord product, PdfWord code)
    {
        if (Math.Abs(code.Top - product.Top) <= 4 && code.X0 > product.X0)
        {
            return true;
        }

        var verticalGap = code.Top - product.Top;
        var xOverlaps = code.X0 <= product.X1 + 2 && code.X1 >= product.X0 - 2;
        return verticalGap > 0 && verticalGap <= 16 && xOverlaps;
    }

    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(IReadOnlyList<PdfWord> words, PdfWord header)
    {
        var band = words
            .Where(word => word.PageIndex == header.PageIndex && word.Top >= header.Top - 14 && word.Top <= header.Top + 22)
            .OrderBy(word => word.X0)
            .ToList();

        // Header labels span multiple visual lines (e.g. "Selected" / "Start" / "Date" stacked).
        // Cluster header words into vertical columns by overlapping x-extent so each cluster
        // represents one logical column whose left edge is the cluster's leftmost x0.
        var clusters = new List<List<PdfWord>>();
        foreach (var word in band)
        {
            var match = clusters.FirstOrDefault(cluster =>
                cluster.Any(other => other.X0 <= word.X1 + 4 && other.X1 >= word.X0 - 4));
            if (match is null)
            {
                clusters.Add([word]);
            }
            else
            {
                match.Add(word);
            }
        }

        var anchored = clusters
            .Select(cluster => new
            {
                X0 = cluster.Min(word => word.X0),
                X1 = cluster.Max(word => word.X1),
                Tokens = cluster.Select(word => word.Text).ToHashSet(StringComparer.Ordinal)
            })
            .OrderBy(cluster => cluster.X0)
            .ToList();

        var named = new List<(string Name, double X0, double X1)>();
        var foundProductCode = false;
        var foundProduct = false;
        foreach (var cluster in anchored)
        {
            string? name = null;
            if (!foundProductCode && cluster.Tokens.Contains("Code") && cluster.Tokens.Contains("Product"))
            {
                name = "Product Code";
                foundProductCode = true;
            }
            else if (!foundProduct && cluster.Tokens.Contains("Product"))
            {
                name = "Product";
                foundProduct = true;
            }
            else if (cluster.Tokens.Contains("Term") || cluster.Tokens.Contains("(Months)"))
            {
                name = "Term (Months)";
            }
            else if (cluster.Tokens.Contains("Selected") || cluster.Tokens.Contains("Start") || cluster.Tokens.Contains("Date"))
            {
                name = "Selected Start Date";
            }
            else if (cluster.Tokens.Contains("List"))
            {
                name = "List Unit Price";
            }
            else if (cluster.Tokens.Contains("Discount"))
            {
                // Captured for column-range completeness only; the value is ignored downstream.
                name = "Total Discount";
            }
            else if (cluster.Tokens.Contains("Quantity"))
            {
                name = "Quantity";
            }
            else if (cluster.Tokens.Contains("Net") && cluster.Tokens.Contains("Unit"))
            {
                name = "Net Unit Price";
            }
            else if (cluster.Tokens.Contains("Net") && cluster.Tokens.Contains("Price"))
            {
                // No "Unit" token → this is the rightmost "Total Net Price" column. Ignored.
                name = "Total Net Price";
            }

            if (name is not null)
            {
                named.Add((name, cluster.X0, cluster.X1));
            }
        }

        if (!foundProductCode || !foundProduct)
        {
            throw new ParseError("detect", "Could not find the Product Code table header.", "Could not resolve Product Code columns");
        }

        // Compute each column's left boundary. Text columns and narrow numeric columns snap to
        // the next header cluster's X0. Wide right-aligned numeric columns ("List Unit Price",
        // "Net Unit Price", "Total Net Price") need extra leftward padding because their body
        // cells (e.g. "USD 157,199.04") extend past the header word's X0 by several points.
        // The padding amount is column-specific and tuned empirically against real quotes.
        var sorted = named.OrderBy(entry => entry.X0).ToList();
        var lefts = new double[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            if (i == 0)
            {
                lefts[i] = sorted[i].X0;
            }
            else
            {
                var padding = LeftPadding(sorted[i].Name);
                lefts[i] = padding > 0
                    ? sorted[i].X0 - padding
                    : sorted[i].X0;
            }
        }

        var ranges = new Dictionary<string, (double Left, double Right)>(StringComparer.Ordinal);
        for (var i = 0; i < sorted.Count; i++)
        {
            var right = i == sorted.Count - 1 ? header.PageWidth : lefts[i + 1];
            ranges[sorted[i].Name] = (lefts[i], right);
        }

        return ranges;
    }

    private static double LeftPadding(string name) => name switch
    {
        "List Unit Price" => 5.0,
        "Net Unit Price" => 10.0,
        "Total Net Price" => 25.0,
        _ => 0.0
    };

    [GeneratedRegex(@"^[A-Z0-9-]+$")]
    private static partial Regex ProductCodePattern();

    [GeneratedRegex(@"^Page\s+\d+\s+of\s+\d+$")]
    private static partial Regex PageFooterPattern();

    private sealed record CurrentItem(
        List<string> CodeParts,
        List<string> DescriptionParts,
        Dictionary<string, string> Cells);
}

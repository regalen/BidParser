using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Strike.QuotePdf;

/// <summary>Parses Strike Group Australia quotation PDFs.</summary>
public sealed partial class StrikeQuotePdfParser : IParser
{
    // Every resolvable item row in the retained fixtures has at least a 5pt inter-column corridor.
    // Resolve each row independently and use median boundaries so one short first code cannot pull
    // the Product Code / Description boundary left for the whole quote.
    private const double ContentBandGutter = 5.0;
    private static readonly string[] Columns = ["Product Code", "Description", "QTY", "Price", "Value"];
    public string Slug => ParserSlugs.StrikeQuotePdf;
    public string DisplayName => "Quote (PDF)";
    public string Vendor => Vendors.Strike;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsOnCost => true;

    public double Detect(string path)
    {
        try
        {
            var words = PdfWordCollector.CollectWords(path).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            return PdfTableHelpers.FindSequence(words, ["QUOTE", "REFERENCE"]) is not null && FindHeader(words) is not null ? 0.9 : 0.0;
        }
        catch { return 0.0; }
    }

    public ParseResult Parse(string path)
    {
        var words = PdfWordCollector.CollectWords(path).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var headerIndex = FindHeader(words)
            ?? throw new ParseError(
                "detect", "Could not find the Strike quote table.", "Missing Product Code / QTY header.");
        var stopIndex = words.FindIndex(headerIndex, word => word.Text == "Delivery");
        if (stopIndex < 0)
        {
            throw new ParseError(
                "extract", "Could not locate Strike quote totals.", "Missing Delivery Charge Value anchor.");
        }

        var header = words[headerIndex];
        var qtyCentre = HeaderCentre(words, headerIndex, "QTY");
        var priceCentre = HeaderCentre(words, headerIndex, "Price");
        var bodyIndex = Enumerable.Range(headerIndex + 1, stopIndex - headerIndex - 1)
            .Cast<int?>()
            .FirstOrDefault(index =>
                words[index!.Value].Top > header.Top
                && words[index.Value].X0 < priceCentre
                && words[index.Value].X1 > qtyCentre
                && Integer().IsMatch(words[index.Value].Text));
        if (bodyIndex is null)
        {
            throw new ParseError("extract", "Could not find any Strike quote lines.", "Missing QTY row.");
        }

        var bodyStart = words[bodyIndex.Value];
        var columns = BuildColumns(words, headerIndex, bodyIndex.Value, stopIndex, header.PageWidth);
        var rows = PdfTableHelpers.RowsBetween(
            words,
            PdfTableHelpers.RowStartTop(words, bodyStart),
            bodyStart.PageIndex,
            columns,
            "Delivery");
        var logical = PdfTableHelpers.GroupByAnchor(rows, row => Integer().IsMatch(PdfTableHelpers.Cell(row.Cells, "QTY")));
        var extracted = logical.Select((group, index) =>
        {
            var vpn = TextCleaner.JoinWrapped(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Product Code")));
            var description = TextCleaner.JoinSpaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Description")));
            var qtyText = TextCleaner.JoinUnspaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "QTY")));
            var price = TextCleaner.JoinUnspaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Price")));
            var valueText = TextCleaner.JoinUnspaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Value")));
            var value = Money(valueText, "Value");
            var item = new LineItem
            {
                Vpn = vpn,
                Description = description,
                Qty = Number(qtyText, "QTY"),
                MinQty = 1,
                Msrp = 0m,
                Cost = Money(price, "Price"),
                LineSequence = (index + 1).ToString(),
                Raw = PdfTableHelpers.RawDict(group, new Dictionary<string, string>
                {
                    ["Product Code"] = vpn,
                    ["Description"] = description,
                    ["QTY"] = qtyText,
                    ["Price"] = price,
                    ["Value"] = valueText
                })
            };
            return (Item: item, Value: value);
        }).ToList();
        var items = extracted.Select(line => line.Item).ToList();
        var tail = PdfTableHelpers.WordStreamText(words.Skip(stopIndex));
        var deliveryText = Regex.Match(
            tail, @"Delivery\s+Charge\s+Value\s+([^\s]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var delivery = DecimalOrNull(deliveryText);
        var totalMatch = Regex.Match(
            tail, @"Total\s+\(ex\.\s*tax\)\s*([$\d,.]+)", RegexOptions.IgnoreCase);
        if (!totalMatch.Success)
        {
            throw new ParseError(
                "totals", "Could not locate the Strike quote total.", "Missing Total (ex. tax) amount.");
        }

        var totalText = totalMatch.Groups[1].Value;
        var quotedTotal = DecimalCleaner.Parse(totalText) - (delivery ?? 0m);
        var quote = ExtractQuoteReference(words) ?? string.Empty;
        var (bidNumber, bidRevision) = BidMetadataCleaner.CleanRevisionless(quote);
        var computed = extracted.Sum(line => line.Value);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quote,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = new ValidationResult
            {
                ComputedTotal = computed,
                QuotedTotal = quotedTotal,
                Difference = computed - quotedTotal,
                Matches = Math.Abs(computed - quotedTotal) <= 0.01m
            }
        };
    }

    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(
        IReadOnlyList<PdfWord> words, int headerIndex, int bodyIndex, int stopIndex, double pageWidth)
    {
        var headers = Columns
            .Select(name => (Name: name, Centre: HeaderCentre(words, headerIndex, name)))
            .ToList();
        var qtyCentre = headers.Single(header => header.Name == "QTY").Centre;
        var priceCentre = headers.Single(header => header.Name == "Price").Centre;
        var candidates = words.Skip(bodyIndex).Take(stopIndex - bodyIndex)
            .Where(word => word.X0 < priceCentre && word.X1 > qtyCentre && Integer().IsMatch(word.Text))
            .GroupBy(word => (word.PageIndex, Coordinate: word.LineY ?? (word.Top + word.Bottom) / 2))
            .Select(group => group.First())
            .Select(anchor =>
            {
                try
                {
                    return PdfTableHelpers.ContentColumnRanges(
                        headers, PdfTableHelpers.WordsOnRow(words, anchor), ContentBandGutter, pageWidth);
                }
                catch (ParseError)
                {
                    return null;
                }
            })
            .Where(columns => columns is not null)
            .Cast<IReadOnlyDictionary<string, (double Left, double Right)>>()
            .ToList();

        if (candidates.Count == 0)
            throw new ParseError(
                "detect", "Could not resolve the Strike quote columns.",
                "No item row contained all five content columns.");

        var boundaries = Enumerable.Range(1, Columns.Length - 1)
            .Select(index => Median(candidates.Select(candidate => candidate[Columns[index]].Left)))
            .ToList();
        var allBoundaries = new[] { 0d }.Concat(boundaries).Append(pageWidth).ToList();
        return Columns.Select((name, index) => (name, Range: (allBoundaries[index], allBoundaries[index + 1])))
            .ToDictionary(pair => pair.name, pair => pair.Range);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    private static int? FindHeader(IReadOnlyList<PdfWord> words)
    {
        for (var i = 0; i < words.Count - 5; i++)
        {
            if (words[i].Text == "Product"
                && words[i + 1].Text == "Code"
                && words.Skip(i).Take(16).Any(word => word.Text == "QTY"))
            {
                return i;
            }
        }

        return null;
    }

    private static double HeaderCentre(IReadOnlyList<PdfWord> words, int start, string name)
    {
        var tokens = name.Split(' ');
        var index = PdfTableHelpers.FindSequence(words, tokens, start)
            ?? throw new ParseError(
                "detect", "Could not resolve the Strike quote columns.", $"Missing {name} header.");
        var match = words.Skip(index).Take(tokens.Length);
        return (match.Min(word => word.X0) + match.Max(word => word.X1)) / 2;
    }

    private static decimal? DecimalOrNull(string value)
    {
        try
        {
            return value.Length == 0 || !value.Any(char.IsDigit) ? null : DecimalCleaner.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static int Number(string value, string sourceColumn)
    {
        var match = Regex.Match(value, @"\d+");
        if (!match.Success) throw NumericError(sourceColumn);
        try
        {
            return DecimalCleaner.ParseInt(match.Value);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw NumericError(sourceColumn);
        }
    }

    private static decimal Money(string value, string sourceColumn)
    {
        var match = Regex.Match(value, @"\d[\d,]*(?:\.\d+)?");
        if (!match.Success) throw NumericError(sourceColumn);
        try
        {
            return DecimalCleaner.Parse(match.Value);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw NumericError(sourceColumn);
        }
    }

    private static ParseError NumericError(string sourceColumn) => new(
        "extract",
        $"Could not read the Strike {sourceColumn} value.",
        $"Missing or invalid {sourceColumn} value.");

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Integer();

    private static string? ExtractQuoteReference(IReadOnlyList<PdfWord> words)
    {
        var index = PdfTableHelpers.FindSequence(words, ["QUOTE", "REFERENCE"]);
        if (index is null)
        {
            return null;
        }

        var header = words[index.Value];
        return words.Where(w => w.PageIndex == header.PageIndex && w.Top > header.Top + 3 && w.X0 < header.X1 && w.X1 > header.X0)
            .OrderBy(word => word.Top)
            .Select(word => TextCleaner.Clean(word.Text))
            .FirstOrDefault();
    }
}

using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Epson.QuotePdf;

/// <summary>Parses Epson project-price quotation PDFs.</summary>
public sealed partial class EpsonQuotePdfParser : IParser
{
    // Epson's Model and Product Description content nearly touch: the narrowest retained corridor
    // is 1.29pt (the other fixtures measure 3.09pt and 4.43pt). One point is therefore intentional;
    // measured-boundary tests make a font/layout drift fail visibly instead of shifting prices.
    private const double ContentBandGutter = 1.0;
    private readonly Func<string, IReadOnlyList<PdfWord>> collectWords;

    private static readonly string[] Columns =
    [
        "Epson Product Code", "Model", "Product Description", "Price per unit ($AUD ex GST)"
    ];

    public string Slug => ParserSlugs.EpsonQuotePdf;
    public string DisplayName => "Quote (PDF)";
    public string Vendor => Vendors.Epson;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsOnCost => true;

    public EpsonQuotePdfParser() : this(PdfWordCollector.CollectWords) { }

    internal EpsonQuotePdfParser(Func<string, IReadOnlyList<PdfWord>> collectWords)
    {
        this.collectWords = collectWords;
    }
    public double Detect(string path)
    {
        try
        {
            var words = collectWords(path)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .ToList();
            return words.Any(word => word.Text == "Contract")
                   && PdfTableHelpers.FindSequence(words, ["Epson", "Product", "Code"]) is not null
                ? 0.9
                : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        var words = collectWords(path).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var headerIndex = PdfTableHelpers.FindSequence(words, ["Epson", "Product", "Code"])
            ?? throw new ParseError("detect", "Could not find the Epson quote table.", "Missing Epson Product Code header.");
        var stopIndex = words.FindIndex(headerIndex, w => w.Text.StartsWith("Terms", StringComparison.OrdinalIgnoreCase));
        if (stopIndex < 0)
        {
            throw new ParseError(
                "extract", "Could not locate the end of the Epson quote table.", "Missing Terms anchor.");
        }

        var header = words[headerIndex];
        var modelCentre = HeaderCentre(words, headerIndex, "Model");
        var bodyIndex = Enumerable.Range(headerIndex + 1, stopIndex - headerIndex - 1)
            .Cast<int?>()
            .FirstOrDefault(index =>
                words[index!.Value].Top > header.Top
                && words[index.Value].X0 < modelCentre
                && ProductCode().IsMatch(words[index.Value].Text));
        if (bodyIndex is null)
        {
            throw new ParseError(
                "extract", "Could not find any Epson quote lines.", "Missing Epson product row.");
        }

        var bodyStart = words[bodyIndex.Value];
        var columns = BuildColumns(words, headerIndex, bodyIndex.Value, stopIndex, header.PageWidth);
        // PdfPig may emit the anchor as either "Terms" or a combined token such as "Terms&" on
        // different platforms. Reuse the token located above; RowsBetween excludes its complete
        // visual row so adjacent footer fragments cannot leak into the final product.
        var rows = PdfTableHelpers.RowsBetween(
            words,
            PdfTableHelpers.RowStartTop(words, bodyStart),
            bodyStart.PageIndex,
            columns,
            words[stopIndex].Text);
        var stream = PdfTableHelpers.WordStreamText(words);
        var qtyText = Regex.Match(stream, @"Effective\s+Immediately.*?(?:up\s+)?to\s+([\d,]+)", RegexOptions.IgnoreCase);
        if (!qtyText.Success)
        {
            throw new ParseError(
                "extract", "Could not find the quote-level quantity.", "Missing 'up to' quantity.");
        }

        var qty = DecimalCleaner.ParseInt(qtyText.Groups[1].Value);
        var items = PdfTableHelpers
            .GroupByAnchor(
                rows,
                row => ProductCode().IsMatch(PdfTableHelpers.Cell(row.Cells, "Epson Product Code")))
            .Select((group, index) =>
            {
                var vpn = TextCleaner.JoinUnspaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Epson Product Code")));
                var description = TextCleaner.JoinSpaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Product Description")));
                var price = TextCleaner.JoinUnspaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, "Price per unit ($AUD ex GST)")));
                return new LineItem
                {
                    Vpn = vpn,
                    Description = description,
                    Qty = qty,
                    MinQty = 1,
                    Msrp = 0m,
                    Cost = Money(price, "Price per unit ($AUD ex GST)"),
                    LineSequence = (index + 1).ToString(),
                    Raw = PdfTableHelpers.RawDict(group, new Dictionary<string, string>
                    {
                        ["Epson Product Code"] = vpn,
                        ["Product Description"] = description,
                        ["Price per unit ($AUD ex GST)"] = price
                    })
                };
            })
            .ToList();
        var contract = Regex.Match(stream, @"Contract\s+No:\s*(\S+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var filenameBid = BidMetadataCleaner.FromFilename(path);
        if (!contract.All(char.IsDigit)) contract = filenameBid.BidNumber ?? string.Empty;
        var (bidNumber, bidRevision) = BidMetadataCleaner.CleanRevisionless(contract);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = contract,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = null,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = new ValidationResult
            {
                ComputedTotal = items.Sum(item => item.Cost * item.Qty),
                QuotedTotal = null,
                Matches = true,
                Difference = 0m
            }
        };
    }

    private static double HeaderCentre(IReadOnlyList<PdfWord> words, int start, string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = PdfTableHelpers.FindSequence(words, tokens, start)
            ?? throw new ParseError(
                "detect", "Could not resolve the Epson quote columns.", $"Missing {name} header.");
        var match = words.Skip(index).Take(tokens.Length);
        return (match.Min(word => word.X0) + match.Max(word => word.X1)) / 2;
    }

    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(
        IReadOnlyList<PdfWord> words, int headerIndex, int bodyIndex, int stopIndex, double pageWidth)
    {
        var region = words.Skip(bodyIndex).Take(stopIndex - bodyIndex);
        var headers = Columns
            .Select(name => (Name: name, Centre: HeaderCentre(words, headerIndex, name)))
            .ToList();
        return PdfTableHelpers.ContentColumnRanges(headers, region, ContentBandGutter, pageWidth);
    }

    private static decimal Money(string value, string sourceColumn)
    {
        var match = Regex.Match(value, @"\d[\d,]*(?:\.\d+)?");
        if (!match.Success)
            throw new ParseError(
                "extract",
                $"Could not read the Epson {sourceColumn} value.",
                $"Missing or invalid {sourceColumn} value.");
        try
        {
            return DecimalCleaner.Parse(match.Value);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new ParseError(
                "extract",
                $"Could not read the Epson {sourceColumn} value.",
                $"Missing or invalid {sourceColumn} value.");
        }
    }

    [GeneratedRegex(@"^[A-Z]\d[A-Z0-9-]+$")]
    private static partial Regex ProductCode();
}

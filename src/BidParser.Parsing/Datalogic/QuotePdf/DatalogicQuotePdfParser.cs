using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Datalogic.QuotePdf;

/// <summary>Parses Datalogic price-exception quotation PDFs.</summary>
public sealed partial class DatalogicQuotePdfParser : IParser
{
    // The narrowest inter-column whitespace corridor across the three retained fixtures is
    // 12.16pt; 11pt separates all nine content bands without treating normal word spaces as bands.
    private const double ContentBandGutter = 11.0;
    private readonly Func<string, IReadOnlyList<PdfWord>> collectWords;

    private static readonly string[] Columns =
    [
        "ID #", "Part Number", "Description", "Qty", "List Price", "Std. Disc.",
        "Target Disc.", "Target Unit Price", "Total Value"
    ];

    public string Slug => ParserSlugs.DatalogicQuotePdf;
    public string DisplayName => "Quote (PDF)";
    public string Vendor => Vendors.Datalogic;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsOnCost => true;

    public DatalogicQuotePdfParser() : this(PdfWordCollector.CollectWords) { }

    internal DatalogicQuotePdfParser(Func<string, IReadOnlyList<PdfWord>> collectWords)
    {
        this.collectWords = collectWords;
    }

    public double Detect(string path)
    {
        try
        {
            var words = CollectWords(path);
            return PdfTableHelpers.FindSequence(words, ["ID", "#", "Part", "Number"]) is not null
                   && words.Any(w => w.Text == "Datalogic") ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        var words = CollectWords(path);
        var headerIndex = PdfTableHelpers.FindSequence(words, ["ID", "#", "Part", "Number"])
            ?? throw new ParseError("detect", "Could not find the Datalogic quote table.", "Missing ID # / Part Number header.");
        var stopIndex = words.FindIndex(headerIndex, w => w.Text == "Grand");
        if (stopIndex < 0)
        {
            throw new ParseError("totals", "Could not locate the Grand Total.", "Missing Grand Total.");
        }

        var header = words[headerIndex];
        var bodyIndex = FindBodyStart(words, headerIndex, stopIndex);
        var bodyStart = words[bodyIndex];
        var columns = BuildColumns(words, headerIndex, bodyIndex, stopIndex, header.PageWidth);
        var rows = PdfTableHelpers.RowsBetween(words, PdfTableHelpers.RowStartTop(words, bodyStart), bodyStart.PageIndex, columns, "Grand");
        var minQty = ExtractInt(PdfTableHelpers.WordStreamText(words), @"Min\s+Shipment\s+Size:\s*(\d+)") ?? 1;
        var currency = Extract(PdfTableHelpers.WordStreamText(words), @"Currency:\s*([A-Za-z]+)");
        EnsureAudCurrency(currency);

        var items = PdfTableHelpers
            .GroupByAnchor(rows, row => PdfTableHelpers.Cell(row.Cells, "ID #").Length > 0)
            .Select((group, index) =>
            {
                var vpn = TextCleaner.JoinUnspaced(group.Select(r => PdfTableHelpers.Cell(r.Cells, "Part Number")));
                var description = TextCleaner.JoinSpaced(group.Select(r => PdfTableHelpers.Cell(r.Cells, "Description")));
                var qtyText = TextCleaner.JoinUnspaced(group.Select(r => PdfTableHelpers.Cell(r.Cells, "Qty")));
                var listPrice = TextCleaner.JoinUnspaced(group.Select(r => PdfTableHelpers.Cell(r.Cells, "List Price")));
                var targetPrice = TextCleaner.JoinUnspaced(group.Select(r => PdfTableHelpers.Cell(r.Cells, "Target Unit Price")));
                return new LineItem
                {
                    Vpn = vpn,
                    Description = description,
                    Qty = Number(qtyText, "Qty"),
                    MinQty = minQty,
                    Msrp = Money(listPrice, "List Price"),
                    Cost = Money(targetPrice, "Target Unit Price"),
                    LineSequence = (index + 1).ToString(),
                    Raw = PdfTableHelpers.RawDict(group, new Dictionary<string, string>
                    {
                        ["Part Number"] = vpn,
                        ["Description"] = description,
                        ["Qty"] = qtyText,
                        ["List Price"] = listPrice,
                        ["Target Unit Price"] = targetPrice
                    })
                };
            }).ToList();
        var totalText = Extract(PdfTableHelpers.WordStreamText(words.Skip(stopIndex)), @"Grand\s+Total:\s*([$\d,.]+)")
            ?? throw new ParseError(
                "totals", "Could not locate the Grand Total.", "Grand Total amount missing.");
        var total = DecimalCleaner.Parse(totalText);
        var bid = Extract(PdfTableHelpers.WordStreamText(words), @"Price\s+Exception:\s*([^\s]+)");
        var quote = Extract(PdfTableHelpers.WordStreamText(words), @"Quotation:\s*([^\s]+)") ?? string.Empty;
        var filenameBid = BidMetadataCleaner.FromFilename(path);
        var parsedBid = BidMetadataCleaner.CleanRevisionless(bid);
        var (bidNumber, bidRevision) = parsedBid.BidNumber?.StartsWith("PE", StringComparison.OrdinalIgnoreCase) == true
            ? parsedBid
            : filenameBid;
        if (!quote.All(char.IsDigit))
        {
            quote = words.Select(word => word.Text).FirstOrDefault(value => QuotationNumber().IsMatch(value))
                ?? filenameBid.BidNumber
                ?? string.Empty;
        }
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quote,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = total,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = ParseValidation.Validate(items, total)
        };
    }

    private List<PdfWord> CollectWords(string path)
    {
        var words = collectWords(path).Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        RealignHangingGlyphs(words);
        return words;
    }

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex QuotationNumber();

    /// <summary>
    /// Datalogic's lone description hyphens have ~2pt boxes whose midlines hang 4–6pt below their
    /// printed line. Snap a short glyph to an immediately adjacent full-height word before rows are
    /// built; isolated price placeholders have no adjacent word and remain untouched.
    /// </summary>
    private static void RealignHangingGlyphs(List<PdfWord> words)
    {
        const double shortGlyphHeightRatio = 0.6;
        const double horizontalAdjacency = 4.0;
        const double verticalReach = 10.0;

        var heights = words.Select(word => word.Bottom - word.Top).Order().ToList();
        if (heights.Count == 0) return;
        var maxShortGlyphHeight = heights[heights.Count / 2] * shortGlyphHeightRatio;

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Bottom - word.Top > maxShortGlyphHeight) continue;

            var midline = (word.Top + word.Bottom) / 2.0;
            var neighbour = words
                .Where(candidate => candidate.PageIndex == word.PageIndex
                    && candidate.Bottom - candidate.Top > maxShortGlyphHeight
                    && (word.X0 - candidate.X1 is >= -0.5 and <= horizontalAdjacency
                        || candidate.X0 - word.X1 is >= -0.5 and <= horizontalAdjacency))
                .Select(candidate => (Word: candidate,
                    Distance: Math.Abs((candidate.Top + candidate.Bottom) / 2.0 - midline)))
                .Where(candidate => candidate.Distance <= verticalReach)
                .OrderBy(candidate => candidate.Distance)
                .Select(candidate => candidate.Word)
                .FirstOrDefault();

            if (neighbour is not null)
                words[i] = word with { Top = neighbour.Top, Bottom = neighbour.Bottom };
        }
    }

    private static int FindBodyStart(IReadOnlyList<PdfWord> words, int headerIndex, int stopIndex)
    {
        for (var i = headerIndex + 4; i < stopIndex; i++)
        {
            if (words[i].Top > words[headerIndex].Top && int.TryParse(words[i].Text, out _))
            {
                return i;
            }
        }

        throw new ParseError("extract", "Could not find any Datalogic quote lines.", "Missing numeric ID # row.");
    }

    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(IReadOnlyList<PdfWord> words, int headerIndex, int bodyIndex, int stopIndex, double pageWidth)
    {
        var header = words[headerIndex];
        var region = words.Skip(bodyIndex).Take(stopIndex - bodyIndex);
        var centres = Columns.Select(name => (name, Centre: HeaderCentre(words, header, name))).ToList();
        return PdfTableHelpers.ContentColumnRanges(centres, region, ContentBandGutter, pageWidth);
    }

    private static double HeaderCentre(IReadOnlyList<PdfWord> words, PdfWord header, string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var headerIndex = Enumerable.Range(0, words.Count).First(index => ReferenceEquals(words[index], header));
        var start = PdfTableHelpers.FindSequence(words, tokens, headerIndex)
            ?? throw new ParseError(
                "detect", "Could not resolve the Datalogic quote columns.", $"Missing {name} header.");
        var matches = words.Skip(start).Take(tokens.Length).ToList();
        return (matches.Min(w => w.X0) + matches.Max(w => w.X1)) / 2;
    }

    private static string? Extract(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int? ExtractInt(string text, string pattern) => int.TryParse(Extract(text, pattern), out var value) ? value : null;

    private static int Number(string value, string sourceColumn)
    {
        var match = Regex.Match(value, @"\d+(?:\.\d+)?");
        if (!match.Success)
            throw NumericError(sourceColumn);
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
        if (!match.Success)
            throw NumericError(sourceColumn);
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
        $"Could not read the Datalogic {sourceColumn} value.",
        $"Missing or invalid {sourceColumn} value.");

    private static void EnsureAudCurrency(string? currency)
    {
        if (!string.Equals(currency, "AUD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParseError("currency", $"This quote is in {currency ?? "an unknown currency"}, but Datalogic quotes must be in AUD.", "Unsupported Datalogic quote currency.");
        }
    }
}

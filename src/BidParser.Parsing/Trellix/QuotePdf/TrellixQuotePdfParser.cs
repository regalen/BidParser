using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Trellix.QuotePdf;

/// <summary>Parses Trellix Distributor Quote Report PDFs.</summary>
public sealed partial class TrellixQuotePdfParser : IParser
{
    private readonly Func<string, IReadOnlyList<PdfWord>> collectWords;

    // The report prints the first word of each heading above its column. Date and term values can
    // begin up to 4.7pt left of that word; 6pt keeps them in their measured source columns while
    // preserving right-side description words that would cross a midpoint seam.
    private const double HeaderLeftAllowance = 6.0;
    private static readonly (string Name, string Token, int Occurrence, bool Optional)[] Headings =
    [
        ("Line Item", "Line", 0, false),
        ("Product Description", "Product", 0, false),
        ("Material Category", "Material", 0, false),
        ("Program Type", "Program", 0, false),
        ("Program Benefit", "Program", 1, false),
        ("Terms Length", "Term", 0, false),
        ("FCS Date", "FCS", 0, false),
        ("Start Date", "Start", 0, false),
        ("End Date", "End", 0, false),
        ("End of Life Date", "End", 1, false),
        ("QTY Hardware", "QTY", 0, false),
        ("QTY Software of support (Nodes)", "QTY", 1, false),
        ("Channel SKU", "Channel", 0, false),
        ("Data Center", "Data", 0, true),
        ("Grant #s", "Grant", 0, false),
        ("Latest Serial Number", "Latest", 0, false),
        ("Original Serial Number", "Original", 0, true),
        ("MSRP Per Unit", "MSRP", 0, false),
        ("Total MSRP", "Total", 0, false),
        ("Cost Per Unit", "Cost", 0, false),
        ("Standard Disti Cost per Unit", "Standard", 0, false),
        ("Total Standard Disti Cost", "Total", 1, false),
        ("Final Disti Cost", "Final", 0, false),
        ("Currency", "Currency", 0, true)
    ];

    public string Slug => ParserSlugs.TrellixQuotePdf;
    public string DisplayName => "Quote (PDF)";
    public string Vendor => Vendors.Trellix;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public TrellixQuotePdfParser() : this(PdfWordCollector.CollectWords) { }

    internal TrellixQuotePdfParser(Func<string, IReadOnlyList<PdfWord>> collectWords)
    {
        this.collectWords = collectWords;
    }

    public double Detect(string path)
    {
        try
        {
            var words = CollectWords(path);
            if (!HasReportIdentity(words)) return 0.0;
            var headers = FindTableHeaders(words);
            if (headers.Count == 0) return 0.0;
            BuildColumns(words.Where(word => word.PageIndex == headers[0].PageIndex).ToList(), headers[0]);
            return 0.9;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        var words = CollectWords(path);
        if (!HasReportIdentity(words))
            throw new ParseError("detect", "This is not a Trellix Distributor Quote Report.",
                "Missing Trellix Distributor Quote Report identity.");

        var headers = FindTableHeaders(words);
        if (headers.Count == 0)
            throw new ParseError("detect", "Could not find the Trellix quote table.",
                "Missing Channel SKU / quantity table header.");

        var stream = PdfTableHelpers.WordStreamText(words);
        var currency = Regex.Match(stream, @"\bQuote\s+Currency:\s*([A-Za-z]{3})", RegexOptions.IgnoreCase);
        if (!currency.Success || !currency.Groups[1].Value.Equals("AUD", StringComparison.OrdinalIgnoreCase))
            throw new ParseError("currency",
                $"This Trellix quote declares {(!currency.Success ? "an unknown currency" : currency.Groups[1].Value)}; AUD is required.",
                "Unsupported or unreadable Trellix quote currency.");

        var physicalRows = new List<PdfRow>();
        foreach (var header in headers)
        {
            var pageWords = words.Where(word => word.PageIndex == header.PageIndex).ToList();
            var columns = BuildColumns(pageWords, header);
            var nodes = pageWords.FirstOrDefault(word => word.Text == "(Nodes)"
                && word.X0 >= columns["QTY Software of support (Nodes)"].Left
                && word.X0 < columns["QTY Software of support (Nodes)"].Right
                && word.Top > header.Top && word.Top < header.Top + 60)
                ?? throw new ParseError("detect", "Could not resolve the Trellix table header.",
                    "Missing QTY Software of support (Nodes) heading.");
            var stopTop = pageWords
                .Where(word => word.Top > nodes.Bottom
                    && ((word.Text.StartsWith('*') && word.X0 < 150)
                        || (word.Text == "Distribution" && word.X0 > header.PageWidth / 2)))
                .Select(word => word.Top)
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            physicalRows.AddRange(PdfTableHelpers.RowsBetween(
                pageWords.Where(word => word.Top < stopTop),
                nodes.Bottom, header.PageIndex, columns, "__TRELLIX_NO_STOP__"));
        }

        var groups = PdfTableHelpers.GroupByAnchor(physicalRows, row =>
            PdfTableHelpers.Cell(row.Cells, "MSRP Per Unit").Length > 0
            && (PdfTableHelpers.Cell(row.Cells, "Total MSRP").Length > 0
                || PdfTableHelpers.Cell(row.Cells, "Cost Per Unit").Length > 0));
        if (groups.Count == 0)
            throw new ParseError("extract", "Could not find Trellix quote lines.",
                "No price-bearing product row found in the Trellix table.");

        var items = groups.Select((group, index) => ParseLine(group, index + 1)).ToList();
        var totalMatch = Regex.Match(stream,
            @"\bTotal\s+Distribution\s+Cost:\s*([$\d,.]+)", RegexOptions.IgnoreCase);
        if (!totalMatch.Success)
            throw new ParseError("totals", "Could not locate Total Distribution Cost.",
                "Missing Trellix Total Distribution Cost amount.");
        var total = Money(totalMatch.Groups[1].Value, "Total Distribution Cost", stage: "totals");

        var quoteMatch = Regex.Match(stream, @"\bQuote\s+Number:\s*(Q-\d+)", RegexOptions.IgnoreCase);
        var quoteNumber = quoteMatch.Success ? quoteMatch.Groups[1].Value : string.Empty;
        var (bidNumber, bidRevision) = BidMetadataCleaner.CleanRevisionless(quoteNumber);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
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

    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(
        IReadOnlyList<PdfWord> pageWords, PdfWord header)
    {
        var headerLine = pageWords
            .Where(word => Math.Abs(word.Top - header.Top) <= 1.5 && !string.IsNullOrWhiteSpace(word.Text))
            .OrderBy(word => word.X0)
            .ToList();
        var positions = new List<(string Name, double X0)>();
        foreach (var (name, token, occurrence, optional) in Headings)
        {
            var matches = headerLine.Where(word => word.Text == token).ToList();
            if (matches.Count <= occurrence)
            {
                if (optional) continue;
                throw new ParseError("detect", "Could not resolve the Trellix table columns.",
                    $"Missing {name} heading.");
            }
            positions.Add((name, matches[occurrence].X0));
        }

        if (positions.Zip(positions.Skip(1), (left, right) => left.X0 >= right.X0).Any(invalid => invalid))
            throw new ParseError("detect", "Could not resolve the Trellix table columns.",
                "Trellix table headings are out of order.");

        return positions.Select((position, index) =>
        {
            var left = index == 0 ? 0 : position.X0 - HeaderLeftAllowance;
            var right = index == positions.Count - 1 ? header.PageWidth
                : positions[index + 1].X0 - HeaderLeftAllowance;
            return (position.Name, Range: (left, right));
        }).ToDictionary(pair => pair.Name, pair => pair.Range);
    }

    private static LineItem ParseLine(IReadOnlyList<PdfRow> rows, int sequence)
    {
        string Field(string name) => TextCleaner.JoinSpaced(rows.Select(row => PdfTableHelpers.Cell(row.Cells, name)));
        string Joined(string name) => TextCleaner.JoinUnspaced(rows.Select(row => PdfTableHelpers.Cell(row.Cells, name)));

        var rawSku = Field("Channel SKU");
        var sku = RemoveWhitespace(rawSku);
        if (sku.Length == 0)
            throw new ParseError("extract", $"Line {sequence} has no Channel SKU.",
                "Missing Trellix Channel SKU.");

        var hardware = Quantity(Joined("QTY Hardware"), "QTY Hardware", sequence);
        var software = Quantity(Joined("QTY Software of support (Nodes)"),
            "QTY Software of support (Nodes)", sequence);
        if (hardware > 0 && software > 0)
            throw new ParseError("extract", $"Line {sequence} has both Trellix quantity fields populated.",
                "Both QTY Hardware and QTY Software of support (Nodes) are positive.");
        if (hardware == 0 && software == 0)
            throw new ParseError("extract", $"Line {sequence} has no valid Trellix quantity.",
                "Neither QTY Hardware nor QTY Software of support (Nodes) is positive.");
        var qty = Math.Max(hardware, software);

        var totalMsrp = Money(Joined("Total MSRP"), "Total MSRP", sequence);
        var cost = Money(Joined("Cost Per Unit"), "Cost Per Unit", sequence);
        var commentParts = new[]
        {
            Field("Program Type"),
            Joined("Terms Length"),
            RemoveWhitespace(Field("Grant #s"))
        }.Where(value => value.Length > 0).ToList();
        var serial = Field("Latest Serial Number");

        return new LineItem
        {
            Vpn = sku,
            Description = Field("Product Description"),
            SerialNumber = serial.Length == 0 ? null : serial,
            Cost = cost,
            Qty = qty,
            Msrp = totalMsrp / qty,
            StartDate = Date(Joined("Start Date"), "Start Date", sequence),
            EndDate = Date(Joined("End Date"), "End Date", sequence),
            Comments = commentParts.Count == 0 ? null : string.Join(" | ", commentParts),
            LineSequence = sequence.ToString(CultureInfo.InvariantCulture),
            Raw = PdfTableHelpers.RawDict(rows)
        };
    }

    private List<PdfWord> CollectWords(string path) =>
        collectWords(path).Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();

    private static bool HasReportIdentity(IReadOnlyList<PdfWord> words)
    {
        var stream = PdfTableHelpers.WordStreamText(words);
        return words.Any(word => word.Text.Equals("Trellix", StringComparison.OrdinalIgnoreCase))
            && Regex.IsMatch(stream, @"Distributor\s+Quote\s+Report", RegexOptions.IgnoreCase);
    }

    private static IReadOnlyList<PdfWord> FindTableHeaders(IReadOnlyList<PdfWord> words) => words
        .Where(word => word.Text == "Channel"
            && words.Any(other => other.PageIndex == word.PageIndex && other.Text == "SKU"
                && Math.Abs(other.X0 - word.X0) <= 3 && other.Top > word.Top
                && other.Top < word.Top + 15))
        .OrderBy(word => word.PageIndex)
        .ThenBy(word => word.Top)
        .ToList();

    private static int Quantity(string value, string column, int sequence)
    {
        if (value.Length == 0) return 0;
        try
        {
            var number = DecimalCleaner.Parse(value);
            if (number < 0 || number > int.MaxValue || number != decimal.Truncate(number))
                throw new FormatException();
            return (int)number;
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new ParseError("extract", $"Line {sequence} has an invalid {column} value.",
                $"Invalid Trellix {column} quantity.");
        }
    }

    private static decimal Money(string value, string column, int? sequence = null, string stage = "extract")
    {
        try
        {
            return DecimalCleaner.Parse(value);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new ParseError(stage,
                $"Could not read Trellix {column}{(sequence is null ? string.Empty : $" on line {sequence}")}.",
                $"Missing or invalid Trellix {column} amount.");
        }
    }

    private static DateOnly? Date(string value, string column, int sequence)
    {
        if (value.Length == 0) return null;
        if (DateOnly.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)) return date;
        throw new ParseError("extract", $"Could not read Trellix {column} on line {sequence}.",
            $"Invalid Trellix {column} date.");
    }

    private static string RemoveWhitespace(string value) => Regex.Replace(value, @"\s+", string.Empty);
}

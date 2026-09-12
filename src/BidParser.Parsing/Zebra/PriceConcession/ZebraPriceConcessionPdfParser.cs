using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Zebra.PriceConcession;

/// <summary>
/// Parses Zebra PartnerConnect "Price Concession" PDF letters.
///
/// Layout:
///   • A details block (Account, Reseller, Currency, dates, etc.) followed by
///   • A "Price Concession Items" table with 10 columns:
///     Part No. | Description | Min. First Order Only | Min. Qty | Max. Qty
///     | List Price | Standard Discount % | Total Discount % | Unit Special Price | Cancelled
///
/// Extraction challenges:
///   • Descriptions wrap across multiple lines (leading and trailing continuation rows).
///   • A description fragment can appear on the PREVIOUS page before the Part No. row
///     (page-break split); it is buffered and prepended when the Part No. row arrives.
///   • PdfPig's NearestNeighbour extractor can fuse the List Price with the Standard
///     Discount % into a single word (e.g. "1,830.2471.43"). The extractor uses a
///     first-match regex to recover the list price.
/// </summary>
public sealed partial class ZebraPriceConcessionPdfParser : IParser
{
    public string Slug => ParserSlugs.ZebraPcrPdf;
    public string DisplayName => "PCR (PDF)";
    public string Vendor => Vendors.Zebra;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsOnCost => true;

    public double Detect(string path)
    {
        try
        {
            var words = PdfWordCollector.CollectWords(path)
                .Where(w => w.Text.Trim().Length > 0)
                .ToList();
            var hasPci = PdfTableHelpers.FindSequence(words, ["Price", "Concession", "Items"]) is not null;
            var hasCurrency = words.Any(w => w.Text == "AUD" || w.Text == "USD");
            return hasPci ? (hasCurrency ? 0.85 : 0.75) : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        // PdfPig can emit single-space words from styled table cells in Zebra PDFs.
        // Filter them out so sequence-search and column bucketing are not disrupted.
        var words = PdfWordCollector.CollectWords(path)
            .Where(w => w.Text.Trim().Length > 0)
            .ToList();

        // ── 1. Locate the "Price Concession Items" section anchor ───────────────
        var sectionIdx = PdfTableHelpers.FindSequence(words, ["Price", "Concession", "Items"])
            ?? throw new ParseError(
                "detect",
                "Could not find the 'Price Concession Items' section.",
                "Missing 'Price Concession Items' anchor.");

        // ── 2. Find the table header row (the "Part" of "Part No.") ────────────
        var headerWord = FindPartNoHeader(words, sectionIdx)
            ?? throw new ParseError(
                "detect",
                "Could not find the 'Part No.' table header.",
                "Missing 'Part No.' header row.");

        // ── 3. Extract currency from details block (above the header) ──────────
        var currency = ExtractCurrency(words, sectionIdx);

        // ── 4. Extract PCR quote number ────────────────────────────────────────
        var quoteNumber = ExtractQuoteNumber(words, path);

        // ── 5. Build column X-ranges from the header band ─────────────────────
        var columns = BuildColumns(words, headerWord);

        // ── 6. Collect rows from just below the header to the stop token ───────
        // Stop at "Concession:" which is the first distinctive word of
        // "This Price Concession:" that follows the items table.
        var rows = PdfTableHelpers.RowsBetween(
            words, headerWord.Top, headerWord.PageIndex, columns, stopToken: "Concession:");

        // ── 7. Classify and merge rows into normalised item rows ───────────────
        var itemRows = MergeRows(rows);

        // ── 8. Delegate to shared extractor ───────────────────────────────────
        return ZebraPriceConcessionExtractor.Build(
            itemRows,
            currency,
            quoteNumber,
            PdfTableHelpers.WordStreamText(words),
            Path.GetFileName(path),
            Slug);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Header location

    private static PdfWord? FindPartNoHeader(IReadOnlyList<PdfWord> words, int startIndex)
    {
        // Look for "Part" followed within 8 words by "No." on the same page at a
        // similar Y (within 4pt) — avoids "Part" in body text that has no "No." nearby.
        for (var i = startIndex; i < words.Count - 1; i++)
        {
            if (words[i].Text != "Part") continue;
            var part = words[i];
            for (var j = i + 1; j < Math.Min(i + 8, words.Count); j++)
            {
                var candidate = words[j];
                if (candidate.PageIndex != part.PageIndex) break;
                if (candidate.Text == "No."
                    && Math.Abs(candidate.Top - part.Top) <= 4
                    && candidate.X0 > part.X0)
                {
                    return part;
                }
            }
        }
        return null;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Column building

    /// <summary>The eight data columns, left to right. "~" columns exist only to bound their neighbours.</summary>
    private static readonly string[] DataColumns =
    [
        "~MinFirst", "Min. Qty", "Max. Qty", "List Price", "~StdDisc", "~TotalDisc",
        "Unit Special Price", "Cancelled",
    ];

    /// <summary>
    /// Columns the extractor actually reads. An unresolved one would not fail loudly — a missing
    /// column key reads as an empty cell, which is a zero price or an unset cancelled flag — so the
    /// grid is rejected outright instead.
    /// </summary>
    private static readonly string[] RequiredColumns =
    [
        "Min. Qty", "Max. Qty", "List Price", "Unit Special Price", "Cancelled",
    ];

    /// <summary>
    /// Identifies each column's left boundary from unfused header anchors and the repeated body grid.
    /// Zebra PDFs can fuse adjacent header cells, so document geometry rather than sample coordinates
    /// supplies any header boundary which is not independently readable.
    /// </summary>
    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(
        IReadOnlyList<PdfWord> words, PdfWord headerAnchor)
    {
        // Collect words in the header's Y-band on the same page.
        var band = words
            .Where(w => w.PageIndex == headerAnchor.PageIndex
                && w.Top >= headerAnchor.Top - 15
                && w.Top <= headerAnchor.Top + 28)
            .ToList();

        double? Anchor(string text, double? maxX = null)
        {
            return band
                .Where(w => w.Text == text && (maxX is null || w.X0 <= maxX))
                .Select(w => (double?)w.X0)
                .FirstOrDefault();
        }

        var partX0 = Anchor("Part", maxX: 100)
            ?? throw new ParseError("detect", "Could not locate the Part No. column.", "Missing Part header anchor.");
        var descX0 = Anchor("Description")
            ?? throw new ParseError("detect", "Could not locate the Description column.", "Missing Description header anchor.");

        // "Min." appears twice in the header: once for "Min. First Order Only" and
        // once for "Min. Qty". Sort by X and take positionally.
        var minWords = band
            .Where(w => w.Text == "Min.")
            .OrderBy(w => w.X0)
            .ToList();
        var headerAnchors = new Dictionary<string, double?>
        {
            ["~MinFirst"] = minWords.Count > 0 ? minWords[0].X0 : null,
            ["Min. Qty"] = minWords.Count > 1 ? minWords[1].X0 : null,
            ["Max. Qty"] = Anchor("Max."),
            ["List Price"] = Anchor("List"),
            ["~StdDisc"] = Anchor("Standard"),
            ["~TotalDisc"] = Anchor("Total"),
            ["Unit Special Price"] = Anchor("Unit"),
            ["Cancelled"] = Anchor("Cancelled"),
        };

        var body = TableBodyWords(words, headerAnchor);
        var gridEdges = FindGridEdges(body, band, partX0, descX0);

        var pins = new Dictionary<string, double>();
        PinFromHeader(pins, headerAnchors, gridEdges);
        PinFromContent(pins, gridEdges, body);
        FillBetweenPins(pins, gridEdges);
        ValidateGrid(pins);

        var headers = new List<(string Name, double X0)> { ("Part No.", partX0), ("Description", descX0) };
        headers.AddRange(DataColumns
            .Where(pins.ContainsKey)
            .Select(name => (name, pins[name])));
        return PdfTableHelpers.ColumnRanges(headers, headerAnchor.PageWidth);
    }

    /// <summary>
    /// The candidate column left edges, from three independent sources so that no single PdfPig
    /// artifact can hide a column: the header cells (whose wrapped second line — "Qty", "Price" —
    /// still sits at its own column's X even when the first line fuses with its neighbours), every
    /// Y/N flag word (unambiguous, so one occurrence is enough), and numeric body words whose left
    /// edge repeats down the table. Numbers need the repetition test because a description can
    /// contain one; a one-item table has nothing to repeat, which is why the other two sources exist.
    /// </summary>
    private static List<double> FindGridEdges(
        IReadOnlyList<PdfWord> body, IReadOnlyList<PdfWord> band, double partX0, double descX0)
    {
        var itemRowCount = body.Count(word => word.X0 >= partX0 - 3.0 && word.X0 < descX0);
        var minimum = Math.Max(2, itemRowCount / 2);

        var dataWords = body.Where(word => word.X0 > descX0 + 1).ToList();
        var numericEdges = dataWords
            .Where(word => IsNumeric(word.Text))
            .GroupBy(word => Math.Round(word.X0, 1))
            .Where(group => group.Count() >= minimum)
            .Select(group => group.Min(word => word.X0));
        var flagEdges = dataWords.Where(word => IsFlag(word.Text)).Select(word => word.X0);
        var headerEdges = band.Where(word => word.X0 > descX0 + 1).Select(word => word.X0);

        return Merge(numericEdges.Concat(flagEdges).Concat(headerEdges));
    }

    /// <summary>Collapses edges within 2pt of each other — the same column, measured off different glyphs.</summary>
    private static List<double> Merge(IEnumerable<double> edges)
    {
        return edges.Order().Aggregate(new List<double>(), (merged, edge) =>
        {
            if (merged.Count == 0 || edge - merged[^1] > 2.0) merged.Add(edge);
            return merged;
        });
    }

    /// <summary>Ties each column whose header word survived unfused to the grid edge under it.</summary>
    private static void PinFromHeader(
        Dictionary<string, double> pins, IReadOnlyDictionary<string, double?> anchors, IReadOnlyList<double> edges)
    {
        var used = new HashSet<double>();
        foreach (var name in DataColumns)
        {
            if (anchors.GetValueOrDefault(name) is not { } anchor) continue;

            var match = edges
                .Where(edge => !used.Contains(edge) && Math.Abs(edge - anchor) <= 2.5)
                .Cast<double?>()
                .FirstOrDefault();
            if (match is null) continue;

            pins[name] = match.Value;
            used.Add(match.Value);
        }
    }

    /// <summary>
    /// Pins the columns identifiable by what they hold rather than by their header: the table opens
    /// and closes on a Y/N flag column, and the Unit Special Price is the column immediately left of
    /// the closing flag. This is what carries a document whose header cells all fused together.
    /// </summary>
    private static void PinFromContent(
        Dictionary<string, double> pins, List<double> edges, IReadOnlyList<PdfWord> body)
    {
        if (edges.Count == 0) return;

        var flagEdges = edges.Where(edge => IsFlagEdge(edge, body)).ToList();
        if (flagEdges.Count > 0)
        {
            // Only the outermost edges can be the flag columns; a Y/N-looking column anywhere else
            // is not one, so it must not be allowed to claim an end.
            if (!pins.ContainsKey("~MinFirst") && flagEdges[0] == edges[0]) pins["~MinFirst"] = edges[0];
            if (!pins.ContainsKey("Cancelled") && flagEdges[^1] == edges[^1] && edges.Count > 1)
            {
                pins["Cancelled"] = edges[^1];
            }
        }

        if (pins.ContainsKey("Unit Special Price") || !pins.TryGetValue("Cancelled", out var cancelled)) return;

        var index = edges.IndexOf(cancelled);
        if (index > 0 && !pins.ContainsValue(edges[index - 1])) pins["Unit Special Price"] = edges[index - 1];
    }

    /// <summary>
    /// Assigns the still-unpinned columns to the still-unused edges, one gap between pins at a time.
    /// Filling per gap rather than zipping the two lists end to end keeps a missing edge contained:
    /// the List Price and Standard Discount % columns fuse into a single word in some documents, so
    /// that column contributes no edge, and a global zip would shift every later column left by one.
    /// </summary>
    private static void FillBetweenPins(Dictionary<string, double> pins, List<double> edges)
    {
        var checkpoints = new List<(int Column, int Edge)> { (-1, -1) };
        checkpoints.AddRange(DataColumns
            .Select((name, column) => (Name: name, Column: column))
            .Where(pair => pins.ContainsKey(pair.Name))
            .Select(pair => (pair.Column, Edge: edges.IndexOf(pins[pair.Name]))));
        checkpoints.Add((DataColumns.Length, edges.Count));

        foreach (var (start, end) in checkpoints.Zip(checkpoints.Skip(1)))
        {
            var column = start.Column + 1;
            for (var edge = start.Edge + 1; edge < end.Edge && column < end.Column; edge++, column++)
            {
                pins[DataColumns[column]] = edges[edge];
            }
        }
    }

    /// <summary>Rejects a grid that is incomplete or out of order, rather than parsing empty cells.</summary>
    private static void ValidateGrid(IReadOnlyDictionary<string, double> pins)
    {
        var resolved = DataColumns.Where(pins.ContainsKey).Select(name => pins[name]).ToList();
        var ascending = resolved.Zip(resolved.Skip(1)).All(pair => pair.Second > pair.First);

        if (pins.ContainsKey("~MinFirst") && ascending && RequiredColumns.All(pins.ContainsKey)) return;

        throw new ParseError(
            "detect",
            "Could not determine the Price Concession table's column positions.",
            "Unrecognisable Zebra Price Concession table geometry.");
    }

    private static bool IsFlag(string text) => text.Trim() is "Y" or "N";

    /// <summary>
    /// Whether a token opens like a number. Deliberately loose: PdfPig fuses a price with the
    /// discount % beside it ("1,830.2471.43"), and that word still marks its column's left edge.
    /// </summary>
    private static bool IsNumeric(string text)
    {
        var trimmed = text.AsSpan().TrimStart(['$', '(']);
        return trimmed.Length > 0 && char.IsAsciiDigit(trimmed[0]);
    }

    private static bool IsFlagEdge(double edge, IEnumerable<PdfWord> words)
    {
        var values = words.Where(word => Math.Abs(word.X0 - edge) <= 2.0)
            .Select(word => word.Text.Trim())
            .ToList();
        return values.Count > 0 && values.TrueForAll(IsFlag);
    }

    /// <summary>
    /// The words below the header band up to the table's closing text. Ordered like
    /// <see cref="PdfTableHelpers.RowsBetween"/> does it — by page, then row, then X — so the stop
    /// token is found at its visual position rather than wherever PdfPig happened to emit it.
    /// </summary>
    private static IReadOnlyList<PdfWord> TableBodyWords(IReadOnlyList<PdfWord> words, PdfWord header)
    {
        var body = words
            .Where(word => word.PageIndex > header.PageIndex
                || (word.PageIndex == header.PageIndex && word.Top > header.Top + 28.0))
            .OrderBy(word => word.PageIndex)
            .ThenBy(word => (word.Top + word.Bottom) / 2.0)
            .ThenBy(word => word.X0)
            .ToList();

        var stopIndex = body.FindIndex(word => word.Text == "Concession:");
        return stopIndex >= 0 ? body[..stopIndex] : body;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Row merging

    /// <summary>
    /// Converts the raw <see cref="PdfRow"/> sequence into a flat list of
    /// <see cref="ZebraPriceConcessionExtractor.ItemRow"/>, attaching every
    /// description continuation line to the item it belongs to.
    ///
    /// Zebra renders each item as a block whose Part No. is **vertically centred**
    /// on the description, so a three-line description puts one line ABOVE the
    /// Part No. row and one BELOW it. <see cref="RowBlocks"/> resolves which item
    /// each description-only row belongs to; fragments are re-joined in reading
    /// order, so a leading line lands at the front of the description regardless
    /// of which page it was found on.
    /// </summary>
    private static IReadOnlyList<ZebraPriceConcessionExtractor.ItemRow> MergeRows(
        IReadOnlyList<PdfRow> rows)
    {
        var partIndexes = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (C(rows[i], "Part No.").Length > 0) partIndexes.Add(i);
        }

        if (partIndexes.Count == 0) return [];

        var blocks = new RowBlocks(rows, partIndexes);

        // Description fragments per item, keyed by row index so they re-join in reading order.
        var fragments = partIndexes.ToDictionary(index => index, _ => new List<(int Index, string Text)>());

        for (var i = 0; i < rows.Count; i++)
        {
            var desc = C(rows[i], "Description");
            if (desc.Length == 0) continue;

            var owner = C(rows[i], "Part No.").Length > 0 ? i : blocks.OwnerOf(i);
            if (owner is not null) fragments[owner.Value].Add((i, desc));
        }

        return partIndexes
            .Select(index =>
            {
                var row = rows[index];
                var description = TextCleaner.Clean(string.Join(
                    ' ',
                    fragments[index].OrderBy(fragment => fragment.Index).Select(fragment => fragment.Text)));

                return new ZebraPriceConcessionExtractor.ItemRow(
                    C(row, "Part No."),
                    description,
                    C(row, "Min. Qty"), C(row, "Max. Qty"),
                    C(row, "List Price"), C(row, "Unit Special Price"),
                    C(row, "Cancelled"));
            })
            .ToList();
    }

    private static string C(PdfRow row, string key)
        => row.Cells.TryGetValue(key, out var value) ? TextCleaner.Clean(value) : string.Empty;

    // ────────────────────────────────────────────────────────────────────────────
    // Metadata extraction

    private static string ExtractCurrency(IReadOnlyList<PdfWord> words, int beforeIndex)
    {
        for (var i = 0; i < Math.Min(beforeIndex + 20, words.Count); i++)
        {
            if (!string.Equals(words[i].Text, "Currency", StringComparison.Ordinal)) continue;
            // Currency code is within a few words of "Currency" label
            for (var j = i + 1; j < Math.Min(i + 6, words.Count); j++)
            {
                var t = words[j].Text;
                if (t is "AUD" or "USD" or "EUR" or "GBP" or "NZD") return t;
            }
        }
        return "AUD";
    }

    private static string ExtractQuoteNumber(IReadOnlyList<PdfWord> words, string path)
    {
        // PdfPig renders "PC Request ID #:81391641,Revision #:2.0" — the number may appear
        // as a word starting with "#:" immediately followed by digits.
        foreach (var word in words)
        {
            var m = PcrIdPattern().Match(word.Text);
            if (m.Success) return m.Groups[1].Value;
        }

        return ZebraPriceConcessionExtractor.QuoteNumberFromFilename(path);
    }

    [GeneratedRegex(@"^#:(\d{7,})")]
    private static partial Regex PcrIdPattern();
}

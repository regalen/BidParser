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
    public string Slug => ParserSlugs.ZebraPriceConcessionPdf;
    public string DisplayName => "Price Concession (PDF)";
    public string Vendor => Vendors.Zebra;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

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

    /// <summary>
    /// Identifies each column's left boundary by locating anchor words in the
    /// header band (±28pt around the "Part" header word). Hardcoded fallbacks
    /// match the measured positions from the three sample PDFs (page width 864pt).
    /// </summary>
    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildColumns(
        IReadOnlyList<PdfWord> words, PdfWord headerAnchor)
    {
        // Collect words in the header's Y-band on the same page.
        var band = words
            .Where(w => w.PageIndex == headerAnchor.PageIndex
                && w.Top >= headerAnchor.Top - 15
                && w.Top <= headerAnchor.Top + 28)
            .ToList();

        double Anchor(string text, double fallback, double? maxX = null)
        {
            var match = band
                .Where(w => w.Text == text && (maxX is null || w.X0 <= maxX))
                .Select(w => (double?)w.X0)
                .FirstOrDefault();
            return match ?? fallback;
        }

        var partX0 = Anchor("Part", 47.0, maxX: 100);
        var descX0 = Anchor("Description", 151.0);

        // "Min." appears twice in the header: once for "Min. First Order Only" and
        // once for "Min. Qty". Sort by X and take positionally.
        var minWords = band
            .Where(w => w.Text == "Min.")
            .OrderBy(w => w.X0)
            .ToList();
        var minFirstX0 = minWords.Count >= 1 ? minWords[0].X0 : 527.0;
        var minQtyX0   = minWords.Count >= 2 ? minWords[1].X0 : 575.0;

        var maxX0       = Anchor("Max.",      595.0);
        var listX0      = Anchor("List",      617.0);
        var standardX0  = Anchor("Standard",  653.0);
        var totalX0     = Anchor("Total",     698.0);
        var unitX0      = Anchor("Unit",      735.0);
        var cancelledX0 = Anchor("Cancelled", 773.0);

        var headers = new List<(string Name, double X0)>
        {
            ("Part No.",          partX0),
            ("Description",       descX0),
            ("~MinFirst",         minFirstX0),   // boundary only; suppresses Y/N bleed into Description
            ("Min. Qty",          minQtyX0),
            ("Max. Qty",          maxX0),
            ("List Price",        listX0),
            ("~StdDisc",          standardX0),   // boundary only
            ("~TotalDisc",        totalX0),      // boundary only
            ("Unit Special Price", unitX0),
            ("Cancelled",         cancelledX0),
        };

        return PdfTableHelpers.ColumnRanges(headers, headerAnchor.PageWidth);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Row merging

    /// <summary>
    /// Converts the raw <see cref="PdfRow"/> sequence into a flat list of
    /// <see cref="ZebraPriceConcessionExtractor.ItemRow"/>, merging description
    /// continuation lines (including cross-page fragments that appear BEFORE their
    /// Part No. row in Y-order) into a single coherent row per item.
    ///
    /// Classification heuristic for description-only rows
    /// (those between two Part No. rows, where intent is ambiguous):
    ///   • If the row is on the SAME PAGE as the last committed content AND the
    ///     Y gap is ≤ 9 pt → TRAILING continuation of the current item.
    ///     (Within-item continuation lines are ~6–7 pt apart; 9 pt gives headroom.)
    ///   • Otherwise (large gap, or cross-page) → LEADING description buffered for
    ///     the next item.
    ///
    /// This handles:
    ///   (a) Normal one- or two-line trailing descriptions (close to Part No. row).
    ///   (b) Cross-page page-break splits (description fragment at bottom of page N,
    ///       Part No. on page N+1 — gap from prior content ≫ 9 pt → buffer correctly).
    ///   (c) Same-page leading descriptions where a description-only row sits just
    ///       above the next Part No. row rather than trailing the current one.
    /// </summary>
    private static IReadOnlyList<ZebraPriceConcessionExtractor.ItemRow> MergeRows(
        IReadOnlyList<PdfRow> rows)
    {
        var result = new List<ZebraPriceConcessionExtractor.ItemRow>();
        var bufferedDesc = new List<string>(); // leading fragments for the next item
        CurrentItem? current = null;

        // Track Y-position of the last content we appended to the current item so
        // we can detect the gap to the next description-only row.
        double lastContentTop = double.MinValue;
        int lastContentPage = -1;

        foreach (var row in rows)
        {
            var partNo = C(row, "Part No.");
            var desc = C(row, "Description");

            if (partNo.Length > 0)
            {
                // ── Item row: emit previous item, open a new one ─────────────
                if (current is not null) result.Add(current.Build());

                current = new CurrentItem(
                    partNo,
                    C(row, "Min. Qty"), C(row, "Max. Qty"),
                    C(row, "List Price"), C(row, "Unit Special Price"),
                    C(row, "Cancelled"));

                // Prepend any description fragments buffered before this Part No.
                foreach (var d in bufferedDesc) current.AddLeadingDesc(d);
                bufferedDesc.Clear();

                if (desc.Length > 0) current.AppendDesc(desc);

                // Reset trailing-content tracker to the Part No. row itself.
                lastContentTop = row.Top;
                lastContentPage = row.PageIndex;
            }
            else if (desc.Length > 0)
            {
                // ── Description-only row: trailing continuation or leading fragment ─
                var samePageGap = (current is not null && row.PageIndex == lastContentPage)
                    ? row.Top - lastContentTop
                    : double.MaxValue;

                if (samePageGap <= 9.0)
                {
                    // Close gap on the same page → trailing continuation of current item.
                    current!.AppendDesc(desc);
                    lastContentTop = row.Top;
                    // lastContentPage stays the same
                }
                else
                {
                    // Large gap or cross-page → leading fragment for the next item.
                    bufferedDesc.Add(desc);
                }
            }
            // else: noise — ignore
        }

        if (current is not null) result.Add(current.Build());

        return result;
    }

    private static string C(PdfRow row, string key)
        => row.Cells.TryGetValue(key, out var value) ? TextCleaner.Clean(value) : string.Empty;

    // ────────────────────────────────────────────────────────────────────────────
    // Mutable accumulator for multi-line items

    private sealed class CurrentItem(
        string partNo, string minQty, string maxQty,
        string listPrice, string unitPrice, string cancelled)
    {
        private readonly List<string> _descParts = [];
        private readonly List<string> _leadingParts = [];

        public void AddLeadingDesc(string text) => _leadingParts.Add(text);
        public void AppendDesc(string text) => _descParts.Add(text);

        public ZebraPriceConcessionExtractor.ItemRow Build()
        {
            var allParts = _leadingParts.Concat(_descParts).Where(d => d.Length > 0);
            var description = TextCleaner.Clean(string.Join(" ", allParts));
            return new ZebraPriceConcessionExtractor.ItemRow(
                partNo, description, minQty, maxQty, listPrice, unitPrice, cancelled);
        }
    }

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

    [GeneratedRegex(@"^#:(\d+)")]
    private static partial Regex PcrIdPattern();
}

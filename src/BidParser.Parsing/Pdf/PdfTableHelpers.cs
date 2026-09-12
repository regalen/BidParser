using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;

namespace BidParser.Parsing.Pdf;

/// <summary>
/// Anchor-based helpers for reconstructing tables from a flat stream of positioned PDF words:
/// locate header token sequences, bucket words into rows by vertical position and into columns by
/// X-range (<see cref="RowsBetween"/> / <see cref="ColumnRanges"/>), and extract the quote total.
/// <see cref="FuseCurrencyTokens"/> pre-joins split "USD 1,234" amounts. Everything keys off
/// coordinates, never fixed offsets.
/// </summary>
public static partial class PdfTableHelpers
{
    private const double RowTolerance = 3.5;

    /// <summary>Flattens a word stream back to space-joined text (e.g. for signature matching).</summary>
    public static string WordStreamText(IEnumerable<PdfWord> words)
    {
        return string.Join(' ', words.Select(word => word.Text));
    }

    public static int? FindSequence(IReadOnlyList<PdfWord> words, IReadOnlyList<string> sequence, int startIndex = 0)
    {
        for (var i = startIndex; i <= words.Count - sequence.Count; i++)
        {
            var matches = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (!string.Equals(words[i + j].Text, sequence[j], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return i;
            }
        }

        return null;
    }

    public static int? FindProductCodeHeader(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count - 1; i++)
        {
            var first = words[i];
            var second = words[i + 1];
            if (first.Text == "Product"
                && second.Text == "Code"
                && first.PageIndex == second.PageIndex
                && Math.Abs(first.Top - second.Top) <= 3
                && second.X0 > first.X0)
            {
                return i;
            }
        }

        return null;
    }

    public static int? FindRenewalHeader(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text == "No")
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Reconstructs table rows from the words that follow the header (after <paramref name="startTop"/>
    /// on <paramref name="startPage"/>, across page breaks) up to <paramref name="stopToken"/>. Words are
    /// grouped into rows by vertical proximity (±3.5pt), then each row's words are bucketed into the
    /// supplied column X-ranges. Returns rows ordered top-to-bottom.
    ///
    /// Proximity is measured from the baseline for ordinary horizontal text. PdfPig's glyph-tight boxes
    /// can vary by glyph (and collapse to zero height in Type 3 fonts), whereas a baseline represents
    /// where the text was placed. Rotated text and synthetic words fall back to their tight-box midline.
    /// </summary>
    public static IReadOnlyList<PdfRow> RowsBetween(
        IEnumerable<PdfWord> words,
        double startTop,
        int startPage,
        IReadOnlyDictionary<string, (double Left, double Right)> columns,
        string stopToken = "TOTAL:")
    {
        var bodyWords = RemovePageFooters(words
            .Where(word => word.PageIndex > startPage || (word.PageIndex == startPage && word.Top > startTop))
            .OrderBy(word => word.PageIndex)
            .ThenBy(RowCoordinate)
            .ThenBy(word => word.X0)
            .ToList())
            .ToList();

        var stopIndex = bodyWords.FindIndex(word => word.Text == stopToken);
        if (stopIndex >= 0)
        {
            var stopWord = bodyWords[stopIndex];
            var stopCoordinate = RowCoordinate(stopWord);
            bodyWords = bodyWords
                .Where(word => word.PageIndex < stopWord.PageIndex
                    || (word.PageIndex == stopWord.PageIndex
                        && RowCoordinate(word) < stopCoordinate - RowTolerance))
                .ToList();
        }

        var rows = new List<List<PdfWord>>();
        foreach (var word in bodyWords)
        {
            var row = rows.FirstOrDefault(existing =>
                existing[0].PageIndex == word.PageIndex
                && Math.Abs(RowCoordinate(existing[0]) - RowCoordinate(word)) <= RowTolerance);

            if (row is null)
            {
                rows.Add([word]);
            }
            else
            {
                row.Add(word);
            }
        }

        AbsorbShortGlyphRows(rows);

        // The boundaries are the same for every row and column, and splitting a word is independent
        // of which column it lands in, so both are computed once rather than per cell.
        var boundaries = columns.Values.Select(range => range.Left).Distinct().Order().ToList();

        return rows
            .OrderBy(row => row[0].PageIndex)
            .ThenBy(row => RowCoordinate(row[0]))
            .Select(row =>
            {
                var fragments = row.SelectMany(word => SplitAtColumnBoundaries(word, boundaries)).ToList();
                var cells = columns.ToDictionary(
                    pair => pair.Key,
                    pair => TextCleaner.Clean(string.Join(' ', fragments
                        .Where(word => word.X0 >= pair.Value.Left && word.X0 < pair.Value.Right)
                        .OrderBy(word => word.X0)
                        .Select(word => word.Text))));
                return new PdfRow(row[0].PageIndex, row.Min(word => word.Top), RowCoordinate(row[0]), cells);
            })
            .ToList();
    }

    /// <summary>
    /// Splits a PdfPig word only where a known table boundary falls in one of its letter gaps.
    /// Letter geometry, rather than a global kerning threshold, makes this safe for fused cells.
    /// </summary>
    private static IEnumerable<PdfWord> SplitAtColumnBoundaries(PdfWord word, IReadOnlyList<double> allBoundaries)
    {
        var candidateBoundaries = allBoundaries
            .Where(boundary => word.X0 < boundary && boundary < word.X1)
            .ToList();
        if (candidateBoundaries.Count == 0 || word.Letters is not { Count: > 1 }) return [word];

        // A boundary inside a glyph is not a valid split point. Only cut actual letter gaps.
        var boundaries = candidateBoundaries
            .Where(boundary => word.Letters.Zip(word.Letters.Skip(1), (left, right) => (left, right))
                .Any(pair => pair.left.X1 <= boundary && pair.right.X0 >= boundary))
            .ToList();
        if (boundaries.Count == 0) return [word];

        var fragments = word.Letters
            .GroupBy(letter => boundaries.Count(boundary => letter.X1 > boundary))
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .Where(letters => letters.Count > 0)
            .Select(letters => new PdfWord(
                string.Concat(letters.Select(letter => letter.Value)),
                letters.Min(letter => letter.X0), letters.Max(letter => letter.X1),
                word.Top, word.Bottom, word.PageIndex, word.PageWidth, letters, word.PageHeight, word.LineY,
                word.PageUsesBaselineGeometry))
            .ToList();

        return fragments;
    }

    private static double Midline(PdfWord word) => (word.Top + word.Bottom) / 2.0;

    private static double RowCoordinate(PdfWord word) => word.LineY ?? Midline(word);

    /// <summary>A word this much shorter than the table's typical word cannot carry a line of its own.</summary>
    private const double ShortGlyphHeightRatio = 0.6;

    /// <summary>
    /// Folds rows built entirely from short glyphs back into the nearest real line.
    ///
    /// Some fonts report a bounding box for a lone hyphen that sits *below* the line it belongs to and
    /// does not overlap it at all — ~6pt adrift, further than a whole line pitch in a Zebra table — so
    /// no midline tolerance wide enough to catch it is safe. Such a word instead lands in a row by
    /// itself, and a row holding nothing but glyphs too short to be a line of text is that artifact
    /// rather than real content: a genuine one-character cell ("1", "N", "-") still carries a
    /// full-height word somewhere on its line. Merging is bounded by half the table's own line pitch,
    /// so the repair scales with the document instead of assuming a font size.
    /// </summary>
    private static void AbsorbShortGlyphRows(List<List<PdfWord>> rows)
    {
        if (rows.Count < 2) return;

        var baselinePages = rows
            .SelectMany(row => row)
            .Where(word => word.PageUsesBaselineGeometry)
            .Select(word => word.PageIndex)
            .ToHashSet();
        var heights = rows
            .SelectMany(row => row)
            .Where(word => !baselinePages.Contains(word.PageIndex))
            .Select(word => word.Bottom - word.Top)
            .Order()
            .ToList();
        if (heights.Count == 0)
        {
            // Every page already uses the collector's stable baseline geometry, so no tight-box
            // repair is applicable.
            return;
        }
        var maxShortGlyphHeight = heights[heights.Count / 2] * ShortGlyphHeightRatio;

        bool IsShortGlyphRow(List<PdfWord> row) =>
            !baselinePages.Contains(row[0].PageIndex)
            && row.TrueForAll(word => word.Bottom - word.Top <= maxShortGlyphHeight);

        var orphans = rows.Where(IsShortGlyphRow).ToList();
        if (orphans.Count == 0) return;

        // Line pitch is measured over real lines only — the orphans sit between them and would
        // otherwise halve it.
        var realRows = rows
            .Where(row => !IsShortGlyphRow(row))
            .OrderBy(row => row[0].PageIndex)
            .ThenBy(row => RowCoordinate(row[0]))
            .ToList();

        if (realRows.Count < 2) return;

        var pitches = realRows
            .Zip(realRows.Skip(1), (first, second) => (first, second))
            .Where(pair => pair.first[0].PageIndex == pair.second[0].PageIndex)
            .Where(pair => !baselinePages.Contains(pair.first[0].PageIndex))
            .Select(pair => RowCoordinate(pair.second[0]) - RowCoordinate(pair.first[0]))
            .Where(pitch => pitch > 0)
            .Order()
            .ToList();

        if (pitches.Count == 0) return;
        var maxDistance = pitches[pitches.Count / 2] / 2.0;

        foreach (var orphan in orphans)
        {
            var target = realRows
                .Where(row => row[0].PageIndex == orphan[0].PageIndex)
                .Select(row => (Row: row, Distance: Math.Abs(RowCoordinate(row[0]) - RowCoordinate(orphan[0]))))
                .Where(candidate => candidate.Distance <= maxDistance)
                .OrderBy(candidate => candidate.Distance)
                .Select(candidate => candidate.Row)
                .FirstOrDefault();

            if (target is null) continue;

            target.AddRange(orphan);
            rows.Remove(orphan);
        }
    }

    /// <summary>
    /// Removes complete <c>Page N of M</c> footers before table rows are bucketed or words are split.
    /// Tokens must share a visual line on one page and sit at an edge, so table prose containing the
    /// same words remains available to parsers.
    /// </summary>
    internal static IReadOnlyList<PdfWord> RemovePageFooters(IReadOnlyList<PdfWord> words)
    {
        var footers = new HashSet<PdfWord>();
        foreach (var page in words.GroupBy(word => word.PageIndex))
        {
            var lines = new List<List<PdfWord>>();
            foreach (var word in page.OrderBy(RowCoordinate).ThenBy(word => word.X0))
            {
                var line = lines.FirstOrDefault(existing => Math.Abs(RowCoordinate(existing[0]) - RowCoordinate(word)) <= RowTolerance);
                if (line is null) lines.Add([word]); else line.Add(word);
            }

            foreach (var line in lines)
            {
                var ordered = line.OrderBy(word => word.X0).ToList();
                var contentTokens = ordered
                    .Select((word, index) => (Word: word, Index: index))
                    .Where(token => !string.IsNullOrWhiteSpace(token.Word.Text))
                    .ToList();
                for (var i = 0; i + 3 < contentTokens.Count; i++)
                {
                    if (string.Equals(contentTokens[i].Word.Text, "Page", StringComparison.OrdinalIgnoreCase)
                        && IsAllDigits(contentTokens[i + 1].Word.Text)
                        && string.Equals(contentTokens[i + 2].Word.Text, "of", StringComparison.OrdinalIgnoreCase)
                        && IsAllDigits(contentTokens[i + 3].Word.Text)
                        && IsNearPageEdge(contentTokens[i].Word))
                    {
                        // The page counter commonly shares a line with a vendor name. The whole
                        // visual line is footer material; retaining its prefix would append the
                        // vendor name to the final logical table row.
                        // `line` already uses the same measured 3.5pt tolerance as table-row
                        // reconstruction. Strike's mixed-font name/counter differs by only 0.38pt;
                        // removing this resolved visual line avoids a broader page-wide Y window.
                        footers.UnionWith(line);
                    }
                }
            }
        }

        return footers.Count == 0 ? words : words.Where(word => !footers.Contains(word)).ToList();
    }

    private static bool IsNearPageEdge(PdfWord word)
        => word.PageHeight is { } pageHeight
            && (word.Top <= 36 || word.Bottom >= pageHeight - 36);

    private static bool IsAllDigits(string value) => value.Length > 0 && value.All(char.IsDigit);

    public static decimal? TotalFromWords(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text != "TOTAL:")
            {
                continue;
            }

            var tail = string.Join(' ', words.Skip(i + 1).Take(8).Select(word => word.Text));
            var match = TotalPattern().Match(tail);
            if (match.Success)
            {
                return DecimalCleaner.Parse(match.Value);
            }
        }

        return null;
    }

    public static Dictionary<string, string> RawDict(params (string Key, string? Value)[] values)
    {
        return values.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);
    }

    public static IReadOnlyDictionary<string, string> RawDict(IReadOnlyDictionary<string, string> cells)
    {
        return cells
            .Select(pair => (pair.Key, Value: TextCleaner.Clean(pair.Value)))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>
    /// Captures every physical row of a logical PDF item. Callers can replace joined values for
    /// columns whose wrapping rules are not space-separated (for example a split part number).
    /// </summary>
    public static IReadOnlyDictionary<string, string> RawDict(
        IReadOnlyList<PdfRow> rows,
        IReadOnlyDictionary<string, string>? joinedOverrides = null)
    {
        var raw = rows
            .SelectMany(row => row.Cells.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(key => (Key: key, Value: TextCleaner.JoinSpaced(rows.Select(row => Cell(row.Cells, key)))))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (joinedOverrides is not null)
        {
            foreach (var (key, value) in joinedOverrides)
            {
                var clean = TextCleaner.Clean(value);
                if (clean.Length == 0) raw.Remove(key); else raw[key] = clean;
            }
        }

        return raw;
    }

    public static string Cell(IReadOnlyDictionary<string, string> cells, string key)
    {
        return cells.TryGetValue(key, out var value) ? TextCleaner.Clean(value) : string.Empty;
    }

    public static bool HasAny(IReadOnlyDictionary<string, string> cells, params string[] keys)
    {
        return keys.Any(key => Cell(cells, key).Length > 0);
    }

    /// <summary>
    /// Turns header (name, left-X) anchors into half-open [Left, Right) column ranges: each column
    /// extends to the next header's X (the last runs to <paramref name="pageWidth"/>). Feeds
    /// <see cref="RowsBetween"/>'s per-column bucketing.
    /// </summary>
    public static IReadOnlyDictionary<string, (double Left, double Right)> ColumnRanges(
        IReadOnlyList<(string Name, double X0)> headers,
        double pageWidth)
    {
        var ordered = headers.OrderBy(header => header.X0).ToList();
        return ordered
            .Select((header, index) =>
            {
                var right = index + 1 < ordered.Count ? ordered[index + 1].X0 : pageWidth;
                return (header.Name, Range: (header.X0, right));
            })
            .ToDictionary(pair => pair.Name, pair => pair.Range);
    }

    /// <summary>
    /// Resolves columns in PDFs with centred header labels but left-aligned body data. Unlike
    /// <see cref="ColumnRanges"/>, this finds the vertical whitespace corridors between complete
    /// content bands, so it does not assume header X positions are cell boundaries.
    /// </summary>
    public static IReadOnlyDictionary<string, (double Left, double Right)> ContentColumnRanges(
        IReadOnlyList<(string Name, double Centre)> headers,
        IEnumerable<PdfWord> regionWords,
        double minGutter,
        double pageWidth)
    {
        var intervals = RemovePageFooters(regionWords.ToList())
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => (Left: word.X0, Right: word.X1))
            .OrderBy(interval => interval.Left)
            .ToList();
        var bands = new List<(double Left, double Right)>();
        foreach (var interval in intervals)
        {
            if (bands.Count == 0 || interval.Left - bands[^1].Right >= minGutter)
                bands.Add(interval);
            else
                bands[^1] = (bands[^1].Left, Math.Max(bands[^1].Right, interval.Right));
        }

        // PdfPig's measured word widths vary slightly by platform. A narrow space inside a text
        // column can therefore cross minGutter and create an extra band on Windows even though the
        // same fixture forms one band on Linux. Preserve the widest inter-column corridors by
        // joining the closest adjacent bands until the header-defined column count is reached.
        while (bands.Count > headers.Count && bands.Count > 1)
        {
            var mergeIndex = Enumerable.Range(0, bands.Count - 1)
                .MinBy(index => bands[index + 1].Left - bands[index].Right);
            bands[mergeIndex] = (bands[mergeIndex].Left, bands[mergeIndex + 1].Right);
            bands.RemoveAt(mergeIndex + 1);
        }

        if (bands.Count != headers.Count)
            throw new ParseError("detect", "Could not resolve the table columns.",
                $"Expected {headers.Count} content columns but found {bands.Count}: {string.Join(", ", bands.Select(band => $"{band.Left:0.0}-{band.Right:0.0}"))}.");

        var boundaries = new double[bands.Count + 1];
        boundaries[0] = 0;
        boundaries[^1] = pageWidth;
        for (var i = 1; i < bands.Count; i++) boundaries[i] = (bands[i - 1].Right + bands[i].Left) / 2;
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Centre < boundaries[i] || headers[i].Centre >= boundaries[i + 1])
            {
                throw new ParseError(
                    "detect",
                    "Could not resolve the table columns.",
                    $"Header '{headers[i].Name}' falls outside its content band.");
            }
        }

        return headers.Select((header, index) => (header.Name, Range: (boundaries[index], boundaries[index + 1])))
            .ToDictionary(pair => pair.Name, pair => pair.Range);
    }

    /// <summary>Returns a start coordinate that includes every word sharing the anchor's visual row.</summary>
    public static double RowStartTop(IEnumerable<PdfWord> words, PdfWord anchor)
    {
        var coordinate = RowCoordinate(anchor);
        var top = words.Where(word => word.PageIndex == anchor.PageIndex && Math.Abs(RowCoordinate(word) - coordinate) <= RowTolerance)
            .Min(word => word.Top);
        return Math.BitDecrement(top);
    }

    /// <summary>Returns all words on the anchor's visual row, ordered left to right.</summary>
    public static IReadOnlyList<PdfWord> WordsOnRow(IEnumerable<PdfWord> words, PdfWord anchor)
    {
        var coordinate = RowCoordinate(anchor);
        return words.Where(word => word.PageIndex == anchor.PageIndex && Math.Abs(RowCoordinate(word) - coordinate) <= RowTolerance)
            .OrderBy(word => word.X0)
            .ToList();
    }

    /// <summary>Groups raw PDF text lines into logical rows, starting a new group at each anchor.</summary>
    public static IReadOnlyList<IReadOnlyList<PdfRow>> GroupByAnchor(
        IEnumerable<PdfRow> rows, Func<PdfRow, bool> isAnchor)
    {
        var groups = new List<IReadOnlyList<PdfRow>>();
        var current = new List<PdfRow>();
        foreach (var row in rows)
        {
            if (isAnchor(row))
            {
                if (current.Count > 0) groups.Add(current);
                current = [row];
            }
            else if (current.Count > 0) current.Add(row);
        }
        if (current.Count > 0) groups.Add(current);
        return groups;
    }

    /// <summary>
    /// Recovers cell boundaries for a table whose header text is <em>centred</em> in each cell —
    /// where header X0 and a fixed padding offset both fail because the printed cell widths vary
    /// from document to document. Walks the recurrence b_i = 2*centre_i - b_(i-1) outward from a
    /// known seam (the boundary at <paramref name="seamIndex"/>'s left edge), which reproduces the
    /// real cell edges because a centred label satisfies left + right = 2*centre for its own cell.
    /// Returns half-open [Left, Right) bands, the first clamped to 0 and the last to
    /// <paramref name="pageWidth"/>. Throws <see cref="ParseError"/> (stage "detect") if the
    /// resulting boundaries are not strictly increasing.
    /// </summary>
    public static IReadOnlyDictionary<string, (double Left, double Right)> CentredColumnRanges(
        IReadOnlyList<(string Name, double Centre)> headers,
        int seamIndex,
        double seamLeft,
        double pageWidth)
    {
        var boundaries = new double[headers.Count + 1];
        boundaries[seamIndex] = seamLeft;

        for (var i = seamIndex; i < headers.Count; i++)
        {
            boundaries[i + 1] = (2 * headers[i].Centre) - boundaries[i];
        }

        for (var i = seamIndex; i > 0; i--)
        {
            boundaries[i - 1] = (2 * headers[i - 1].Centre) - boundaries[i];
        }

        boundaries[0] = 0;
        boundaries[^1] = pageWidth;

        for (var i = 1; i < boundaries.Length; i++)
        {
            if (boundaries[i] <= boundaries[i - 1])
            {
                throw new ParseError("detect",
                    "Could not resolve the table columns.",
                    "Centred-column boundaries did not resolve to a strictly increasing sequence.");
            }
        }

        return headers
            .Select((header, index) => (header.Name, Range: (boundaries[index], boundaries[index + 1])))
            .ToDictionary(pair => pair.Name, pair => pair.Range);
    }

    /// <summary>
    /// Pre-fuses each "USD" token with its nearby numeric amount into a single synthetic
    /// word anchored at the amount's coordinates. This ensures that amounts which wrap
    /// onto the line below their "USD" prefix still land in the correct price column when
    /// bucketed, and that DecimalCleaner never sees a bare "USD" token.
    /// </summary>
    public static IReadOnlyList<PdfWord> FuseCurrencyTokens(IReadOnlyList<PdfWord> words)
    {
        const int lookAhead = 6;
        var consumed = new bool[words.Count];
        var result = new List<PdfWord>(words.Count);

        for (var i = 0; i < words.Count; i++)
        {
            if (consumed[i])
            {
                continue;
            }

            var current = words[i];
            if (current.Text == "USD")
            {
                var matchIndex = -1;
                for (var j = i + 1; j < words.Count && j <= i + lookAhead; j++)
                {
                    if (consumed[j])
                    {
                        continue;
                    }

                    var candidate = words[j];
                    if (candidate.PageIndex == current.PageIndex
                        && candidate.Top >= current.Top - 3.5
                        && candidate.Top - current.Top <= 15.0
                        && CurrencyAmount().IsMatch(candidate.Text))
                    {
                        matchIndex = j;
                        break;
                    }
                }

                if (matchIndex >= 0)
                {
                    var amount = words[matchIndex];
                    result.Add(new PdfWord(
                        $"USD {amount.Text}",
                        amount.X0, amount.X1,
                        amount.Top, amount.Bottom,
                        amount.PageIndex, amount.PageWidth, PageHeight: amount.PageHeight, LineY: amount.LineY,
                        PageUsesBaselineGeometry: amount.PageUsesBaselineGeometry));
                    consumed[matchIndex] = true;
                    continue;
                }
            }

            result.Add(current);
        }

        return result;
    }

    [GeneratedRegex(@"(?:USD\s*)?[$]?\s*[-+]?\d[\d,]*(?:\.\d+)?")]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"^\d[\d,]*(?:\.\d+)?$")]
    private static partial Regex CurrencyAmount();
}

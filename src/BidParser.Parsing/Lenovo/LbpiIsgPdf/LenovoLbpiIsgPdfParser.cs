using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Lenovo.LbpiIsgPdf;

/// <summary>
/// Parses Lenovo LBP-I ISG Quote PDFs. The PRODUCT AND SERVICE DETAILS grid carries either
/// flat priced lines, or Solution ID rows whose total prices the numbered lines beneath them;
/// the CONFIGURATION DETAILS grid contributes per-line components, joined by line number.
/// One parent per Solution ID: its cost is the solution total divided by its quantity, and
/// every other line of the solution is emitted as a zero-cost child.
/// See docs/lenovo_lbpi_isg_pdf.md.
/// </summary>
public sealed partial class LenovoLbpiIsgPdfParser : IParser
{
    public string Slug => ParserSlugs.LenovoLbpiIsgPdf;
    public string DisplayName => "LBP-I ISG Quote (PDF)";
    public string Vendor => Vendors.LenovoIsg;
    public string OutputVendorName => Vendors.LenovoOutput;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];
    public bool SupportsSolutionIdSplit => true;

    /// <summary>A stop token no PDF word will ever equal; the table region is pre-bounded instead.</summary>
    private const string NoStopToken = "￿";

    /// <summary>
    /// Recognition signature: the PRODUCT AND SERVICE DETAILS grid under the Lenovo Global
    /// Technology letterhead — present on every LBP-I ISG quote and on no other known format.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            var words = CollectWords(path);
            var hasGrid = PdfTableHelpers.FindSequence(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"]) is not null;
            var hasLetterhead = PdfTableHelpers.FindSequence(words, ["Lenovo", "Global", "Technology"]) is not null;
            return hasGrid && hasLetterhead ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// LBP-I PDFs make PdfPig's word extractor emit whitespace-only tokens between words; they
    /// carry no content, break exact token-sequence matching, and their zero-height boxes skew
    /// row-height statistics, so they are dropped at collection.
    /// </summary>
    private static List<PdfWord> CollectWords(string path)
    {
        var words = PdfWordCollector.CollectWords(path)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToList();
        RealignHangingGlyphs(words);
        return words;
    }

    /// <summary>A word this much shorter than the document's typical word is a bare glyph.</summary>
    private const double ShortGlyphHeightRatio = 0.6;

    /// <summary>
    /// This producer reports a lone hyphen's bounding box well below the line it belongs to —
    /// far enough to land in the <em>next</em> visual row's midline band, which scrambles a
    /// wrapped description ("DM3010H - Complete" becomes "DM3010H Complete … -"). A glyph that
    /// short cannot carry a line of its own, so snap it back onto the line of its horizontally
    /// adjacent full-height neighbour before any row grouping. Price-placeholder hyphens sit
    /// alone in their column with no adjacent neighbour and are left untouched.
    /// </summary>
    private static void RealignHangingGlyphs(List<PdfWord> words)
    {
        const double horizontalAdjacency = 4.0;
        const double verticalReach = 10.0;

        var heights = words.Select(word => word.Bottom - word.Top).Order().ToList();
        if (heights.Count == 0)
        {
            return;
        }

        var maxShortGlyphHeight = heights[heights.Count / 2] * ShortGlyphHeightRatio;

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Bottom - word.Top > maxShortGlyphHeight)
            {
                continue;
            }

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
            {
                words[i] = word with { Top = neighbour.Top, Bottom = neighbour.Bottom };
            }
        }
    }

    public ParseResult Parse(string path)
    {
        var words = CollectWords(path);

        var sectionIndex = PdfTableHelpers.FindSequence(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"])
            ?? throw new ParseError(
                "detect",
                "Could not find the PRODUCT AND SERVICE DETAILS section.",
                "Missing PRODUCT AND SERVICE DETAILS anchor.");

        ValidateCurrency(words, sectionIndex);

        var headerIndex = PdfTableHelpers.FindSequence(words, ["Line", "Item", "Part", "Number"], sectionIndex)
            ?? throw new ParseError(
                "detect",
                "Could not find the Line Item / Part Number table header.",
                "Missing product grid header row.");

        var header = words[headerIndex];
        var configIndex = PdfTableHelpers.FindSequence(words, ["CONFIGURATION", "DETAILS"], headerIndex);
        var productEnd = ResolveProductEnd(words, headerIndex, configIndex);

        var columns = BuildProductColumns(words, header);
        var rows = SplitPartNumberColumn(PdfTableHelpers.RowsBetween(
            words.Take(productEnd), header.Top + 8, header.PageIndex, columns, NoStopToken));

        var (solutions, flatLines, quotedTotal) = ExtractProductGrid(rows);

        if (quotedTotal is null)
        {
            throw new ParseError(
                "totals",
                "Could not locate the Grand Total row.",
                "Missing 'AUD Ex GST' grand total.");
        }

        var configSections = configIndex is int start
            ? ExtractConfigSections(words, start)
            : new Dictionary<int, List<ConfigComponent>>();

        var items = AssembleItems(solutions, flatLines, configSections);

        var quoteMetadata = ExtractQuoteMetadata(words, sectionIndex, path);
        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteMetadata.QuoteNumber,
                BidNumber = quoteMetadata.BidNumber,
                BidRevision = quoteMetadata.BidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = ParseValidation.Validate(items, quotedTotal)
        };
    }

    internal sealed record ProductLine(
        int Number,
        string Vpn,
        List<string> DescriptionParts,
        int Qty,
        string UnitText,
        string TotalText);

    internal sealed class Solution(string sid, decimal total, string totalText)
    {
        public string Sid { get; } = sid;
        public decimal Total { get; } = total;
        public string TotalText { get; } = totalText;
        public List<ProductLine> Lines { get; } = [];
    }

    internal sealed record ConfigComponent(string Vpn, List<string> DescriptionParts, int Qty);

    /// <summary>
    /// The product grid ends at CONFIGURATION DETAILS or, on parts-only quotes, at the
    /// transmittal paragraph. With neither present the grid cannot be safely bounded — reading
    /// on would group terms-and-conditions clauses as table rows — so extraction fails closed.
    /// </summary>
    internal static int ResolveProductEnd(IReadOnlyList<PdfWord> words, int headerIndex, int? configIndex)
        => configIndex
            ?? PdfTableHelpers.FindSequence(words, ["Please", "transmit", "this", "quote"], headerIndex)
            ?? throw new ParseError(
                "extract",
                "Could not find the end of the PRODUCT AND SERVICE DETAILS grid.",
                "Missing CONFIGURATION DETAILS / transmittal grid terminator.");

    /// <summary>
    /// How far the Part Number column's content may overhang its header text on the left. Every
    /// column centres both its header and its values, so a value wider than its header spills
    /// evenly to both sides: a 10-character VPN already reaches ~5pt past each edge of
    /// "Part Number". Opening the column this far fits a VPN of up to twelve characters across
    /// every sample quote, and costs nothing on the left — the nearest content there is a line
    /// number, still ~5pt clear at three digits. (The boundary can fall inside the "Item" header
    /// word; page-break header repeats are recognised on their Line Item cell, which still reads
    /// "Line…", so that is harmless.)
    /// </summary>
    private const double PartNumberOverhang = 12.0;

    /// <summary>
    /// Column X anchors come from the header's own words, offset from the header word edges
    /// rather than taken verbatim because every column centres its values (see
    /// <see cref="PartNumberOverhang"/>).
    ///
    /// Part Number and Description are deliberately read as <em>one</em> column, split apart
    /// afterwards by <see cref="SplitPartNumberColumn"/>. A boundary between them cannot be
    /// placed safely: the widest VPN overruns the "Number" header's right edge on five of the
    /// seven sample quotes, so the old <c>number.X1 + 4</c> anchor sat inside the VPN — on
    /// BRDAS010260417V1 and BRDAS010965820V1 by as little as 0.04pt — and only escaped cutting
    /// it in half because <c>SplitAtColumnBoundaries</c> refuses to cut mid-glyph.
    /// </summary>
    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildProductColumns(
        IReadOnlyList<PdfWord> words,
        PdfWord header)
    {
        var headerWords = words
            .Where(word => word.PageIndex == header.PageIndex
                && word.Top >= header.Top - 16 && word.Top <= header.Top + 16)
            .ToList();

        var part = headerWords.FirstOrDefault(word => word.Text == "Part");
        var number = headerWords.FirstOrDefault(word => word.Text == "Number" && word.X0 > (part?.X0 ?? 0));
        var qty = headerWords.FirstOrDefault(word => word.Text == "Qty");
        var unit = headerWords.FirstOrDefault(word => word.Text == "Unit");
        var totalPrice = headerWords
            .Where(word => word.Text == "Total" && word.X0 > (unit?.X0 ?? 0))
            .OrderBy(word => word.X0)
            .FirstOrDefault();

        if (part is null || number is null || qty is null || unit is null || totalPrice is null)
        {
            throw new ParseError(
                "detect",
                "Could not resolve the product grid columns.",
                "Incomplete LBP-I ISG Quote (PDF) product grid header.");
        }

        var headers = new List<(string Name, double X0)>
        {
            ("Line Item", header.X0),
            ("Part Number", part.X0 - PartNumberOverhang),
            ("Qty", qty.X0 - 4),
            ("Unit Price", unit.X0 - 4),
            ("Total Price", totalPrice.X0 - 2)
        };

        return PdfTableHelpers.ColumnRanges(headers, header.PageWidth);
    }

    /// <summary>
    /// Splits the merged Part Number / Description cell back into two. The row's own Line Item
    /// cell decides whether a part number leads it: a numbered grid line always carries one, a
    /// Solution ID row carries one with a blank line number, and everything else — wrapped
    /// description fragments and the page-break header repeats — is description text only.
    /// Descriptions never render left of the part number, so the leading token is the whole of it.
    /// </summary>
    internal static IReadOnlyList<PdfRow> SplitPartNumberColumn(IReadOnlyList<PdfRow> rows)
    {
        return rows.Select(row =>
        {
            var merged = PdfTableHelpers.Cell(row.Cells, "Part Number");
            if (merged.Length == 0)
            {
                return row;
            }

            var space = merged.IndexOf(' ');
            var leading = space < 0 ? merged : merged[..space];
            var lineItem = PdfTableHelpers.Cell(row.Cells, "Line Item");
            var leadsWithPartNumber =
                int.TryParse(lineItem, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || (lineItem.Length == 0 && SolutionIdPattern().IsMatch(leading));

            var cells = new Dictionary<string, string>(row.Cells)
            {
                ["Part Number"] = leadsWithPartNumber ? leading : string.Empty,
                ["Description"] = leadsWithPartNumber
                    ? (space < 0 ? string.Empty : merged[(space + 1)..])
                    : merged
            };

            return row with { Cells = cells };
        }).ToList();
    }

    internal static (List<Solution> Solutions, List<ProductLine> FlatLines, decimal? QuotedTotal) ExtractProductGrid(
        IReadOnlyList<PdfRow> rows)
    {
        // First pass: keep table rows (SID / numbered / description continuation), mining the
        // quoted total out of the Grand Total rows that repeat at every page break.
        var kept = new List<PdfRow>();
        var anchorIndexes = new List<int>();
        decimal? quotedTotal = null;

        foreach (var row in rows)
        {
            var lineItem = PdfTableHelpers.Cell(row.Cells, "Line Item");
            var partNumber = PdfTableHelpers.Cell(row.Cells, "Part Number");
            var description = PdfTableHelpers.Cell(row.Cells, "Description");
            var joined = string.Join(' ', row.Cells.Values.Where(value => value.Length > 0));

            if (joined.Contains("Grand Total", StringComparison.OrdinalIgnoreCase))
            {
                var match = ExGstTotal().Match(joined);
                if (match.Success)
                {
                    quotedTotal = RoundMoney(DecimalCleaner.Parse(match.Groups[1].Value));
                }
                continue;
            }

            // Column-header repeats at page breaks ("Line Item | Part Number | …" and the
            // "Unit price excl. GST / (AUD)" lines above and below it).
            if (lineItem.StartsWith("Line", StringComparison.Ordinal)
                || partNumber.StartsWith("Part", StringComparison.Ordinal))
            {
                continue;
            }

            var isSid = lineItem.Length == 0 && SolutionIdPattern().IsMatch(partNumber);
            var isNumbered = int.TryParse(lineItem, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            var isContinuation = !isSid && !isNumbered
                && description.Length > 0 && lineItem.Length == 0 && partNumber.Length == 0;

            if (!isSid && !isNumbered && !isContinuation)
            {
                continue;
            }

            if (isSid || isNumbered)
            {
                anchorIndexes.Add(kept.Count);
            }

            kept.Add(row);
        }

        // Second pass: hand wrapped description fragments to their anchor row.
        var descriptions = CollectDescriptions(kept, anchorIndexes);

        // Third pass: build solutions and flat lines in reading order.
        var solutions = new List<Solution>();
        var flatLines = new List<ProductLine>();
        Solution? currentSolution = null;

        foreach (var index in anchorIndexes)
        {
            var cells = kept[index].Cells;
            var lineItem = PdfTableHelpers.Cell(cells, "Line Item");
            var partNumber = PdfTableHelpers.Cell(cells, "Part Number");

            if (lineItem.Length == 0)
            {
                var totalText = PdfTableHelpers.Cell(cells, "Total Price");
                if (totalText.Length == 0 || totalText == "-")
                {
                    throw new ParseError(
                        "extract",
                        $"Solution {partNumber} has no total price.",
                        "Solution ID row without a total price.");
                }

                currentSolution = new Solution(partNumber, RoundMoney(DecimalCleaner.Parse(totalText)), totalText);
                solutions.Add(currentSolution);
                continue;
            }

            // A blank quantity parses as 0 so it reaches the staged parent-quantity guard in
            // AssembleItems instead of surfacing as an unstaged FormatException.
            var line = new ProductLine(
                DecimalCleaner.ParseInt(lineItem),
                partNumber,
                descriptions[index],
                DecimalCleaner.ParseOptionalInt(PdfTableHelpers.Cell(cells, "Qty")) ?? 0,
                PdfTableHelpers.Cell(cells, "Unit Price"),
                PdfTableHelpers.Cell(cells, "Total Price"));

            if (currentSolution is not null)
            {
                currentSolution.Lines.Add(line);
            }
            else
            {
                flatLines.Add(line);
            }
        }

        return (solutions, flatLines, quotedTotal);
    }

    internal static Dictionary<int, List<ConfigComponent>> ExtractConfigSections(
        IReadOnlyList<PdfWord> words,
        int configIndex)
    {
        // The section heading was found, so an unresolvable header or terminator is an
        // extraction failure — returning an empty map here would silently drop every component
        // while total validation still passes (pricing sits on the parents).
        var headerIndex = PdfTableHelpers.FindSequence(words, ["No.", "Components", "Description", "Qty"], configIndex)
            ?? throw new ParseError(
                "extract",
                "Could not resolve the CONFIGURATION DETAILS table header.",
                "Missing No. / Components / Description / Qty header row.");

        var header = words[headerIndex];
        var components = words[headerIndex + 1];
        var qty = words[headerIndex + 3];
        var configEnd = PdfTableHelpers.FindSequence(words, ["Please", "transmit", "this", "quote"], configIndex)
            ?? throw new ParseError(
                "extract",
                "Could not find the end of the CONFIGURATION DETAILS grid.",
                "Missing transmittal terminator after CONFIGURATION DETAILS.");

        // "No." and "Components" are read as a single cell. Lenovo centres the Components header
        // over a column whose width follows the Description content, so the header word drifts
        // right of its own values by anywhere from 5pt to 11pt between quotes — further than the
        // gap to the No. column itself, which leaves no X boundary between the two that holds
        // across quotes. The row's leading integer identifies the section instead.
        var columns = PdfTableHelpers.ColumnRanges(
        [
            ("Components", header.X0 - 2),
            ("Description", components.X1 + 6),
            ("Qty", qty.X0 - 8)
        ], header.PageWidth);

        var rows = PdfTableHelpers.RowsBetween(
            words.Take(configEnd), header.Top + 2, header.PageIndex, columns, NoStopToken);

        // Same two-pass shape as the product grid: keep real rows, then let RowBlocks assign the
        // vertically-centred wrapped description fragments to their anchor row.
        var kept = new List<PdfRow>();
        var anchorIndexes = new List<int>();

        foreach (var row in rows)
        {
            var component = PdfTableHelpers.Cell(row.Cells, "Components");
            var description = PdfTableHelpers.Cell(row.Cells, "Description");

            // Header repeats on every page of the section; the merged cell reads "No. Components".
            if (component == "Components" || component.StartsWith("No.", StringComparison.Ordinal))
            {
                continue;
            }

            if (component.Length > 0)
            {
                anchorIndexes.Add(kept.Count);
                kept.Add(row);
            }
            else if (description.Length > 0)
            {
                kept.Add(row);
            }
        }

        var descriptions = CollectDescriptions(kept, anchorIndexes);

        var sections = new Dictionary<int, List<ConfigComponent>>();
        List<ConfigComponent>? currentSection = null;

        foreach (var index in anchorIndexes)
        {
            var cells = kept[index].Cells;
            var (number, vpn) = SplitLeadingCell(PdfTableHelpers.Cell(cells, "Components"));

            if (number is int sectionNumber)
            {
                // The numbered row echoes the product grid line (VPN, description, qty); it only
                // keys the section and is not emitted itself.
                currentSection = [];
                sections[sectionNumber] = currentSection;
                continue;
            }

            currentSection?.Add(new ConfigComponent(
                vpn,
                descriptions[index],
                DecimalCleaner.ParseOptionalInt(PdfTableHelpers.Cell(cells, "Qty")) ?? 0));
        }

        // The heading and the header both resolved, so a grid that yields nothing is a column or
        // row-classification failure, not an empty section. Children are zero-cost, so total
        // validation still passes without them: dropping them silently is the one outcome this
        // parser must never produce.
        if (sections.Count == 0)
        {
            throw new ParseError(
                "extract",
                "The CONFIGURATION DETAILS grid contains no configuration sections.",
                "CONFIGURATION DETAILS grid produced no sections.");
        }

        var empty = sections.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).Order().ToList();
        if (empty.Count > 0)
        {
            throw new ParseError(
                "extract",
                $"Configuration section(s) {string.Join(", ", empty)} list no components.",
                "CONFIGURATION DETAILS section without components.");
        }

        return sections;
    }

    /// <summary>
    /// Splits the merged No./Components cell. A section row leads with its product-grid line
    /// number followed by the echoed VPN ("1 7DGDCTO1WW"); a component row carries the code alone
    /// — and that code can itself be all digits ("5977", "6400", "6201"), so a leading integer
    /// only names a section when a second token follows it. A long VPN on a section row can be
    /// cut by the Description boundary ("1 7DGDCTO1"), which leaves the leading integer intact
    /// and costs nothing: section rows are never emitted.
    /// </summary>
    internal static (int? Number, string Vpn) SplitLeadingCell(string cell)
    {
        var tokens = cell.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2
            && int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? (number, tokens[1])
                : (null, cell);
    }

    internal static List<LineItem> AssembleItems(
        IReadOnlyList<Solution> solutions,
        IReadOnlyList<ProductLine> flatLines,
        IReadOnlyDictionary<int, List<ConfigComponent>> configSections)
    {
        // Every configuration section is keyed by a product grid Line Item number. A key no
        // product line carries means the line-number extraction or the join failed — dropping
        // the section would silently omit components while total validation still passes.
        var lineNumbers = flatLines.Select(line => line.Number)
            .Concat(solutions.SelectMany(solution => solution.Lines).Select(line => line.Number))
            .ToHashSet();
        var unmatched = configSections.Keys.Where(key => !lineNumbers.Contains(key)).Order().ToList();
        if (unmatched.Count > 0)
        {
            throw new ParseError(
                "extract",
                $"Configuration section(s) {string.Join(", ", unmatched)} do not match any product line.",
                "Unmatched CONFIGURATION DETAILS section numbers.");
        }

        var items = new List<LineItem>();
        var parentIndex = 0;
        var childIndex = 0;

        void AddChildrenOf(ProductLine line, string? solutionId)
        {
            if (!configSections.TryGetValue(line.Number, out var section))
            {
                return;
            }

            foreach (var component in section)
            {
                items.Add(CreateChild(
                    component.Vpn, component.DescriptionParts, component.Qty,
                    solutionId, parentIndex, ++childIndex));
            }
        }

        // A line quoted outside any solution carries its own unit price ("-" parses as zero).
        foreach (var line in flatLines)
        {
            parentIndex++;
            childIndex = 0;
            var unitText = line.UnitText == "-" ? "" : line.UnitText;
            items.Add(CreateParent(
                line, RoundMoney(DecimalCleaner.Parse(unitText, defaultZero: true)),
                solutionId: null, parentIndex));
            AddChildrenOf(line, solutionId: null);
        }

        foreach (var solution in solutions)
        {
            if (solution.Lines.Count == 0)
            {
                throw new ParseError(
                    "extract",
                    $"Solution {solution.Sid} has no line items.",
                    "Solution ID row with no numbered lines.");
            }

            // One parent per Solution ID: the first numbered line carries the whole solution's
            // price, spread to a per-unit cost so cost x qty reproduces the solution total.
            var parent = solution.Lines[0];
            if (parent.Qty <= 0)
            {
                throw new ParseError(
                    "extract",
                    $"Solution {solution.Sid} parent line has no quantity.",
                    "Solution parent line without a quantity.");
            }

            parentIndex++;
            childIndex = 0;
            items.Add(CreateParent(
                parent, RoundMoney(solution.Total / parent.Qty), solution.Sid, parentIndex,
                solution.TotalText));
            AddChildrenOf(parent, solution.Sid);

            foreach (var line in solution.Lines.Skip(1))
            {
                items.Add(CreateChild(
                    line.Vpn, line.DescriptionParts, line.Qty,
                    solution.Sid, parentIndex, ++childIndex,
                    line.Number));
                AddChildrenOf(line, solution.Sid);
            }
        }

        return items;
    }

    /// <summary>
    /// How much larger than the neighbouring row pitch a gap must be to count as inter-block
    /// padding when repairing cross-page ownership: wrapped lines sit one pitch apart while
    /// block padding is well over twice that.
    /// </summary>
    private const double PaddingContrastRatio = 1.5;

    /// <summary>Wrapped description fragments per anchor row, in reading order.</summary>
    private static Dictionary<int, List<string>> CollectDescriptions(
        IReadOnlyList<PdfRow> kept,
        IReadOnlyList<int> anchorIndexes)
    {
        var owners = AssignDescriptionOwners(kept, anchorIndexes);
        var descriptions = anchorIndexes.ToDictionary(index => index, _ => new List<string>());

        for (var index = 0; index < kept.Count; index++)
        {
            var description = PdfTableHelpers.Cell(kept[index].Cells, "Description");
            if (description.Length == 0)
            {
                continue;
            }

            if (owners[index] is int anchor && descriptions.TryGetValue(anchor, out var parts))
            {
                parts.Add(description);
            }
        }

        return descriptions;
    }

    /// <summary>
    /// Resolves which anchor each row belongs to. Descriptions are vertically centred on their
    /// anchor and can straddle a page break, which RowBlocks resolves — but its cross-page rule
    /// only reaches the row immediately adjacent to an anchor, so the second and later lines of
    /// a block split by a page break would be stolen by the neighbouring anchor. Two repair
    /// passes re-chain such rows onto the block whose lines they visibly continue: a row sitting
    /// one wrap-pitch from a row of one block, but a padding-sized gap from the other block,
    /// belongs with the former. Rows whose deciding gap is unmeasurable (it spans the page
    /// break) keep RowBlocks' assignment.
    /// </summary>
    internal static int?[] AssignDescriptionOwners(
        IReadOnlyList<PdfRow> rows,
        IReadOnlyList<int> anchorIndexes)
    {
        var blocks = new RowBlocks(rows, anchorIndexes);
        var anchors = new HashSet<int>(anchorIndexes);
        var owners = new int?[rows.Count];

        for (var index = 0; index < rows.Count; index++)
        {
            owners[index] = anchors.Contains(index) ? index : blocks.OwnerOf(index);
        }

        // Forward pass: a row assigned to the NEXT anchor while the row directly above it
        // continues the PREVIOUS block stays with that block when it sits closer to the row
        // above than to the row below (or, at a page bottom, when its pitch matches the chain).
        for (var index = 1; index < rows.Count; index++)
        {
            if (anchors.Contains(index) || anchors.Contains(index - 1)) continue;
            if (owners[index - 1] is not int above || owners[index] is not int current || above == current) continue;
            if (above >= index || current <= index) continue;
            if (rows[index].PageIndex != rows[index - 1].PageIndex) continue;

            var gapAbove = rows[index].Midline - rows[index - 1].Midline;
            if (index + 1 < rows.Count && rows[index + 1].PageIndex == rows[index].PageIndex)
            {
                // A tie clings to the block above: with reading order in its favour, an
                // equidistant row continues the block that is already open.
                if (gapAbove <= rows[index + 1].Midline - rows[index].Midline)
                {
                    owners[index] = above;
                }
            }
            else if (index - 2 >= 0
                && rows[index - 2].PageIndex == rows[index - 1].PageIndex
                && owners[index - 2] == above)
            {
                // No measurable gap below (page bottom): join the chain above only when this
                // row's pitch agrees with the chain's own pitch in both directions — a chain
                // whose last link is padding-sized is not a wrap chain.
                var chainPitch = rows[index - 1].Midline - rows[index - 2].Midline;
                if (gapAbove <= chainPitch * PaddingContrastRatio
                    && chainPitch <= gapAbove * PaddingContrastRatio)
                {
                    owners[index] = above;
                }
            }
        }

        // Backward pass: the mirror image, for the leading lines of a block that starts at the
        // bottom of one page with its anchor on the next.
        for (var index = rows.Count - 2; index >= 0; index--)
        {
            if (anchors.Contains(index) || anchors.Contains(index + 1)) continue;
            if (owners[index] is not int current || owners[index + 1] is not int below || current == below) continue;
            if (current >= index || below <= index + 1) continue;
            if (rows[index].PageIndex != rows[index + 1].PageIndex) continue;

            var gapBelow = rows[index + 1].Midline - rows[index].Midline;
            if (index - 1 >= 0 && rows[index - 1].PageIndex == rows[index].PageIndex)
            {
                // Strictly closer only: a tie stays with the previous block, mirroring the
                // forward pass's reading-order preference.
                if (gapBelow < rows[index].Midline - rows[index - 1].Midline)
                {
                    owners[index] = below;
                }
            }
            else if (index + 2 < rows.Count
                && rows[index + 2].PageIndex == rows[index + 1].PageIndex
                && owners[index + 2] == below)
            {
                var chainPitch = rows[index + 2].Midline - rows[index + 1].Midline;
                if (gapBelow <= chainPitch * PaddingContrastRatio
                    && chainPitch <= gapBelow * PaddingContrastRatio)
                {
                    owners[index] = below;
                }
            }
        }

        return owners;
    }

    private static LineItem CreateParent(
        ProductLine line,
        decimal cost,
        string? solutionId,
        int parentIndex,
        string? solutionTotalText = null)
        => new()
        {
            Vpn = line.Vpn,
            Description = JoinDescription(line.DescriptionParts),
            Cost = cost,
            Qty = line.Qty,
            SolutionId = solutionId,
            Comments = solutionId is null ? null : $"Solution ID: {solutionId}",
            LineSequence = parentIndex.ToString(CultureInfo.InvariantCulture),
            Raw = BuildRaw(line, solutionId, solutionTotalText)
        };

    private static LineItem CreateChild(
        string vpn,
        List<string> descriptionParts,
        int qty,
        string? solutionId,
        int parentIndex,
        int childIndex,
        int? lineNumber = null)
        => new()
        {
            Vpn = vpn,
            Description = JoinDescription(descriptionParts),
            Cost = 0m,
            Qty = qty,
            SolutionId = solutionId,
            Comments = null,
            LineSequence = $"{parentIndex}.{childIndex:D2}",
            Raw = BuildChildRaw(vpn, JoinDescription(descriptionParts), qty, lineNumber)
        };

    private static IReadOnlyDictionary<string, string> BuildChildRaw(
        string vpn,
        string? description,
        int qty,
        int? lineNumber)
    {
        var raw = new Dictionary<string, string>();
        if (lineNumber is int number)
        {
            raw["Line Item"] = number.ToString(CultureInfo.InvariantCulture);
            raw["Part Number"] = vpn;
        }
        else
        {
            raw["Components"] = vpn;
        }
        if (description is not null) raw["Description"] = description;
        raw["Qty"] = qty.ToString(CultureInfo.InvariantCulture);
        return raw;
    }

    private static IReadOnlyDictionary<string, string> BuildRaw(
        ProductLine line,
        string? solutionId,
        string? solutionTotalText)
    {
        var raw = new Dictionary<string, string>
        {
            ["Line Item"] = line.Number.ToString(CultureInfo.InvariantCulture),
            ["Part Number"] = line.Vpn,
            ["Qty"] = line.Qty.ToString(CultureInfo.InvariantCulture)
        };
        if (JoinDescription(line.DescriptionParts) is string description) raw["Description"] = description;
        if (line.UnitText.Length > 0 && line.UnitText != "-") raw["Unit price excl. GST (AUD)"] = line.UnitText;
        if (line.TotalText.Length > 0 && line.TotalText != "-") raw["Total price excl. GST (AUD)"] = line.TotalText;
        if (solutionId is not null) raw["Solution ID"] = solutionId;
        if (solutionTotalText is not null) raw["Solution total excl. GST (AUD)"] = solutionTotalText;
        return raw;
    }

    private static string? JoinDescription(IReadOnlyList<string> parts)
    {
        var joined = TextCleaner.JoinSpaced(parts);
        return joined.Length == 0 ? null : joined;
    }

    private static void ValidateCurrency(IReadOnlyList<PdfWord> words, int sectionIndex)
    {
        var headerText = PdfTableHelpers.WordStreamText(words.Take(sectionIndex));
        var match = CurrencyField().Match(headerText);
        if (match.Success && !string.Equals(match.Groups[1].Value, "AUD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParseError(
                "currency",
                $"This quote is in {match.Groups[1].Value}, but LBP-I ISG quotes must be in AUD.",
                $"Unsupported currency '{match.Groups[1].Value}'.");
        }
    }

    /// <summary>
    /// Splits "Quote No.: BRDAS010079884 V1" into its parts. The version group stays optional —
    /// it was written that way before bid metadata existed, which is evidence that files without
    /// one turn up. A file missing it keeps its bid number and defaults the revision.
    /// </summary>
    private static (string QuoteNumber, string? BidNumber, string? BidRevision) ExtractQuoteMetadata(IReadOnlyList<PdfWord> words, int sectionIndex, string path)
    {
        var headerText = PdfTableHelpers.WordStreamText(words.Take(sectionIndex));
        var match = QuoteNumberField().Match(headerText);
        var fallback = BidMetadataCleaner.FromFilename(path);
        var quoteNumber = match.Success
            ? match.Groups[1].Value + match.Groups[2].Value
            : Path.GetFileNameWithoutExtension(path);
        var bid = BidMetadataCleaner.Clean(
            match.Success ? match.Groups[1].Value : null,
            match.Success ? match.Groups[2].Value : null);
        if (bid.BidNumber is null) bid = fallback;
        return (quoteNumber, bid.BidNumber, bid.BidRevision);
    }

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"^SID[A-Za-z0-9]+$")]
    private static partial Regex SolutionIdPattern();

    [GeneratedRegex(@"Ex\s*GST\s*-*>?\s*\$?([0-9][\d,]*(?:\.\d+)?)")]
    private static partial Regex ExGstTotal();

    [GeneratedRegex(@"Currency\s*:\s*([A-Za-z]{3})")]
    private static partial Regex CurrencyField();

    [GeneratedRegex(@"Quote\s*No\.?\s*:\s*([A-Z]{2,}\d+)\s*(V\d+)?")]
    private static partial Regex QuoteNumberField();
}

using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Lenovo.BrdaDcgPdf;

public sealed class LenovoBrdaDcgPdfParser : IParser
{
    public string Slug => ParserSlugs.LenovoBrdaDcgPdf;
    public string DisplayName => "BRDA DCG (PDF)";
    public string Vendor => Vendors.Lenovo;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    public double Detect(string path)
    {
        try
        {
            var words = PdfWordCollector.CollectWords(path);
            var hasSec1 = FindSectionAnchor(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"]) is not null;
            var hasSec2 = FindSectionAnchor(words, ["CONFIGURATION", "DETAILS"]) is not null;
            return hasSec1 && hasSec2 ? 0.85 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        var words = PdfWordCollector.CollectWords(path);

        var (section1Entries, quotedTotal) = ParseSection1(words);
        var children = ParseSection2(words);
        var items = Assemble(section1Entries, children);
        var quoteNumber = ExtractQuoteNumber(words, path);
        var validation = ParseValidation.Validate(items, quotedTotal);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = quotedTotal,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Section 1 — PRODUCT AND SERVICE DETAILS
    // ──────────────────────────────────────────────────────────────────────────────

    private static (List<Section1Entry> Entries, decimal? QuotedTotal) ParseSection1(IReadOnlyList<PdfWord> words)
    {
        var anchorIdx = FindSectionAnchor(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"])
            ?? throw new ParseError("detect", "Could not find the PRODUCT AND SERVICE DETAILS section.", "Missing section 1 anchor");

        var headerWord = FindSection1Header(words, anchorIdx);
        var columns = BuildSection1Columns(words, headerWord);

        // Stop at "Grand" — first word of the Grand Total line
        var rows = PdfTableHelpers.RowsBetween(words, headerWord.Top, headerWord.PageIndex, columns, stopToken: "Grand");
        var quotedTotal = ExtractQuotedTotal(words, anchorIdx);

        var entries = new List<Section1Entry>();
        CurrentSection1Item? current = null;
        // Some parent rows have their description text in a y-cluster that appears BEFORE
        // the numbered line-item row in the word stream (the description sits ~7pt above the
        // VPN row, which exceeds RowsBetween's 3.5pt tolerance so they land in separate rows).
        // While the current item is a CONFIG, buffer orphaned description rows so they can be
        // prepended to the next PARENT.
        var bufferedDescriptions = new List<string>();

        foreach (var row in rows)
        {
            var lineItemCell = CellValue(row, "Line Item");
            var partNumberCell = CellValue(row, "Part Number");
            var descriptionCell = CellValue(row, "Description");
            var qtyCell = CellValue(row, "Qty");
            var unitPriceCell = CellValue(row, "Unit Price");

            if (int.TryParse(lineItemCell, out var lineNo) && lineNo > 0)
            {
                // PARENT — numbered line item; unit price is "-" (cost = 0)
                if (current is not null)
                {
                    entries.Add(current.Build());
                }

                current = new CurrentSection1Item(Section1EntryType.Parent, partNumberCell, lineNo, SafeParseQty(qtyCell), 0m);
                // Prepend any descriptions that arrived before this PARENT row in y-order
                foreach (var d in bufferedDescriptions)
                    current.DescriptionParts.Add(d);
                bufferedDescriptions.Clear();
                if (descriptionCell.Length > 0)
                {
                    current.DescriptionParts.Add(descriptionCell);
                }
            }
            else if (lineItemCell.Length == 0 && partNumberCell.Length > 0 && IsNumericPrice(unitPriceCell))
            {
                // CONFIG — bold part-number row with a real unit price
                if (current is not null)
                {
                    entries.Add(current.Build());
                }

                bufferedDescriptions.Clear(); // fresh config scope — discard any stale buffers
                current = new CurrentSection1Item(Section1EntryType.Config, partNumberCell, 0, SafeParseQty(qtyCell), ParsePrice(unitPriceCell));
                // Per spec: config description == config id (its own part number)
                current.DescriptionParts.Add(partNumberCell);
            }
            else if (lineItemCell.Length == 0 && partNumberCell.Length == 0 && descriptionCell.Length > 0)
            {
                if (current?.Type == Section1EntryType.Config)
                {
                    // Description orphaned before the upcoming PARENT — buffer it
                    bufferedDescriptions.Add(descriptionCell);
                }
                else if (current is not null)
                {
                    // Normal continuation — append wrapped text to current PARENT
                    current.DescriptionParts.Add(descriptionCell);
                    // Merge a Qty that wrapped onto a continuation line
                    if (current.Qty == 0 && qtyCell.Length > 0)
                    {
                        current.Qty = SafeParseQty(qtyCell);
                    }
                }
            }
            // else: noise — ignore
        }

        if (current is not null)
        {
            entries.Add(current.Build());
        }

        return (entries, quotedTotal);
    }

    private static PdfWord FindSection1Header(IReadOnlyList<PdfWord> words, int startIndex)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text != "Line")
            {
                continue;
            }

            var lineWord = words[i];
            var hasItem = words
                .Skip(i + 1)
                .Take(8)
                .Any(w => w.Text == "Item"
                    && w.PageIndex == lineWord.PageIndex
                    && Math.Abs(w.Top - lineWord.Top) <= 4
                    && w.X0 > lineWord.X0);

            if (hasItem)
            {
                return lineWord;
            }
        }

        throw new ParseError("detect", "Could not find the Line Item table header in section 1.", "Missing section 1 header");
    }

    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildSection1Columns(
        IReadOnlyList<PdfWord> words, PdfWord headerWord)
    {
        // "Unit" and "Total" sit ~6pt ABOVE "Line" in this PDF, so extend the band upward.
        // The second header line "(AUD)" sits ~7pt below; extend 30pt downward.
        var band = words
            .Where(w => w.PageIndex == headerWord.PageIndex
                && w.Top >= headerWord.Top - 10
                && w.Top <= headerWord.Top + 30)
            .ToList();

        var lineX0 = headerWord.X0;

        // "Item" right-edge (X1) is the reliable boundary between "Line Item" and "Part Number"
        // columns — VPN codes in the PDF start just to the right of "Item".X1 but to the
        // LEFT of the "Part" header word's X0 (minor PDF indentation artefact).
        var itemWord = band.FirstOrDefault(w => w.Text == "Item" && w.X0 > lineX0);
        var partX0 = itemWord is not null
            ? itemWord.X1 + 1   // just past the right edge of "Item"
            : throw new ParseError("detect", "Could not find Item header in section 1.", "Missing Item word");

        // "Number" (second word of "Part Number" header) X1+1 is the reliable left boundary
        // for Description — description text starts at x0≈144 which is LEFT of the
        // "Description" header word's X0=233, but just right of "Number".X1≈144.
        var numberWord = band.FirstOrDefault(w => w.Text == "Number" && w.X0 > partX0);
        var descX0 = numberWord is not null
            ? numberWord.X1 + 1
            : throw new ParseError("detect", "Could not locate Description column in section 1.", "Missing Number word");

        var qtyX0 = band
            .Where(w => w.Text == "Qty" && w.X0 > descX0)
            .Select(w => (double?)w.X0)
            .FirstOrDefault()
            ?? throw new ParseError("detect", "Could not locate Qty column in section 1.", "Missing Qty column");

        // "Unit" is the leftmost anchor word for the Unit price column (it sits above "Line")
        var unitX0 = band
            .Where(w => w.Text == "Unit" && w.X0 > qtyX0)
            .Select(w => (double?)w.X0)
            .FirstOrDefault()
            ?? throw new ParseError("detect", "Could not locate Unit price column in section 1.", "Missing Unit column");

        var totalX0 = band
            .Where(w => w.Text == "Total" && w.X0 > unitX0)
            .Select(w => (double?)w.X0)
            .FirstOrDefault()
            ?? throw new ParseError("detect", "Could not locate Total price column in section 1.", "Missing Total column");

        var headers = new List<(string Name, double X0)>
        {
            ("Line Item", lineX0),
            ("Part Number", partX0),
            ("Description", descX0),
            ("Qty", qtyX0),
            ("Unit Price", unitX0),
            ("Total Price", totalX0)
        };

        return PdfTableHelpers.ColumnRanges(headers, headerWord.PageWidth);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Section 2 — CONFIGURATION DETAILS
    // ──────────────────────────────────────────────────────────────────────────────

    private static Dictionary<int, List<ChildItem>> ParseSection2(IReadOnlyList<PdfWord> words)
    {
        var anchorIdx = FindSectionAnchor(words, ["CONFIGURATION", "DETAILS"])
            ?? throw new ParseError("detect", "Could not find the CONFIGURATION DETAILS section.", "Missing section 2 anchor");

        var headerWord = FindSection2Header(words, anchorIdx);
        var columns = BuildSection2Columns(words, headerWord);

        // Stop at "Please" — first word of the footer paragraph after the table
        var rows = PdfTableHelpers.RowsBetween(words, headerWord.Top, headerWord.PageIndex, columns, stopToken: "Please");

        var children = new Dictionary<int, List<ChildItem>>();
        var currentParentLineNo = -1;
        CurrentChildItem? currentChild = null;

        foreach (var row in rows)
        {
            var noCell = CellValue(row, "No");
            var componentsCell = CellValue(row, "Components");
            var descriptionCell = CellValue(row, "Description");
            var qtyCell = CellValue(row, "Qty");

            // Skip repeated header rows (printed on every PDF page)
            if (componentsCell == "Components" || noCell is "No" or "No.")
            {
                continue;
            }

            if (int.TryParse(noCell, out var parentLineNo) && parentLineNo > 0)
            {
                // Group header — sets current parent reference
                FlushChild(ref currentChild, currentParentLineNo, children);
                currentParentLineNo = parentLineNo;

                // In this PDF the group header row also carries the first component on the
                // same visual line (the component VPN sits just right of the group number)
                if (componentsCell.Length > 0)
                {
                    currentChild = new CurrentChildItem(componentsCell, SafeParseQty(qtyCell));
                    if (descriptionCell.Length > 0)
                    {
                        currentChild.DescriptionParts.Add(descriptionCell);
                    }
                }
            }
            else if (noCell.Length == 0 && componentsCell.Length > 0)
            {
                // Regular child row
                FlushChild(ref currentChild, currentParentLineNo, children);
                currentChild = new CurrentChildItem(componentsCell, SafeParseQty(qtyCell));
                if (descriptionCell.Length > 0)
                {
                    currentChild.DescriptionParts.Add(descriptionCell);
                }
            }
            else if (noCell.Length == 0 && componentsCell.Length == 0 && descriptionCell.Length > 0)
            {
                // Continuation of current child's description
                currentChild?.DescriptionParts.Add(descriptionCell);
            }
        }

        FlushChild(ref currentChild, currentParentLineNo, children);
        return children;
    }

    private static PdfWord FindSection2Header(IReadOnlyList<PdfWord> words, int startIndex)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text != "No" && words[i].Text != "No.")
            {
                continue;
            }

            var noWord = words[i];
            var hasComponents = words
                .Skip(i + 1)
                .Take(10)
                .Any(w => w.Text == "Components"
                    && w.PageIndex == noWord.PageIndex
                    && Math.Abs(w.Top - noWord.Top) <= 4
                    && w.X0 > noWord.X0);

            if (hasComponents)
            {
                return noWord;
            }
        }

        throw new ParseError("detect", "Could not find the No./Components table header in section 2.", "Missing section 2 header");
    }

    private static IReadOnlyDictionary<string, (double Left, double Right)> BuildSection2Columns(
        IReadOnlyList<PdfWord> words, PdfWord headerWord)
    {
        var band = words
            .Where(w => w.PageIndex == headerWord.PageIndex
                && w.Top >= headerWord.Top - 4
                && w.Top <= headerWord.Top + 4)
            .ToList();

        var noX0 = headerWord.X0;

        // Component VPN codes start just to the right of "No."'s right edge (X1), but
        // to the LEFT of the "Components" header word — use X1+1 as the column boundary.
        var compBoundary = headerWord.X1 + 1;

        // "Components" header X1+1 is the reliable left boundary for Description —
        // description text in section 2 starts at x0≈127 which is LEFT of the
        // "Description" header X0=307, but just right of "Components".X1≈119.
        var compWord = band.FirstOrDefault(w => w.Text == "Components" && w.X0 > compBoundary);
        var descX0 = compWord is not null
            ? compWord.X1 + 1
            : throw new ParseError("detect", "Could not locate Description column in section 2.", "Missing Components word (sec 2)");

        var qtyX0 = band
            .Where(w => w.Text == "Qty" && w.X0 > descX0)
            .Select(w => (double?)w.X0)
            .FirstOrDefault()
            ?? throw new ParseError("detect", "Could not locate Qty column in section 2.", "Missing Qty column (sec 2)");

        // PDF word coordinates have floating-point imprecision: data words whose visual
        // position matches the column header may have x0 slightly LESS than the header's x0.
        // Subtract 1 pt from the No and Qty column left boundaries so group numbers and
        // quantities are not dropped or misfiled into an adjacent column.
        var headers = new List<(string Name, double X0)>
        {
            ("No", noX0 - 1.0),
            ("Components", compBoundary),
            ("Description", descX0),
            ("Qty", qtyX0 - 1.0)
        };

        return PdfTableHelpers.ColumnRanges(headers, headerWord.PageWidth);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Assembly
    // ──────────────────────────────────────────────────────────────────────────────

    private static List<LineItem> Assemble(List<Section1Entry> entries, Dictionary<int, List<ChildItem>> children)
    {
        var items = new List<LineItem>();
        var globalSeq = 0;

        foreach (var entry in entries)
        {
            globalSeq++;
            items.Add(new LineItem
            {
                Vpn = entry.Vpn,
                Description = entry.Description,
                Qty = entry.Qty,
                Cost = entry.Cost,
                LineSequence = globalSeq.ToString(),
                Term = null,
                Msrp = null,
                MinQty = null,
                Raw = new Dictionary<string, string>()
            });

            if (entry.Type == Section1EntryType.Parent && children.TryGetValue(entry.LineNo, out var childList))
            {
                var childIdx = 0;
                foreach (var child in childList)
                {
                    // Skip the redundant "self" component: when a child's VPN matches its parent's VPN,
                    // the row is the parent unit re-listed inside its own component breakdown. Including
                    // it produces duplicate lines (e.g. 2 and 2.01 with the same VPN).
                    if (string.Equals(child.Vpn, entry.Vpn, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    childIdx++;
                    items.Add(new LineItem
                    {
                        Vpn = child.Vpn,
                        Description = child.Description,
                        Qty = child.Qty,
                        Cost = 0m,
                        LineSequence = $"{globalSeq}.{childIdx:D2}",
                        Term = null,
                        Msrp = null,
                        MinQty = null,
                        Raw = new Dictionary<string, string>()
                    });
                }
            }
        }

        return items;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Quoted total — "Ex GST-> <amount>"
    // ──────────────────────────────────────────────────────────────────────────────

    private static decimal? ExtractQuotedTotal(IReadOnlyList<PdfWord> words, int startIndex)
    {
        for (var i = startIndex; i < words.Count - 2; i++)
        {
            // Two GST totals exist: "Inc GST->" and "Ex GST->". We want "Ex".
            if (!string.Equals(words[i].Text, "Ex", StringComparison.Ordinal))
            {
                continue;
            }

            var gstIndex = -1;
            for (var j = i + 1; j < Math.Min(i + 6, words.Count); j++)
            {
                if (words[j].Text.StartsWith("GST", StringComparison.OrdinalIgnoreCase))
                {
                    gstIndex = j;
                    break;
                }
            }

            if (gstIndex < 0)
            {
                continue;
            }

            for (var k = gstIndex + 1; k < Math.Min(gstIndex + 6, words.Count); k++)
            {
                try
                {
                    var amount = DecimalCleaner.Parse(words[k].Text);
                    if (amount > 0)
                    {
                        return amount;
                    }
                }
                catch
                {
                    // Not a parseable number — keep scanning
                }
            }
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Quote number — "Quote No.:" metadata; fallback to filename
    // ──────────────────────────────────────────────────────────────────────────────

    private static string ExtractQuoteNumber(IReadOnlyList<PdfWord> words, string path)
    {
        for (var i = 0; i < words.Count - 1; i++)
        {
            if (!string.Equals(words[i].Text, "Quote", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Find "No.:" or "No" within a few non-blank words on the same y-band
            var noIdx = -1;
            for (var j = i + 1; j < Math.Min(i + 8, words.Count); j++)
            {
                if (words[j].Text.Trim().Length == 0)
                {
                    continue;
                }

                if (words[j].Text.StartsWith("No", StringComparison.OrdinalIgnoreCase)
                    && words[j].PageIndex == words[i].PageIndex
                    && Math.Abs(words[j].Top - words[i].Top) <= 5)
                {
                    noIdx = j;
                    break;
                }

                break; // non-blank word that isn't "No" — stop
            }

            if (noIdx < 0)
            {
                continue;
            }

            // Collect non-blank tokens after "No.:" on the same y-band.
            // The quote number may be split across two tokens (e.g. "BRDAS010260417" + "V1")
            // with a small inter-token gap. Stop when there is a large x-gap (which separates
            // the left-column value from the right-column metadata keys).
            var parts = new List<string>();
            double? prevX1 = null;

            for (var k = noIdx + 1; k < Math.Min(noIdx + 30, words.Count); k++)
            {
                var text = words[k].Text.Trim();

                // Skip blank tokens
                if (text.Length == 0)
                {
                    continue;
                }

                // Stop if moved to a different page
                if (words[k].PageIndex != words[i].PageIndex)
                {
                    break;
                }

                // Skip tokens on a very different y-band (inter-line spacing artefacts)
                if (Math.Abs(words[k].Top - words[i].Top) > 8)
                {
                    continue;
                }

                // Stop on a large horizontal gap (right-column content starts here)
                if (prevX1.HasValue && words[k].X0 - prevX1.Value > 50)
                {
                    break;
                }

                // Skip standalone colon
                if (text == ":")
                {
                    continue;
                }

                parts.Add(text);
                prevX1 = words[k].X1;
            }

            if (parts.Count > 0)
            {
                return string.Concat(parts);
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Section anchor finder — tolerates blank space tokens between words by matching
    // all target words on the same y-band rather than requiring consecutive tokens.
    // ──────────────────────────────────────────────────────────────────────────────

    private static int? FindSectionAnchor(IReadOnlyList<PdfWord> words, string[] targets, int startIndex = 0)
    {
        for (var i = startIndex; i <= words.Count - targets.Length; i++)
        {
            if (!string.Equals(words[i].Text, targets[0], StringComparison.Ordinal))
            {
                continue;
            }

            var first = words[i];
            var allFound = true;

            for (var t = 1; t < targets.Length; t++)
            {
                var found = words
                    .Skip(i + 1)
                    .Take(targets.Length * 3) // generous window around the first word
                    .Any(w => string.Equals(w.Text, targets[t], StringComparison.Ordinal)
                        && w.PageIndex == first.PageIndex
                        && Math.Abs(w.Top - first.Top) <= 5);

                if (!found)
                {
                    allFound = false;
                    break;
                }
            }

            if (allFound)
            {
                return i;
            }
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────────

    private static string CellValue(PdfRow row, string key)
    {
        return row.Cells.TryGetValue(key, out var value) ? TextCleaner.Clean(value) : string.Empty;
    }

    private static bool IsNumericPrice(string text)
    {
        if (text.Length == 0 || text is "-" or "–")
        {
            return false;
        }

        var cleaned = text
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(cleaned, out _);
    }

    private static decimal ParsePrice(string text)
    {
        return text is "-" or "–" ? 0m : DecimalCleaner.Parse(text, defaultZero: true);
    }

    private static int SafeParseQty(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        try
        {
            return DecimalCleaner.ParseInt(text);
        }
        catch
        {
            return 0;
        }
    }

    private static void FlushChild(
        ref CurrentChildItem? child,
        int parentLineNo,
        Dictionary<int, List<ChildItem>> children)
    {
        if (child is null)
        {
            return;
        }

        if (!children.TryGetValue(parentLineNo, out var list))
        {
            list = [];
            children[parentLineNo] = list;
        }

        list.Add(child.Build());
        child = null;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Private types
    // ──────────────────────────────────────────────────────────────────────────────

    private enum Section1EntryType { Config, Parent }

    private sealed record Section1Entry(
        Section1EntryType Type, string Vpn, string Description, int Qty, decimal Cost, int LineNo);

    private sealed class CurrentSection1Item(
        Section1EntryType type, string vpn, int lineNo, int qty, decimal cost)
    {
        public Section1EntryType Type { get; } = type;
        public string Vpn { get; } = vpn;
        public int LineNo { get; } = lineNo;
        public int Qty { get; set; } = qty;
        public decimal Cost { get; } = cost;
        public List<string> DescriptionParts { get; } = [];

        public Section1Entry Build() =>
            new(Type, Vpn, TextCleaner.JoinSpaced(DescriptionParts), Qty, Cost, LineNo);
    }

    private sealed record ChildItem(string Vpn, string Description, int Qty);

    private sealed class CurrentChildItem(string vpn, int qty)
    {
        public string Vpn { get; } = vpn;
        public int Qty { get; } = qty;
        public List<string> DescriptionParts { get; } = [];

        public ChildItem Build() => new(Vpn, TextCleaner.JoinSpaced(DescriptionParts), Qty);
    }
}

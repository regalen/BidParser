using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Pdf;

namespace BidParser.Parsing.Lenovo.LbpiIdgPdf;

/// <summary>
/// Parses Lenovo LBP-I IDG (Intelligent Devices Group) Quote PDFs — laptops, monitors, docks with
/// no Solution IDs. The PRODUCT AND SERVICE DETAILS grid carries one priced line per product; the
/// CONFIGURATION DETAILS grid contributes zero-cost CTO component children, joined to their parent
/// by product-grid line number. Unlike LBP-I ISG, Part Number and Description are genuinely
/// separate ruled columns here, so no merged-cell splitting is required.
/// See docs/lenovo_lbpi_idg_pdf.md.
/// </summary>
public sealed partial class LenovoLbpiIdgPdfParser : IParser
{
    public string Slug => ParserSlugs.LenovoLbpiIdgPdf;
    public string DisplayName => "LBP-I IDG Quote (PDF)";
    public string Vendor => Vendors.LenovoIdg;
    public string OutputVendorName => Vendors.LenovoOutput;
    public string AcceptedMime => "application/pdf";
    public string CrmTemplate => CrmTemplates.NoCalculation;
    public IReadOnlyList<string> AvailableTemplates => [CrmTemplates.NoCalculation, CrmTemplates.Uplift];

    /// <summary>A stop token no PDF word will ever equal; the table regions are pre-bounded instead.</summary>
    private const string NoStopToken = "￿";

    /// <summary>Emitted as Vpn for a CONFIGURATION DETAILS row whose Components label is blank.</summary>
    private const string PlaceholderVpn = "SPEC";

    /// <summary>
    /// How far apart two raw text lines' midlines must sit, on the same page, to start a new
    /// logical row rather than continue wrapped text. Measured against both fixtures: intra-block
    /// (wrap) gaps run 5.0-10.5pt, inter-block (real new row) gaps run 13.2-13.5pt — 12.0pt sits
    /// between them with ~1.5pt of clearance either side. The two populations separate because the
    /// wrap gap is the font's own leading while the inter-block gap is that leading plus a constant
    /// cell padding, so the margin does not shrink as a block gains wrapped lines.
    ///
    /// The failure modes are asymmetric. Too high and two rows merge, losing the second's Line Item
    /// cell — the item is dropped and the total no longer reconciles, so it fails loudly. Too low
    /// and a block splits: the anchor row carries no Description text of its own (on a wrapped line
    /// the description lives entirely on the non-anchor rows), so the parent is emitted with an
    /// empty description while prices and quantity stay correct and the total still reconciles.
    /// That direction fails silently, which is why the threshold sits nearer the upper bound.
    /// </summary>
    private const double BlockGap = 12.0;

    /// <summary>
    /// Recognition signature: the PRODUCT AND SERVICE DETAILS grid with its "#" line-number header
    /// (LBP-I ISG uses "Line Item" and a different letterhead) plus the "Bid Request No." anchor
    /// LBP-I ISG quotes never carry.
    /// </summary>
    public double Detect(string path)
    {
        try
        {
            var words = CollectWords(path);
            var hasGrid = PdfTableHelpers.FindSequence(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"]) is not null;
            var hasIdgHeader = PdfTableHelpers.FindSequence(words, ["#", "Part", "Number", "Description", "Qty"]) is not null;
            var stream = PdfTableHelpers.WordStreamText(words);
            var hasBidRequest = BidRequestField().IsMatch(stream)
                || stream.Contains("Bid Request No.", StringComparison.OrdinalIgnoreCase);
            return hasGrid && hasIdgHeader && hasBidRequest ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private static List<PdfWord> CollectWords(string path)
    {
        return PdfWordCollector.CollectWords(path)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToList();
    }

    public ParseResult Parse(string path)
    {
        var words = CollectWords(path);

        var sectionIndex = PdfTableHelpers.FindSequence(words, ["PRODUCT", "AND", "SERVICE", "DETAILS"])
            ?? throw new ParseError(
                "detect",
                "Could not find the PRODUCT AND SERVICE DETAILS section.",
                "Missing PRODUCT AND SERVICE DETAILS anchor.");

        var headerIndex = PdfTableHelpers.FindSequence(words, ["#", "Part", "Number", "Description", "Qty"], sectionIndex)
            ?? throw new ParseError(
                "detect",
                "Could not find the # / Part Number / Description / Qty table header.",
                "Missing product grid header row.");

        var header = words[headerIndex];
        var configIndex = PdfTableHelpers.FindSequence(words, ["CONFIGURATION", "DETAILS"], headerIndex);
        var productEnd = configIndex
            ?? FindGridEnd(words, headerIndex)
            ?? throw new ParseError(
                "extract",
                "Could not find the end of the PRODUCT AND SERVICE DETAILS grid.",
                "Missing CONFIGURATION DETAILS / MTM / TERMS AND CONDITIONS grid terminator.");

        var productColumns = BuildProductColumns(words, headerIndex);
        var productRows = PdfTableHelpers.RowsBetween(
            words.Take(productEnd), header.Top + 8, header.PageIndex, productColumns, NoStopToken);

        var (lines, quotedTotal, currency) = ExtractProductGrid(productRows);

        if (quotedTotal is null)
        {
            throw new ParseError(
                "totals",
                "Could not locate the Grand Total row.",
                "Missing Grand Total row.");
        }

        if (currency is not null && !string.Equals(currency, "AUD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParseError(
                "currency",
                $"This quote is in {currency}, but LBP-I IDG quotes must be in AUD.",
                $"Unsupported currency '{currency}'.");
        }

        var configSections = new Dictionary<int, List<ConfigComponent>>();
        if (configIndex is int configStart)
        {
            var configEnd = FindGridEnd(words, configStart)
                ?? throw new ParseError(
                    "extract",
                    "Could not find the end of the CONFIGURATION DETAILS grid.",
                    "Missing MTM / TERMS AND CONDITIONS terminator after CONFIGURATION DETAILS.");
            configSections = ExtractConfigSections(words, configStart, configEnd);
        }

        var items = AssembleItems(lines, configSections);

        var (quoteNumber, bidNumber, bidRevision) = ExtractQuoteMetadata(words, sectionIndex, path);

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bidNumber,
                BidRevision = bidRevision,
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

    /// <summary>
    /// Locates the MTM recap table. The heading is matched together with the first two words of
    /// its own header row rather than on the bare "MTM" token: Lenovo uses "MTM" as ordinary prose
    /// (it is their term for a machine type model), so a lone token would also match a component
    /// label or a product description and end the region early — silently, since the components it
    /// would drop are zero-cost and the quote still reconciles without them.
    /// </summary>
    private static int? FindMtmRecap(IReadOnlyList<PdfWord> words, int startIndex)
        => PdfTableHelpers.FindSequence(words, ["MTM", "Line", "Item#"], startIndex);

    /// <summary>
    /// Locates the boilerplate terms heading that closes the quote body on every LBP-I IDG
    /// template. It is matched as its three uppercase tokens: the clause body's own lower-case
    /// "Lenovo Terms and Conditions" cannot collide under <c>FindSequence</c>'s ordinal
    /// comparison, and it sits below the heading in any case.
    /// </summary>
    private static int? FindTermsHeading(IReadOnlyList<PdfWord> words, int startIndex)
        => PdfTableHelpers.FindSequence(words, ["TERMS", "AND", "CONDITIONS"], startIndex);

    /// <summary>
    /// Bounds whichever grid precedes it, at whichever of the MTM recap or the terms heading comes
    /// first. The MTM recap is optional — Lenovo omits the whole table when no product line
    /// resolves to a machine type model (an unreleased CTO placeholder does not), which is why the
    /// terms heading has to be a terminator in its own right rather than an afterthought.
    ///
    /// The minimum is taken rather than falling back to the terms heading only when MTM is absent,
    /// so a stray "MTM Line Item#" below the terms heading cannot reopen the region across the
    /// clauses — reading them in would emit terms prose as component rows.
    ///
    /// Each caller searches from its own grid's start: a terminator above CONFIGURATION DETAILS
    /// must not terminate the section below it.
    /// </summary>
    internal static int? FindGridEnd(IReadOnlyList<PdfWord> words, int startIndex)
    {
        var mtm = FindMtmRecap(words, startIndex);
        var terms = FindTermsHeading(words, startIndex);
        return (mtm, terms) switch
        {
            (int a, int b) => Math.Min(a, b),
            (int a, null) => a,
            (null, int b) => b,
            _ => null
        };
    }

    internal sealed record ProductLine(
        int Number, string VpnRaw, string DescriptionRaw, int Qty, string UnitText, string TotalExclText);

    internal sealed record ConfigComponent(string LabelRaw, string? DescriptionRaw, int Qty);

    /// <summary>
    /// Column X anchors come from the header's own words. Unlike LBP-I ISG, Part Number and
    /// Description are genuinely separate ruled columns here — both vary in printed width between
    /// quotes (unlike Qty/Unit Price/Total Excl/Total Incl, which sit at pixel-identical positions
    /// in every sample), so their shared boundary is recovered via the centred-header recurrence
    /// rather than a fixed offset. The "(AUD)" sub-header line is ignored for centre purposes: its
    /// horizontal extent always sits inside its parent header's main line, so including it would
    /// not change the computed centre beyond the recurrence's own tolerance.
    /// </summary>
    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildProductColumns(
        IReadOnlyList<PdfWord> words,
        int headerIndex)
    {
        var header = words[headerIndex];
        var core = words.Skip(headerIndex).Take(20)
            .Where(word => word.PageIndex == header.PageIndex && word.Text != "(AUD)")
            .ToList();

        var part = core.FirstOrDefault(word => word.Text == "Part");
        var number = core.FirstOrDefault(word => word.Text == "Number" && word.X0 > (part?.X0 ?? 0));
        var description = core.FirstOrDefault(word => word.Text == "Description");
        var qty = core.FirstOrDefault(word => word.Text == "Qty");
        var unit = core.FirstOrDefault(word => word.Text == "Unit");
        var gsts = core.Where(word => word.Text == "GST").OrderBy(word => word.X0).ToList();
        var totals = core.Where(word => word.Text == "Total").OrderBy(word => word.X0).ToList();

        if (part is null || number is null || description is null || qty is null
            || unit is null || gsts.Count < 3 || totals.Count < 2)
        {
            throw new ParseError(
                "detect",
                "Could not resolve the product grid columns.",
                "Incomplete LBP-I IDG Quote (PDF) product grid header.");
        }

        var headers = new List<(string Name, double Centre)>
        {
            ("Line Item", (header.X0 + header.X1) / 2),
            ("Part Number", (part.X0 + number.X1) / 2),
            ("Description", (description.X0 + description.X1) / 2),
            ("Qty", (qty.X0 + qty.X1) / 2),
            ("Unit Price", (unit.X0 + gsts[0].X1) / 2),
            ("Total Excl", (totals[0].X0 + gsts[1].X1) / 2),
            ("Total Incl", (totals[1].X0 + gsts[2].X1) / 2)
        };

        return PdfTableHelpers.CentredColumnRanges(headers, seamIndex: 4, seamLeft: unit.X0 - 2, header.PageWidth);
    }

    /// <summary>
    /// Line Item# and Components are also separate ruled columns (see <see cref="BuildProductColumns"/>).
    /// Unlike the product grid, "Line" is itself centred in a column much wider than the header
    /// text (the Components column alone runs from 90pt to 270pt wide between the two fixtures),
    /// so the header word's own left edge is not a safe seam — the seam instead comes from the
    /// section rows' line numbers, the only content the Line Item# column carries, which sit one
    /// point inside the real cell edge and land at the same X in both fixtures.
    ///
    /// The scan is confined to integer tokens left of the "Item#" header's right edge, which no
    /// Components content can reach: that column opens at <c>header.X0 + item.X1 - seam</c>, and a
    /// header word never starts left of its own cell, so the boundary is always at or right of
    /// <c>item.X1</c>. A bare minimum over every word in the region would instead let unrelated
    /// page furniture set the seam — this template already prints a marketing paragraph 5pt left
    /// of the grid, and any such text below the heading would shift every boundary with it.
    /// </summary>
    internal static IReadOnlyDictionary<string, (double Left, double Right)> BuildConfigColumns(
        IReadOnlyList<PdfWord> words,
        int headerIndex,
        int configEnd)
    {
        var header = words[headerIndex];
        var core = words.Skip(headerIndex).Take(6)
            .Where(word => word.PageIndex == header.PageIndex)
            .ToList();

        var item = core.FirstOrDefault(word => word.Text == "Item#");
        var components = core.FirstOrDefault(word => word.Text == "Components");
        var description = core.FirstOrDefault(word => word.Text == "Description");
        var qty = core.FirstOrDefault(word => word.Text == "Qty");

        if (item is null || components is null || description is null || qty is null)
        {
            throw new ParseError(
                "extract",
                "Could not resolve the CONFIGURATION DETAILS table header.",
                "Incomplete Line Item# / Components / Description / Qty header row.");
        }

        var headers = new List<(string Name, double Centre)>
        {
            ("Line Item#", (header.X0 + item.X1) / 2),
            ("Components", (components.X0 + components.X1) / 2),
            ("Description", (description.X0 + description.X1) / 2),
            ("Qty", (qty.X0 + qty.X1) / 2)
        };

        // Take() yields nothing for a non-positive count, so an inverted region falls through to
        // the ParseError below rather than throwing out of an empty Min().
        var lineNumbers = words.Skip(headerIndex).Take(configEnd - headerIndex)
            .Where(word => word.X0 < item.X1
                && int.TryParse(word.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToList();

        if (lineNumbers.Count == 0)
        {
            throw new ParseError(
                "extract",
                "Could not resolve the CONFIGURATION DETAILS table columns.",
                "No CONFIGURATION DETAILS section line number to anchor the column seam on.");
        }

        var seamLeft = lineNumbers.Min(word => word.X0) - 1;

        return PdfTableHelpers.CentredColumnRanges(headers, seamIndex: 0, seamLeft, header.PageWidth);
    }

    /// <summary>
    /// Groups raw single-text-line <see cref="PdfRow"/>s (one per <c>RowsBetween</c> output) into
    /// logical table rows and joins each column's text across the group with
    /// <see cref="TextCleaner.JoinSpaced"/>. A row starts a new logical row iff it is on the same
    /// page as the previous kept row and its midline gap from that row is at least
    /// <see cref="BlockGap"/>, or the page changed and the row is itself an anchor — a page change
    /// alone never starts a new logical row, which is what carries a wrapped description across a
    /// page break.
    /// </summary>
    internal static List<PdfRow> GroupLogicalRows(IReadOnlyList<PdfRow> rows, Func<PdfRow, bool> isAnchor)
    {
        var groups = new List<List<PdfRow>>();

        foreach (var row in rows)
        {
            if (groups.Count > 0)
            {
                var previous = groups[^1][^1];
                var samePage = row.PageIndex == previous.PageIndex;
                var startsNewRow = samePage
                    ? row.Midline - previous.Midline >= BlockGap
                    : isAnchor(row);

                if (!startsNewRow)
                {
                    groups[^1].Add(row);
                    continue;
                }
            }

            groups.Add([row]);
        }

        var columnNames = rows.Count > 0 ? rows[0].Cells.Keys.ToList() : [];
        return groups.Select(group => new PdfRow(
            group[0].PageIndex,
            group.Min(row => row.Top),
            group[0].Midline,
            columnNames.ToDictionary(
                name => name,
                name => TextCleaner.JoinSpaced(group.Select(row => PdfTableHelpers.Cell(row.Cells, name)))))
        ).ToList();
    }

    private static bool IsProductAnchor(PdfRow row)
    {
        var lineItem = PdfTableHelpers.Cell(row.Cells, "Line Item");
        var unitPrice = PdfTableHelpers.Cell(row.Cells, "Unit Price");
        return int.TryParse(lineItem, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && unitPrice.Length > 0;
    }

    /// <summary>
    /// Filters header-repeats and the repeating Grand Total row (mining the quoted total and
    /// currency out of the last one), groups the remainder into logical rows, then keeps only the
    /// anchored ones — this is how the marketing paragraph between the grid and CONFIGURATION
    /// DETAILS is dropped, since it never carries a numbered Line Item cell.
    ///
    /// The header itself repeats at every page break as <em>three</em> raw rows too far apart in
    /// midline to merge in <c>RowsBetween</c> — "# Part Number Description Qty", the wrapped
    /// "Unit/Total price excl./incl. GST" line above it, and the "(AUD)" line below it — so all
    /// three shapes must be filtered, or the middle and bottom ones (which carry no Line Item cell
    /// of their own) get silently absorbed into the nearest real row by the logical-row grouper.
    /// </summary>
    internal static (List<ProductLine> Lines, decimal? QuotedTotal, string? Currency) ExtractProductGrid(
        IReadOnlyList<PdfRow> rawRows)
    {
        var filtered = new List<PdfRow>();
        decimal? quotedTotal = null;
        string? currency = null;

        foreach (var row in rawRows)
        {
            var lineItem = PdfTableHelpers.Cell(row.Cells, "Line Item");
            var partNumber = PdfTableHelpers.Cell(row.Cells, "Part Number");
            var unitPrice = PdfTableHelpers.Cell(row.Cells, "Unit Price");

            // The Part Number test is an exact match, not a prefix: it is a second reading of the
            // same row `lineItem == "#"` already catches, so widening it buys nothing and would
            // silently drop a real line whose part number happens to begin with "Part". The Unit
            // Price test stays a prefix because that cell holds the wrapped "Unit price excl. GST"
            // header text, and a real price cell can never begin with letters.
            if (lineItem == "#"
                || partNumber == "Part Number"
                || unitPrice.StartsWith("Unit", StringComparison.Ordinal)
                || unitPrice == "(AUD)")
            {
                continue;
            }

            if (unitPrice == "Grand Total")
            {
                var totalExclText = PdfTableHelpers.Cell(row.Cells, "Total Excl");
                var match = LeadingCurrency().Match(totalExclText);
                if (match.Success)
                {
                    currency = match.Groups[1].Value;
                    quotedTotal = RoundMoney(DecimalCleaner.Parse(totalExclText));
                }

                continue;
            }

            filtered.Add(row);
        }

        var logical = GroupLogicalRows(filtered, IsProductAnchor);

        var lines = new List<ProductLine>();
        foreach (var row in logical)
        {
            if (!IsProductAnchor(row))
            {
                continue;
            }

            var lineItem = PdfTableHelpers.Cell(row.Cells, "Line Item");
            lines.Add(new ProductLine(
                DecimalCleaner.ParseInt(lineItem),
                PdfTableHelpers.Cell(row.Cells, "Part Number"),
                PdfTableHelpers.Cell(row.Cells, "Description"),
                DecimalCleaner.ParseOptionalInt(PdfTableHelpers.Cell(row.Cells, "Qty")) ?? 0,
                PdfTableHelpers.Cell(row.Cells, "Unit Price"),
                PdfTableHelpers.Cell(row.Cells, "Total Excl")));
        }

        return (lines, quotedTotal, currency);
    }

    /// <summary>
    /// A section opens on the row that echoes its product-grid line number (Line Item# parses as
    /// an integer) and is not itself emitted; every following row until the next section (or the
    /// grid terminator) is a component keyed by its Components label, or <see cref="PlaceholderVpn"/>
    /// when that label is blank.
    ///
    /// Component quantities come from <see cref="ComponentQty"/>.
    /// </summary>
    internal static Dictionary<int, List<ConfigComponent>> ExtractConfigSections(
        IReadOnlyList<PdfWord> words,
        int configIndex,
        int configEnd)
    {
        var headerIndex = PdfTableHelpers.FindSequence(words, ["Line", "Item#", "Components", "Description", "Qty"], configIndex)
            ?? throw new ParseError(
                "extract",
                "Could not resolve the CONFIGURATION DETAILS table header.",
                "Missing Line Item# / Components / Description / Qty header row.");

        var header = words[headerIndex];
        var columns = BuildConfigColumns(words, headerIndex, configEnd);

        var rawRows = PdfTableHelpers.RowsBetween(
            words.Take(configEnd), header.Top + 2, header.PageIndex, columns, NoStopToken);

        var filtered = rawRows.Where(row => PdfTableHelpers.Cell(row.Cells, "Components") != "Components").ToList();
        var logical = GroupLogicalRows(
            filtered, row => PdfTableHelpers.Cell(row.Cells, "Components").Length > 0);

        var sections = new Dictionary<int, List<ConfigComponent>>();
        List<ConfigComponent>? currentSection = null;

        foreach (var row in logical)
        {
            var lineItemText = PdfTableHelpers.Cell(row.Cells, "Line Item#");
            var componentsText = PdfTableHelpers.Cell(row.Cells, "Components");
            var descriptionText = PdfTableHelpers.Cell(row.Cells, "Description");
            var qtyText = PdfTableHelpers.Cell(row.Cells, "Qty");

            if (int.TryParse(lineItemText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionNumber))
            {
                currentSection = [];
                sections[sectionNumber] = currentSection;
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            currentSection.Add(new ConfigComponent(
                componentsText,
                descriptionText.Length > 0 ? descriptionText : null,
                ComponentQty(qtyText)));
        }

        // The heading and header both resolved, so a grid that yields nothing is a column or
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
    /// A component's quantity: the printed Qty cell, or 1 when it is blank <em>or a literal 0</em>.
    ///
    /// The configurator prints 0 on an unselected option slot ("HDD Bay NVMe SSD" / "No HDD Bay
    /// NVME SSD" / "0"), which carries exactly the meaning of the far more common unselected row
    /// that prints no quantity at all — and those already default to 1. Passing the 0 through would
    /// put a quantity-0 line in col F of the CRM workbook, which is not a thing a quote line can
    /// be. Children are zero-cost presentation rows; the ordered quantity is the parent's.
    /// </summary>
    internal static int ComponentQty(string qtyText)
        => DecimalCleaner.ParseOptionalInt(qtyText) is int qty && qty > 0 ? qty : 1;

    internal static List<LineItem> AssembleItems(
        IReadOnlyList<ProductLine> lines,
        IReadOnlyDictionary<int, List<ConfigComponent>> configSections)
    {
        var lineNumbers = lines.Select(line => line.Number).ToHashSet();
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

        foreach (var line in lines)
        {
            parentIndex++;
            items.Add(CreateParent(line, parentIndex));

            if (!configSections.TryGetValue(line.Number, out var section))
            {
                continue;
            }

            var childIndex = 0;
            foreach (var component in section)
            {
                childIndex++;
                items.Add(CreateChild(component, line.Number, parentIndex, childIndex));
            }
        }

        return items;
    }

    private static LineItem CreateParent(ProductLine line, int parentIndex)
        => new()
        {
            Vpn = AsciiCleaner.StripNonAscii(line.VpnRaw),
            Description = line.DescriptionRaw.Length > 0 ? AsciiCleaner.StripNonAscii(line.DescriptionRaw) : null,
            Cost = RoundMoney(DecimalCleaner.Parse(line.UnitText, defaultZero: true)),
            Qty = line.Qty,
            LineSequence = parentIndex.ToString(CultureInfo.InvariantCulture),
            Raw = BuildParentRaw(line)
        };

    private static LineItem CreateChild(ConfigComponent component, int lineNumber, int parentIndex, int childIndex)
        => new()
        {
            Vpn = component.LabelRaw.Length > 0 ? AsciiCleaner.StripNonAscii(component.LabelRaw) : PlaceholderVpn,
            Description = component.DescriptionRaw is not null ? AsciiCleaner.StripNonAscii(component.DescriptionRaw) : null,
            Cost = 0m,
            Qty = component.Qty,
            LineSequence = $"{parentIndex}.{childIndex:D2}",
            Raw = BuildChildRaw(component, lineNumber)
        };

    private static IReadOnlyDictionary<string, string> BuildParentRaw(ProductLine line)
    {
        var raw = new Dictionary<string, string>
        {
            ["Line Item"] = line.Number.ToString(CultureInfo.InvariantCulture),
            ["Part Number"] = line.VpnRaw,
            ["Qty"] = line.Qty.ToString(CultureInfo.InvariantCulture)
        };
        if (line.DescriptionRaw.Length > 0) raw["Description"] = line.DescriptionRaw;
        if (line.UnitText.Length > 0) raw["Unit price excl. GST (AUD)"] = line.UnitText;
        if (line.TotalExclText.Length > 0) raw["Total price excl. GST (AUD)"] = line.TotalExclText;
        return raw;
    }

    private static IReadOnlyDictionary<string, string> BuildChildRaw(ConfigComponent component, int lineNumber)
    {
        var raw = new Dictionary<string, string>
        {
            ["Line Item"] = lineNumber.ToString(CultureInfo.InvariantCulture),
            ["Qty"] = component.Qty.ToString(CultureInfo.InvariantCulture)
        };
        if (component.LabelRaw.Length > 0) raw["Components"] = component.LabelRaw;
        if (component.DescriptionRaw is not null) raw["Description"] = component.DescriptionRaw;
        return raw;
    }

    /// <summary>
    /// Splits "Bid Request No.BRPAS010280894 V1" into its parts (PdfPig keeps "No." and the bid
    /// number as separate tokens, unlike LBP-I ISG's "Quote No.:" field, so no merged-token
    /// fallback is needed here). Best-effort: a file missing the match keeps no bid identity and
    /// falls back to the filename stem for the quote number.
    /// </summary>
    private static (string QuoteNumber, string? BidNumber, string? BidRevision) ExtractQuoteMetadata(
        IReadOnlyList<PdfWord> words, int sectionIndex, string path)
    {
        var headerText = PdfTableHelpers.WordStreamText(words.Take(sectionIndex));
        var match = BidRequestField().Match(headerText);
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

    [GeneratedRegex(@"Bid\s+Request\s+No\.?\s*([A-Z]{2,}\d+)\s*(V\d+)?")]
    private static partial Regex BidRequestField();

    [GeneratedRegex(@"^([A-Za-z]+)")]
    private static partial Regex LeadingCurrency();
}

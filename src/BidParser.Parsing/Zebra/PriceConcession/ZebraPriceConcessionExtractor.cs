using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;

namespace BidParser.Parsing.Zebra.PriceConcession;

/// <summary>
/// Shared row-to-ParseResult conversion for Zebra Price Concession documents (PDF and XLS).
/// Both parsers normalise their source format into a list of <see cref="ItemRow"/> structs,
/// then delegate here to produce the canonical <see cref="ParseResult"/>.
/// </summary>
internal static partial class ZebraPriceConcessionExtractor
{
    internal readonly record struct ItemRow(
        string PartNo,
        string Description,
        string MinQty,
        string MaxQty,
        string ListPrice,
        string UnitSpecialPrice,
        string Cancelled);

    /// <summary>
    /// Produces a <see cref="ParseResult"/> from the normalised row sequence.
    /// </summary>
    /// <param name="rows">Normalised item rows extracted by the format-specific parser.</param>
    /// <param name="currency">Currency code from the document's details block (e.g. "AUD").</param>
    /// <param name="quoteNumber">PCR identifier (from document text or filename).</param>
    /// <param name="documentText">
    /// Flattened document prose — the PDF word stream or the XLS document text. The bid number and
    /// revision are read from it, never from the filename.
    /// </param>
    /// <param name="sourceFilename">Original upload filename (basename only).</param>
    /// <param name="parserSlug">Slug of the calling parser, stamped on the metadata.</param>
    internal static ParseResult Build(
        IReadOnlyList<ItemRow> rows,
        string currency,
        string quoteNumber,
        string documentText,
        string sourceFilename,
        string parserSlug)
    {
        var (bidNumber, bidRevision) = BidMetadataFromText(documentText);
        var bid = BidMetadataCleaner.Clean(bidNumber, bidRevision);
        if (bid.BidNumber is null)
        {
            var revision = RevisionField().Match(TextCleaner.Clean(documentText)).Groups[1].Value;
            var filenameBid = BidMetadataCleaner.FromFilename(sourceFilename);
            bid = BidMetadataCleaner.Clean(
                filenameBid.BidNumber ?? QuoteNumberFromFilename(sourceFilename),
                revision.Length > 0 ? revision : filenameBid.BidRevision);
        }
        var items = new List<LineItem>();
        var lineIndex = 1;

        foreach (var rawRow in rows)
        {
            // Normalise PdfPig fusion: when Unit Special Price and Cancelled are typeset
            // close together the NearestNeighbour extractor may merge them into a single
            // word (e.g. "145.00" + "N" → "145.00N"). Split the flag back out.
            var row = SplitFusedCancelled(rawRow);

            var lineSequence = lineIndex.ToString(CultureInfo.InvariantCulture);
            var isCancelled = string.Equals(row.Cancelled.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

            LineItem item;
            if (isCancelled)
            {
                // Cancelled lines: zero pricing, qty = 1, min_qty = blank, comment set.
                // The writer emits a literal 0 for cost/MSRP (never the zero-price sentinel) —
                // CRM does not recognise 0 as a price and pulls the standard price from SAP.
                item = new LineItem
                {
                    Vpn = row.PartNo,
                    Description = row.Description.Length > 0 ? row.Description : null,
                    Cost = 0m,
                    Qty = 1,
                    MinQty = null,
                    Msrp = 0m,
                    IsCancelled = true,
                    Comments = "Cancelled (Standard Price)",
                    LineSequence = lineSequence,
                    Raw = BuildRaw(row)
                };
            }
            else
            {
                // Use first-decimal extraction for both price columns: PdfPig can fuse
                // adjacent numbers (e.g. "1,830.24" + "71.43" → "1,830.2471.43" for
                // List Price, or "145.00" + "N" → "145.00N" handled by SplitFusedCancelled).
                var cost = ParseFirstDecimal(row.UnitSpecialPrice) ?? DecimalCleaner.Parse(row.UnitSpecialPrice, defaultZero: true);
                var maxQty = ParseOptionalQty(row.MaxQty);
                var minQty = ParseOptionalQty(row.MinQty);
                var msrp = ParseFirstDecimal(row.ListPrice);

                item = new LineItem
                {
                    Vpn = row.PartNo,
                    Description = row.Description.Length > 0 ? row.Description : null,
                    Cost = cost,
                    // Qty comes from Min. Qty (the order quantity CRM prices against);
                    // Max. Qty is surfaced in the comment instead.
                    Qty = minQty ?? 1,
                    MinQty = minQty,
                    Msrp = msrp,
                    Comments = maxQty is not null ? $"Max Qty: {maxQty.Value}" : null,
                    IsCancelled = false,
                    LineSequence = lineSequence,
                    Raw = BuildRaw(row)
                };
            }

            items.Add(item);
            lineIndex++;
        }

        // No quoted total exists in Zebra PCR documents — construct ValidationResult directly
        // with Matches = true so the frontend's mismatch modal never fires.
        var computedTotal = items.Sum(i => i.Cost * i.Qty);
        var computedRounded = Math.Round(computedTotal, 2, MidpointRounding.AwayFromZero);
        var validation = new ValidationResult
        {
            Matches = true,
            Difference = 0m,
            ComputedTotal = computedRounded,
            QuotedTotal = null
        };

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
                Supplier = Vendors.Zebra,
                Currency = currency.Length > 0 ? currency : "AUD",
                QuotedTotal = null,
                SourceFilename = sourceFilename,
                ParserSlug = parserSlug
            },
            LineItems = items,
            Validation = validation
        };
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Fusion normalisation

    /// <summary>
    /// PdfPig's NearestNeighbour extractor can merge the Unit Special Price value with
    /// the single-character Cancelled flag when they are typeset in adjacent narrow cells
    /// (e.g. "145.00" + "N" → "145.00N").  When the Cancelled field is empty but the
    /// price field ends with 'Y' or 'N', split the flag back out.
    /// </summary>
    private static ItemRow SplitFusedCancelled(ItemRow row)
    {
        if (row.Cancelled.Length > 0) return row;          // already separated
        if (row.UnitSpecialPrice.Length == 0) return row;  // nothing to split

        var lastChar = row.UnitSpecialPrice[^1];
        if (lastChar is 'Y' or 'N')
        {
            return row with
            {
                UnitSpecialPrice = row.UnitSpecialPrice[..^1].TrimEnd(),
                Cancelled = lastChar.ToString()
            };
        }
        return row;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Numeric helpers

    private static int? ParseOptionalQty(string text)
    {
        return text.Length == 0 ? null : DecimalCleaner.ParseInt(text);
    }

    /// <summary>
    /// Extracts the first decimal number from a string that may contain a fused pair of
    /// numbers (e.g. PdfPig can render "1,830.24" and "71.43" as "1,830.2471.43" when
    /// the List Price and Standard Discount % are typeset with minimal horizontal gap).
    /// Always captures two decimal places which is consistent with Zebra PCR pricing.
    /// </summary>
    private static decimal? ParseFirstDecimal(string text)
    {
        if (text.Length == 0) return null;

        // Match the first occurrence of a decimal number (digits, optional commas, decimal point, digits).
        // The {2,} ensures we capture a proper price (not a lone integer fragment).
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\d[\d,]*\.\d{2}");
        if (!match.Success) return null;

        var clean = match.Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.Parse(clean, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Raw dict

    private static IReadOnlyDictionary<string, string> BuildRaw(ItemRow row)
    {
        var raw = new Dictionary<string, string>();
        if (row.PartNo.Length > 0) raw["Part No."] = row.PartNo;
        if (row.Description.Length > 0) raw["Description"] = row.Description;
        if (row.MinQty.Length > 0) raw["Min. Qty"] = row.MinQty;
        if (row.MaxQty.Length > 0) raw["Max. Qty"] = row.MaxQty;
        if (row.ListPrice.Length > 0) raw["List Price"] = row.ListPrice;
        if (row.UnitSpecialPrice.Length > 0) raw["Unit Special Price"] = row.UnitSpecialPrice;
        if (row.Cancelled.Length > 0) raw["Cancelled"] = row.Cancelled;
        return raw;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Quote number helpers (shared by both parsers)

    /// <summary>
    /// Derives the PCR identifier from the filename.
    /// E.g. "PC# 81391641 (Rev #  2.0)" → "81391641".
    /// Falls back to the raw basename if no 7+-digit run is found.
    /// </summary>
    internal static string QuoteNumberFromFilename(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        // Use (?<!\d) / (?!\d) rather than \b because \b treats underscores as word chars,
        // which would prevent matching the digits in "Zebra_PC_81391641".
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(?<!\d)(\d{7,})(?!\d)");
        return m.Success ? m.Groups[1].Value : name;
    }

    /// <summary>
    /// Reads the bid number and revision from the document prose shared by both formats:
    /// "PC Request ID #:81391641,Revision #:2.0". Revisions stay as written ("2.0" is not "2").
    /// Returns empty strings when the anchor is absent; the caller decides that is fatal.
    /// </summary>
    private static (string BidNumber, string BidRevision) BidMetadataFromText(string documentText)
    {
        var match = PcRequestField().Match(TextCleaner.Clean(documentText));
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    [GeneratedRegex(@"PC\s+Request\s+ID\s*#?\s*:\s*(\d+)\s*,?\s*Revision\s*#?\s*:\s*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex PcRequestField();

    [GeneratedRegex(@"Revision\s*#?\s*:\s*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionField();
}

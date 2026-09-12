using System.Text;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using ExcelDataReader;

namespace BidParser.Parsing.Cisco.CcwQuoteXls;

/// <summary>
/// Parses Cisco "CCW Price Quotation" spreadsheets — legacy OLE binary .xls read via ExcelDataReader.
/// Anchor-based on the '#', 'Part Number', 'Part Description', 'Quantity', 'Unit Net Price',
/// 'Unit List Price (With Duration)', 'Included Item' header row.
/// Emits ANZ-GENERIC No Calculation output.
/// See docs/cisco_ccw_quote_xls.md.
/// </summary>
public sealed class CiscoCcwQuoteXlsParser : IParser
{
    static CiscoCcwQuoteXlsParser()
    {
        // ExcelDataReader needs legacy code-page support to decode .xls strings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string Slug => ParserSlugs.CiscoCcwQuoteXls;
    public string DisplayName => "CCW Quote (XLS)";
    public string Vendor => Vendors.Cisco;
    public string AcceptedMime => "application/vnd.ms-excel";
    public string CrmTemplate => CrmTemplates.NoCalculation;

    public ParseResult Parse(string path)
    {
        var rows = ReadSheet(path);

        var headerRowIndex = FindHeaderRow(rows)
            ?? throw new ParseError("detect", "Could not find the line-item header row.", "Missing required CCW table headers.");

        var columns = MapColumns(rows[headerRowIndex]);
        ValidateCurrency(rows, headerRowIndex);

        var quoteNumber = ExtractQuoteNumber(rows, path);
        var bid = BidMetadataCleaner.CleanRevisionless(ExtractQuoteId(rows));
        var items = ExtractRows(rows, headerRowIndex + 1, columns);

        var computedTotal = decimal.Round(items.Sum(i => i.Cost * i.Qty), 2, MidpointRounding.AwayFromZero);
        var validation = new ValidationResult
        {
            ComputedTotal = computedTotal,
            QuotedTotal = null,
            Matches = true,
            Difference = 0m
        };

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = quoteNumber,
                BidNumber = bid.BidNumber,
                BidRevision = bid.BidRevision,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = null,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

    private static List<object?[]> ReadSheet(string path)
    {
        using var stream = File.OpenRead(path);

        // Magic-byte validation accepts OLE *or* HTML for application/vnd.ms-excel (Zebra ships
        // styled HTML under a .xls name), so a non-OLE .xls reaches us here and the binary reader
        // throws. Surface it as a wrong-file-type selection rather than an unhandled exception.
        IExcelDataReader reader;
        try
        {
            reader = ExcelReaderFactory.CreateBinaryReader(stream);
        }
        catch (Exception ex) when (ex is not ParseError)
        {
            throw new ParseError(
                "detect",
                "File is not a legacy binary .xls workbook.",
                "Could not open the file as a legacy OLE .xls workbook.");
        }

        using var _ = reader;

        var rows = new List<object?[]>();
        if (!reader.Read())
        {
            return rows;
        }

        do
        {
            var row = new object?[reader.FieldCount];
            for (var col = 0; col < reader.FieldCount; col++)
            {
                row[col] = reader.GetValue(col);
            }
            rows.Add(row);
        } while (reader.Read());

        return rows;
    }

    private static int? FindHeaderRow(IReadOnlyList<object?[]> rows)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var labels = row.Select(c => TextCleaner.Clean(c)).ToList();
            if (labels.Contains("#", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Part Number", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Part Description", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Quantity", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Unit Net Price", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Unit List Price (With Duration)", StringComparer.OrdinalIgnoreCase) &&
                labels.Contains("Included Item", StringComparer.OrdinalIgnoreCase))
            {
                return rowIndex;
            }
        }
        return null;
    }

    private sealed record ColumnIndices(
        int Seq,
        int Vpn,
        int Description,
        int Qty,
        int UnitNetPrice,
        int UnitListPriceWithDuration,
        int IncludedItem);

    private static ColumnIndices MapColumns(object?[] headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 0; col < headerRow.Length; col++)
        {
            var cleaned = TextCleaner.Clean(headerRow[col]);
            if (!string.IsNullOrEmpty(cleaned) && !map.ContainsKey(cleaned))
            {
                map[cleaned] = col;
            }
        }

        return new ColumnIndices(
            Seq: GetRequired(map, "#"),
            Vpn: GetRequired(map, "Part Number"),
            Description: GetRequired(map, "Part Description"),
            Qty: GetRequired(map, "Quantity"),
            UnitNetPrice: GetRequired(map, "Unit Net Price"),
            UnitListPriceWithDuration: GetRequired(map, "Unit List Price (With Duration)"),
            IncludedItem: GetRequired(map, "Included Item")
        );
    }

    private static int GetRequired(Dictionary<string, int> map, string label)
    {
        if (!map.TryGetValue(label, out var index))
        {
            throw new ParseError("detect", $"Missing required column '{label}'.", $"Header missing column: {label}");
        }
        return index;
    }

    /// <summary>
    /// Confirms the quote is AUD-denominated. The ANZ-GENERIC template has no foreign-currency
    /// columns, so a non-AUD quote would silently write foreign amounts into local ones. Fails
    /// closed: an absent or empty "Currency:" label is an error, never an implicit pass.
    /// </summary>
    private static void ValidateCurrency(IReadOnlyList<object?[]> rows, int headerRowIndex)
    {
        for (var r = 0; r < headerRowIndex; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                var val = TextCleaner.Clean(row[c]);
                if (string.Equals(val, "Currency:", StringComparison.OrdinalIgnoreCase))
                {
                    for (var right = c + 1; right < row.Length; right++)
                    {
                        var curr = TextCleaner.Clean(row[right]);
                        if (!string.IsNullOrEmpty(curr))
                        {
                            if (!string.Equals(curr, "AUD", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new ParseError("currency", "Quote is not denominated in AUD.", $"Currency '{curr}' is not supported. Only AUD quotes are accepted.");
                            }
                            return;
                        }
                    }
                }
            }
        }

        throw new ParseError(
            "currency",
            "Could not determine the quote currency.",
            "No 'Currency:' value was found above the line-item table. Only AUD quotes are accepted.");
    }

    /// <summary>Legacy QuoteNumber: the header's Quote ID, falling back to the filename.</summary>
    private static string ExtractQuoteNumber(IReadOnlyList<object?[]> rows, string path)
    {
        var quoteId = ExtractQuoteId(rows);
        return quoteId.Length > 0 ? quoteId : Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The "Quote ID:" header value, or empty when the header is absent (no fallback).</summary>
    private static string ExtractQuoteId(IReadOnlyList<object?[]> rows)
    {
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                if (!string.Equals(TextCleaner.Clean(row[c]), "Quote ID:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var right = c + 1; right < row.Length; right++)
                {
                    var quoteId = TextCleaner.Clean(row[right]);
                    if (quoteId.Length > 0)
                    {
                        return quoteId;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static List<LineItem> ExtractRows(IReadOnlyList<object?[]> rows, int startRow, ColumnIndices cols)
    {
        var items = new List<LineItem>();
        var parentIndex = 0;
        var childIndex = 0;

        LineItemBuilder? currentBuilder = null;

        for (var r = startRow; r < rows.Count; r++)
        {
            var row = rows[r];
            var rawSeq = TextCleaner.Clean(GetCell(row, cols.Seq));

            if (string.Equals(rawSeq, "Adjustments", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!string.IsNullOrEmpty(rawSeq))
            {
                if (currentBuilder != null)
                {
                    items.Add(currentBuilder.Build());
                    currentBuilder = null;
                }

                // Cisco numbers lines over three levels ("2.0" parent, "2.1" child, "2.0.1"
                // support SKU); the repo convention is two ("2", "2.01"). A parent is a bare
                // number ("1") or exactly one dot ending in ".0" ("1.0") — note "3.10" is a
                // child, not a parent. Everything else nests under the current parent; a child
                // seen before any parent is promoted rather than dropped, so no line is lost.
                var parts = rawSeq.Split('.');
                var isParent = parts.Length == 1 || (parts.Length == 2 && parts[1] == "0");

                string seqStr;
                if (isParent || parentIndex == 0)
                {
                    parentIndex++;
                    childIndex = 0;
                    seqStr = parentIndex.ToString();
                }
                else
                {
                    childIndex++;
                    seqStr = $"{parentIndex}.{childIndex:D2}";
                }

                var vpn = TextCleaner.Clean(GetCell(row, cols.Vpn));
                var desc = TextCleaner.Clean(GetCell(row, cols.Description));
                var includedItem = TextCleaner.Clean(GetCell(row, cols.IncludedItem));

                // Source text is kept verbatim for Raw; the LineItem carries the derived values.
                var qtyText = TextCleaner.Clean(GetCell(row, cols.Qty));
                var msrpText = TextCleaner.Clean(GetCell(row, cols.UnitListPriceWithDuration));
                var costText = TextCleaner.Clean(GetCell(row, cols.UnitNetPrice));

                var qty = DecimalCleaner.ParseInt(GetCell(row, cols.Qty));
                var msrp = decimal.Round(
                    DecimalCleaner.Parse(GetCell(row, cols.UnitListPriceWithDuration), defaultZero: true),
                    2, MidpointRounding.AwayFromZero);
                var cost = decimal.Round(
                    DecimalCleaner.Parse(GetCell(row, cols.UnitNetPrice), defaultZero: true),
                    2, MidpointRounding.AwayFromZero);

                var raw = new Dictionary<string, string>();
                if (rawSeq.Length > 0) raw["#"] = rawSeq;
                if (vpn.Length > 0) raw["Part Number"] = vpn;
                if (desc.Length > 0) raw["Part Description"] = desc;
                if (qtyText.Length > 0) raw["Quantity"] = qtyText;
                if (msrpText.Length > 0) raw["Unit List Price (With Duration)"] = msrpText;
                if (costText.Length > 0) raw["Unit Net Price"] = costText;
                if (includedItem.Length > 0) raw["Included Item"] = includedItem;

                currentBuilder = new LineItemBuilder(seqStr, vpn, desc, qty, msrp, cost, includedItem, raw);
            }
            else
            {
                var colBText = TextCleaner.Clean(GetCell(row, cols.Vpn));
                if (!string.IsNullOrEmpty(colBText) && currentBuilder != null)
                {
                    currentBuilder.AddContinuation(colBText);
                }
            }
        }

        if (currentBuilder != null)
        {
            items.Add(currentBuilder.Build());
        }

        return items;
    }

    private static object? GetCell(object?[] row, int colIndex)
    {
        return colIndex >= 0 && colIndex < row.Length ? row[colIndex] : null;
    }

    /// <summary>
    /// Accumulates one line item plus any continuation rows that follow it. Cisco puts the
    /// subscription term on a separate row below its item (col A blank, col B holding the text),
    /// so an item cannot be built until the next item row — or the table terminator — is reached.
    /// </summary>
    private sealed class LineItemBuilder(
        string lineSequence,
        string vpn,
        string description,
        int qty,
        decimal msrp,
        decimal cost,
        string includedItem,
        Dictionary<string, string> raw)
    {
        private readonly List<string> _continuations = new();

        public void AddContinuation(string text)
        {
            _continuations.Add(text);
        }

        public LineItem Build()
        {
            var includedPart = $"Included Item/Support: {includedItem}";
            var comments = _continuations.Count > 0
                ? $"{string.Join(" ", _continuations)} {includedPart}"
                : includedPart;

            if (_continuations.Count > 0)
            {
                raw["Continuation"] = string.Join(" ", _continuations);
            }

            return new LineItem
            {
                LineSequence = lineSequence,
                Vpn = vpn,
                Description = description.Length > 0 ? description : null,
                Qty = qty,
                Msrp = msrp,
                Cost = cost,
                Comments = comments,
                Raw = raw
            };
        }
    }
}

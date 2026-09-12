namespace BidParser.Parsing.Pdf;

/// <summary>
/// A reconstructed table row: its page and vertical position, plus the cell text bucketed by column name.
/// <paramref name="Top"/> is the highest word top in the row; <paramref name="Midline"/> is the row's
/// baseline for ordinary horizontal text (or a tight-box midpoint as a fallback), which is the value
/// to use when measuring the gap between rows.
/// </summary>
public sealed record PdfRow(int PageIndex, double Top, double Midline, IReadOnlyDictionary<string, string> Cells);

namespace BidParser.Parsing.Pdf;

public sealed record PdfRow(int PageIndex, double Top, IReadOnlyDictionary<string, string> Cells);

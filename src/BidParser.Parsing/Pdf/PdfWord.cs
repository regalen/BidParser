namespace BidParser.Parsing.Pdf;

public sealed record PdfWord(
    string Text,
    double X0,
    double X1,
    double Top,
    double Bottom,
    int PageIndex,
    double PageWidth);

namespace BidParser.Parsing.Pdf;

/// <summary>
/// One glyph of a <see cref="PdfWord"/> with its horizontal extent. <see cref="Value"/> is a string
/// rather than a char because a ligature covers several characters of the word's text. Retained so a
/// word PdfPig merged across a cell boundary can be cut at the letter gap the boundary falls in.
/// </summary>
public sealed record PdfLetter(string Value, double X0, double X1);

/// <summary>
/// A positioned PDF token. <see cref="Top"/> and <see cref="Bottom"/> retain PdfPig's tight-box
/// geometry; <see cref="LineY"/> is populated for horizontal text when the page-level compatibility
/// path is active. <see cref="PageUsesBaselineGeometry"/> carries that exact page decision through
/// later word transformations, including for rotated words whose own <see cref="LineY"/> is null.
/// </summary>
public sealed record PdfWord(
    string Text,
    double X0,
    double X1,
    double Top,
    double Bottom,
    int PageIndex,
    double PageWidth,
    IReadOnlyList<PdfLetter>? Letters = null,
    double? PageHeight = null,
    double? LineY = null,
    bool PageUsesBaselineGeometry = false);

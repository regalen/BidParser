using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace BidParser.Parsing.Pdf;

/// <summary>
/// Extracts every word from a PDF via PdfPig, converting each to a <see cref="PdfWord"/> with
/// top-left-origin coordinates (PdfPig's bottom-left Y is flipped so that a smaller Top means
/// higher on the page). This flat, positioned word stream is what the parsers reconstruct tables from.
/// </summary>
public static class PdfWordCollector
{
    public static IReadOnlyList<PdfWord> CollectWords(string path)
    {
        var words = new List<PdfWord>();

        using var document = PdfDocument.Open(path);
        var pageIndex = 0;
        foreach (var page in document.GetPages())
        {
            var pageWords = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            // Baselines are a compatibility path for a page whose tight Type 3 boxes have
            // collapsed, not a replacement for normal PDF geometry. This preserves the tight
            // line-pitch and short-glyph behaviour used by the Lenovo and Zebra parsers.
            // PdfPig emits explicit whitespace words whose boxes often remain non-collapsed, so
            // counting them would dilute the signal from the visible text this decision governs.
            var contentWords = pageWords.Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
            var useBaselineGeometry = contentWords.Count > 0
                && contentWords.Count(word => word.BoundingBox.Height <= PdfGeometry.CollapsedBoxHeightEpsilon) * 2
                    >= contentWords.Count;

            foreach (var word in pageWords)
            {
                var box = word.BoundingBox;
                var textBottom = page.Height - box.Top;
                var textTop = textBottom - (box.Top - box.Bottom);
                var letters = word.Letters.ToList();
                var lineY = GetLineY(
                    useBaselineGeometry,
                    page.Height,
                    letters.Select(letter => (letter.StartBaseLine.Y, letter.EndBaseLine.Y)).ToList());
                var usesStableWordGeometry = lineY is not null;
                words.Add(new PdfWord(
                    word.Text,
                    box.Left,
                    box.Right,
                    textTop,
                    textBottom,
                    pageIndex,
                    page.Width,
                    letters.Select(letter => new PdfLetter(
                        letter.Value,
                        usesStableWordGeometry
                            ? Math.Min(letter.StartBaseLine.X, letter.EndBaseLine.X)
                            : letter.BoundingBox.Left,
                        usesStableWordGeometry
                            ? Math.Max(letter.StartBaseLine.X, letter.EndBaseLine.X)
                            : letter.BoundingBox.Right)).ToList(),
                    page.Height,
                    lineY,
                    useBaselineGeometry));
            }

            pageIndex++;
        }

        return words;
    }

    internal static double? GetLineY(
        bool pageUsesBaselineGeometry,
        double pageHeight,
        IReadOnlyList<(double StartY, double EndY)> baselines)
    {
        if (!pageUsesBaselineGeometry
            || baselines.Count == 0
            || baselines.Any(baseline =>
                Math.Abs(baseline.EndY - baseline.StartY) > PdfGeometry.HorizontalBaselineTolerance))
        {
            return null;
        }

        return pageHeight - Median(baselines.Select(baseline => baseline.StartY));
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        return ordered[ordered.Count / 2];
    }
}

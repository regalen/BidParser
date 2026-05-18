using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace BidParser.Parsing.Pdf;

public static class PdfWordCollector
{
    public static IReadOnlyList<PdfWord> CollectWords(string path)
    {
        var words = new List<PdfWord>();

        using var document = PdfDocument.Open(path);
        var pageIndex = 0;
        foreach (var page in document.GetPages())
        {
            foreach (var word in page.GetWords(NearestNeighbourWordExtractor.Instance).SelectMany(PdfPigWordSplitter.Split))
            {
                var box = word.BoundingBox;
                var textBottom = page.Height - box.Top;
                var textTop = textBottom - (box.Top - box.Bottom);
                words.Add(new PdfWord(
                    word.Text,
                    box.Left,
                    box.Right,
                    textTop,
                    textBottom,
                    pageIndex,
                    page.Width));
            }

            pageIndex++;
        }

        return words;
    }
}

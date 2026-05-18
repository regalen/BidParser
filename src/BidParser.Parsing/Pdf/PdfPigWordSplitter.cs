using UglyToad.PdfPig.Content;

namespace BidParser.Parsing.Pdf;

public static class PdfPigWordSplitter
{
    public static IEnumerable<Word> Split(Word word)
    {
        yield return word;
    }
}

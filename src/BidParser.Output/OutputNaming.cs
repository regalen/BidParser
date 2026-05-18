namespace BidParser.Output;

public static class OutputNaming
{
    public static string OutputFilename(string sourceFilename)
    {
        return $"{Path.GetFileNameWithoutExtension(sourceFilename)}_parsed.xlsx";
    }
}

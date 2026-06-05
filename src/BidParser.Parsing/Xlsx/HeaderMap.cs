using BidParser.Domain.Models;

namespace BidParser.Parsing.Xlsx;

public sealed record HeaderMap(int RowNumber, IReadOnlyDictionary<string, int> Columns)
{
    public int Require(string label)
    {
        if (Columns.TryGetValue(label, out var column))
        {
            return column;
        }

        // A missing required column means the selected parser does not recognise this
        // file's layout. Surface it as a "detect"-stage ParseError so ParseService can
        // classify it as a wrong-file-type selection (rather than a genuine failure).
        throw new ParseError(
            "detect",
            $"Missing the '{label}' column.",
            $"Missing required header '{label}'.");
    }
}

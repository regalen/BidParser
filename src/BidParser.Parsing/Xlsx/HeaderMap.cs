namespace BidParser.Parsing.Xlsx;

public sealed record HeaderMap(int RowNumber, IReadOnlyDictionary<string, int> Columns)
{
    public int Require(string label)
    {
        if (Columns.TryGetValue(label, out var column))
        {
            return column;
        }

        throw new InvalidOperationException($"Missing required header '{label}'.");
    }
}

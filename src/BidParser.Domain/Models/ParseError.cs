namespace BidParser.Domain.Models;

public sealed class ParseError(string stage, string hint, string message) : Exception(message)
{
    public string Stage { get; } = stage;
    public string Hint { get; } = hint;
}

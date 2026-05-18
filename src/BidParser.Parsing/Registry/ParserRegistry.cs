using BidParser.Domain.Abstractions;

namespace BidParser.Parsing.Registry;

public sealed class ParserRegistry : IParserRegistry
{
    public IReadOnlyList<IParser> Parsers { get; } = Array.Empty<IParser>();
}

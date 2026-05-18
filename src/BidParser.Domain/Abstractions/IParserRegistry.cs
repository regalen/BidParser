namespace BidParser.Domain.Abstractions;

public interface IParserRegistry
{
    IReadOnlyList<IParser> Parsers { get; }
}

namespace BidParser.Domain.Abstractions;

/// <summary>The ordered set of all registered parsers — the single extension point for new formats.</summary>
public interface IParserRegistry
{
    IReadOnlyList<IParser> Parsers { get; }
}

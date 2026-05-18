namespace BidParser.Domain.Models;

public sealed record ParseResult
{
    public required QuoteMetadata Metadata { get; init; }
    public required IReadOnlyList<LineItem> LineItems { get; init; }
    public required ValidationResult Validation { get; init; }
}

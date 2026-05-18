namespace BidParser.Domain.Models;

public sealed record QuoteMetadata
{
    public required string QuoteNumber { get; init; }
    public required string Supplier { get; init; }
    public required string Currency { get; init; }
    public decimal? QuotedTotal { get; init; }
    public required string SourceFilename { get; init; }
    public required string ParserSlug { get; init; }
}

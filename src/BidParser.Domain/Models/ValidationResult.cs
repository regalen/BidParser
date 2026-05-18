namespace BidParser.Domain.Models;

public sealed record ValidationResult
{
    public required decimal ComputedTotal { get; init; }
    public decimal? QuotedTotal { get; init; }
    public required bool Matches { get; init; }
    public required decimal Difference { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

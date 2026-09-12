namespace BidParser.Domain.Models;

/// <summary>
/// Outcome of total-validation: the computed Σ(cost × qty), the quoted total (if any),
/// whether they match within tolerance, their signed difference, and any warnings.
/// </summary>
public sealed record ValidationResult
{
    public required decimal ComputedTotal { get; init; }
    public decimal? QuotedTotal { get; init; }
    public required bool Matches { get; init; }
    public required decimal Difference { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

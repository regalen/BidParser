namespace BidParser.Domain.Models;

/// <summary>The complete output of a parse: quote metadata, the extracted lines, and the total-validation result.</summary>
public sealed record ParseResult
{
    public required QuoteMetadata Metadata { get; init; }
    public required IReadOnlyList<LineItem> LineItems { get; init; }
    public required ValidationResult Validation { get; init; }

    /// <summary>True when the source quote contains parent/base SKU items ineligible for Dell rebate/MDF.</summary>
    public bool HasRebateIneligibleItems { get; init; }
}

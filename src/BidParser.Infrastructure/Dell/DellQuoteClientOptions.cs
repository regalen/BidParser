namespace BidParser.Infrastructure.Dell;

/// <summary>Process-wide safety limits for a single Dell quote retrieval operation.</summary>
public sealed record DellQuoteClientOptions(long MaxResponseBytes)
{
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(10);
}

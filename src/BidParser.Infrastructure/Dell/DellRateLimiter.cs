using System.Threading.RateLimiting;

namespace BidParser.Infrastructure.Dell;

/// <summary>
/// Enforces Dell's one-quote-request-per-second limit. This limiter is per process, which is correct
/// for the shipped single-container deployment; multiple app instances would require a distributed limiter.
/// </summary>
public sealed class DellRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true,
        QueueLimit = 16,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken ct) =>
        _limiter.AcquireAsync(1, ct);

    public void Dispose() => _limiter.Dispose();
}

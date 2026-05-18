using System.Collections.Concurrent;

namespace BidParser.Api.Auth;

public sealed class AuthRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _buckets = new();

    public RateLimitResult Check(string key, int limit)
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        lock (bucket)
        {
            while (bucket.Count > 0 && now - bucket.Peek() >= Window)
            {
                bucket.Dequeue();
            }

            if (bucket.Count >= limit)
            {
                var retryAfter = Math.Max(1, (int)(Window - (now - bucket.Peek())).TotalSeconds);
                return new RateLimitResult(false, retryAfter);
            }

            bucket.Enqueue(now);
            return new RateLimitResult(true, null);
        }
    }

    public void Clear()
    {
        _buckets.Clear();
    }
}

public sealed record RateLimitResult(bool Allowed, int? RetryAfterSeconds);

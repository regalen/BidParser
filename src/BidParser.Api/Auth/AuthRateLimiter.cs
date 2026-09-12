using System.Collections.Concurrent;

namespace BidParser.Api.Auth;

/// <summary>
/// In-memory sliding-window rate limiter for auth endpoints, keyed by IP / attempted username. Each key
/// holds a 60-second bucket of request timestamps; once the map grows past a threshold, drained buckets
/// are swept opportunistically so unauthenticated traffic can't grow it without bound. Singleton.
/// </summary>
public sealed class AuthRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    // Once the dictionary grows past this many keys, a Check opportunistically
    // sweeps out drained buckets so unauthenticated traffic (each distinct IP /
    // attempted username adds a key) can't grow it without bound.
    private const int SweepThreshold = 1024;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _buckets = new();
    private readonly TimeProvider _time;

    // Parameterless ctor for DI (TimeProvider isn't registered); tests inject a fake.
    public AuthRateLimiter() : this(TimeProvider.System)
    {
    }

    public AuthRateLimiter(TimeProvider timeProvider)
    {
        _time = timeProvider;
    }

    /// <summary>Records an attempt for <paramref name="key"/>; returns Allowed=false with a RetryAfter when the per-window <paramref name="limit"/> is exceeded.</summary>
    public RateLimitResult Check(string key, int limit)
    {
        var now = _time.GetUtcNow();
        var bucket = _buckets.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        RateLimitResult result;
        lock (bucket)
        {
            while (bucket.Count > 0 && now - bucket.Peek() >= Window)
            {
                bucket.Dequeue();
            }

            if (bucket.Count >= limit)
            {
                var retryAfter = Math.Max(1, (int)(Window - (now - bucket.Peek())).TotalSeconds);
                result = new RateLimitResult(false, retryAfter);
            }
            else
            {
                bucket.Enqueue(now);
                result = new RateLimitResult(true, null);
            }
        }

        if (_buckets.Count > SweepThreshold)
        {
            EvictExpired(now);
        }

        return result;
    }

    public void Clear()
    {
        _buckets.Clear();
    }

    internal int BucketCount => _buckets.Count;

    private void EvictExpired(DateTimeOffset now)
    {
        foreach (var pair in _buckets)
        {
            var bucket = pair.Value;
            lock (bucket)
            {
                while (bucket.Count > 0 && now - bucket.Peek() >= Window)
                {
                    bucket.Dequeue();
                }

                if (bucket.Count == 0)
                {
                    // Pair overload only removes when the stored value is still this
                    // instance, so we don't drop a bucket a concurrent Check just
                    // re-populated for the same key.
                    _buckets.TryRemove(pair);
                }
            }
        }
    }
}

/// <summary>Whether an attempt is allowed, and how many seconds to wait before retrying when it isn't.</summary>
public sealed record RateLimitResult(bool Allowed, int? RetryAfterSeconds);

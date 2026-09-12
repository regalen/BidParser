using BidParser.Api.Auth;
using FluentAssertions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class AuthRateLimiterTests
{
    [Fact]
    public void AllowsUpToLimitThenBlocksWithinWindow()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new AuthRateLimiter(time);

        for (var i = 0; i < 5; i++)
        {
            limiter.Check("ip:1.2.3.4", 5).Allowed.Should().BeTrue();
        }

        var blocked = limiter.Check("ip:1.2.3.4", 5);
        blocked.Allowed.Should().BeFalse();
        blocked.RetryAfterSeconds.Should().BeGreaterThan(0);

        // Once the 60s window rolls past, the bucket drains and requests flow again.
        time.Advance(TimeSpan.FromSeconds(61));
        limiter.Check("ip:1.2.3.4", 5).Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvictsDrainedBucketsOncePastThreshold()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new AuthRateLimiter(time);

        // Distinct keys grow the dictionary past the sweep threshold.
        for (var i = 0; i < 2000; i++)
        {
            limiter.Check($"ip:{i}", 5);
        }
        limiter.BucketCount.Should().Be(2000);

        // Advance past the window so every existing bucket is now expired, then a
        // single further Check triggers the opportunistic sweep.
        time.Advance(TimeSpan.FromSeconds(61));
        limiter.Check("ip:trigger", 5);

        // All 2000 expired buckets are evicted; only the fresh "trigger" bucket remains.
        limiter.BucketCount.Should().Be(1);
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}

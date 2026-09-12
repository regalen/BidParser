using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BidParser.Infrastructure.Dell;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class DellHttpClientTests
{
    private const string ClientSecret = "never-log-this-client-secret";
    private const string FullToken = "abc123-never-log-the-full-token";

    [Fact]
    public async Task Token_is_cached_and_expires_in_controls_ninety_percent_refresh()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-04T00:00:00Z"));
        var handler = new StubHandler((_, call, _) => Task.FromResult(TokenResponse($"token-{call}", 60)));
        var provider = CreateTokenProvider(handler, time);
        var config = Config();

        (await provider.GetTokenAsync(config, CancellationToken.None)).Should().Be("token-1");
        (await provider.GetTokenAsync(config, CancellationToken.None)).Should().Be("token-1");
        handler.CallCount.Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(53));
        (await provider.GetTokenAsync(config, CancellationToken.None)).Should().Be("token-1");
        time.Advance(TimeSpan.FromSeconds(1));
        (await provider.GetTokenAsync(config, CancellationToken.None)).Should().Be("token-2");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_cold_cache_requests_use_one_token_fetch()
    {
        var handler = new StubHandler(async (_, _, ct) =>
        {
            await Task.Delay(50, ct);
            return TokenResponse(FullToken, 3600);
        });
        var provider = CreateTokenProvider(handler, TimeProvider.System);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync(Config(), CancellationToken.None)));

        tokens.Should().OnlyContain(token => token == FullToken);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Basic_auth_variant_sends_credentials_only_in_authorization_header()
    {
        AuthenticationHeaderValue? authorization = null;
        string? body = null;
        var handler = new StubHandler(async (request, _, ct) =>
        {
            authorization = request.Headers.Authorization;
            body = await request.Content!.ReadAsStringAsync(ct);
            return TokenResponse(FullToken, 3600);
        });
        var provider = CreateTokenProvider(handler, TimeProvider.System);

        await provider.GetTokenAsync(Config() with { UseBasicAuthForToken = true }, CancellationToken.None);

        authorization.Should().NotBeNull();
        authorization!.Scheme.Should().Be("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!))
            .Should().Be($"client-id:{ClientSecret}");
        body.Should().Be("grant_type=client_credentials");
    }

    [Fact]
    public async Task Token_logs_contain_neither_secret_nor_full_token()
    {
        var logger = new CollectingLogger<DellAuthTokenProvider>();
        var handler = new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600)));
        var provider = CreateTokenProvider(handler, TimeProvider.System, logger);

        await provider.GetTokenAsync(Config(), CancellationToken.None);

        var logs = string.Join('\n', logger.Messages);
        logs.Should().NotContain(ClientSecret);
        logs.Should().NotContain(FullToken);
        logs.Should().Contain(FullToken[..6]);
    }

    [Fact]
    public async Task Quote_request_pins_the_configured_api_version_header()
    {
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        string? acceptsVersion = null;
        string? clientId = null;
        var quoteHandler = new StubHandler((request, _, _) =>
        {
            acceptsVersion = Header(request, "Accepts-version");
            clientId = Header(request, "ClientId");
            return Task.FromResult(JsonResponse("{}"));
        });
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(
            quoteHandler,
            tokenProvider,
            limiter,
            QuoteOptions(),
            Config() with { ApiVersion = "3.0" });

        await client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        // Dell serves an unspecified default version when the header is absent.
        acceptsVersion.Should().Be("3.0");
        clientId.Should().Be("Swagger");
    }

    [Fact]
    public async Task Quote_401_invalidates_token_and_retries_exactly_once()
    {
        var authHandler = new StubHandler((_, call, _) =>
            Task.FromResult(TokenResponse($"token-{call}", 3600)));
        var tokenProvider = CreateTokenProvider(authHandler, TimeProvider.System);
        var authorizations = new ConcurrentQueue<string>();
        var quoteHandler = new StubHandler((request, call, _) =>
        {
            authorizations.Enqueue(request.Headers.Authorization?.Parameter ?? string.Empty);
            return Task.FromResult(call == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse("{\"quoteNumber\":9000000000003}"));
        });
        using var limiter = new DellRateLimiter();
        var client = new DellQuoteClient(
            new HttpClient(quoteHandler),
            new StubSettingsReader(Config()),
            tokenProvider,
            limiter,
            QuoteOptions(),
            TimeProvider.System,
            NullLogger<DellQuoteClient>.Instance);

        var result = await client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        Encoding.UTF8.GetString(result.RawJson).Should().Contain("9000000000003");
        quoteHandler.CallCount.Should().Be(2);
        authHandler.CallCount.Should().Be(2);
        authorizations.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task A_second_401_surfaces_without_looping()
    {
        var authHandler = new StubHandler((_, call, _) =>
            Task.FromResult(TokenResponse($"token-{call}", 3600)));
        var tokenProvider = CreateTokenProvider(authHandler, TimeProvider.System);
        var quoteHandler = new StubHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var limiter = new DellRateLimiter();
        var client = new DellQuoteClient(
            new HttpClient(quoteHandler),
            new StubSettingsReader(Config()),
            tokenProvider,
            limiter,
            QuoteOptions(),
            TimeProvider.System,
            NullLogger<DellQuoteClient>.Instance);

        var act = () => client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        (await act.Should().ThrowAsync<DellApiException>()).Which.Kind.Should().Be(DellApiFailure.Unauthorized);
        quoteHandler.CallCount.Should().Be(2);
        authHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public void Quote_url_uses_separate_escaped_path_segments()
    {
        var uri = DellQuoteClient.BuildQuoteUri(
            DellApiSettingsService.DefaultQuoteUrlTemplate,
            "9000000000003",
            "1",
            "en-au");

        uri.AbsoluteUri.Should().Be(
            "https://apigtwb2c.us.dell.com/PROD/QuoteSearchApi/api/quote/9000000000003/1/en-au");
        uri.AbsoluteUri.Should().NotContain("9000000000003.1");
    }

    [Fact]
    public async Task Token_endpoint_429_is_classified_as_rate_limited()
    {
        var handler = new StubHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var provider = CreateTokenProvider(handler, TimeProvider.System);

        var act = () => provider.GetTokenAsync(Config(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<DellApiException>()).Which;
        exception.Kind.Should().Be(DellApiFailure.RateLimited);
        exception.HttpStatus.Should().Be(429);
    }

    [Fact]
    public async Task Excessive_retry_after_is_clamped()
    {
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        var quoteHandler = new StubHandler((_, call, _) =>
        {
            if (call > 1)
            {
                return Task.FromResult(JsonResponse("{\"quoteNumber\":9000000000003}"));
            }

            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(
            quoteHandler,
            tokenProvider,
            limiter,
            QuoteOptions() with
            {
                OperationTimeout = TimeSpan.FromSeconds(3),
                MaxRetryAfter = TimeSpan.FromMilliseconds(10),
            });

        var result = await client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        Encoding.UTF8.GetString(result.RawJson).Should().Contain("9000000000003");
        quoteHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_deadline_surfaces_as_typed_timeout()
    {
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        var quoteHandler = new StubHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(
            quoteHandler,
            tokenProvider,
            limiter,
            QuoteOptions() with { OperationTimeout = TimeSpan.FromMilliseconds(100) });

        var act = () => client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        (await act.Should().ThrowAsync<DellApiException>()).Which.Kind.Should().Be(DellApiFailure.Timeout);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reclassified_as_a_Dell_timeout()
    {
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        var quoteHandler = new StubHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(quoteHandler, tokenProvider, limiter, QuoteOptions());
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => client.GetQuoteAsync("9000000000003", "1", "en-au", callerCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Declared_oversized_quote_response_is_rejected()
    {
        var content = new ByteArrayContent(new byte[65]);
        content.Headers.ContentLength = 65;

        var exception = await FetchFailureAsync(content, maxResponseBytes: 64);

        exception.Kind.Should().Be(DellApiFailure.Upstream);
    }

    [Fact]
    public async Task Chunked_oversized_quote_response_is_rejected_while_streaming()
    {
        var content = new StreamContent(new NonSeekableReadStream(new byte[65]));
        content.Headers.ContentLength.Should().BeNull();

        var exception = await FetchFailureAsync(content, maxResponseBytes: 64);

        exception.Kind.Should().Be(DellApiFailure.Upstream);
    }

    [Fact]
    public async Task Quote_response_within_limit_is_returned_unchanged()
    {
        var expected = Encoding.UTF8.GetBytes("{\"quoteNumber\":9000000000003}");
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        var quoteHandler = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        }));
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(
            quoteHandler,
            tokenProvider,
            limiter,
            QuoteOptions(expected.Length));

        var result = await client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);

        result.RawJson.Should().Equal(expected);
    }

    [Fact]
    public void Malformed_quote_url_is_a_typed_bad_request()
    {
        var act = () => DellQuoteClient.BuildQuoteUri(
            "not-an-absolute-url/{quoteNumber}/{quoteVersion}/{locale}",
            "9000000000003",
            "1",
            "en-au");

        act.Should().Throw<DellApiException>().Which.Kind.Should().Be(DellApiFailure.BadRequest);
    }

    [Fact]
    public async Task Rate_limiter_spaces_ten_queued_requests_and_never_overlaps_short_work()
    {
        using var limiter = new DellRateLimiter();
        var stopwatch = Stopwatch.StartNew();
        var active = 0;
        var maximumActive = 0;

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var lease = await limiter.AcquireAsync(CancellationToken.None);
            lease.IsAcquired.Should().BeTrue();
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            await Task.Delay(25);
            Interlocked.Decrement(ref active);
        });

        await Task.WhenAll(tasks);

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(8.5));
        maximumActive.Should().Be(1);
    }

    private static DellAuthTokenProvider CreateTokenProvider(
        HttpMessageHandler handler,
        TimeProvider timeProvider,
        ILogger<DellAuthTokenProvider>? logger = null) => new(
        new StubHttpClientFactory(new HttpClient(handler)),
        timeProvider,
        logger ?? NullLogger<DellAuthTokenProvider>.Instance);

    private static DellQuoteClient CreateQuoteClient(
        HttpMessageHandler quoteHandler,
        DellAuthTokenProvider tokenProvider,
        DellRateLimiter limiter,
        DellQuoteClientOptions options,
        DellApiConfig? config = null) => new(
        new HttpClient(quoteHandler) { Timeout = Timeout.InfiniteTimeSpan },
        new StubSettingsReader(config ?? Config()),
        tokenProvider,
        limiter,
        options,
        TimeProvider.System,
        NullLogger<DellQuoteClient>.Instance);

    private static DellQuoteClientOptions QuoteOptions(long maxResponseBytes = 1024 * 1024) =>
        new(maxResponseBytes);

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : null;

    private static async Task<DellApiException> FetchFailureAsync(HttpContent content, long maxResponseBytes)
    {
        var tokenProvider = CreateTokenProvider(
            new StubHandler((_, _, _) => Task.FromResult(TokenResponse(FullToken, 3600))),
            TimeProvider.System);
        var quoteHandler = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        }));
        using var limiter = new DellRateLimiter();
        var client = CreateQuoteClient(
            quoteHandler,
            tokenProvider,
            limiter,
            QuoteOptions(maxResponseBytes));

        var act = () => client.GetQuoteAsync("9000000000003", "1", "en-au", CancellationToken.None);
        return (await act.Should().ThrowAsync<DellApiException>()).Which;
    }

    private static DellApiConfig Config() => new(
        "https://example.test/token",
        "client-id",
        ClientSecret,
        "https://example.test/quote/{quoteNumber}/{quoteVersion}/{locale}",
        "en-au",
        "Swagger",
        "4.0",
        false);

    private static HttpResponseMessage TokenResponse(string token, int expiresIn) =>
        JsonResponse($"{{\"access_token\":\"{token}\",\"expires_in\":{expiresIn}}}");

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (observed >= value) return;
        } while (Interlocked.CompareExchange(ref maximum, value, observed) != observed);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubSettingsReader(DellApiConfig config) : IDellApiSettingsReader
    {
        public Task<DellApiConfig?> GetAsync(CancellationToken ct) => Task.FromResult<DellApiConfig?>(config);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, Interlocked.Increment(ref _callCount), cancellationToken);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

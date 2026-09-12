using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace BidParser.Infrastructure.Dell;

/// <summary>Fetches raw Dell Quote API JSON without coupling transport concerns to the parsers.</summary>
public sealed class DellQuoteClient(
    HttpClient httpClient,
    IDellApiSettingsReader settingsReader,
    DellAuthTokenProvider tokenProvider,
    DellRateLimiter rateLimiter,
    DellQuoteClientOptions options,
    TimeProvider timeProvider,
    ILogger<DellQuoteClient> logger) : IDellQuoteClient
{
    public async Task<DellQuoteFetchResult> GetQuoteAsync(
        string quoteNumber,
        string quoteVersion,
        string locale,
        CancellationToken ct)
    {
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operationCts.CancelAfter(options.OperationTimeout);
        try
        {
            return await GetQuoteCoreAsync(
                quoteNumber,
                quoteVersion,
                locale,
                operationCts.Token);
        }
        catch (OperationCanceledException ex) when (
            !ct.IsCancellationRequested && operationCts.IsCancellationRequested)
        {
            throw new DellApiException(DellApiFailure.Timeout, DellApiMessages.Upstream, null, ex);
        }
    }

    private async Task<DellQuoteFetchResult> GetQuoteCoreAsync(
        string quoteNumber,
        string quoteVersion,
        string locale,
        CancellationToken ct)
    {
        var config = await settingsReader.GetAsync(ct)
            ?? throw new DellApiException(DellApiFailure.NotConfigured, DellApiMessages.NotConfigured);
        var quoteId = $"{quoteNumber}.{quoteVersion}";
        var uri = BuildQuoteUri(config.QuoteUrlTemplate, quoteNumber, quoteVersion, locale);
        var unauthorizedRetries = 0;
        var rateLimitAttempts = 0;
        var upstreamAttempts = 0;

        while (true)
        {
            var token = await tokenProvider.GetTokenAsync(config, ct);
            try
            {
                using var response = await SendOnceAsync(uri, token, config.ClientIdHeader, config.ApiVersion, quoteNumber, quoteVersion, ct);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await ReadBoundedAsync(response.Content, options.MaxResponseBytes, ct);
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                    return new DellQuoteFetchResult(bytes, contentType);
                }

                switch (response.StatusCode)
                {
                    case HttpStatusCode.BadRequest:
                        throw new DellApiException(DellApiFailure.BadRequest, DellApiMessages.BadRequest(quoteId), (int)response.StatusCode);
                    case HttpStatusCode.Unauthorized when unauthorizedRetries++ == 0:
                        tokenProvider.Invalidate();
                        continue;
                    case HttpStatusCode.Unauthorized:
                        throw new DellApiException(DellApiFailure.Unauthorized, DellApiMessages.Unauthorized, (int)response.StatusCode);
                    case HttpStatusCode.Forbidden:
                        throw new DellApiException(DellApiFailure.Forbidden, DellApiMessages.Forbidden(quoteId), (int)response.StatusCode);
                    case HttpStatusCode.NotFound:
                        throw new DellApiException(DellApiFailure.NotFound, DellApiMessages.NotFound(quoteId), (int)response.StatusCode);
                    case HttpStatusCode.TooManyRequests:
                        rateLimitAttempts++;
                        if (rateLimitAttempts >= 5)
                        {
                            throw new DellApiException(DellApiFailure.RateLimited, DellApiMessages.RateLimited, (int)response.StatusCode);
                        }

                        await DelayAsync(RetryAfter(response) ?? Backoff(rateLimitAttempts), ct);
                        continue;
                    default:
                        if ((int)response.StatusCode >= 500)
                        {
                            upstreamAttempts++;
                            if (upstreamAttempts >= 3)
                            {
                                throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream, (int)response.StatusCode);
                            }

                            await DelayAsync(Backoff(upstreamAttempts), ct);
                            continue;
                        }

                        throw new DellApiException(DellApiFailure.BadRequest, DellApiMessages.BadRequest(quoteId), (int)response.StatusCode);
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                upstreamAttempts++;
                if (upstreamAttempts >= 3)
                {
                    throw new DellApiException(DellApiFailure.Timeout, DellApiMessages.Upstream, null, ex);
                }

                await DelayAsync(Backoff(upstreamAttempts), ct);
            }
        }
    }

    public static Uri BuildQuoteUri(
        string template,
        string quoteNumber,
        string quoteVersion,
        string locale)
    {
        var url = template
            .Replace("{quoteNumber}", Uri.EscapeDataString(quoteNumber), StringComparison.Ordinal)
            .Replace("{quoteVersion}", Uri.EscapeDataString(quoteVersion), StringComparison.Ordinal)
            .Replace("{locale}", Uri.EscapeDataString(locale), StringComparison.Ordinal);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new DellApiException(
                DellApiFailure.BadRequest,
                DellApiMessages.BadRequest($"{quoteNumber}.{quoteVersion}"));
        }

        return uri;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        Uri uri,
        string token,
        string clientIdHeader,
        string apiVersion,
        string quoteNumber,
        string quoteVersion,
        CancellationToken ct)
    {
        using var lease = await rateLimiter.AcquireAsync(ct);
        if (!lease.IsAcquired)
        {
            throw new DellApiException(DellApiFailure.RateLimited, DellApiMessages.RateLimited, 429);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("ClientId", clientIdHeader);
        // Without Accepts-version Dell serves an unspecified default; the parsers need post-v1 fields.
        request.Headers.Add("Accepts-version", apiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            logger.LogInformation(
                "Dell quote request {QuoteNumber}/{QuoteVersion} returned {StatusCode} in {ElapsedMs}ms",
                quoteNumber,
                quoteVersion,
                (int)response.StatusCode,
                Convert.ToInt64(Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            return response;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Dell quote request {QuoteNumber}/{QuoteVersion} timed out after {ElapsedMs}ms",
                quoteNumber,
                quoteVersion,
                Convert.ToInt64(Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            throw;
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        TimeSpan? requestedDelay = null;
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            requestedDelay = delta;
        }
        else if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - timeProvider.GetUtcNow();
            requestedDelay = delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return requestedDelay is { } value && value > options.MaxRetryAfter
            ? options.MaxRetryAfter
            : requestedDelay;
    }

    private static TimeSpan Backoff(int attempt)
    {
        var seconds = 2d * Math.Pow(2d, attempt - 1);
        return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble() * 0.25d);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        Task.Delay(delay, timeProvider, ct);

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } contentLength && contentLength > maxBytes)
        {
            throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream);
        }

        await using var source = await content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, ct);
            if (bytesRead == 0)
            {
                return destination.ToArray();
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream);
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }
    }
}

public sealed record DellQuoteFetchResult(byte[] RawJson, string ContentType);

public interface IDellQuoteClient
{
    Task<DellQuoteFetchResult> GetQuoteAsync(
        string quoteNumber,
        string quoteVersion,
        string locale,
        CancellationToken ct);
}

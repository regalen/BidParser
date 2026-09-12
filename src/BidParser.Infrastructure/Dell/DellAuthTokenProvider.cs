using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace BidParser.Infrastructure.Dell;

/// <summary>Caches Dell OAuth client-credentials tokens and serialises cold-cache refreshes.</summary>
public sealed class DellAuthTokenProvider(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<DellAuthTokenProvider> logger) : IDellAuthTokenCache
{
    public const string HttpClientName = "DellAuth";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CachedToken? _cachedToken;
    private int _generation;

    public async Task<string> GetTokenAsync(DellApiConfig config, CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cachedToken);
        if (IsUsable(cached))
        {
            return cached!.AccessToken;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            cached = Volatile.Read(ref _cachedToken);
            if (IsUsable(cached))
            {
                return cached!.AccessToken;
            }

            var generation = Volatile.Read(ref _generation);
            var fetched = await FetchTokenAsync(config, ct);
            if (generation == Volatile.Read(ref _generation))
            {
                Volatile.Write(ref _cachedToken, fetched);
            }

            return fetched.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _cachedToken, null);
    }

    private bool IsUsable(CachedToken? token) =>
        token is not null && timeProvider.GetUtcNow() < token.RefreshAtUtc;

    private async Task<CachedToken> FetchTokenAsync(DellApiConfig config, CancellationToken ct)
    {
        logger.LogInformation("Fetching a Dell OAuth access token");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenUrl);
        var values = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
        };

        if (config.UseBasicAuthForToken)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
        else
        {
            values.Add(new("client_id", config.ClientId));
            values.Add(new("client_secret", config.ClientSecret));
        }

        request.Content = new FormUrlEncodedContent(values);
        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DellApiException(DellApiFailure.Timeout, DellApiMessages.Upstream, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream, null, ex);
        }

        using (response)
        {
            return await ReadTokenAsync(response, ct);
        }
    }

    private async Task<CachedToken> ReadTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var kind = response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => DellApiFailure.Unauthorized,
                HttpStatusCode.TooManyRequests => DellApiFailure.RateLimited,
                _ => DellApiFailure.Upstream,
            };
            var message = kind switch
            {
                DellApiFailure.Unauthorized => DellApiMessages.Unauthorized,
                DellApiFailure.RateLimited => DellApiMessages.RateLimited,
                _ => DellApiMessages.Upstream,
            };
            throw new DellApiException(kind, message, (int)response.StatusCode);
        }

        TokenResponse? tokenResponse;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            tokenResponse = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream, (int)response.StatusCode, ex);
        }

        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken) || tokenResponse.ExpiresIn <= 0)
        {
            throw new DellApiException(DellApiFailure.Upstream, DellApiMessages.Upstream, (int)response.StatusCode);
        }

        var refreshAt = timeProvider.GetUtcNow().AddSeconds(tokenResponse.ExpiresIn * 0.9d);
        var prefixLength = Math.Min(6, tokenResponse.AccessToken.Length);
        logger.LogInformation(
            "Fetched Dell OAuth token {TokenPrefix}... with lifetime {ExpiresInSeconds}s",
            tokenResponse.AccessToken[..prefixLength],
            tokenResponse.ExpiresIn);
        return new CachedToken(tokenResponse.AccessToken, refreshAt);
    }

    /// <summary>
    /// The OAuth 2.0 client-credentials token response (RFC 6749 §5.1). The snake_case wire names are
    /// mandated by the spec, so they are pinned here with explicit attributes rather than inferred from
    /// a serializer-wide naming policy — this is the one place the mapping belongs.
    /// </summary>
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAtUtc);
}

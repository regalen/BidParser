using System.IO;
using System.Net.Http;
namespace BidParser.Desktop.Configuration;

/// <summary>
/// Fetches a public document under a fixed transport policy: no credentials, no redirects, no
/// retries, an independent per-request timeout, and a hard size cap read before the body is
/// materialised. Every failure is routine and silent — the caller keeps whatever it already had.
/// </summary>
public sealed class PublicDocumentFetcher(HttpClient client)
{
    /// <summary>Creates the single application-lifetime client, with system proxy and TLS defaults.</summary>
    public static HttpClient CreateClient(string userAgent)
    {
        // A redirect is treated as a failure rather than followed: the configuration lives at a
        // known URL, and anything redirecting away from it is not that document.
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    /// <returns>The document text, or null for any failure at all.</returns>
    public async Task<string?> TryFetchAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        // Linked rather than HttpClient.Timeout so the three startup requests each get their own
        // five seconds instead of sharing one budget.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DesktopEndpoints.RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            // Content type is deliberately not checked: raw.githubusercontent.com serves JSON as
            // text/plain. The parsers decide whether the bytes are a valid document.
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadCappedAsync(stream, maxBytes, timeout.Token);
        }
        catch (Exception)
        {
            // Timeout, DNS failure, TLS failure, proxy refusal, blocked host, malformed response:
            // all routine, none worth interrupting the user for.
            return null;
        }
    }

    /// <summary>Reads at most <paramref name="maxBytes"/>; a body that exceeds the cap is rejected.</summary>
    private static async Task<string?> ReadCappedAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        using var content = new MemoryStream();

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (content.Length + read > maxBytes)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(content.ToArray());
    }
}

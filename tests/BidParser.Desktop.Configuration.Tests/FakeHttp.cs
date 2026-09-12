using System.Net;
using System.Text;

namespace BidParser.Desktop.Configuration.Tests;

/// <summary>Serves one scripted response, or throws, without touching the network.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    private FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        => this.respond = respond;

    public List<HttpRequestMessage> Requests { get; } = [];

    public static FakeHttpHandler Returning(
        HttpStatusCode status,
        string body,
        string contentType = "text/plain",
        long? contentLengthOverride = null)
        => new((_, _) =>
        {
            var content = new StringContent(body, Encoding.UTF8, contentType);
            if (contentLengthOverride is { } length)
            {
                content.Headers.ContentLength = length;
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        });

    public static FakeHttpHandler Redirecting(string location)
        => new((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = new StringContent(string.Empty) };
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        });

    public static FakeHttpHandler Throwing(Exception error)
        => new((_, _) => Task.FromException<HttpResponseMessage>(error));

    /// <summary>Never answers, so the per-request timeout is what ends the call.</summary>
    public static FakeHttpHandler Hanging()
        => new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(request, cancellationToken);
    }
}

internal sealed class UnreachableException : Exception;

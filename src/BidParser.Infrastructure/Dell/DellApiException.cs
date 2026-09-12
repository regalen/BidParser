namespace BidParser.Infrastructure.Dell;

/// <summary>A sanitised Dell transport failure safe to map to the parse error response.</summary>
public sealed class DellApiException(
    DellApiFailure kind,
    string userMessage,
    int? httpStatus = null,
    Exception? innerException = null) : Exception(userMessage, innerException)
{
    public DellApiFailure Kind { get; } = kind;
    public string UserMessage { get; } = userMessage;
    public int? HttpStatus { get; } = httpStatus;
}

public enum DellApiFailure
{
    NotConfigured,
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    Upstream,
    Timeout,
    BadRequest,
}

public static class DellApiMessages
{
    public const string NotConfigured = "The Dell quote API is not configured. Ask an administrator to set it up in Settings → Dell API.";
    public const string Unauthorized = "Dell rejected the stored API credentials. Ask an administrator to check Settings → Dell API.";
    public const string RateLimited = "Dell is rate-limiting requests. Wait a moment and try again.";
    public const string Upstream = "Dell's quote service is not responding. Try again shortly.";

    public static string NotFound(string quoteId) =>
        $"Dell has no quote {quoteId}. Check the quote number and version and try again.";

    public static string Forbidden(string quoteId) =>
        $"This account is not entitled to view quote {quoteId}. Contact the Dell Integration Team.";

    public static string BadRequest(string quoteId) =>
        $"Dell rejected the request for quote {quoteId}. Check the quote ID format.";
}

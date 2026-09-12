namespace BidParser.Infrastructure.Entities;

/// <summary>
/// Singleton Dell Quote API configuration row (always <c>Id == 1</c>). The client secret is stored
/// only as ASP.NET Data Protection ciphertext; plaintext exists transiently in the settings service.
/// Timestamps are stamped centrally by <see cref="Persistence.AppDbContext"/>.
/// </summary>
public sealed class DellApiSettings
{
    public int Id { get; set; }
    public required string TokenUrl { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecretProtected { get; set; }
    public required string QuoteUrlTemplate { get; set; }
    public required string DefaultLocale { get; set; }
    public required string ClientIdHeader { get; set; }

    /// <summary>
    /// Value sent in the Dell <c>Accepts-version</c> request header. Dell applies an unspecified
    /// default version when the header is absent, so this is pinned explicitly.
    /// </summary>
    public required string ApiVersion { get; set; }
    public bool UseBasicAuthForToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

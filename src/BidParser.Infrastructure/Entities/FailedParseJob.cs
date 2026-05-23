namespace BidParser.Infrastructure.Entities;

using BidParser.Domain.Models;

public sealed class FailedParseJob
{
    public int Id { get; set; }

    // Nullable FK — user may be deleted later. Snapshot fields below preserve identity.
    public int? UserId { get; set; }
    public required string UserUsername { get; set; }
    public string? UserName { get; set; }

    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }

    public required string SourceFilename { get; set; }
    public required string SourcePath { get; set; }     // path under /data/files/originals/

    public required FailureCategory Category { get; set; }

    // Populated for ParseError only — null for unhandled exceptions.
    public string? Stage { get; set; }
    public string? Hint { get; set; }
    public string? Message { get; set; }

    // ex.ToString() — type, message, stack trace, inner exceptions. TEXT, uncapped.
    public required string ErrorDetail { get; set; }

    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}

public enum FailureCategory
{
    MagicByteMismatch,
    ParserError,
    UnhandledException,
}

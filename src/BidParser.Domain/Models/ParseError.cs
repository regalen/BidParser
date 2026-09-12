namespace BidParser.Domain.Models;

/// <summary>
/// A parser-raised failure carrying a machine-readable <see cref="Stage"/> and a user-facing
/// <see cref="Hint"/>. Stage "detect" is special: it signals a wrong-file-type selection and is
/// reclassified to "fileType" by ParseService (never a recorded failure). Other stages
/// ("currency", "extract", "upload", …) are genuine failures surfaced as HTTP 422.
/// </summary>
public sealed class ParseError(string stage, string hint, string message) : Exception(message)
{
    /// <summary>Machine-readable failure stage; drives the SPA's error-modal branching.</summary>
    public string Stage { get; } = stage;
    /// <summary>Short user-facing hint shown in the error modal.</summary>
    public string Hint { get; } = hint;
}

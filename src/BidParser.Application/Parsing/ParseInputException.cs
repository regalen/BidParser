namespace BidParser.Application.Parsing;

public enum ParseInputErrorKind
{
    UnknownParser,
    VendorMismatch,
    UnsupportedExtension,
    ExtensionMismatch,
    MagicByteMismatch,
    AutoDetectionFailed,
    WrongFileType,
    UnsupportedTemplate,
    MissingRequiredOption,
    UnsupportedSplit,
    SourceTooLarge,
    InvalidDestination,
    DestinationExists
}

/// <summary>A host-neutral validation/selection error for local quote processing.</summary>
public sealed class ParseInputException(
    ParseInputErrorKind kind,
    string message,
    string? stage = null,
    string? suggestedParserName = null) : Exception(message)
{
    public ParseInputErrorKind Kind { get; } = kind;
    public string? Stage { get; } = stage;
    public string? SuggestedParserName { get; } = suggestedParserName;
}

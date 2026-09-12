namespace BidParser.Application.Parsing;

/// <summary>Host-neutral projection of a concrete parser or synthesized Auto entry.</summary>
public sealed record ParserDescriptor(
    string Slug,
    string DisplayName,
    string Vendor,
    string AcceptedMime,
    IReadOnlyList<string> AcceptedMimes,
    IReadOnlyList<string> AcceptedExtensions,
    string CrmTemplate,
    IReadOnlyList<string> AvailableTemplates,
    bool SupportsSubComponentDetail,
    bool SupportsSolutionIdSplit,
    bool SupportsOnCost,
    string SolutionSplitLabel,
    bool IsAuto);

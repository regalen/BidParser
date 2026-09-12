using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;

namespace BidParser.Application.Parsing;

/// <summary>Builds the ordered parser catalog shared by HTTP and local desktop hosts.</summary>
public sealed class ParserCatalog(IParserRegistry registry)
{
    public IReadOnlyList<ParserDescriptor> GetAll()
    {
        var descriptors = new List<ParserDescriptor>();
        var seenVendors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parser in registry.Parsers)
        {
            if (seenVendors.Add(parser.Vendor)
                && AutoDetectTypes.ForVendor(parser.Vendor) is { } auto)
            {
                var autoMimes = registry.Parsers
                    .Where(candidate => candidate.Vendor == auto.Vendor)
                    .SelectMany(candidate => candidate.AcceptedMimes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                descriptors.Add(new ParserDescriptor(
                    auto.Slug,
                    AutoDetectTypes.DisplayName,
                    auto.Vendor,
                    AcceptedMime: string.Empty,
                    autoMimes,
                    SourceFormatInspector.ExtensionsFor(autoMimes),
                    auto.CrmTemplate,
                    auto.AvailableTemplates,
                    SupportsSubComponentDetail: false,
                    auto.SupportsSolutionIdSplit,
                    auto.SupportsOnCost,
                    SolutionSplitLabel: "Solution ID",
                    IsAuto: true));
            }

            descriptors.Add(new ParserDescriptor(
                parser.Slug,
                parser.DisplayName,
                parser.Vendor,
                parser.AcceptedMime,
                parser.AcceptedMimes,
                SourceFormatInspector.ExtensionsFor(parser.AcceptedMimes),
                parser.CrmTemplate,
                parser.AvailableTemplates,
                parser.SupportsSubComponentDetail,
                parser.SupportsSolutionIdSplit,
                parser.SupportsOnCost,
                parser.SolutionSplitLabel,
                IsAuto: false));
        }

        return descriptors;
    }
}

using BidParser.Domain.Abstractions;
using BidParser.Parsing.Nutanix.SoftwareOnlyPdf;

namespace BidParser.Parsing.Registry;

public sealed class ParserRegistry : IParserRegistry
{
    public IReadOnlyList<IParser> Parsers { get; } =
    [
        new NutanixSoftwareOnlyPdfParser()
    ];
}

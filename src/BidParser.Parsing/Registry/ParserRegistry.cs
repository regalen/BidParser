using BidParser.Domain.Abstractions;
using BidParser.Parsing.Hp.BidXlsx;
using BidParser.Parsing.Hp.OneConfigXlsx;
using BidParser.Parsing.Lenovo.BrdaDcgPdf;
using BidParser.Parsing.Lenovo.BrdaDcgXlsx;
using BidParser.Parsing.Nutanix.HardwareOnlyPdf;
using BidParser.Parsing.Nutanix.HardwareOnlyXlsx;
using BidParser.Parsing.Nutanix.RenewalPdf;
using BidParser.Parsing.Nutanix.SoftwareOnlyPdf;
using BidParser.Parsing.Nutanix.SoftwareOnlyXlsx;

namespace BidParser.Parsing.Registry;

public sealed class ParserRegistry : IParserRegistry
{
    public IReadOnlyList<IParser> Parsers { get; } =
    [
        new NutanixSoftwareOnlyPdfParser(),
        new NutanixSoftwareOnlyXlsxParser(),
        new NutanixRenewalPdfParser(),
        new NutanixHardwareOnlyPdfParser(),
        new NutanixHardwareOnlyXlsxParser(),
        new HpBidXlsxParser(),
        new HpOneConfigXlsxParser(),
        new LenovoBrdaDcgPdfParser(),
        new LenovoBrdaDcgXlsxParser()
    ];
}

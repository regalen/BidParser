using BidParser.Domain.Abstractions;
using BidParser.Parsing.Hp.BidXlsx;
using BidParser.Parsing.Hp.GlobalBidXlsx;
using BidParser.Parsing.Hp.OneConfigXlsx;
using BidParser.Parsing.Hp.ServicesXlsx;
using BidParser.Parsing.Hpe.BidXlsx;
using BidParser.Parsing.Lenovo.LbpeIsgXls;
using BidParser.Parsing.Lenovo.LbpiIdgPdf;
using BidParser.Parsing.Lenovo.LbpiIsgPdf;
using BidParser.Parsing.Nutanix.HardwareOnlyPdf;
using BidParser.Parsing.Zebra.PriceConcession;
using BidParser.Parsing.Nutanix.HardwareOnlyXlsx;
using BidParser.Parsing.Nutanix.RenewalPdf;
using BidParser.Parsing.Nutanix.RenewalXlsx;
using BidParser.Parsing.Nutanix.SoftwareOnlyPdf;
using BidParser.Parsing.Nutanix.SoftwareOnlyXlsx;
using BidParser.Parsing.Dell.CtoJson;
using BidParser.Parsing.Dell.Apos;
using BidParser.Parsing.Cisco.CcwQuoteXls;
using BidParser.Parsing.Datalogic.QuotePdf;
using BidParser.Parsing.Epson.QuotePdf;
using BidParser.Parsing.Strike.QuotePdf;
using BidParser.Parsing.Trellix.QuotePdf;

namespace BidParser.Parsing.Registry;

/// <summary>
/// The explicit, ordered list of every parser the app exposes — the single extension point for a
/// new format (append one line here). Registration order is the order surfaced by /api/parsers and
/// the UI dropdown. Deliberately no assembly scanning: the wiring is meant to be visible.
/// </summary>
public sealed class ParserRegistry : IParserRegistry
{
    public IReadOnlyList<IParser> Parsers { get; } =
    [
        new NutanixSoftwareOnlyPdfParser(),
        new NutanixSoftwareOnlyXlsxParser(),
        new NutanixRenewalPdfParser(),
        new NutanixRenewalXlsxParser(),
        new NutanixHardwareOnlyPdfParser(),
        new NutanixHardwareOnlyXlsxParser(),
        new HpBidXlsxParser(),
        new HpGlobalBidXlsxParser(),
        new HpOneConfigXlsxParser(),
        new HpServicesXlsxParser(),
        new HpeBidXlsxParser(),
        new LenovoLbpeIsgXlsParser(),
        new LenovoLbpiIsgPdfParser(),
        new LenovoLbpiIdgPdfParser(),
        new ZebraPriceConcessionPdfParser(),
        new ZebraPriceConcessionXlsParser(),
        new DellCtoJsonParser(),
        new DellAposJsonParser(),
        new CiscoCcwQuoteXlsParser(),
        new DatalogicQuotePdfParser(),
        new EpsonQuotePdfParser(),
        new StrikeQuotePdfParser(),
        new TrellixQuotePdfParser()
    ];
}

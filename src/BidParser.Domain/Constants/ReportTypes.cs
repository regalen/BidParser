namespace BidParser.Domain.Constants;

/// <summary>
/// Hardcoded mapping from a parser slug (vendor + file type combination) to the
/// "report type" users are told to use when sending the quote to the customer.
/// Single source of truth shared by both products: the web app surfaces it via
/// <c>/api/parsers</c> and the desktop app reads it directly. A slug absent from
/// the map renders no report-type guidance.
/// </summary>
public static class ReportTypes
{
    public const string Standard = "Standard";
    public const string StartEndDate = "Start End Date";
    public const string HardwareSoh = "Hardware SOH";

    private static readonly IReadOnlyDictionary<string, string> BySlug = new Dictionary<string, string>
    {
        [ParserSlugs.NutanixSoftwareOnlyPdf] = Standard,
        [ParserSlugs.NutanixSoftwareOnlyXlsx] = Standard,
        [ParserSlugs.NutanixRenewalPdf] = StartEndDate,
        [ParserSlugs.NutanixRenewalXlsx] = StartEndDate,
        [ParserSlugs.NutanixHardwareOnlyPdf] = Standard,
        [ParserSlugs.NutanixHardwareOnlyXlsx] = Standard,
        [ParserSlugs.HpBidXlsx] = HardwareSoh,
        [ParserSlugs.HpGlobalBidXlsx] = HardwareSoh,
        [ParserSlugs.HpOneConfigXlsx] = Standard,
        [ParserSlugs.LenovoBrdaDcgPdf] = HardwareSoh,
        [ParserSlugs.LenovoBrdaDcgXlsx] = HardwareSoh,
        [ParserSlugs.ZebraPriceConcessionPdf] = HardwareSoh,
        [ParserSlugs.ZebraPriceConcessionXls] = HardwareSoh,
    };

    /// <summary>Report type for a parser slug, or <c>null</c> when none is mapped.</summary>
    public static string? For(string slug) => BySlug.GetValueOrDefault(slug);
}

namespace BidParser.Domain.Constants;

/// <summary>
/// An auto-detect dropdown entry for one vendor: sentinel slug, the default CRM template, and
/// every template the entry advertises (must be the intersection of the vendor's parsers).
/// </summary>
public sealed record AutoDetectType(
    string Vendor,
    string Slug,
    string CrmTemplate,
    IReadOnlyList<string> AvailableTemplates,
    bool SupportsSolutionIdSplit = false,
    bool SupportsOnCost = false);

/// <summary>
/// The vendors that expose an "Auto (detect format)" file-type entry. Not parsers: /api/parsers
/// synthesizes a DTO per entry and ParseService resolves the real parser via FormatDetection.
/// A vendor's Auto entry may only advertise templates common to ALL of that vendor's parsers.
/// </summary>
public static class AutoDetectTypes
{
    public const string DisplayName = "Auto (detect format)";

    public const string NoMatchMessage = "Auto detection was not able to identify the file type. Please try again using the file type drop down and choose the file type.";

    public static string DetectedButFailed(string formatName) =>
        $"The file was detected as {formatName} but could not be parsed. Select the specific file type and try again.";

    public static readonly IReadOnlyList<AutoDetectType> All =
    [
        new(Vendors.Nutanix, ParserSlugs.NutanixAuto, CrmTemplates.ForeignUplift,
            [CrmTemplates.ForeignUplift]),
        new(Vendors.Zebra, ParserSlugs.ZebraAuto, CrmTemplates.NoCalculation,
            [CrmTemplates.NoCalculation, CrmTemplates.Uplift], SupportsOnCost: true),
        // Both Lenovo ISG parsers split by Solution ID, so the Auto entry may advertise it too.
        // Lenovo IDG (LBP-I IDG) has no Solution IDs and no Auto entry — it is a single format.
        new(Vendors.LenovoIsg, ParserSlugs.LenovoAuto, CrmTemplates.NoCalculation,
            [CrmTemplates.NoCalculation, CrmTemplates.Uplift],
            SupportsSolutionIdSplit: true),
        // Dell is No Calculation only: the Quote API path writes through AnzGeneric without
        // FX or margin inputs, so Uplift is not offered on either Dell parser.
        new(Vendors.Dell, ParserSlugs.DellAuto, CrmTemplates.NoCalculation,
            [CrmTemplates.NoCalculation]),
        // Trellix PDF and XLSM share both templates and have independent Detect signatures.
        new(Vendors.Trellix, ParserSlugs.TrellixAuto, CrmTemplates.NoCalculation,
            [CrmTemplates.NoCalculation, CrmTemplates.Uplift])
    ];

    public static AutoDetectType? ForVendor(string vendor) =>
        All.FirstOrDefault(a => a.Vendor == vendor);

    public static AutoDetectType? BySlug(string slug) =>
        All.FirstOrDefault(a => a.Slug == slug);
}

namespace BidParser.Desktop.Configuration;

/// <summary>
/// Every public endpoint and transport limit in one place.
/// <para>
/// Trust boundary: these documents are public, unauthenticated and world-editable through ordinary
/// commits to this repository. They may influence <em>wording and safe numeric
/// defaults only</em>. Parser anchors, extraction algorithms, capabilities, validation, output
/// mappings, filenames, CRM rules, URLs and every other executable behaviour are compiled in and
/// cannot be reached from here.
/// </para>
/// </summary>
public static class DesktopEndpoints
{
    public const string PublicRepository = "https://github.com/regalen/BidParser";

    public static readonly Uri GuidanceMessages =
        new("https://raw.githubusercontent.com/regalen/BidParser/main/config/guidanceMessages.json");

    public static readonly Uri VendorDefaults =
        new("https://raw.githubusercontent.com/regalen/BidParser/main/config/vendorDefaults.json");

    public static readonly Uri LatestRelease =
        new("https://api.github.com/repos/regalen/BidParser/releases/latest");

    /// <summary>Independent per-request budget; three startup requests never queue behind each other.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public const long MaxConfigurationBytes = 256 * 1024;
    public const long MaxReleaseMetadataBytes = 128 * 1024;

    /// <summary>
    /// Direct download of the latest release's executable, compiled in from this fixed base — never
    /// a URL the API happened to return. GitHub resolves <c>releases/latest</c> to the same latest
    /// stable release <see cref="LatestRelease"/> reports, and every release publishes exactly one
    /// <c>BidParser.exe</c>.
    /// </summary>
    public static readonly Uri LatestDownload =
        new($"{PublicRepository}/releases/latest/download/BidParser.exe");
}

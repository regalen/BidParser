using System.IO;
using System.Reflection;
using BidParser.Desktop.Services;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// The configuration the window is reading right now. It starts as the bundled documents, validated
/// synchronously so the window is usable before any network call is made, and each document is
/// replaced independently if and when a valid remote copy arrives.
/// </summary>
public sealed class DesktopConfiguration : IGuidanceProvider, IVendorDefaultsSource
{
    private GuidanceCatalog guidance = GuidanceCatalog.Empty;
    private VendorDefaultsCatalog defaults = VendorDefaultsCatalog.Empty;

    private DesktopConfiguration(GuidanceCatalog guidance, VendorDefaultsCatalog defaults)
    {
        this.guidance = guidance;
        this.defaults = defaults;
    }

    /// <summary>Loads the copies embedded in the executable. Always succeeds; a broken bundled document degrades to nothing.</summary>
    public static DesktopConfiguration FromBundled(
        IReadOnlySet<string> knownSlugs, IReadOnlySet<string> knownVendors)
        => new(
            ReadBundledGuidance(knownSlugs) ?? GuidanceCatalog.Empty,
            ReadBundledVendorDefaults(knownVendors) ?? VendorDefaultsCatalog.Empty);

    public static GuidanceCatalog? ReadBundledGuidance(IReadOnlySet<string> knownSlugs)
        => ReadResource("guidanceMessages.json") is { } json
            ? GuidanceCatalogReader.TryRead(json, knownSlugs)
            : null;

    public static VendorDefaultsCatalog? ReadBundledVendorDefaults(IReadOnlySet<string> knownVendors)
        => ReadResource("vendorDefaults.json") is { } json
            ? VendorDefaultsCatalogReader.TryRead(json, knownVendors)
            : null;

    public GuidanceDocument? MessageFor(string parserSlug) => guidance.For(parserSlug);

    public VendorDefaults? ForVendor(string vendor) => defaults.For(vendor);

    public void ReplaceGuidance(GuidanceCatalog catalog) => guidance = catalog;

    public void ReplaceVendorDefaults(VendorDefaultsCatalog catalog) => defaults = catalog;

    private static string? ReadResource(string filename)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            candidate => candidate.EndsWith($".{filename}", StringComparison.Ordinal));
        if (name is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

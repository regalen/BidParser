using BidParser.Desktop.Configuration;

namespace BidParser.Desktop.Services;

/// <summary>A file chosen through, or a destination named by, the Windows shell dialogs.</summary>
public interface IFileDialogService
{
    string? ChooseSourceFile(string filter);
    string? ChooseOutputFile(string suggestedName, string filter, string? initialDirectory);
}

public interface IClipboardService
{
    void SetText(string text);
}

public interface IShellLauncher
{
    void Open(string target);

    /// <summary>Opens the containing folder with the file selected.</summary>
    void RevealInFolder(string path);
}

public interface IThemeService
{
    void Start();
}

/// <summary>
/// Pricing values prefilled when a vendor is selected. Defaults never create a capability: a value
/// for a field the selected template does not use stays collapsed and is never forwarded.
/// </summary>
public sealed record VendorDefaults(
    decimal? FxRate = null,
    decimal? Margin = null,
    decimal? ImPercent = null,
    decimal? OnCostPercent = null);

/// <summary>
/// The active vendor defaults — bundled at first, replaced by validated remote configuration when
/// it arrives. Implementations must be safe to query from the UI thread at any time.
/// </summary>
public interface IVendorDefaultsSource
{
    VendorDefaults? ForVendor(string vendor);
}

/// <summary>Defaults before any configuration document has been loaded.</summary>
public sealed class EmptyVendorDefaultsSource : IVendorDefaultsSource
{
    public VendorDefaults? ForVendor(string vendor) => null;
}

/// <summary>Result-popup guidance keyed by concrete parser slug; Auto never keys guidance.</summary>
public interface IGuidanceProvider
{
    GuidanceDocument? MessageFor(string parserSlug);
}

/// <summary>Guidance for hosts and tests that supply no configuration document.</summary>
public sealed class NoGuidanceProvider : IGuidanceProvider
{
    public GuidanceDocument? MessageFor(string parserSlug) => null;
}

/// <summary>
/// Fetches and validates the public configuration documents. Both methods answer null for every
/// failure — network, schema, or content — and the caller keeps the document it already had.
/// </summary>
public interface IRemoteConfigurationService
{
    Task<GuidanceCatalog?> TryFetchGuidanceAsync(IReadOnlySet<string> knownSlugs, CancellationToken ct);

    Task<VendorDefaultsCatalog?> TryFetchVendorDefaultsAsync(IReadOnlySet<string> knownVendors, CancellationToken ct);
}

/// <summary>Compares the installed build against the latest public release. Null means say nothing.</summary>
public interface IUpdateCheckService
{
    Task<AvailableUpdate?> TryCheckAsync(string installedVersion, CancellationToken ct);
}

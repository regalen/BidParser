using BidParser.Desktop.Services;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// Fetches and validates the two public configuration documents. They are independent: one can be
/// served from the network while the other falls back to its bundled copy.
/// </summary>
public sealed class RemoteConfigurationService(PublicDocumentFetcher fetcher) : IRemoteConfigurationService
{
    public async Task<GuidanceCatalog?> TryFetchGuidanceAsync(
        IReadOnlySet<string> knownSlugs, CancellationToken ct)
    {
        var json = await fetcher.TryFetchAsync(
            DesktopEndpoints.GuidanceMessages, DesktopEndpoints.MaxConfigurationBytes, ct);
        return json is null ? null : GuidanceCatalogReader.TryRead(json, knownSlugs);
    }

    public async Task<VendorDefaultsCatalog?> TryFetchVendorDefaultsAsync(
        IReadOnlySet<string> knownVendors, CancellationToken ct)
    {
        var json = await fetcher.TryFetchAsync(
            DesktopEndpoints.VendorDefaults, DesktopEndpoints.MaxConfigurationBytes, ct);
        return json is null ? null : VendorDefaultsCatalogReader.TryRead(json, knownVendors);
    }
}

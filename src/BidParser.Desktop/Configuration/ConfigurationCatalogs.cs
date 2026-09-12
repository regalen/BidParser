using BidParser.Desktop.Services;

namespace BidParser.Desktop.Configuration;

/// <summary>Validated guidance, keyed by concrete parser slug. Auto slugs never key guidance.</summary>
public sealed class GuidanceCatalog(IReadOnlyDictionary<string, GuidanceDocument> messages)
{
    public static readonly GuidanceCatalog Empty = new(new Dictionary<string, GuidanceDocument>(StringComparer.Ordinal));

    public int Count => messages.Count;

    public GuidanceDocument? For(string parserSlug) => messages.GetValueOrDefault(parserSlug);
}

/// <summary>Validated pricing defaults, keyed by vendor display name.</summary>
public sealed class VendorDefaultsCatalog(IReadOnlyDictionary<string, VendorDefaults> defaults)
{
    public static readonly VendorDefaultsCatalog Empty = new(new Dictionary<string, VendorDefaults>(StringComparer.Ordinal));

    public int Count => defaults.Count;

    public VendorDefaults? For(string vendor) => defaults.GetValueOrDefault(vendor);
}

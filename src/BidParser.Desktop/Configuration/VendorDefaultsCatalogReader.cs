using System.Text.Json;
using BidParser.Desktop.Services;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// Reads a schema-v1 vendor defaults document. Values are prefill only: a default for a field the
/// selected CRM template does not use stays collapsed and is never forwarded to the writer, so this
/// document can change numbers a user sees but can never create a capability.
/// </summary>
public static class VendorDefaultsCatalogReader
{
    private const int RateScale = 4;
    private const int PercentageScale = 2;

    /// <param name="knownVendors">Vendor display names this build registers.</param>
    /// <returns>The catalog, or null when the document must be rejected.</returns>
    public static VendorDefaultsCatalog? TryRead(string json, IReadOnlySet<string> knownVendors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!SchemaVersion.IsSupported(root)
                || !root.TryGetProperty("vendorDefaults", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var defaults = new Dictionary<string, VendorDefaults>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("vendors", out var vendors)
                    || vendors.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                if (!TryReadValue(entry, "fxRate", RateScale, exclusiveZero: true, out var fxRate)
                    || !TryReadValue(entry, "margin", PercentageScale, exclusiveZero: false, out var margin)
                    || !TryReadValue(entry, "imPercent", PercentageScale, exclusiveZero: false, out var imPercent)
                    || !TryReadValue(entry, "onCostPct", PercentageScale, exclusiveZero: false, out var onCost))
                {
                    return null;
                }

                var values = new VendorDefaults(fxRate, margin, imPercent, onCost);
                foreach (var vendor in vendors.EnumerateArray())
                {
                    if (vendor.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    var name = vendor.GetString();
                    if (name is null || !knownVendors.Contains(name))
                    {
                        continue;
                    }

                    if (!defaults.TryAdd(name, values))
                    {
                        return null;
                    }
                }
            }

            return new VendorDefaultsCatalog(defaults);
        }
    }

    /// <summary>
    /// Reads one optional numeric field. An absent field is normal and leaves the input blank; a
    /// present field must be a non-negative number that fits the field's scale exactly, so a value
    /// like 2.855 is rejected rather than silently rounded into a different on-cost.
    /// </summary>
    private static bool TryReadValue(
        JsonElement entry, string name, int scale, bool exclusiveZero, out decimal? value)
    {
        value = null;
        if (!entry.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDecimal(out var number))
        {
            return false;
        }

        if (number < 0m || (exclusiveZero && number == 0m) || decimal.Round(number, scale) != number)
        {
            return false;
        }

        value = number;
        return true;
    }
}

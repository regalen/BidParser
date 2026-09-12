namespace BidParser.Parsing.Nutanix;

/// <summary>Normalizes Nutanix product codes to the uppercase identifiers used by SAP.</summary>
internal static class NutanixVpnNormalizer
{
    internal static string Normalize(string value) => value.ToUpperInvariant();
}

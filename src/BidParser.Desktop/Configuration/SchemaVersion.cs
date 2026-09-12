using System.Globalization;
using System.Text.Json;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// The one schema version this executable understands. A document that does not declare exactly
/// integer 1 is rejected whole — including a <em>newer</em> one, because a newer schema may change
/// the meaning of fields this build would otherwise read confidently.
/// </summary>
public static class SchemaVersion
{
    public const int Supported = 1;

    public static bool IsSupported(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var declared)
            || declared.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        // NumberStyles.None accepts digits only, so "1.0", "1e0", "+1" and " 1" are all rejected —
        // the schema says integer, and a float that happens to equal 1 is not one.
        return int.TryParse(declared.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version == Supported;
    }
}

using System.Text.Json;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// Reads a schema-v1 guidance document. Every rejection is total: a document is either wholly
/// understood and applied, or discarded in favour of the bundled copy. Partially applying a
/// document from a public source would leave the user unable to tell which wording they are seeing.
/// </summary>
public static class GuidanceCatalogReader
{
    /// <param name="knownSlugs">Concrete parser slugs this build registers.</param>
    /// <returns>The catalog, or null when the document must be rejected.</returns>
    public static GuidanceCatalog? TryRead(string json, IReadOnlySet<string> knownSlugs)
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
                || !root.TryGetProperty("guidanceMessages", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var messages = new Dictionary<string, GuidanceDocument>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("fileTypes", out var fileTypes)
                    || fileTypes.ValueKind != JsonValueKind.Array
                    || !entry.TryGetProperty("html", out var html)
                    || html.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                // The markup is validated for every entry, including one whose slugs this build does
                // not know: unsafe content must never be able to hide behind an unrecognised slug.
                if (GuidanceMarkupReader.TryRead(html.GetString()) is not { } guidance)
                {
                    return null;
                }

                foreach (var fileType in fileTypes.EnumerateArray())
                {
                    if (fileType.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    var slug = fileType.GetString();
                    if (slug is null || !knownSlugs.Contains(slug))
                    {
                        // A newer configuration naming formats this build has never heard of stays
                        // useful; the unknown entries are simply skipped.
                        continue;
                    }

                    if (!messages.TryAdd(slug, guidance))
                    {
                        // Two messages claiming one format: there is no defensible winner.
                        return null;
                    }
                }
            }

            return new GuidanceCatalog(messages);
        }
    }
}

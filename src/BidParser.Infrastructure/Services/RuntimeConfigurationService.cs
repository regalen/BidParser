using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Services;

public sealed record RuntimeConfigDocument(string Json, DateTime? UpdatedAt, string? ValidationError = null);
public sealed record RuntimeConfigField(string Key, string Label, int Scale);
public sealed record RuntimeConfigParserReference(string Slug, string DisplayName, string Vendor);
public sealed record RuntimeConfigVendorDefaults(decimal? FxRate, decimal? Margin, decimal? ImPercent, decimal? OnCostPct);
public sealed record ParseUiConfiguration(
    IReadOnlyDictionary<string, RuntimeConfigVendorDefaults> VendorDefaults,
    IReadOnlyDictionary<string, string> GuidanceByParserSlug);

public sealed class RuntimeConfigurationValidationException(string detail) : Exception(detail)
{
    public string Detail { get; } = detail;
}

/// <summary>
/// Reads and independently saves the two administrator-managed JSON documents. Values are read on
/// every request rather than cached so edits take effect immediately across application instances.
/// </summary>
public sealed class RuntimeConfigurationService(AppDbContext db, IParserRegistry registry)
{
    private const int MaxMessageCharacters = 32 * 1024;
    private const int MaxDocumentCharacters = 256 * 1024;
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    // Guidance is sanitized before this formatter runs. Keeping its approved HTML literal makes
    // the administrator JSON editor readable without changing application-wide JSON encoding.
    private static readonly JsonSerializerOptions GuidancePrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<RuntimeConfigField> SupportedFields { get; } =
    [
        new("fxRate", "FX Rate", 4),
        new("margin", "Uplift", 2),
        new("imPercent", "Discount Off MSRP", 2),
        new("onCostPct", "On Cost %", 2)
    ];

    public IReadOnlyList<RuntimeConfigParserReference> ParserReferences => registry.Parsers
        .Select(parser => new RuntimeConfigParserReference(parser.Slug, parser.DisplayName, parser.Vendor))
        .ToList();

    public IReadOnlyList<string> VendorReferences => registry.Parsers
        .Select(parser => parser.Vendor)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

    public async Task<ParseUiConfiguration> GetParseUiConfigurationAsync(CancellationToken ct)
    {
        var rows = await db.RuntimeConfigs.AsNoTracking()
            .Where(row => row.Key == RuntimeConfigKeys.GuidanceMessages || row.Key == RuntimeConfigKeys.VendorDefaults)
            .ToDictionaryAsync(row => row.Key, row => row.JsonPayload, ct);

        // A missing or corrupt document must never break parsing. The admin endpoints remain strict;
        // the active-user surface simply exposes no defaults/guidance until it is repaired.
        try
        {
            var guidance = rows.TryGetValue(RuntimeConfigKeys.GuidanceMessages, out var guidanceJson)
                ? ParseGuidance(guidanceJson)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var defaults = rows.TryGetValue(RuntimeConfigKeys.VendorDefaults, out var defaultsJson)
                ? ParseVendorDefaults(defaultsJson)
                : new Dictionary<string, RuntimeConfigVendorDefaults>(StringComparer.Ordinal);
            return new ParseUiConfiguration(defaults, guidance);
        }
        catch (RuntimeConfigurationValidationException)
        {
            return new ParseUiConfiguration(
                new Dictionary<string, RuntimeConfigVendorDefaults>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    public async Task<RuntimeConfigDocument> GetDocumentAsync(string key, CancellationToken ct)
    {
        EnsureKnownKey(key);
        var row = await db.RuntimeConfigs.AsNoTracking().SingleOrDefaultAsync(config => config.Key == key, ct);
        if (row is null)
        {
            return new RuntimeConfigDocument("[]", null);
        }

        try
        {
            return new RuntimeConfigDocument(Canonicalize(key, row.JsonPayload), row.UpdatedAt);
        }
        catch (RuntimeConfigurationValidationException exception)
        {
            // Keep the strict validation on PUT, but let an administrator repair data that became
            // invalid after a registry or validation-rule change without requiring direct SQL access.
            return new RuntimeConfigDocument(row.JsonPayload, row.UpdatedAt, exception.Detail);
        }
    }

    public async Task<RuntimeConfigDocument> SaveDocumentAsync(string key, string json, CancellationToken ct)
    {
        EnsureKnownKey(key);
        var canonical = Canonicalize(key, json);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await db.RuntimeConfigs.SingleOrDefaultAsync(config => config.Key == key, ct);
            if (row is null)
            {
                row = new RuntimeConfig { Key = key, JsonPayload = canonical };
                db.RuntimeConfigs.Add(row);
            }
            else
            {
                row.JsonPayload = canonical;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return new RuntimeConfigDocument(canonical, row.UpdatedAt);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Bootstrap normally guarantees the row exists. If it was removed, simultaneous
                // first saves can race on the primary key; reload and take the normal update path.
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Runtime configuration save retry limit was exhausted.");
    }

    public string Canonicalize(string key, string json)
    {
        EnsureKnownKey(key);
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxDocumentCharacters)
        {
            throw new RuntimeConfigurationValidationException("Configuration JSON must be nonblank and no larger than 256 KiB.");
        }

        return key == RuntimeConfigKeys.GuidanceMessages
            ? JsonSerializer.Serialize(BuildGuidanceDocument(json), GuidancePrettyJson)
            : JsonSerializer.Serialize(BuildVendorDefaultsDocument(json), PrettyJson);
    }

    private JsonArray BuildGuidanceDocument(string json)
    {
        var root = ParseArray(json);
        var knownSlugs = registry.Parsers.Select(parser => parser.Slug).ToHashSet(StringComparer.Ordinal);
        var assignedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var result = new JsonArray();

        foreach (var node in root)
        {
            if (node is not JsonObject entry || entry.Count != 2 || !entry.ContainsKey("fileTypes") || !entry.ContainsKey("html"))
            {
                throw new RuntimeConfigurationValidationException("Each guidance message must contain exactly fileTypes and html.");
            }
            if (entry["fileTypes"] is not JsonArray fileTypes || fileTypes.Count == 0)
            {
                throw new RuntimeConfigurationValidationException("Each guidance message must contain one or more fileTypes.");
            }
            if (entry["html"] is not JsonValue htmlNode || !htmlNode.TryGetValue<string>(out var html) || string.IsNullOrWhiteSpace(html) || html.Length > MaxMessageCharacters)
            {
                throw new RuntimeConfigurationValidationException("Each guidance HTML message must be nonblank and no larger than 32 KiB.");
            }

            var normalizedSlugs = new JsonArray();
            foreach (var slugNode in fileTypes)
            {
                if (slugNode is not JsonValue slugValue || !slugValue.TryGetValue<string>(out var slug) || string.IsNullOrWhiteSpace(slug)
                    || !knownSlugs.Contains(slug) || !assignedSlugs.Add(slug))
                {
                    throw new RuntimeConfigurationValidationException("Guidance fileTypes must be unique concrete registered parser slugs.");
                }
                normalizedSlugs.Add(slug);
            }

            result.Add(new JsonObject { ["fileTypes"] = normalizedSlugs, ["html"] = SanitizeHtml(html) });
        }
        return result;
    }

    private JsonArray BuildVendorDefaultsDocument(string json)
    {
        var root = ParseArray(json);
        var knownVendors = VendorReferences.ToHashSet(StringComparer.Ordinal);
        var assignedVendors = new HashSet<string>(StringComparer.Ordinal);
        var fields = SupportedFields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var result = new JsonArray();

        foreach (var node in root)
        {
            if (node is not JsonObject entry || !entry.ContainsKey("vendors"))
            {
                throw new RuntimeConfigurationValidationException("Each vendor-default entry must contain vendors and at least one supported numeric field.");
            }
            if (entry.Any(property => property.Key != "vendors" && !fields.ContainsKey(property.Key)))
            {
                throw new RuntimeConfigurationValidationException("Vendor defaults contain an unsupported property.");
            }
            var configuredFields = entry.Where(property => property.Key != "vendors").ToList();
            if (configuredFields.Count == 0 || entry["vendors"] is not JsonArray vendors || vendors.Count == 0)
            {
                throw new RuntimeConfigurationValidationException("Each vendor-default entry must contain vendors and at least one supported numeric field.");
            }

            var normalizedVendors = new JsonArray();
            foreach (var vendorNode in vendors)
            {
                if (vendorNode is not JsonValue vendorValue || !vendorValue.TryGetValue<string>(out var vendor) || string.IsNullOrWhiteSpace(vendor)
                    || !knownVendors.Contains(vendor) || !assignedVendors.Add(vendor))
                {
                    throw new RuntimeConfigurationValidationException("Vendor defaults must use unique concrete registered vendor names.");
                }
                normalizedVendors.Add(vendor);
            }

            var normalized = new JsonObject { ["vendors"] = normalizedVendors };
            foreach (var property in configuredFields)
            {
                if (!TryGetDecimal(property.Value, out var value) || value < 0)
                {
                    throw new RuntimeConfigurationValidationException($"{property.Key} must be a non-negative number.");
                }
                var scale = fields[property.Key].Scale;
                var rounded = decimal.Round(value, scale, MidpointRounding.AwayFromZero);
                var maximum = scale == 4 ? 99_999_999.9999m : 9_999_999_999.99m;
                if (rounded > maximum)
                {
                    throw new RuntimeConfigurationValidationException($"{property.Key} exceeds the supported precision.");
                }
                normalized[property.Key] = JsonValue.Create(rounded);
            }
            result.Add(normalized);
        }
        return result;
    }

    private Dictionary<string, string> ParseGuidance(string json)
    {
        var document = BuildGuidanceDocument(json);
        return document.Cast<JsonObject>()
            .SelectMany(entry => entry["fileTypes"]!.AsArray().Select(slug => new
            {
                Slug = slug!.GetValue<string>(),
                Html = entry["html"]!.GetValue<string>()
            }))
            .ToDictionary(item => item.Slug, item => item.Html, StringComparer.Ordinal);
    }

    private Dictionary<string, RuntimeConfigVendorDefaults> ParseVendorDefaults(string json)
    {
        var document = BuildVendorDefaultsDocument(json);
        return document.Cast<JsonObject>()
            .SelectMany(entry => entry["vendors"]!.AsArray().Select(vendor => new
            {
                Vendor = vendor!.GetValue<string>(),
                Defaults = new RuntimeConfigVendorDefaults(
                    DecimalOrNull(entry["fxRate"]), DecimalOrNull(entry["margin"]),
                    DecimalOrNull(entry["imPercent"]), DecimalOrNull(entry["onCostPct"]))
            }))
            .ToDictionary(item => item.Vendor, item => item.Defaults, StringComparer.Ordinal);
    }

    private static JsonArray ParseArray(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonArray
                ?? throw new RuntimeConfigurationValidationException("Configuration must be a JSON array.");
        }
        catch (JsonException)
        {
            throw new RuntimeConfigurationValidationException("Configuration must be valid JSON.");
        }
    }

    private static bool TryGetDecimal(JsonNode? node, out decimal value)
    {
        value = 0;
        return node is JsonValue jsonValue && jsonValue.TryGetValue<decimal>(out value);
    }

    private static decimal? DecimalOrNull(JsonNode? node) => TryGetDecimal(node, out var value) ? value : null;

    private static string SanitizeHtml(string html)
    {
        // HtmlSanitizer handles malformed markup and dangerous nodes/attributes. Style is restricted
        // further here because it is the sole allowed attribute and only valid on span elements.
        if (System.Text.RegularExpressions.Regex.IsMatch(html, @"<(?!span\b)[^>]+\sstyle\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(html, @"\sstyle\s*=\s*(['""])(?!\s*color\s*:[^;]+;?\s*\1)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            throw new RuntimeConfigurationValidationException("Only span style=color is allowed in guidance HTML.");
        }

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "ul", "ol", "li", "strong", "b", "em", "i", "span", "br"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("style");
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedCssProperties.Add("color");
        var removedUnsafeContent = false;
        sanitizer.RemovingTag += (_, _) => removedUnsafeContent = true;
        sanitizer.RemovingAttribute += (_, _) => removedUnsafeContent = true;
        sanitizer.RemovingStyle += (_, _) => removedUnsafeContent = true;
        var sanitized = sanitizer.Sanitize(html);
        if (removedUnsafeContent)
        {
            throw new RuntimeConfigurationValidationException("Guidance HTML contains unsupported or unsafe markup.");
        }
        return sanitized;
    }

    private static void EnsureKnownKey(string key)
    {
        if (key != RuntimeConfigKeys.GuidanceMessages && key != RuntimeConfigKeys.VendorDefaults)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
}

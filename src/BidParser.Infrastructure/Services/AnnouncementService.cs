using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Services;

public sealed record AnnouncementDocument(bool Enabled, string Html, DateTime? UpdatedAt, string? ValidationError = null);
public sealed record ActiveAnnouncement(string Html, string DismissalKey);

/// <summary>
/// The administrator-managed site announcement shown beneath the page header. It is stored as the
/// <see cref="RuntimeConfigKeys.Announcement"/> runtime-configuration document
/// (<c>{ "enabled": bool, "html": string }</c>); an absent row means no announcement. Values are
/// read on every request so edits take effect immediately across application instances.
/// </summary>
public sealed class AnnouncementService(AppDbContext db)
{
    public const int MaxHtmlCharacters = 4 * 1024;
    private const long SecondsPerDay = 24 * 60 * 60;

    /// <summary>Elements and attributes an announcement may contain; everything else is rejected.</summary>
    public static IReadOnlyList<string> HtmlAllowlist { get; } = ["strong", "b", "em", "i", "u", "br", "a href (http, https, mailto)"];

    private static readonly JsonSerializerOptions StoredJson = new()
    {
        WriteIndented = true,
        // The HTML is sanitized before serialization; keep it literal so the stored row stays readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<AnnouncementDocument> GetDocumentAsync(CancellationToken ct)
    {
        var row = await db.RuntimeConfigs.AsNoTracking()
            .SingleOrDefaultAsync(config => config.Key == RuntimeConfigKeys.Announcement, ct);
        if (row is null)
        {
            return new AnnouncementDocument(false, string.Empty, null);
        }

        try
        {
            var (enabled, html) = Read(row.JsonPayload);
            return new AnnouncementDocument(enabled, html, row.UpdatedAt);
        }
        catch (RuntimeConfigurationValidationException exception)
        {
            // Let an administrator see and repair a stored document that no longer validates.
            return new AnnouncementDocument(false, RawHtml(row.JsonPayload), row.UpdatedAt, exception.Detail);
        }
    }

    /// <summary>
    /// The announcement for one login session, or null when none is enabled. A corrupt stored
    /// document never breaks the page: it simply shows no banner until an administrator repairs it.
    /// </summary>
    public async Task<ActiveAnnouncement?> GetActiveAsync(int userId, long sessionIssuedAt, CancellationToken ct)
    {
        var row = await db.RuntimeConfigs.AsNoTracking()
            .SingleOrDefaultAsync(config => config.Key == RuntimeConfigKeys.Announcement, ct);
        if (row is null) return null;

        try
        {
            var (enabled, html) = Read(row.JsonPayload);
            if (!enabled) return null;
            var key = DismissalKey(userId, sessionIssuedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), row.UpdatedAt);
            return new ActiveAnnouncement(html, key);
        }
        catch (RuntimeConfigurationValidationException)
        {
            return null;
        }
    }

    public async Task<AnnouncementDocument> SaveAsync(bool enabled, string? html, CancellationToken ct)
    {
        var sanitized = Normalize(enabled, html);
        var json = Serialize(enabled, sanitized);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await db.RuntimeConfigs.SingleOrDefaultAsync(config => config.Key == RuntimeConfigKeys.Announcement, ct);
            if (row is null)
            {
                row = new RuntimeConfig { Key = RuntimeConfigKeys.Announcement, JsonPayload = json };
                db.RuntimeConfigs.Add(row);
            }
            else
            {
                row.JsonPayload = json;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return new AnnouncementDocument(enabled, sanitized, row.UpdatedAt);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Two first saves can race on the primary key; reload and take the update path.
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Announcement save retry limit was exhausted.");
    }

    /// <summary>
    /// An opaque token the browser remembers once the user dismisses the banner. It changes — so the
    /// banner reappears — on every new login (a new session issue time), every 24 hours within a
    /// longer-lived session, and whenever an administrator saves the announcement.
    /// </summary>
    public static string DismissalKey(int userId, long sessionIssuedAt, long nowUnixSeconds, DateTime announcementUpdatedAt)
    {
        var period = Math.Max(0, nowUnixSeconds - sessionIssuedAt) / SecondsPerDay;
        var material = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{userId}:{sessionIssuedAt}:{period}:{announcementUpdatedAt.Ticks}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32];
    }

    private static (bool Enabled, string Html) Read(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new RuntimeConfigurationValidationException("The announcement must be a JSON object.");
        }
        catch (JsonException)
        {
            throw new RuntimeConfigurationValidationException("The announcement must be valid JSON.");
        }

        if (root["enabled"] is not JsonValue enabledNode || !enabledNode.TryGetValue<bool>(out var enabled))
        {
            throw new RuntimeConfigurationValidationException("The announcement must contain a boolean enabled flag.");
        }
        var html = root["html"] is JsonValue htmlNode && htmlNode.TryGetValue<string>(out var value) ? value : string.Empty;
        return (enabled, Normalize(enabled, html));
    }

    private static string RawHtml(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["html"] is JsonValue node && node.TryGetValue<string>(out var html) ? html : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string Serialize(bool enabled, string html) =>
        JsonSerializer.Serialize(new JsonObject { ["enabled"] = enabled, ["html"] = html }, StoredJson);

    private static string Normalize(bool enabled, string? html)
    {
        var trimmed = (html ?? string.Empty).Trim();
        if (trimmed.Length > MaxHtmlCharacters)
        {
            throw new RuntimeConfigurationValidationException("The announcement must be no larger than 4 KiB.");
        }
        if (trimmed.Length == 0)
        {
            return enabled
                ? throw new RuntimeConfigurationValidationException("Enter announcement text before enabling it.")
                : string.Empty;
        }

        var sanitized = Sanitize(trimmed);
        if (enabled && string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(sanitized, "<[^>]*>", string.Empty)))
        {
            throw new RuntimeConfigurationValidationException("Enter announcement text before enabling it.");
        }
        return sanitized;
    }

    private static string Sanitize(string html)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["strong", "b", "em", "i", "u", "br", "a"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.AllowedClasses.Clear();

        // Reject rather than silently strip, so an administrator never publishes text that differs
        // from what they typed. Links open in a new tab; the client adds target/rel when rendering.
        var removedUnsafeContent = false;
        sanitizer.RemovingTag += (_, _) => removedUnsafeContent = true;
        sanitizer.RemovingAttribute += (_, _) => removedUnsafeContent = true;
        sanitizer.RemovingStyle += (_, _) => removedUnsafeContent = true;
        sanitizer.RemovingComment += (_, _) => removedUnsafeContent = true;
        var sanitized = sanitizer.Sanitize(html);
        if (removedUnsafeContent)
        {
            throw new RuntimeConfigurationValidationException(
                "The announcement contains unsupported markup. Use only bold, italic, underline, line breaks and http, https or mailto links.");
        }
        return sanitized;
    }
}

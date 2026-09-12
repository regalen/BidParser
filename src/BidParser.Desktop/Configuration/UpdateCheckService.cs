using System.Text.Json;
using BidParser.Desktop.Services;
using NuGet.Versioning;

namespace BidParser.Desktop.Configuration;

/// <summary>An update worth telling the user about, with a release page built from the fixed base URL.</summary>
public sealed record AvailableUpdate(string Installed, string Latest, Uri ReleasePage);

/// <summary>
/// Compares the installed version against the latest public release. Purely informational: it
/// downloads nothing, replaces nothing, and shows nothing at all when anything goes wrong.
/// </summary>
public sealed class UpdateCheckService(PublicDocumentFetcher fetcher) : IUpdateCheckService
{
    public async Task<AvailableUpdate?> TryCheckAsync(string installedVersion, CancellationToken ct)
    {
        // An unparseable installed version means there is nothing meaningful to compare against.
        if (!NuGetVersion.TryParse(installedVersion, out var installed))
        {
            return null;
        }

        var body = await fetcher.TryFetchAsync(
            DesktopEndpoints.LatestRelease, DesktopEndpoints.MaxReleaseMetadataBytes, ct);
        if (body is null || TryReadTag(body) is not { } tag)
        {
            return null;
        }

        // GitHub tags this project's releases "v1.2.3"; strip exactly one leading marker.
        var candidate = tag.StartsWith('v') ? tag[1..] : tag;
        if (!NuGetVersion.TryParse(candidate, out var latest))
        {
            return null;
        }

        // VersionRelease compares version and prerelease label but ignores build metadata, so
        // "1.2.3+abc" and "1.2.3" are the same release. The endpoint excludes drafts and
        // prereleases, so a prerelease build is only notified when a stable release overtakes it.
        return VersionComparer.VersionRelease.Compare(latest, installed) > 0
            ? new AvailableUpdate(installed.ToNormalizedString(), latest.ToNormalizedString(), DesktopEndpoints.ReleasePage(tag))
            : null;
    }

    private static string? TryReadTag(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("tag_name", out var tag)
                && tag.ValueKind == JsonValueKind.String
                ? tag.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

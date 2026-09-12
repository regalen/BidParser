using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BidParser.Infrastructure.Dell;

/// <summary>
/// Reads and updates the singleton Dell API settings row. The client secret crosses this boundary
/// only in <see cref="DellApiConfig"/> for outbound authentication and is never exposed by an API response.
/// </summary>
public sealed class DellApiSettingsService : IDellApiSettingsReader
{
    public const string DefaultTokenUrl = "https://apigtwb2c.us.dell.com/auth/oauth/v2/token";
    public const string DefaultQuoteUrlTemplate = "https://apigtwb2c.us.dell.com/PROD/QuoteSearchApi/api/quote/{quoteNumber}/{quoteVersion}/{locale}";
    public const string InitialLocale = "en-au";
    public const string InitialClientIdHeader = "Swagger";

    /// <summary>
    /// Sent as <c>Accepts-version</c>. Dell assumes an unspecified default version when the header is
    /// omitted, and the parsers depend on post-v1 response fields (the <c>*IncludingShipping</c>
    /// prices and <c>skus[].serviceTags</c>), so the version is pinned rather than left to Dell.
    /// </summary>
    public const string InitialApiVersion = "4.0";

    private const int SingletonId = 1;
    private const string ProtectorPurpose = "BidParser.DellApiSettings.ClientSecret";

    private static readonly Regex ApiVersionPattern = new(
        @"^[A-Za-z0-9.\-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<DellApiSettingsService> _logger;
    private readonly IDellAuthTokenCache _tokenCache;

    public DellApiSettingsService(
        AppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DellApiSettingsService> logger,
        IDellAuthTokenCache tokenCache)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
        _tokenCache = tokenCache;
    }

    public async Task<DellApiConfig?> GetAsync(CancellationToken ct)
    {
        var settings = await _db.DellApiSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SingletonId, ct);
        if (settings is null)
        {
            return null;
        }

        var secret = TryUnprotect(settings.ClientSecretProtected);
        return secret is null ? null : ToConfig(settings, secret);
    }

    public async Task<DellApiSettingsView> GetForAdminAsync(CancellationToken ct)
    {
        var settings = await _db.DellApiSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SingletonId, ct);
        if (settings is null)
        {
            return DellApiSettingsView.Unconfigured;
        }

        return new DellApiSettingsView(
            settings.TokenUrl,
            settings.ClientId,
            TryUnprotect(settings.ClientSecretProtected) is not null,
            settings.QuoteUrlTemplate,
            settings.DefaultLocale,
            settings.ClientIdHeader,
            settings.ApiVersion,
            settings.UseBasicAuthForToken,
            settings.UpdatedAt);
    }

    public async Task SaveAsync(DellApiSettingsUpdate update, CancellationToken ct)
    {
        var normalized = Validate(update);
        var settings = await _db.DellApiSettings
            .SingleOrDefaultAsync(row => row.Id == SingletonId, ct);
        var suppliedSecret = string.IsNullOrWhiteSpace(update.ClientSecret)
            ? null
            : update.ClientSecret;
        var isFirstSave = settings is null;

        if (settings is null)
        {
            if (suppliedSecret is null)
            {
                throw new DellApiSettingsValidationException("Client secret is required when configuring the Dell API for the first time.");
            }

            settings = new DellApiSettings
            {
                Id = SingletonId,
                TokenUrl = normalized.TokenUrl,
                ClientId = normalized.ClientId,
                ClientSecretProtected = _protector.Protect(suppliedSecret),
                QuoteUrlTemplate = normalized.QuoteUrlTemplate,
                DefaultLocale = normalized.DefaultLocale,
                ClientIdHeader = normalized.ClientIdHeader,
                ApiVersion = normalized.ApiVersion,
                UseBasicAuthForToken = normalized.UseBasicAuthForToken,
            };
            _db.DellApiSettings.Add(settings);
        }
        else
        {
            ApplyUpdate(settings, normalized, suppliedSecret);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isFirstSave)
        {
            // Another administrator may have created the singleton after our initial read.
            // Recover only when that row now exists; unrelated insert failures still surface.
            _db.Entry(settings).State = EntityState.Detached;
            var concurrentSettings = await _db.DellApiSettings
                .SingleOrDefaultAsync(row => row.Id == SingletonId, ct);
            if (concurrentSettings is null)
            {
                throw;
            }

            ApplyUpdate(concurrentSettings, normalized, suppliedSecret);
            await _db.SaveChangesAsync(ct);
        }

        _tokenCache.Invalidate();
    }

    private void ApplyUpdate(
        DellApiSettings settings,
        DellApiSettingsUpdate normalized,
        string? suppliedSecret)
    {
        settings.TokenUrl = normalized.TokenUrl;
        settings.ClientId = normalized.ClientId;
        settings.QuoteUrlTemplate = normalized.QuoteUrlTemplate;
        settings.DefaultLocale = normalized.DefaultLocale;
        settings.ClientIdHeader = normalized.ClientIdHeader;
        settings.ApiVersion = normalized.ApiVersion;
        settings.UseBasicAuthForToken = normalized.UseBasicAuthForToken;
        if (suppliedSecret is not null)
        {
            settings.ClientSecretProtected = _protector.Protect(suppliedSecret);
        }
    }

    private string? TryUnprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex,
                "The stored Dell API client secret cannot be decrypted. Re-enter it in Settings -> Dell API; this is required after SESSION_SECRET rotation or Data Protection key loss.");
            return null;
        }
    }

    private static DellApiConfig ToConfig(DellApiSettings settings, string clientSecret) => new(
        settings.TokenUrl,
        settings.ClientId,
        clientSecret,
        settings.QuoteUrlTemplate,
        settings.DefaultLocale,
        settings.ClientIdHeader,
        settings.ApiVersion,
        settings.UseBasicAuthForToken);

    private static DellApiSettingsUpdate Validate(DellApiSettingsUpdate update)
    {
        var tokenUrl = RequiredWithin(update.TokenUrl, 512, "Token URL");
        var clientId = RequiredWithin(update.ClientId, 256, "Client ID");
        var quoteUrlTemplate = RequiredWithin(update.QuoteUrlTemplate, 1024, "Quote URL template");
        var defaultLocale = RequiredWithin(update.DefaultLocale, 16, "Default locale");
        var clientIdHeader = RequiredWithin(update.ClientIdHeader, 128, "ClientId header");
        var apiVersion = RequiredWithin(update.ApiVersion, 16, "API version");

        // The value is written straight into a request header, so keep it to a safe token charset.
        if (!ApiVersionPattern.IsMatch(apiVersion))
        {
            throw new DellApiSettingsValidationException(
                "API version must contain only letters, digits, dots, or hyphens (for example 4.0).");
        }

        RequireHttpsAbsoluteUri(tokenUrl, "Token URL");
        RequireHttpsAbsoluteUri(quoteUrlTemplate, "Quote URL template");

        foreach (var placeholder in new[] { "{quoteNumber}", "{quoteVersion}", "{locale}" })
        {
            if (!quoteUrlTemplate.Contains(placeholder, StringComparison.Ordinal))
            {
                throw new DellApiSettingsValidationException($"Quote URL template must contain {placeholder}.");
            }
        }

        return update with
        {
            TokenUrl = tokenUrl,
            ClientId = clientId,
            QuoteUrlTemplate = quoteUrlTemplate,
            DefaultLocale = defaultLocale,
            ClientIdHeader = clientIdHeader,
            ApiVersion = apiVersion,
        };
    }

    private static string RequiredWithin(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new DellApiSettingsValidationException($"{fieldName} is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new DellApiSettingsValidationException($"{fieldName} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static void RequireHttpsAbsoluteUri(string value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new DellApiSettingsValidationException($"{fieldName} must be an absolute HTTPS URL.");
        }
    }
}

/// <summary>Decrypted settings used only for server-to-server Dell API calls.</summary>
public sealed record DellApiConfig(
    string TokenUrl,
    string ClientId,
    string ClientSecret,
    string QuoteUrlTemplate,
    string DefaultLocale,
    string ClientIdHeader,
    string ApiVersion,
    bool UseBasicAuthForToken);

/// <summary>Secret-free settings state used to compose the administrator response.</summary>
public sealed record DellApiSettingsView(
    string TokenUrl,
    string ClientId,
    bool ClientSecretConfigured,
    string QuoteUrlTemplate,
    string DefaultLocale,
    string ClientIdHeader,
    string ApiVersion,
    bool UseBasicAuthForToken,
    DateTime? UpdatedAt)
{
    public static DellApiSettingsView Unconfigured => new(
        DellApiSettingsService.DefaultTokenUrl,
        string.Empty,
        false,
        DellApiSettingsService.DefaultQuoteUrlTemplate,
        DellApiSettingsService.InitialLocale,
        DellApiSettingsService.InitialClientIdHeader,
        DellApiSettingsService.InitialApiVersion,
        false,
        null);
}

/// <summary>Validated administrator update; a blank client secret preserves an existing value.</summary>
public sealed record DellApiSettingsUpdate(
    string TokenUrl,
    string ClientId,
    string? ClientSecret,
    string QuoteUrlTemplate,
    string DefaultLocale,
    string ClientIdHeader,
    string ApiVersion,
    bool UseBasicAuthForToken);

/// <summary>A safe client-input validation failure mapped to a typed API error by the endpoint.</summary>
public sealed class DellApiSettingsValidationException(string detail) : Exception(detail)
{
    public string Detail { get; } = detail;
}

/// <summary>Restricted settings read used by the outbound quote client.</summary>
public interface IDellApiSettingsReader
{
    Task<DellApiConfig?> GetAsync(CancellationToken ct);
}

/// <summary>Invalidates any cached Dell OAuth token after credentials or endpoints change.</summary>
public interface IDellAuthTokenCache
{
    void Invalidate();
}

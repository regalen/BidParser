using System.Text.Json;
using BidParser.Infrastructure.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace BidParser.Api.Auth;

/// <summary>
/// Single source of truth for session-token creation and parsing. The payload
/// binds the session to a fingerprint of the user's current password hash, so
/// any password change or admin reset revokes every existing session for that
/// user (their stamp no longer matches) without needing a server-side store.
/// </summary>
public sealed class SessionTokenService
{
    private readonly IDataProtector _protector;

    public SessionTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("bidparser-session");
    }

    public string CreateToken(User user)
    {
        var payload = new SessionPayload(
            user.Id,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            StampFor(user.PasswordHash));
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public SessionPayload? TryParse(string cookie)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionPayload>(_protector.Unprotect(cookie));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A cheap, non-reversible fingerprint of the password hash (BCrypt output is
    /// 60 chars; the tail changes on every re-hash). Guarded for short hashes.
    /// </summary>
    public static string StampFor(string passwordHash) =>
        passwordHash.Length >= 8 ? passwordHash[^8..] : passwordHash;
}

public sealed record SessionPayload(int UserId, long IssuedAt, string Stamp);

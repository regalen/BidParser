using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Globalization;
using BidParser.Api.Options;
using BidParser.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BidParser.Api.Auth;

public sealed class SessionCookieAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SessionCookie";
    public const string CookieName = "bidparser_session";
    private readonly IDataProtector _protector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppOptions _appOptions;

    public SessionCookieAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDataProtectionProvider dataProtectionProvider,
        IServiceScopeFactory scopeFactory,
        AppOptions appOptions)
        : base(options, logger, encoder)
    {
        _protector = dataProtectionProvider.CreateProtector("bidparser-session");
        _scopeFactory = scopeFactory;
        _appOptions = appOptions;
    }

    public string CreateSessionToken(int userId)
    {
        var payload = JsonSerializer.Serialize(new SessionPayload(userId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return _protector.Protect(payload);
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrWhiteSpace(cookie))
        {
            return AuthenticateResult.NoResult();
        }

        SessionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SessionPayload>(_protector.Unprotect(cookie));
        }
        catch (Exception)
        {
            return AuthenticateResult.Fail("Invalid session.");
        }

        if (payload is null)
        {
            return AuthenticateResult.Fail("Invalid session.");
        }

        var expiresAt = payload.IssuedAt + (_appOptions.SessionLifetimeHours * 60 * 60);
        if (expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return AuthenticateResult.Fail("Session expired.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == payload.UserId,
            Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("User not found.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("must_change_password", user.MustChangePassword ? "true" : "false")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { detail = "not_authenticated" });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        var detail = Context.User.HasClaim("must_change_password", "true") ? "password_change_required" : "admin_required";
        if (detail == "password_change_required")
        {
            Logger.LogWarning(
                "Authorization denied: password_change_required user={UserId}",
                Context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown");
        }

        return Response.WriteAsJsonAsync(new { detail });
    }

    private sealed record SessionPayload(int UserId, long IssuedAt);
}

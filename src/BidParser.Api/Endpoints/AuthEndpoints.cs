using System.Globalization;
using System.Text.Json;
using BidParser.Api.Auth;
using BidParser.Api.Options;
using BidParser.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AddEndpointFilter<RequireCsrfHeader>();

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", Logout).RequireAuthorization(AuthPolicies.LoggedIn);
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization(AuthPolicies.LoggedIn);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpRequest request,
        HttpResponse response,
        AppDbContext db,
        AuthRateLimiter rateLimiter,
        AppOptions options,
        IDataProtectionProvider dataProtectionProvider)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<LoginRequest>(request);
        if (!body.IsSuccess || body.Value is null || string.IsNullOrWhiteSpace(body.Value.Username) || string.IsNullOrEmpty(body.Value.Password))
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var usernameKey = body.Value.Username.Trim().ToLowerInvariant();
        var ipLimit = CheckRateLimit(rateLimiter, $"ip:{ClientIp(request.HttpContext)}", options.RateLimitAuthPerMin);
        if (ipLimit is not null)
        {
            return ipLimit;
        }
        var userLimit = CheckRateLimit(rateLimiter, $"username:{usernameKey}", options.RateLimitAuthPerMin);
        if (userLimit is not null)
        {
            return userLimit;
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Username == usernameKey);
        if (user is null || !BCrypt.Net.BCrypt.Verify(body.Value.Password, user.PasswordHash))
        {
            return Results.Json(new { detail = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var protector = dataProtectionProvider.CreateProtector("bidparser-session");
        var payload = JsonSerializer.Serialize(new SessionPayload(user.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        response.Cookies.Append(SessionCookieAuthHandler.CookieName, protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = IsSecure(request),
            MaxAge = TimeSpan.FromHours(options.SessionLifetimeHours),
            Path = "/"
        });

        return Results.Ok(new LoginResponse(UserPublic.FromEntity(user)));
    }

    private static IResult Logout(HttpResponse response)
    {
        response.Cookies.Delete(SessionCookieAuthHandler.CookieName, new CookieOptions { Path = "/" });
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext context,
        HttpRequest request,
        AppDbContext db,
        AuthRateLimiter rateLimiter,
        AppOptions options)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<ChangePasswordRequest>(request);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var limit = CheckRateLimit(rateLimiter, $"ip:{ClientIp(context)}", options.RateLimitAuthPerMin);
        if (limit is not null)
        {
            return limit;
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db);
        if (user is null)
        {
            return Results.Json(new { detail = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!BCrypt.Net.BCrypt.Verify(body.Value.OldPassword, user.PasswordHash))
        {
            return Results.Json(new { detail = "Invalid password." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var errors = PasswordPolicy.Validate(body.Value.NewPassword);
        if (errors.Count > 0)
        {
            return Results.Json(new { detail = errors }, statusCode: StatusCodes.Status400BadRequest);
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Value.NewPassword, workFactor: 12);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();

        return Results.Ok(new { ok = true });
    }

    private static IResult? CheckRateLimit(AuthRateLimiter rateLimiter, string key, int limit)
    {
        var result = rateLimiter.Check(key, limit);
        if (result.Allowed)
        {
            return null;
        }

        return new RateLimitExceededResult(result.RetryAfterSeconds ?? 1);
    }

    private static string ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',', 2)[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsSecure(HttpRequest request)
    {
        return request.Headers["X-Forwarded-Proto"].FirstOrDefault() == "https" || request.IsHttps;
    }

    private sealed record SessionPayload(int UserId, long IssuedAt);
    private sealed record LoginRequest(string Username, string Password);
    private sealed record ChangePasswordRequest(string OldPassword, string NewPassword);

    private sealed class RateLimitExceededResult : IResult
    {
        private readonly int _retryAfterSeconds;

        public RateLimitExceededResult(int retryAfterSeconds)
        {
            _retryAfterSeconds = retryAfterSeconds;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = _retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return httpContext.Response.WriteAsJsonAsync(new { detail = "Too many attempts. Please try again later." });
        }
    }
}

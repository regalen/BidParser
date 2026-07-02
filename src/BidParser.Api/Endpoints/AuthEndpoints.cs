using System.Globalization;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Api.Options;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class AuthEndpoints
{
    // A fixed valid BCrypt hash to verify against when the username is unknown,
    // so login costs one BCrypt verification either way and doesn't leak which
    // usernames exist via response timing (L3).
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), workFactor: 12);

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
        SessionTokenService tokens,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<LoginRequest>(request, ct);
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

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Username == usernameKey, ct);
        var passwordOk = user is not null && BCrypt.Net.BCrypt.Verify(body.Value.Password, user.PasswordHash);
        if (user is null)
        {
            // Spend one BCrypt verification even for unknown usernames so timing
            // doesn't distinguish them from wrong passwords (L3).
            BCrypt.Net.BCrypt.Verify(body.Value.Password, DummyHash);
        }
        if (!passwordOk)
        {
            logger.LogWarning("Login failed {Username}", usernameKey);
            return Results.Json(new ApiError("Invalid username or password."), statusCode: StatusCodes.Status401Unauthorized);
        }

        AppendSessionCookie(response, request, options, tokens.CreateToken(user!));

        logger.LogInformation("Login success {Username}", user!.Username);
        return Results.Ok(new LoginResponse(UserPublic.FromEntity(user)));
    }

    private static void AppendSessionCookie(
        HttpResponse response, HttpRequest request, AppOptions options, string token)
    {
        response.Cookies.Append(SessionCookieAuthHandler.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = IsSecure(request),
            MaxAge = TimeSpan.FromHours(options.SessionLifetimeHours),
            Path = "/"
        });
    }

    private static IResult Logout(HttpContext context, HttpResponse response, ILogger<Program> logger)
    {
        response.Cookies.Delete(SessionCookieAuthHandler.CookieName, new CookieOptions { Path = "/" });
        logger.LogInformation(
            "Logout user={UserId}",
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown");
        return Results.Ok(new OkResponse());
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext context,
        HttpRequest request,
        HttpResponse response,
        AppDbContext db,
        AuthRateLimiter rateLimiter,
        AppOptions options,
        SessionTokenService tokens,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<ChangePasswordRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var limit = CheckRateLimit(rateLimiter, $"ip:{ClientIp(context)}", options.RateLimitAuthPerMin);
        if (limit is not null)
        {
            return limit;
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("not_authenticated"), statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!BCrypt.Net.BCrypt.Verify(body.Value.OldPassword, user.PasswordHash))
        {
            logger.LogWarning("Change password failed user={UserId}", user.Id);
            return Results.Json(new ApiError("Invalid password."), statusCode: StatusCodes.Status401Unauthorized);
        }

        var errors = PasswordPolicy.Validate(body.Value.NewPassword);
        if (errors.Count > 0)
        {
            return Results.Json(new PasswordValidationError(errors), statusCode: StatusCodes.Status400BadRequest);
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Value.NewPassword, workFactor: 12);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // Re-issue the cookie with the new password stamp so the current session
        // survives its own password change — every OTHER session for this user is
        // now revoked (their stamp no longer matches the stored hash).
        AppendSessionCookie(response, request, options, tokens.CreateToken(user));

        logger.LogInformation("Change password success user={UserId}", user.Id);
        return Results.Ok(new OkResponse());
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

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool IsSecure(HttpRequest request)
    {
        return request.Headers["X-Forwarded-Proto"].FirstOrDefault() == "https" || request.IsHttps;
    }

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
            return httpContext.Response.WriteAsJsonAsync(new ApiError("Too many attempts. Please try again later."));
        }
    }
}

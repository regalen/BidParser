using System.Globalization;
using System.Text.Json;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
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
        IDataProtectionProvider dataProtectionProvider,
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
        if (user is null || !BCrypt.Net.BCrypt.Verify(body.Value.Password, user.PasswordHash))
        {
            logger.LogWarning("Login failed {Username}", usernameKey);
            return Results.Json(new ApiError("Invalid username or password."), statusCode: StatusCodes.Status401Unauthorized);
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

        logger.LogInformation("Login success {Username}", user.Username);
        return Results.Ok(new LoginResponse(UserPublic.FromEntity(user)));
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
        AppDbContext db,
        AuthRateLimiter rateLimiter,
        AppOptions options,
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
            return httpContext.Response.WriteAsJsonAsync(new ApiError("Too many attempts. Please try again later."));
        }
    }
}

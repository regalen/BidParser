using System.Security.Cryptography;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("", ListUsersAsync);
        group.MapPost("", CreateUserAsync).AddEndpointFilter<RequireCsrfHeader>();
        group.MapPatch("/{userId:int}", UpdateUserAsync).AddEndpointFilter<RequireCsrfHeader>();
        group.MapDelete("/{userId:int}", DeleteUserAsync).AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    private static async Task<IResult> ListUsersAsync(AppDbContext db, CancellationToken ct)
    {
        var users = await db.Users.OrderBy(user => user.Username).ToListAsync(ct);
        return Results.Ok(users.Select(UserPublic.FromEntity));
    }

    private static async Task<IResult> CreateUserAsync(
        HttpContext context,
        HttpRequest request,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<UserCreateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var username = body.Value.Username.Trim();
        var name = body.Value.Name.Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length > 128 || string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return EndpointHelpers.ValidationProblem("Invalid user payload.");
        }
        if (!IsValidRole(body.Value.Role))
        {
            return EndpointHelpers.ValidationProblem("Invalid role.");
        }

        if (await UsernameExistsAsync(db, username, ct))
        {
            return Results.Json(new ApiError("Username already exists."), statusCode: StatusCodes.Status409Conflict);
        }

        var admin = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        var tempPassword = NewTempPassword();
        var user = new User
        {
            Username = username,
            Name = name,
            Role = ParseUserRole(body.Value.Role),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword, workFactor: 12),
            MustChangePassword = true
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await UsernameExistsAsync(db, username, ct))
            {
                return Results.Json(new ApiError("Username already exists."), statusCode: StatusCodes.Status409Conflict);
            }

            throw;
        }

        logger.LogInformation(
            "Admin {Action} user {TargetUserId} by {AdminUserId}",
            "Create",
            user.Id,
            admin?.Id);
        return Results.Ok(new UserWithTempPassword(UserPublic.FromEntity(user), tempPassword));
    }

    private static async Task<IResult> UpdateUserAsync(
        int userId,
        HttpContext context,
        HttpRequest request,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<UserUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("User not found."), statusCode: StatusCodes.Status404NotFound);
        }

        if (body.Value.Username is not null)
        {
            var username = body.Value.Username.Trim();
            if (string.IsNullOrWhiteSpace(username) || username.Length > 128)
            {
                return EndpointHelpers.ValidationProblem("Invalid username.");
            }

            if (!string.Equals(username, user.Username, StringComparison.OrdinalIgnoreCase))
            {
                if (await UsernameExistsAsync(db, username, ct))
                {
                    return Results.Json(new ApiError("Username already exists."), statusCode: StatusCodes.Status409Conflict);
                }

                user.Username = username;
            }
        }

        if (body.Value.Name is not null)
        {
            var name = body.Value.Name.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
            {
                return EndpointHelpers.ValidationProblem("Invalid name.");
            }
            user.Name = name;
        }

        if (body.Value.Role is not null)
        {
            if (!IsValidRole(body.Value.Role))
            {
                return EndpointHelpers.ValidationProblem("Invalid role.");
            }

            var newRole = ParseUserRole(body.Value.Role);
            if (newRole != user.Role)
            {
                if (user.Role == UserRole.Admin && newRole != UserRole.Admin && await AdminCountAsync(db, ct) <= 1)
                {
                    return Results.Json(new ApiError("Cannot remove the last admin."), statusCode: StatusCodes.Status409Conflict);
                }

                user.Role = newRole;
            }
        }

        string? tempPassword = null;
        if (body.Value.ResetPassword)
        {
            tempPassword = NewTempPassword();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword, workFactor: 12);
            user.MustChangePassword = true;
        }

        var admin = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Admin {Action} user {TargetUserId} by {AdminUserId}",
            "Update",
            user.Id,
            admin?.Id);
        return Results.Ok(new UserWithTempPassword(UserPublic.FromEntity(user), tempPassword));
    }

    private static async Task<IResult> DeleteUserAsync(
        int userId,
        HttpContext context,
        AppDbContext db,
        FileStorage storage,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var admin = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (admin is not null && userId == admin.Id)
        {
            return Results.Json(new ApiError("Admins cannot delete themselves."), statusCode: StatusCodes.Status409Conflict);
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("User not found."), statusCode: StatusCodes.Status404NotFound);
        }

        if (user.Role == UserRole.Admin && await AdminCountAsync(db, ct) <= 1)
        {
            return Results.Json(new ApiError("Cannot remove the last admin."), statusCode: StatusCodes.Status409Conflict);
        }

        await db.ParseMetrics
            .Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, (int?)null), ct);

        // Snapshot the file paths before the cascade removes the ParseJob rows —
        // RetentionService discovers files via those rows, so once they're gone
        // the uploads/outputs would be orphaned in UPLOAD_DIR forever.
        var paths = await db.ParseJobs
            .Where(j => j.UserId == userId)
            .Select(j => new { j.SourcePath, j.OutputPath })
            .ToListAsync(ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);

        // Delete files only after the DB commit succeeds.
        foreach (var p in paths)
        {
            storage.TryDelete(p.SourcePath);
            storage.TryDelete(p.OutputPath);
        }

        logger.LogInformation(
            "Admin {Action} user {TargetUserId} by {AdminUserId}",
            "Delete",
            userId,
            admin?.Id);
        return Results.Ok(new OkResponse());
    }

    // 10 hex chars of CSPRNG output — typed once, then forced to change via
    // MustChangePassword. Replaces the old fixed "changeme" credential.
    private static string NewTempPassword() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    private static Task<bool> UsernameExistsAsync(AppDbContext db, string username, CancellationToken ct)
    {
        return db.Users.AnyAsync(user => user.Username == username, ct);
    }

    private static Task<int> AdminCountAsync(AppDbContext db, CancellationToken ct)
    {
        return db.Users.CountAsync(user => user.Role == UserRole.Admin, ct);
    }

    private static bool IsValidRole(string role) =>
        Enum.TryParse<UserRole>(role, ignoreCase: true, out _);

    private static UserRole ParseUserRole(string role) =>
        Enum.Parse<UserRole>(role, ignoreCase: true);

    private sealed record UserCreateRequest(string Username, string Name, string Role = "user");
    private sealed record UserUpdateRequest(string? Username, string? Name, string? Role, bool ResetPassword = false);
}

using BidParser.Api.Auth;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
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

    private static async Task<IResult> ListUsersAsync(AppDbContext db)
    {
        var users = await db.Users.OrderBy(user => user.Username).ToListAsync();
        return Results.Ok(users.Select(UserPublic.FromEntity));
    }

    private static async Task<IResult> CreateUserAsync(HttpRequest request, AppDbContext db)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<UserCreateRequest>(request);
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

        if (await UsernameExistsAsync(db, username))
        {
            return Results.Json(new { detail = "Username already exists." }, statusCode: StatusCodes.Status409Conflict);
        }

        var user = new User
        {
            Username = username,
            Name = name,
            Role = body.Value.Role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("changeme", workFactor: 12),
            MustChangePassword = true
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (await UsernameExistsAsync(db, username))
            {
                return Results.Json(new { detail = "Username already exists." }, statusCode: StatusCodes.Status409Conflict);
            }

            throw;
        }

        return Results.Ok(UserPublic.FromEntity(user));
    }

    private static async Task<IResult> UpdateUserAsync(int userId, HttpRequest request, AppDbContext db)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<UserUpdateRequest>(request);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null)
        {
            return Results.Json(new { detail = "User not found." }, statusCode: StatusCodes.Status404NotFound);
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
                if (await UsernameExistsAsync(db, username))
                {
                    return Results.Json(new { detail = "Username already exists." }, statusCode: StatusCodes.Status409Conflict);
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

        if (body.Value.Role is not null && body.Value.Role != user.Role)
        {
            if (!IsValidRole(body.Value.Role))
            {
                return EndpointHelpers.ValidationProblem("Invalid role.");
            }

            if (user.Role == UserRole.Admin && body.Value.Role != UserRole.Admin && await AdminCountAsync(db) <= 1)
            {
                return Results.Json(new { detail = "Cannot remove the last admin." }, statusCode: StatusCodes.Status409Conflict);
            }

            user.Role = body.Value.Role;
        }

        if (body.Value.ResetPassword)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("changeme", workFactor: 12);
            user.MustChangePassword = true;
        }

        await db.SaveChangesAsync();
        return Results.Ok(UserPublic.FromEntity(user));
    }

    private static async Task<IResult> DeleteUserAsync(int userId, HttpContext context, AppDbContext db)
    {
        var admin = await EndpointHelpers.CurrentUserAsync(context, db);
        if (admin is not null && userId == admin.Id)
        {
            return Results.Json(new { detail = "Admins cannot delete themselves." }, statusCode: StatusCodes.Status409Conflict);
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null)
        {
            return Results.Json(new { detail = "User not found." }, statusCode: StatusCodes.Status404NotFound);
        }

        if (user.Role == UserRole.Admin && await AdminCountAsync(db) <= 1)
        {
            return Results.Json(new { detail = "Cannot remove the last admin." }, statusCode: StatusCodes.Status409Conflict);
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Results.Ok(new { ok = true });
    }

    private static Task<bool> UsernameExistsAsync(AppDbContext db, string username)
    {
        return db.Users.AnyAsync(user => user.Username == username);
    }

    private static Task<int> AdminCountAsync(AppDbContext db)
    {
        return db.Users.CountAsync(user => user.Role == UserRole.Admin);
    }

    private static bool IsValidRole(string role)
    {
        return role is UserRole.Admin or UserRole.User;
    }

    private sealed record UserCreateRequest(string Username, string Name, string Role = UserRole.User);
    private sealed record UserUpdateRequest(string? Username, string? Name, string? Role, bool ResetPassword = false);
}

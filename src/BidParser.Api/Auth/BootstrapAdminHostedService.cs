using BidParser.Api.Options;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Auth;

public sealed class BootstrapAdminHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppOptions _options;

    public BootstrapAdminHostedService(IServiceScopeFactory scopeFactory, AppOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var admin = new User
        {
            Username = _options.AdminUsername,
            Name = "Administrator",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(_options.AdminPassword, workFactor: 12),
            Role = UserRole.Admin,
            MustChangePassword = true
        };

        db.Users.Add(admin);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await db.Users.AnyAsync(cancellationToken))
            {
                throw;
            }

            // Another startup path seeded the first user concurrently.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

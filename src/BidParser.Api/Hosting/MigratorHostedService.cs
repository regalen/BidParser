using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Hosting;

public sealed class MigratorHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MigratorHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

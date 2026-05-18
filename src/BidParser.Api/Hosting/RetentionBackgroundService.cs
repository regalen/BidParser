using BidParser.Api.Options;
using BidParser.Infrastructure.Services;

namespace BidParser.Api.Hosting;

public sealed class RetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _retentionDays;
    private readonly ILogger<RetentionBackgroundService> _logger;

    public RetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        AppOptions options,
        ILogger<RetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _retentionDays = options.RetentionDays;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // sleep first — mirrors Python's _retention_loop() pattern
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<RetentionService>();
                var deleted = await service.CleanupOldParseJobsAsync(_retentionDays, stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Retention cleanup: removed {Count} expired parse jobs", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention cleanup failed");
            }
        }
    }
}

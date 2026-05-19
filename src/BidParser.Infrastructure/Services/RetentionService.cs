using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Services;

public sealed class RetentionService(AppDbContext db, FileStorage storage)
{
    public async Task<int> CleanupOldParseJobsAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var filePaths = await db.ParseJobs
            .Where(j => j.CreatedAt < cutoff)
            .Select(j => new { j.SourcePath, j.OutputPath })
            .ToListAsync(ct);

        foreach (var paths in filePaths)
        {
            storage.TryDelete(paths.SourcePath);
            storage.TryDelete(paths.OutputPath);
        }

        var deleted = await db.ParseJobs
            .Where(j => j.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        return deleted;
    }
}

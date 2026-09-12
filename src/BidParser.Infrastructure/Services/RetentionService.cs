using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Services;

/// <summary>
/// Purges parse jobs and failure records older than RETENTION_DAYS, deleting their stored input/output
/// files first (best-effort) then the rows. Invoked on a schedule by the API's retention background service.
/// </summary>
public sealed class RetentionService(AppDbContext db, FileStorage storage)
{
    /// <summary>Deletes jobs + failures older than <paramref name="retentionDays"/> days and their files; returns the parse-job count removed.</summary>
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

        var oldFailures = await db.FailedParseJobs
            .Where(f => f.CreatedAt < cutoff)
            .Select(f => new { f.SourcePath })
            .ToListAsync(ct);

        foreach (var f in oldFailures)
        {
            storage.TryDelete(f.SourcePath);
        }

        await db.FailedParseJobs
            .Where(f => f.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        return deleted;
    }
}

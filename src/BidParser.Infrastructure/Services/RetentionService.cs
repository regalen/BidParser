using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Services;

public sealed class RetentionService
{
    private readonly AppDbContext _db;
    private readonly FileStorage _storage;

    public RetentionService(AppDbContext db, FileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<int> CleanupOldParseJobsAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var jobs = await _db.ParseJobs.Where(j => j.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var job in jobs)
        {
            _storage.TryDelete(job.SourcePath);
            _storage.TryDelete(job.OutputPath);
            _db.ParseJobs.Remove(job);
        }
        await _db.SaveChangesAsync(ct);
        return jobs.Count;
    }
}

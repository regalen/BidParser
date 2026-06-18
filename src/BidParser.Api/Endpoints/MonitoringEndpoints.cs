namespace BidParser.Api.Endpoints;

using System.Globalization;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitoring").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/runs", ListRunsAsync);
        group.MapGet("/failures/{id:int}/source", GetSourceAsync);
        group.MapGet("/jobs/{id:int}/source", GetJobSourceAsync);
        group.MapGet("/jobs/{id:int}/output", GetJobOutputAsync);
    }

    private static async Task<IResult> ListRunsAsync(
        AppDbContext db,
        IParserRegistry registry,
        CancellationToken cancellationToken,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? vendor = null,
        [FromQuery] int? userId = null,
        [FromQuery] string? parserSlug = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 25,
        [FromQuery] int offset = 0)
    {
        if (!string.IsNullOrEmpty(from) && !DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Results.BadRequest(new ApiError("Invalid 'from' date — expected yyyy-MM-dd."));
        if (!string.IsNullOrEmpty(to) && !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Results.BadRequest(new ApiError("Invalid 'to' date — expected yyyy-MM-dd."));

        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        DateTime? fromUtc = string.IsNullOrEmpty(from)
            ? null
            : DateTime.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToUniversalTime();
        DateTime? toUtc = string.IsNullOrEmpty(to)
            ? null
            : DateTime.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToUniversalTime();

        // success / validation_mismatch live in ParseJob; the exception categories in FailedParseJob.
        var includeJobs = status is null or "success" or "validation_mismatch";
        var includeFailures = status is null or "magic_byte_mismatch" or "parser_error" or "unhandled_exception";

        string Display(string slug) =>
            registry.Parsers.FirstOrDefault(p => p.Slug == slug)?.DisplayName ?? slug;

        // Bound each table's read to the rows that could land on this page once merged.
        var pageCeiling = offset + limit;

        var jobTotal = 0;
        var jobRows = new List<MonitoringRunItem>();
        if (includeJobs)
        {
            var jobs = db.ParseJobs.AsNoTracking().Include(j => j.User).AsQueryable();
            if (fromUtc.HasValue) jobs = jobs.Where(j => j.CreatedAt >= fromUtc.Value);
            if (toUtc.HasValue) jobs = jobs.Where(j => j.CreatedAt < toUtc.Value);
            if (!string.IsNullOrEmpty(vendor)) jobs = jobs.Where(j => j.Vendor == vendor);
            if (userId.HasValue) jobs = jobs.Where(j => j.UserId == userId.Value);
            if (!string.IsNullOrEmpty(parserSlug)) jobs = jobs.Where(j => j.ParserSlug == parserSlug);
            if (status == "success") jobs = jobs.Where(j => j.TotalsMatch);
            else if (status == "validation_mismatch") jobs = jobs.Where(j => !j.TotalsMatch);

            jobTotal = await jobs.CountAsync(cancellationToken);
            var jobPage = await jobs
                .OrderByDescending(j => j.CreatedAt)
                .ThenByDescending(j => j.Id)
                .Take(pageCeiling)
                .ToListAsync(cancellationToken);

            jobRows = jobPage.Select(j => new MonitoringRunItem(
                "job",
                j.Id,
                j.TotalsMatch ? "success" : "validation_mismatch",
                DateTime.SpecifyKind(j.CreatedAt, DateTimeKind.Utc),
                j.UserId,
                j.User?.Username ?? "",
                j.User?.Name,
                j.Vendor,
                j.ParserSlug,
                Display(j.ParserSlug),
                j.SourceFilename,
                File.Exists(j.SourcePath),
                File.Exists(j.OutputPath),
                j.ComputedTotal.ToString("F2", CultureInfo.InvariantCulture),
                j.QuotedTotal?.ToString("F2", CultureInfo.InvariantCulture),
                null, null, null, null)).ToList();
        }

        var failureTotal = 0;
        var failureRows = new List<MonitoringRunItem>();
        if (includeFailures)
        {
            // Validation mismatches are surfaced via their ParseJob (single row, with output).
            var failures = db.FailedParseJobs.AsNoTracking()
                .Where(f => f.Category != FailureCategory.ValidationMismatch);
            if (fromUtc.HasValue) failures = failures.Where(f => f.CreatedAt >= fromUtc.Value);
            if (toUtc.HasValue) failures = failures.Where(f => f.CreatedAt < toUtc.Value);
            if (!string.IsNullOrEmpty(vendor)) failures = failures.Where(f => f.Vendor == vendor);
            if (userId.HasValue) failures = failures.Where(f => f.UserId == userId.Value);
            if (!string.IsNullOrEmpty(parserSlug)) failures = failures.Where(f => f.ParserSlug == parserSlug);
            if (status == "magic_byte_mismatch") failures = failures.Where(f => f.Category == FailureCategory.MagicByteMismatch);
            else if (status == "parser_error") failures = failures.Where(f => f.Category == FailureCategory.ParserError);
            else if (status == "unhandled_exception") failures = failures.Where(f => f.Category == FailureCategory.UnhandledException);

            failureTotal = await failures.CountAsync(cancellationToken);
            var failurePage = await failures
                .OrderByDescending(f => f.CreatedAt)
                .ThenByDescending(f => f.Id)
                .Take(pageCeiling)
                .ToListAsync(cancellationToken);

            failureRows = failurePage.Select(f => new MonitoringRunItem(
                "failure",
                f.Id,
                CategoryToSnakeCase(f.Category),
                DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc),
                f.UserId,
                f.UserUsername,
                f.UserName,
                f.Vendor,
                f.ParserSlug,
                Display(f.ParserSlug),
                f.SourceFilename,
                File.Exists(f.SourcePath),
                false,
                f.ComputedTotal?.ToString("F2", CultureInfo.InvariantCulture),
                f.QuotedTotal?.ToString("F2", CultureInfo.InvariantCulture),
                f.Stage,
                f.Hint,
                f.Message,
                f.ErrorDetail)).ToList();
        }

        var items = jobRows
            .Concat(failureRows)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Results.Ok(new MonitoringRunsResponse(jobTotal + failureTotal, items));
    }

    private static async Task<IResult> GetSourceAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var failure = await db.FailedParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (failure is null || !File.Exists(failure.SourcePath))
        {
            return Results.NotFound();
        }

        return FileResult(failure.SourcePath, failure.SourceFilename);
    }

    private static async Task<IResult> GetJobSourceAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.ParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null || !File.Exists(job.SourcePath))
        {
            return Results.NotFound();
        }

        return FileResult(job.SourcePath, job.SourceFilename);
    }

    private static async Task<IResult> GetJobOutputAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.ParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null || !File.Exists(job.OutputPath))
        {
            return Results.NotFound();
        }

        var downloadName = $"{Path.GetFileNameWithoutExtension(job.SourceFilename)}_parsed.xlsx";
        return Results.File(
            new FileStream(job.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: downloadName);
    }

    private static IResult FileResult(string path, string downloadName)
    {
        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.File(stream, contentType, fileDownloadName: downloadName);
    }

    private static string CategoryToSnakeCase(FailureCategory category) => category switch
    {
        FailureCategory.MagicByteMismatch  => "magic_byte_mismatch",
        FailureCategory.ParserError        => "parser_error",
        FailureCategory.UnhandledException => "unhandled_exception",
        FailureCategory.ValidationMismatch => "validation_mismatch",
        _ => category.ToString().ToLowerInvariant()
    };
}

namespace BidParser.Api.Endpoints;

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

        group.MapGet("/failures", ListFailuresAsync);
        group.MapGet("/failures/{id:int}/source", GetSourceAsync);
    }

    private static async Task<IResult> ListFailuresAsync(
        AppDbContext db,
        IParserRegistry registry,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 25,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var total = await db.FailedParseJobs.CountAsync(cancellationToken);

        var rows = await db.FailedParseJobs
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(f => new FailedParseJobItem(
                f.Id,
                DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc),
                f.UserId,
                f.UserUsername,
                f.UserName,
                f.Vendor,
                f.ParserSlug,
                registry.Parsers.FirstOrDefault(p => p.Slug == f.ParserSlug)?.DisplayName ?? f.ParserSlug,
                f.SourceFilename,
                CategoryToSnakeCase(f.Category),
                f.Stage,
                f.Hint,
                f.Message,
                f.ErrorDetail,
                File.Exists(f.SourcePath),
                f.ComputedTotal?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                f.QuotedTotal?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        return Results.Ok(new FailedParseJobListResponse(total, items));
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

        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(failure.SourcePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = new FileStream(failure.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.File(stream, contentType, fileDownloadName: failure.SourceFilename);
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

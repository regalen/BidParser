namespace BidParser.Api.Endpoints;

using BidParser.Api.Auth;
using BidParser.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/monitoring").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/failures", async (AppDbContext db, BidParser.Domain.Abstractions.IParserRegistry registry, [FromQuery] int limit = 25, [FromQuery] int offset = 0) =>
        {
            limit = Math.Clamp(limit, 1, 100);
            offset = Math.Max(0, offset);

            var query = db.FailedParseJobs.AsNoTracking();
            var total = await query.CountAsync();

            var failures = await query
                .OrderByDescending(f => f.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            var items = failures.Select(f => new
            {
                id = f.Id,
                created_at = f.CreatedAt,
                user_id = f.UserId,
                username = f.UserUsername,
                name = f.UserName,
                vendor = f.Vendor,
                parser_slug = f.ParserSlug,
                parser_display_name = registry.Parsers.FirstOrDefault(p => p.Slug == f.ParserSlug)?.DisplayName ?? f.ParserSlug,
                source_filename = f.SourceFilename,
                category = f.Category.ToString(),
                stage = f.Stage,
                hint = f.Hint,
                message = f.Message,
                error_detail = f.ErrorDetail,
                source_available = File.Exists(f.SourcePath)
            });
            return Results.Ok(new
            {
                total,
                items
            });
        });

        group.MapGet("/failures/{id:int}/source", async (int id, AppDbContext db, HttpContext context) =>
        {
            var failure = await db.FailedParseJobs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
            if (failure is null) return Results.NotFound();

            if (!File.Exists(failure.SourcePath))
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
        });
    }
}

using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using BidParser.Api.Auth;
using BidParser.Api.Serialization;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/history", ListAsync).RequireAuthorization(AuthPolicies.ActiveUser);
        app.MapGet("/api/history/{id:int}/source", DownloadSourceAsync).RequireAuthorization(AuthPolicies.ActiveUser);
        app.MapGet("/api/history/{id:int}/output", DownloadOutputAsync).RequireAuthorization(AuthPolicies.ActiveUser);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        AppDbContext db,
        IParserRegistry registry,
        int limit = 10,
        int offset = 0,
        string? q = null,
        CancellationToken ct = default)
    {
        var user = await EndpointHelpers.CurrentUserAsync(context, db);
        if (user is null)
        {
            return Results.Json(new { detail = "not_authenticated" }, statusCode: 401);
        }

        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(offset, 0);
        var needle = q?.Trim() ?? "";

        var query = db.ParseJobs.Where(j => j.UserId == user.Id);
        if (needle.Length > 0)
        {
            var lower = needle.ToLower();
            query = query.Where(j => j.SourceFilename.ToLower().Contains(lower));
        }

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var parserLookup = registry.Parsers.ToDictionary(p => p.Slug, p => p.DisplayName);
        var rows = jobs.Select(j => new HistoryRow(
            j.Id,
            j.SourceFilename,
            j.Vendor,
            j.ParserSlug,
            parserLookup.TryGetValue(j.ParserSlug, out var name) ? name : j.ParserSlug,
            j.FxRate,
            j.Margin,
            RelativeWhen(j.CreatedAt),
            j.TotalsMatch)).ToList();

        return Results.Ok(new HistoryResponse(rows, total));
    }

    private static async Task<IResult> DownloadSourceAsync(
        int id, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var job = await GetJobForUserAsync(id, context, db, ct);
        if (job is null)
        {
            return Results.Json(new { detail = "Job not found." }, statusCode: 404);
        }

        if (!File.Exists(job.SourcePath))
        {
            return Results.Json(new { detail = "File not found." }, statusCode: 404);
        }

        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{job.SourceFilename}\"";
        return Results.File(
            new FileStream(job.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            MimeForExtension(job.SourcePath));
    }

    private static async Task<IResult> DownloadOutputAsync(
        int id, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var job = await GetJobForUserAsync(id, context, db, ct);
        if (job is null)
        {
            return Results.Json(new { detail = "Job not found." }, statusCode: 404);
        }

        if (!File.Exists(job.OutputPath))
        {
            return Results.Json(new { detail = "File not found." }, statusCode: 404);
        }

        var downloadName = $"{Path.GetFileNameWithoutExtension(job.SourceFilename)}_parsed.xlsx";
        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{downloadName}\"";
        return Results.File(
            new FileStream(job.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private static async Task<ParseJob?> GetJobForUserAsync(
        int id, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var idValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, CultureInfo.InvariantCulture, out var userId))
        {
            return null;
        }

        var job = await db.ParseJobs.SingleOrDefaultAsync(j => j.Id == id, ct);
        return job is null || job.UserId != userId ? null : job;
    }

    private static string MimeForExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

    // Verbatim port of Python's _relative_when(). Strings must match exactly.
    internal static string RelativeWhen(DateTime value)
    {
        var seconds = (int)(DateTime.UtcNow - value).TotalSeconds;
        if (seconds < 60) return "just now";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{minutes}m ago";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h ago";
        var days = hours / 24;
        if (days == 1) return "Yesterday";
        if (days < 7) return $"{days} days ago";
        return value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private sealed record HistoryResponse(
        IReadOnlyList<HistoryRow> Rows,
        int Total);

    private sealed record HistoryRow(
        int Id,
        string SourceFilename,
        string Vendor,
        string ParserSlug,
        string FileTypeDisplay,
        [property: JsonConverter(typeof(HistoryFxRateConverter))] decimal FxRate,
        [property: JsonConverter(typeof(HistoryMarginConverter))] decimal Margin,
        string When,
        bool TotalsMatch);

    private sealed class HistoryFxRateConverter : JsonStringDecimalConverter
    {
        public HistoryFxRateConverter() : base(4) { }
    }

    private sealed class HistoryMarginConverter : JsonStringDecimalConverter
    {
        public HistoryMarginConverter() : base(2) { }
    }
}

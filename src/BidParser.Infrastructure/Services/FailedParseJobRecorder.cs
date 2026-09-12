namespace BidParser.Infrastructure.Services;

using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Writes <see cref="FailedParseJob"/> rows on a fresh DbContext scope (so it works from a catch block
/// even when the request's context is in a bad state). <see cref="RecordAsync"/> classifies an exception
/// into a <see cref="FailureCategory"/>; <see cref="RecordMismatchAsync"/> logs a success-path totals
/// mismatch. Best-effort — callers swallow its failures so the user's request is unaffected.
/// </summary>
public sealed class FailedParseJobRecorder(IServiceScopeFactory scopeFactory)
{
    /// <summary>Classifies <paramref name="ex"/> into a failure category and records it (upload → magic-byte, ParseError → parser, else unhandled).</summary>
    public async Task RecordAsync(
        User user, string vendor, string parserSlug,
        string displayFilename, string sourcePath,
        decimal fxRate, decimal margin,
        Exception ex, ImportType importType, CancellationToken ct)
    {
        var category = ex switch
        {
            ParseError pe when pe.Stage == "upload" => FailureCategory.MagicByteMismatch,
            ParseError                              => FailureCategory.ParserError,
            _                                       => FailureCategory.UnhandledException,
        };

        var failure = new FailedParseJob
        {
            UserId         = user.Id,
            UserUsername   = user.Username,
            UserName       = user.Name,
            Vendor         = vendor,
            ParserSlug     = parserSlug,
            SourceFilename = displayFilename,
            SourcePath     = sourcePath,
            Category       = category,
            Stage          = (ex as ParseError)?.Stage,
            Hint           = (ex as ParseError)?.Hint,
            Message        = (ex as ParseError)?.Message,
            ErrorDetail    = ex.ToString(),
            FxRate         = fxRate,
            Margin         = margin,
            ImportType     = importType,
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(failure);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records a successful parse that produced a totals mismatch so admins can review it
    /// in the monitoring view. The source file is already retained by the corresponding
    /// <c>ParseJob</c> and shared via <paramref name="sourcePath"/> — no copy is made.
    /// </summary>
    public async Task RecordMismatchAsync(
        User user, string vendor, string parserSlug,
        string displayFilename, string sourcePath,
        decimal fxRate, decimal margin,
        decimal computedTotal, decimal? quotedTotal,
        ImportType importType,
        CancellationToken ct)
    {
        var detail = quotedTotal.HasValue
            ? $"Computed total: {computedTotal:F2} · Quoted total: {quotedTotal.Value:F2} · Difference: {Math.Abs(computedTotal - quotedTotal.Value):F2}"
            : $"Computed total: {computedTotal:F2} · Quoted total: (none)";

        var failure = new FailedParseJob
        {
            UserId         = user.Id,
            UserUsername   = user.Username,
            UserName       = user.Name,
            Vendor         = vendor,
            ParserSlug     = parserSlug,
            SourceFilename = displayFilename,
            SourcePath     = sourcePath,
            Category       = FailureCategory.ValidationMismatch,
            ComputedTotal  = computedTotal,
            QuotedTotal    = quotedTotal,
            ErrorDetail    = detail,
            FxRate         = fxRate,
            Margin         = margin,
            ImportType     = importType,
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(failure);
        await db.SaveChangesAsync(ct);
    }
}

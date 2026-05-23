namespace BidParser.Infrastructure.Services;

using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

public sealed class FailedParseJobRecorder(IServiceScopeFactory scopeFactory)
{
    public async Task RecordAsync(
        User user, string vendor, string parserSlug,
        string displayFilename, string sourcePath,
        decimal fxRate, decimal margin,
        Exception ex, CancellationToken ct)
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
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(failure);
        await db.SaveChangesAsync(ct);
    }
}

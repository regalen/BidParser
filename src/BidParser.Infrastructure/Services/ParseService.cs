using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using BidParser.Output;

namespace BidParser.Infrastructure.Services;

public sealed class ParseService(IParserRegistry registry, FileStorage storage, AppDbContext db, FailedParseJobRecorder failureRecorder)
{
    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public async Task<ParseServiceResult> ParseAsync(
        User user,
        Stream fileStream,
        string uploadFilename,
        string vendor,
        string parserSlug,
        decimal fxRate,
        decimal margin,
        long maxUploadBytes,
        CancellationToken ct = default)
    {
        var parser = ResolveParser(parserSlug, vendor);
        ValidateExtension(uploadFilename, parser.AcceptedMime);

        var displayFilename = Path.GetFileName(uploadFilename);
        var sourcePath = storage.NewOriginalPath(displayFilename);
        var outputPath = storage.NewOutputPath();

        await storage.SaveUploadAsync(fileStream, sourcePath, maxUploadBytes, ct);

        try
        {
            await ValidateMagicBytesAsync(sourcePath, parser.AcceptedMime, ct);

            var result = parser.Parse(sourcePath);

            ForeignUpliftWriter.WriteForeignUplift(
                result.LineItems,
                outputPath,
                margin,
                fxRate,
                vendor.ToUpperInvariant(),
                result.Metadata.Currency,
                parser.Slug);

            var fxRateRounded = Math.Round(fxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(margin, 2, MidpointRounding.AwayFromZero);

            var job = new ParseJob
            {
                UserId = user.Id,
                Vendor = vendor,
                ParserSlug = parser.Slug,
                SourceFilename = displayFilename,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                FxRate = fxRateRounded,
                Margin = marginRounded,
                ComputedTotal = result.Validation.ComputedTotal,
                QuotedTotal = result.Validation.QuotedTotal,
                TotalsMatch = result.Validation.Matches
            };

            var metric = new ParseMetric
            {
                UserId = user.Id,
                UserUsername = user.Username,
                UserName = user.Name,
                Vendor = vendor,
                ParserSlug = parser.Slug,
                SourceFilename = displayFilename,
                Currency = result.Metadata.Currency,
                QuotedTotal = result.Validation.QuotedTotal,
                ComputedTotal = result.Validation.ComputedTotal,
                TotalsMatch = result.Validation.Matches,
                FxRate = fxRateRounded,
                Margin = marginRounded,
                ParseJob = job,
            };

            user.DefaultVendor = vendor;
            user.FxRate = fxRateRounded;
            user.Margin = marginRounded;
            db.Update(user);
            db.Add(job);
            db.Add(metric);
            await db.SaveChangesAsync(ct);

            if (!result.Validation.Matches)
            {
                // Record a monitoring entry so admins can review mismatches.
                // This is best-effort: we swallow any recorder failure so the
                // user's successful parse is unaffected.
                try
                {
                    await failureRecorder.RecordMismatchAsync(
                        user, vendor, parser.Slug, displayFilename, sourcePath,
                        fxRateRounded, marginRounded,
                        result.Validation.ComputedTotal, result.Validation.QuotedTotal,
                        ct);
                }
                catch
                {
                    // Monitoring record failed — parse job is already committed, continue.
                }
            }

            return new ParseServiceResult(
                job,
                OutputNaming.OutputFilename(displayFilename),
                outputPath,
                result.Validation);
        }
        catch (Exception ex)
        {
            storage.TryDelete(outputPath);

            var fxRateRounded = Math.Round(fxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(margin, 2, MidpointRounding.AwayFromZero);

            await failureRecorder.RecordAsync(
                user, vendor, parser.Slug, displayFilename, sourcePath,
                fxRateRounded, marginRounded, ex, ct);

            throw;
        }
    }

    private IParser ResolveParser(string parserSlug, string vendor)
    {
        var parser = registry.Parsers.FirstOrDefault(p => p.Slug == parserSlug);
        if (parser is null)
        {
            throw new ParseValidationException(400, "Unknown parser.");
        }

        if (parser.Vendor != vendor)
        {
            throw new ParseValidationException(400, "Parser does not match vendor.");
        }

        return parser;
    }

    private static void ValidateExtension(string filename, string parserAcceptedMime)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (!ExtensionToMime.TryGetValue(ext, out var extensionMime))
        {
            throw new ParseValidationException(415, "Only PDF and XLSX files are supported.");
        }

        if (extensionMime != parserAcceptedMime)
        {
            throw new ParseValidationException(400, "File extension does not match selected parser.");
        }
    }

    private static async Task ValidateMagicBytesAsync(string path, string parserAcceptedMime, CancellationToken ct)
    {
        var header = new byte[4];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = await stream.ReadAsync(header, ct);

        var matches = parserAcceptedMime switch
        {
            "application/pdf" => bytesRead >= 4
                && header[0] == 0x25
                && header[1] == 0x50
                && header[2] == 0x44
                && header[3] == 0x46,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => bytesRead >= 4
                && header[0] == 0x50
                && header[1] == 0x4B
                && header[2] == 0x03
                && header[3] == 0x04,
            _ => false
        };

        if (!matches)
        {
            throw new ParseError("upload", "Unsupported file format.", "Unsupported file format.");
        }
    }
}

public sealed record ParseServiceResult(
    ParseJob Job,
    string OutputFilename,
    string OutputPath,
    ValidationResult Validation);

public sealed class ParseValidationException : Exception
{
    public ParseValidationException(int statusCode, string detail) : base(detail)
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    public int StatusCode { get; }
    public string Detail { get; }
}

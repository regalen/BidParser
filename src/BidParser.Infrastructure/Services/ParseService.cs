using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using BidParser.Output;
using Microsoft.Extensions.Logging;

namespace BidParser.Infrastructure.Services;

public sealed class ParseService(IParserRegistry registry, FileStorage storage, AppDbContext db, FailedParseJobRecorder failureRecorder, ILogger<ParseService> logger)
{
    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel"
    };

    public async Task<ParseServiceResult> ParseAsync(
        User user,
        Stream fileStream,
        string uploadFilename,
        string vendor,
        string parserSlug,
        decimal? fxRate,
        decimal? margin,
        decimal? imPercent,
        decimal? onCostPercent,
        string? crmTemplate,
        long maxUploadBytes,
        CancellationToken ct = default)
    {
        var parser = ResolveParser(parserSlug, vendor);
        ValidateExtension(uploadFilename, parser.AcceptedMime);

        var displayFilename = Path.GetFileName(uploadFilename);
        var sourcePath = storage.NewOriginalPath(displayFilename);
        var outputPath = storage.NewOutputPath();

        await storage.SaveUploadAsync(fileStream, sourcePath, maxUploadBytes, ct);

        // Effective values used by writers and persisted on the ParseJob/ParseMetric ledger.
        // The User's defaults are only updated when the caller explicitly supplied a value
        // (see User mutation block below), so omitting margin from an HP No Calculation parse
        // doesn't clobber a saved value.
        var effectiveFxRate = fxRate ?? 1m;
        var effectiveMargin = margin ?? 0m;

        try
        {
            await ValidateMagicBytesAsync(sourcePath, parser.AcceptedMime, ct);

            var result = parser.Parse(sourcePath);

            // Resolve the effective CRM template, validate it against what the parser supports.
            var template = string.IsNullOrEmpty(crmTemplate) ? parser.CrmTemplate : crmTemplate;
            if (!parser.AvailableTemplates.Contains(template))
            {
                throw new ParseValidationException(400, "Unknown CRM template for this parser.");
            }

            switch (template)
            {
                case CrmTemplates.ForeignUplift:
                    ForeignUpliftWriter.WriteForeignUplift(
                        result.LineItems, outputPath, effectiveMargin, effectiveFxRate,
                        vendor.ToUpperInvariant(), result.Metadata.Currency, parser.Slug);
                    break;
                case CrmTemplates.NoCalculation:
                    AnzGenericWriter.Write(
                        result.LineItems, outputPath, "No Calculation",
                        includeMargin: false, effectiveMargin, vendor.ToUpperInvariant(),
                        onCost: onCostPercent);
                    break;
                case CrmTemplates.Uplift:
                    AnzGenericWriter.Write(
                        result.LineItems, outputPath, "Uplift",
                        includeMargin: true, effectiveMargin, vendor.ToUpperInvariant(),
                        onCost: onCostPercent);
                    break;
                case CrmTemplates.PercentOffWithUplift:
                    if (imPercent is null)
                    {
                        throw new ParseValidationException(400, "IM% is required for OneConfig (XLSX).");
                    }
                    PercentOffWithUpliftWriter.Write(
                        result.LineItems, outputPath, effectiveMargin, imPercent.Value, vendor.ToUpperInvariant());
                    break;
                default:
                    throw new ParseValidationException(400, "Unsupported CRM template.");
            }

            var fxRateRounded = Math.Round(effectiveFxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(effectiveMargin, 2, MidpointRounding.AwayFromZero);

            var job = new ParseJob
            {
                UserId = user.Id,
                Vendor = vendor,
                ParserSlug = parser.Slug,
                CrmTemplate = template,
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

            // User defaults: only the last-used vendor is remembered. fx_rate / margin /
            // im_percent are intentionally NOT persisted — the user must enter them
            // every parse so a stale value never gets silently applied.
            user.DefaultVendor = vendor;
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
                catch (Exception ex)
                {
                    // Monitoring record failed — parse job is already committed, continue.
                    logger.LogError(ex,
                        "Failed to record validation_mismatch monitoring entry for parse job {ParseJobId} (user {UserId}, parser {ParserSlug})",
                        job.Id, user.Id, parser.Slug);
                }
            }

            var cancelledLines = result.LineItems
                .Where(i => i.IsCancelled)
                .Select(i => new CancelledLine(i.LineSequence ?? string.Empty, i.Vpn))
                .ToList();

            return new ParseServiceResult(
                job,
                OutputNaming.OutputFilename(displayFilename),
                outputPath,
                result.Validation,
                result.Metadata.Currency,
                cancelledLines);
        }
        catch (Exception ex)
        {
            storage.TryDelete(outputPath);

            var fxRateRounded = Math.Round(effectiveFxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(effectiveMargin, 2, MidpointRounding.AwayFromZero);

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
            throw new ParseValidationException(415, "Only PDF, XLS, and XLSX files are supported.");
        }

        if (extensionMime != parserAcceptedMime)
        {
            throw new ParseValidationException(400, "File extension does not match selected parser.");
        }
    }

    private static async Task ValidateMagicBytesAsync(string path, string parserAcceptedMime, CancellationToken ct)
    {
        // Read enough bytes to detect either OLE compound doc or an HTML signature.
        var header = new byte[512];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = await stream.ReadAsync(header, ct);

        var matches = parserAcceptedMime switch
        {
            "application/pdf" => bytesRead >= 4
                && header[0] == 0x25    // %
                && header[1] == 0x50    // P
                && header[2] == 0x44    // D
                && header[3] == 0x46,   // F
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => bytesRead >= 4
                && header[0] == 0x50    // P
                && header[1] == 0x4B    // K
                && header[2] == 0x03
                && header[3] == 0x04,
            // OLE Compound Document signature (real legacy .xls, e.g. Lenovo BRDA DCG)
            // OR HTML content (Zebra Price Concession exports HTML under a .xls filename).
            // DELIBERATE DEVIATION: Zebra's portal saves styled HTML with a .xls extension.
            // Real-OLE files still pass the OLE check; HTML files are identified by finding
            // a '<' character in the leading 512 bytes after skipping ASCII whitespace.
            "application/vnd.ms-excel" => IsOleCompoundDoc(header, bytesRead)
                                       || IsHtmlContent(header, bytesRead),
            _ => false
        };

        if (!matches)
        {
            throw new ParseError("upload", "Unsupported file format.", "Unsupported file format.");
        }
    }

    private static bool IsOleCompoundDoc(byte[] header, int bytesRead)
        => bytesRead >= 8
            && header[0] == 0xD0
            && header[1] == 0xCF
            && header[2] == 0x11
            && header[3] == 0xE0
            && header[4] == 0xA1
            && header[5] == 0xB1
            && header[6] == 0x1A
            && header[7] == 0xE1;

    private static bool IsHtmlContent(byte[] header, int bytesRead)
    {
        // After skipping leading ASCII whitespace, a '<' identifies an HTML document.
        for (var i = 0; i < bytesRead; i++)
        {
            if (header[i] == (byte)' ' || header[i] == (byte)'\t'
                || header[i] == (byte)'\r' || header[i] == (byte)'\n')
            {
                continue;
            }
            return header[i] == (byte)'<';
        }
        return false;
    }
}

public sealed record ParseServiceResult(
    ParseJob Job,
    string OutputFilename,
    string OutputPath,
    ValidationResult Validation,
    string Currency,
    IReadOnlyList<CancelledLine> CancelledLines);

/// <summary>
/// A line item flagged as cancelled in the source document.
/// Surfaced to the frontend via the <c>X-Cancelled-Lines</c> response header.
/// </summary>
public sealed record CancelledLine(string Line, string Vpn);

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

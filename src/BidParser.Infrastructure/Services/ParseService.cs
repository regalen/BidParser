using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace BidParser.Infrastructure.Services;

/// <summary>
/// Orchestrates a single parse end to end: resolve + validate the parser and file, persist the upload,
/// run the parser, dispatch to the matching output writer, then record the outcome. Success writes a
/// <see cref="ParseJob"/> + <see cref="ParseMetric"/> (and, on a totals mismatch, a best-effort monitoring
/// entry). A "detect"-stage failure is reclassified as a wrong-file-type selection — nothing is persisted,
/// the upload is deleted, and the likely-correct type is suggested via sibling <c>Detect()</c>; other
/// exceptions are recorded as <see cref="FailedParseJob"/>s. See AGENTS.md "Wrong file-type handling".
/// </summary>
public sealed class ParseService(
    QuoteParseService quoteParseService,
    WorkbookWriteService workbookWriteService,
    FileStorage storage,
    AppDbContext db,
    FailedParseJobRecorder failureRecorder,
    ILogger<ParseService> logger)
{
    /// <summary>
    /// Parses <paramref name="fileStream"/> with the parser identified by <paramref name="parserSlug"/>
    /// and writes the standardised output for <paramref name="crmTemplate"/> (defaults to the parser's).
    /// Returns the output path + validation on success; throws <c>ParseError("fileType")</c> when the
    /// selected parser does not recognise the file (wrong file-type selection).
    /// </summary>
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
        bool includeSubComponentDetail,
        bool splitBySolutionId,
        long maxUploadBytes,
        CancellationToken ct = default)
    {
        QuoteParseSelection selection;
        try
        {
            selection = quoteParseService.ResolveSelection(uploadFilename, vendor, parserSlug);
        }
        catch (ParseInputException inputError)
        {
            throw ToValidationException(inputError);
        }

        var importType = selection.IsAuto ? ImportType.Auto : ImportType.Manual;
        IParser? parser = selection.Parser;

        var displayFilename = Path.GetFileName(uploadFilename);
        var sourcePath = storage.NewOriginalPath(displayFilename);
        var outputPath = storage.NewOutputPath(splitBySolutionId ? ".zip" : ".xlsx");

        await storage.SaveUploadAsync(fileStream, sourcePath, maxUploadBytes, ct);

        // Effective values used by writers and persisted on the ParseJob/ParseMetric ledger.
        // The User's defaults are only updated when the caller explicitly supplied a value
        // (see User mutation block below), so omitting margin from an HP No Calculation parse
        // doesn't clobber a saved value.
        var effectiveFxRate = fxRate ?? 1m;
        var effectiveMargin = margin ?? 0m;

        try
        {
            var parsedQuote = await quoteParseService.ParseAsync(
                new QuoteParseRequest(
                    sourcePath,
                    vendor,
                    parserSlug,
                    new ParseOptions { IncludeSubComponentDetail = includeSubComponentDetail },
                    displayFilename),
                selection,
                ct);
            parser = parsedQuote.Parser;
            var result = parsedQuote.Result;

            var writeResult = await workbookWriteService.WriteAsync(
                new WorkbookWriteRequest(
                    parsedQuote,
                    outputPath,
                    crmTemplate,
                    effectiveFxRate,
                    effectiveMargin,
                    imPercent,
                    onCostPercent,
                    splitBySolutionId),
                ct);
            var template = writeResult.CrmTemplate;
            var outputWorkbookCount = writeResult.WorkbookCount;

            var fxRateRounded = Math.Round(effectiveFxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(effectiveMargin, 2, MidpointRounding.AwayFromZero);

            // Bid metadata is best-effort, so a broken anchor degrades silently by design. Log it:
            // this line, and `bid_number IS NULL` on rows created after deployment, are the only
            // signals that a vendor changed a header and a format needs its anchor revisited.
            if (result.Metadata.BidNumber is null)
            {
                logger.LogWarning(
                    "No bid metadata extracted for {Filename} using {Slug} — the history row will show no bid number",
                    displayFilename, parser.Slug);
            }

            var job = new ParseJob
            {
                UserId = user.Id,
                Vendor = vendor,
                ParserSlug = parser.Slug,
                CrmTemplate = template,
                SourceFilename = displayFilename,
                BidNumber = result.Metadata.BidNumber,
                BidRevision = result.Metadata.BidRevision,
                SourcePath = sourcePath,
                OutputPath = outputPath,
                FxRate = fxRateRounded,
                Margin = marginRounded,
                ComputedTotal = result.Validation.ComputedTotal,
                QuotedTotal = result.Validation.QuotedTotal,
                TotalsMatch = result.Validation.Matches,
                SplitBySolutionId = splitBySolutionId,
                ImportType = importType,
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
                ImportType = importType,
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
                        importType,
                        ct);
                }
                catch (Exception ex)
                {
                    // Monitoring record failed — parse job is already committed, continue.
                    logger.LogError(ex,
                        "Failed to record validationMismatch monitoring entry for parse job {ParseJobId} (user {UserId}, parser {ParserSlug})",
                        job.Id, user.Id, parser.Slug);
                }
            }

            var cancelledLines = result.LineItems
                .Where(i => i.IsCancelled)
                .Select(i => new CancelledLine(i.LineSequence ?? string.Empty, i.Vpn))
                .ToList();

            return new ParseServiceResult(
                job,
                writeResult.OutputFilename,
                outputPath,
                outputWorkbookCount,
                result.Validation,
                result.Metadata.Currency,
                cancelledLines,
                result.HasRebateIneligibleItems);
        }
        catch (ParseInputException inputError) when (
            inputError.Kind is ParseInputErrorKind.AutoDetectionFailed or ParseInputErrorKind.WrongFileType)
        {
            // The selected parser could not recognise the file's layout (missing table
            // anchor or required column) — almost always a wrong file-type selection, not
            // a genuine parser failure. Suggest the correct type by comparing the file
            // against the other formats for the SAME vendor, drop everything (no monitoring
            // entry, no parse job/metric) and delete the stored upload.
            storage.TryDelete(outputPath);
            storage.TryDelete(sourcePath);

            logger.LogInformation(
                "Wrong file type for {Filename}: selected {Slug}, suggested {Suggested}",
                displayFilename, parser?.Slug ?? parserSlug, inputError.SuggestedParserName ?? "(none)");

            throw new ParseError("fileType", inputError.Message, inputError.Message);
        }
        catch (ParseInputException inputError) when (inputError.Kind != ParseInputErrorKind.MagicByteMismatch)
        {
            storage.TryDelete(outputPath);
            storage.TryDelete(sourcePath);
            throw ToValidationException(inputError);
        }
        catch (ParseValidationException)
        {
            // User-input 400 (unknown/unsupported CRM template, missing IM%) — a
            // client error, not a genuine parse failure. Drop both files and don't
            // record a FailedParseJob so it doesn't show up as monitoring noise.
            storage.TryDelete(outputPath);
            storage.TryDelete(sourcePath);
            throw;
        }
        catch (Exception ex)
        {
            storage.TryDelete(outputPath);

            var fxRateRounded = Math.Round(effectiveFxRate, 4, MidpointRounding.AwayFromZero);
            var marginRounded = Math.Round(effectiveMargin, 2, MidpointRounding.AwayFromZero);

            var recordedException = ex is ParseInputException { Kind: ParseInputErrorKind.MagicByteMismatch } inputError
                ? new ParseError("upload", inputError.Message, inputError.Message)
                : ex;

            await failureRecorder.RecordAsync(
                user, vendor, parser?.Slug ?? parserSlug, displayFilename, sourcePath,
                fxRateRounded, marginRounded, recordedException, importType, ct);

            if (!ReferenceEquals(recordedException, ex))
            {
                throw recordedException;
            }

            throw;
        }
    }

    private static ParseValidationException ToValidationException(ParseInputException error)
        => new(
            error.Kind == ParseInputErrorKind.UnsupportedExtension ? 415 : 400,
            error.Message);
}

/// <summary>The outcome of a successful parse handed back to the API: the persisted job, the download filename + path, validation, currency, and parse warnings.</summary>
public sealed record ParseServiceResult(
    ParseJob Job,
    string OutputFilename,
    string OutputPath,
    int OutputWorkbookCount,
    ValidationResult Validation,
    string Currency,
    IReadOnlyList<CancelledLine> CancelledLines,
    bool HasRebateIneligibleItems);

/// A line item flagged as cancelled in the source document.
/// Surfaced to the frontend via the <c>X-Cancelled-Lines</c> response header.
/// </summary>
public sealed record CancelledLine(string Line, string Vpn);

/// <summary>A client-input error (bad parser/vendor/template, missing IM%) carrying an HTTP status; not a recorded parse failure.</summary>
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

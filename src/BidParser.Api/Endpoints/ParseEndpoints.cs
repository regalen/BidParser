using System.Globalization;
using System.Text.Json;
using System.Diagnostics;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using Microsoft.Extensions.Primitives;
using BidParser.Api.Options;
using BidParser.Application.Output;
using BidParser.Domain;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Dell;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using BidParser.Infrastructure.Storage;

namespace BidParser.Api.Endpoints;

/// <summary>
/// POST /api/parse and POST /api/parse/preview (active users; CSRF-guarded, rate-limited):
/// core upload and per-line pricing preview endpoints.
/// </summary>
public static class ParseEndpoints
{
    private static readonly JsonSerializerOptions LinePricingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapParseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/parse", ParseAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>()
            .RequireRateLimiting("parse");

        app.MapPost("/api/parse/preview", PreviewAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>()
            .RequireRateLimiting("parsePreview");

        return app;
    }

    private sealed record CommonParseForm(
        IFormCollection Form,
        IFormFile? File,
        string Vendor,
        string ParserSlug,
        string? CrmTemplate,
        string? QuoteId);

    private static async Task<(CommonParseForm? Parsed, IResult? Error)> ReadCommonFormAsync(
        HttpContext context,
        AppOptions appOptions,
        bool allowQuoteId,
        CancellationToken ct)
    {
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return (null, Results.Json(new ApiError("File is too large."), statusCode: 413));
        }
        catch (InvalidDataException)
        {
            return (null, Results.Json(new ApiError("File is too large."), statusCode: 413));
        }

        var file = form.Files.GetFile("file");
        var vendor = form["vendor"].FirstOrDefault()?.Trim();
        var parserSlug = form["parserSlug"].FirstOrDefault()?.Trim();
        var crmTemplate = form["crmTemplate"].FirstOrDefault()?.Trim();
        var quoteId = form["quoteId"].FirstOrDefault()?.Trim();

        if (!allowQuoteId && !string.IsNullOrEmpty(quoteId))
        {
            return (null, Results.Json(new ApiError("quoteId is not supported for preview."), statusCode: 400));
        }

        if (file is null && string.IsNullOrEmpty(quoteId))
        {
            return (null, Results.Json(new ApiError(allowQuoteId ? "file or quoteId is required." : "file is required."), statusCode: 400));
        }

        if (file is not null && !string.IsNullOrEmpty(quoteId))
        {
            return (null, Results.Json(new ApiError("Provide either file or quoteId, not both."), statusCode: 400));
        }

        if (string.IsNullOrEmpty(vendor))
        {
            return (null, Results.Json(new ApiError("vendor is required."), statusCode: 400));
        }

        if (string.IsNullOrEmpty(parserSlug))
        {
            return (null, Results.Json(new ApiError("parserSlug is required."), statusCode: 400));
        }

        if (!string.IsNullOrEmpty(quoteId) && vendor != Vendors.Dell)
        {
            return (null, Results.Json(new ApiError("quoteId is only supported for Dell."), statusCode: 400));
        }

        // Dell has no manual file-type selection: CTO vs APOS is disambiguated by dell_auto's
        // Detect() scoring, never by the caller. Rejected here (not just hidden in the SPA) so
        // a direct API call can't bypass the UI-level restriction.
        if (vendor == Vendors.Dell && parserSlug != ParserSlugs.DellAuto)
        {
            return (null, Results.Json(new ApiError("Dell only supports automatic file-type detection."), statusCode: 400));
        }

        if (file is not null && file.Length > appOptions.MaxUploadBytes)
        {
            return (null, Results.Json(new ApiError("File is too large."), statusCode: 413));
        }

        return (new CommonParseForm(form, file, vendor, parserSlug, crmTemplate, quoteId), null);
    }

    private static async Task<IResult> PreviewAsync(
        HttpContext context,
        AppDbContext db,
        ParseService parseService,
        AppOptions appOptions,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var (common, error) = await ReadCommonFormAsync(context, appOptions, allowQuoteId: false, ct);
        if (error is not null)
        {
            return error;
        }

        var (form, file, vendor, parserSlug, crmTemplate, _) = common!;

        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("notAuthenticated"), statusCode: 401);
        }

        var displayFilename = Path.GetFileName(file!.FileName ?? "quote");
        ParseServicePreviewResult previewResult;
        try
        {
            await using var stream = file.OpenReadStream();
            previewResult = await parseService.PreviewAsync(
                user, stream, displayFilename, vendor, parserSlug, crmTemplate, appOptions.MaxUploadBytes, ct);
        }
        catch (ParseValidationException ex)
        {
            return Results.Json(new ApiError(ex.Detail), statusCode: ex.StatusCode);
        }
        catch (UploadTooLargeException)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }
        catch (ParseError ex)
        {
            logger.LogWarning(
                ex,
                "Preview parse error for {Filename} using {Slug} at stage {Stage}",
                displayFilename,
                parserSlug,
                ex.Stage);

            return Results.Json(
                new ParseErrorResponse(new ParseErrorDetail(ex.Stage, ex.Hint, ex.Message)),
                statusCode: 422);
        }

        var preview = previewResult.Preview;
        var validation = previewResult.Validation;

        var response = new ParsePreviewResponse(
            ParserSlug: preview.ParserSlug,
            ParserDisplayName: preview.ParserDisplayName,
            Currency: preview.Currency,
            CrmTemplate: preview.CrmTemplate,
            UsesFxRate: preview.UsesFxRate,
            ImPercentEditable: preview.ImPercentEditable,
            ImPercentRequired: preview.ImPercentRequired,
            Validation: new ParsePreviewValidationResponse(
                Matches: validation.Matches,
                ComputedTotal: validation.ComputedTotal,
                QuotedTotal: validation.QuotedTotal,
                Warnings: validation.Warnings),
            Lines: preview.Lines.Select(line => new ParsePreviewLineResponse(
                Index: line.Index,
                LineNumber: line.LineNumber,
                Vpn: line.Vpn,
                Description: line.Description,
                Qty: line.Qty,
                Msrp: line.Msrp,
                SourceCost: line.SourceCost,
                DerivedImPercent: line.DerivedImPercent,
                IsCancelled: line.IsCancelled)).ToList());

        return Results.Ok(response);
    }

    private static async Task<IResult> ParseAsync(
        HttpContext context,
        AppDbContext db,
        ParseService parseService,
        DellApiSettingsService dellSettings,
        IDellQuoteClient dellQuoteClient,
        AppOptions appOptions,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var (common, error) = await ReadCommonFormAsync(context, appOptions, allowQuoteId: true, ct);
        if (error is not null)
        {
            return error;
        }

        var (form, file, vendor, parserSlug, crmTemplate, quoteId) = common!;

        var fxRateStr = form["fxRate"].FirstOrDefault()?.Trim();
        var marginStr = form["margin"].FirstOrDefault()?.Trim();
        var imPercentStr = form["imPercent"].FirstOrDefault()?.Trim();
        var onCostPctStr = form["onCostPct"].FirstOrDefault()?.Trim();
        var linePricingStr = form["linePricing"].FirstOrDefault()?.Trim();
        var splitBySolutionId = form["splitBySolutionId"].FirstOrDefault()?.Trim() is "true" or "1";

        IReadOnlyList<LinePricingEntry>? linePricing = null;
        if (!string.IsNullOrEmpty(linePricingStr))
        {
            List<LinePricingRequestItem>? parsedEntries;
            try
            {
                parsedEntries = JsonSerializer.Deserialize<List<LinePricingRequestItem>>(
                    linePricingStr,
                    LinePricingJsonOptions);
            }
            catch (JsonException)
            {
                return Results.Json(new ApiError("Invalid linePricing."), statusCode: 400);
            }

            if (parsedEntries is null || parsedEntries.Any(entry => entry is null || entry.Vpn is null))
            {
                return Results.Json(new ApiError("Invalid linePricing."), statusCode: 400);
            }

            if (!string.IsNullOrEmpty(marginStr) || !string.IsNullOrEmpty(imPercentStr))
            {
                return Results.Json(
                    new ApiError("Provide per-line pricing or header margin/imPercent, not both."),
                    statusCode: 400);
            }

            linePricing = parsedEntries
                .Select(entry => new LinePricingEntry(entry.Index, entry.Vpn, entry.Margin, entry.ImPercent))
                .ToList();
        }

        // fxRate, margin, and imPercent are all optional at the wire level. They are
        // passed as nullable decimals to ParseService so that omitting a value doesn't
        // clobber the user's saved default. The writer-side defaults (fxRate=1, margin=0)
        // are applied inside ParseService when null.
        // NumberStyles.Number (not .Any) rejects currency symbols, parenthesised
        // negatives, and exponents; a further < 0 check keeps these consistent with
        // /me/settings, which enforces non-negative. A negative margin would flow
        // straight into the output workbook and the metrics ledger otherwise.
        decimal? fxRate = null;
        if (!string.IsNullOrEmpty(fxRateStr))
        {
            if (!decimal.TryParse(fxRateStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFxRate)
                || parsedFxRate < 0)
            {
                return Results.Json(new ApiError("Invalid fxRate."), statusCode: 400);
            }
            fxRate = parsedFxRate;
        }

        if (linePricing is not null)
        {
            if (fxRate is null)
            {
                return Results.Json(new ApiError("fxRate is required with per-line pricing."), statusCode: 400);
            }

            if (fxRate.Value <= 0m || fxRate.Value != decimal.Round(fxRate.Value, 4))
            {
                return Results.Json(new ApiError("Invalid fxRate."), statusCode: 400);
            }
        }

        decimal? margin = null;
        if (!string.IsNullOrEmpty(marginStr))
        {
            if (!decimal.TryParse(marginStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedMargin)
                || parsedMargin < 0)
            {
                return Results.Json(new ApiError("Invalid margin."), statusCode: 400);
            }
            margin = parsedMargin;
        }

        decimal? imPercent = null;
        if (!string.IsNullOrEmpty(imPercentStr))
        {
            if (!decimal.TryParse(imPercentStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedImPercent)
                || parsedImPercent < 0)
            {
                return Results.Json(new ApiError("Invalid imPercent."), statusCode: 400);
            }
            imPercent = parsedImPercent;
        }

        decimal? onCostPct = null;
        if (!string.IsNullOrEmpty(onCostPctStr))
        {
            if (!decimal.TryParse(onCostPctStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedOnCostPct)
                || parsedOnCostPct < 0)
            {
                return Results.Json(new ApiError("Invalid onCostPct."), statusCode: 400);
            }
            onCostPct = parsedOnCostPct;
        }

        // Dell CTO SKU filtering is unconditional: false == suppress child SKUs under Displays
        // items and child SKUs that restate their parent. The opt-out that briefly exposed the
        // full tree has been withdrawn, so the wire can no longer switch the filtering off.
        // Only DellCtoJsonParser honours this; every other parser ignores it, and DellAposJsonParser
        // has nothing to switch — APOS emits every source SKU.
        // Pinned here at the HTTP boundary rather than in ParseService or the parser so the
        // policy sits in one obvious place and re-exposing it stays a one-line change.
        const bool includeSubComponents = false;

        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("notAuthenticated"), statusCode: 401);
        }

        ParseServiceResult result;
        var stopwatch = Stopwatch.StartNew();
        var displayFilename = file is null
            ? $"{quoteId}.json"
            : Path.GetFileName(file.FileName ?? "quote");
        try
        {
            Stream stream;
            if (!string.IsNullOrEmpty(quoteId))
            {
                if (!DellQuoteId.TryParse(quoteId, out var quoteNumber, out var quoteVersion))
                {
                    return Results.Json(new ApiError("Invalid Dell quote ID."), statusCode: 400);
                }

                var config = await dellSettings.GetAsync(ct)
                    ?? throw new DellApiException(DellApiFailure.NotConfigured, DellApiMessages.NotConfigured);
                var fetched = await dellQuoteClient.GetQuoteAsync(
                    quoteNumber,
                    quoteVersion,
                    config.DefaultLocale,
                    ct);
                stream = new MemoryStream(fetched.RawJson, writable: false);
            }
            else
            {
                stream = file!.OpenReadStream();
            }

            await using var ownedStream = stream;
            result = await parseService.ParseAsync(
                user, stream, displayFilename, vendor, parserSlug,
                fxRate, margin, imPercent, onCostPct, crmTemplate, includeSubComponents,
                splitBySolutionId,
                appOptions.MaxUploadBytes, ct, linePricing);
        }
        catch (DellApiException ex)
        {
            logger.LogWarning(ex,
                "Dell quote fetch failed for {QuoteId} kind={Kind} status={Status}",
                quoteId,
                ex.Kind,
                ex.HttpStatus);

            return Results.Json(
                new ParseErrorResponse(new ParseErrorDetail("dellApi", ex.UserMessage, ex.UserMessage)),
                statusCode: 422);
        }
        catch (ParseValidationException ex)
        {
            return Results.Json(new ApiError(ex.Detail), statusCode: ex.StatusCode);
        }
        catch (UploadTooLargeException)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }
        catch (ParseError ex)
        {
            logger.LogWarning(
                ex,
                "Parse error for {Filename} using {Slug} at stage {Stage}",
                displayFilename,
                parserSlug,
                ex.Stage);

            return Results.Json(
                new ParseErrorResponse(new ParseErrorDetail(ex.Stage, ex.Hint, ex.Message)),
                statusCode: 422);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Parse {Slug} ok user={UserId} computed={Computed:F2} quoted={Quoted:F2} match={Match} ms={Ms}",
            result.Job.ParserSlug,
            user.Id,
            result.Validation.ComputedTotal,
            result.Validation.QuotedTotal.GetValueOrDefault(),
            result.Validation.Matches,
            stopwatch.ElapsedMilliseconds);

        context.Response.Headers["X-Parser-Slug"] = result.Job.ParserSlug;
        context.Response.Headers["X-Validation"] = result.Validation.Matches ? "match" : "mismatch";
        if (result.Validation.Warnings.Count > 0)
        {
            context.Response.Headers["X-Validation-Warnings"] = ValidationWarningsHeader(result.Validation.Warnings);
        }
        context.Response.Headers["X-Currency"] = result.Currency;
        if (result.Job.SplitBySolutionId)
        {
            context.Response.Headers["X-Split-Count"] = result.OutputWorkbookCount.ToString(CultureInfo.InvariantCulture);
        }

        // Emit cancelled-line details so the frontend can surface a warning modal.
        // Format: "line:VPN;line:VPN;..." (VPNs are ASCII and never contain ':' or ';').
        if (result.CancelledLines.Count > 0)
        {
            context.Response.Headers["X-Cancelled-Lines"] =
                string.Join(';', result.CancelledLines.Select(cl => $"{cl.Line}:{cl.Vpn}"));
        }

        if (result.HasRebateIneligibleItems)
        {
            context.Response.Headers["X-Rebate-Ineligible"] = "true";
        }
        context.Response.Headers["X-Computed-Total"] =
            result.Validation.ComputedTotal.ToString("F2", CultureInfo.InvariantCulture);

        if (result.Validation.QuotedTotal.HasValue)
        {
            context.Response.Headers["X-Quoted-Total"] =
                result.Validation.QuotedTotal.Value.ToString("F2", CultureInfo.InvariantCulture);
        }
        else
        {
            // ASP.NET Core strips empty-string StringValues; use the array constructor
            // to preserve the header as present-with-empty-value per the API contract.
            context.Response.Headers["X-Quoted-Total"] = new StringValues(new string[] { "" });
        }

        return Results.File(
            new FileStream(result.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            result.Job.SplitBySolutionId
                ? "application/zip"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: result.OutputFilename);
    }

    private static string ValidationWarningsHeader(IReadOnlyList<string> warnings)
    {
        // Default JSON escaping keeps this ASCII-safe, including source SKUs with Unicode or newlines.
        // Bound the header so a quote with many mismatches can still be downloaded through proxies.
        var included = new List<string>();
        foreach (var warning in warnings)
        {
            included.Add(warning);
            if (JsonSerializer.Serialize(included).Length <= 3800) continue;

            included.RemoveAt(included.Count - 1);
            included.Add($"{warnings.Count - included.Count} additional validation warnings omitted. Check the remaining source lines.");
            break;
        }

        return JsonSerializer.Serialize(included);
    }
}

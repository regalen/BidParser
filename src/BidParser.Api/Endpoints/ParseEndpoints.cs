using System.Globalization;
using System.Diagnostics;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using Microsoft.Extensions.Primitives;
using BidParser.Api.Options;
using BidParser.Domain;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Dell;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using BidParser.Infrastructure.Storage;

namespace BidParser.Api.Endpoints;

/// <summary>
/// POST /api/parse (active users; CSRF-guarded, rate-limited "parse"): the core upload endpoint. Reads the
/// multipart form (file + vendor/parser/template + numeric inputs), delegates to <see cref="ParseService"/>,
/// and streams back the generated workbook or Solution-ID ZIP — surfacing cancelled lines via the
/// X-Cancelled-Lines header and parser errors as typed 4xx bodies.
/// </summary>
public static class ParseEndpoints
{
    public static IEndpointRouteBuilder MapParseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/parse", ParseAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser)
            .AddEndpointFilter<RequireCsrfHeader>()
            .RequireRateLimiting("parse");

        return app;
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
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }
        catch (InvalidDataException)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }

        var file = form.Files.GetFile("file");
        var vendor = form["vendor"].FirstOrDefault()?.Trim();
        var parserSlug = form["parserSlug"].FirstOrDefault()?.Trim();
        var fxRateStr = form["fxRate"].FirstOrDefault()?.Trim();
        var marginStr = form["margin"].FirstOrDefault()?.Trim();
        var imPercentStr = form["imPercent"].FirstOrDefault()?.Trim();
        var onCostPctStr = form["onCostPct"].FirstOrDefault()?.Trim();
        var crmTemplate = form["crmTemplate"].FirstOrDefault()?.Trim();
        var quoteId = form["quoteId"].FirstOrDefault()?.Trim();
        var splitBySolutionId = form["splitBySolutionId"].FirstOrDefault()?.Trim() is "true" or "1";

        if (file is null && string.IsNullOrEmpty(quoteId))
        {
            return Results.Json(new ApiError("file or quoteId is required."), statusCode: 400);
        }

        if (file is not null && !string.IsNullOrEmpty(quoteId))
        {
            return Results.Json(new ApiError("Provide either file or quoteId, not both."), statusCode: 400);
        }

        if (string.IsNullOrEmpty(vendor))
        {
            return Results.Json(new ApiError("vendor is required."), statusCode: 400);
        }

        if (string.IsNullOrEmpty(parserSlug))
        {
            return Results.Json(new ApiError("parserSlug is required."), statusCode: 400);
        }

        if (!string.IsNullOrEmpty(quoteId) && vendor != Vendors.Dell)
        {
            return Results.Json(new ApiError("quoteId is only supported for Dell."), statusCode: 400);
        }

        // Dell has no manual file-type selection: CTO vs APOS is disambiguated by dell_auto's
        // Detect() scoring, never by the caller. Rejected here (not just hidden in the SPA) so
        // a direct API call can't bypass the UI-level restriction.
        if (vendor == Vendors.Dell && parserSlug != ParserSlugs.DellAuto)
        {
            return Results.Json(new ApiError("Dell only supports automatic file-type detection."), statusCode: 400);
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

        if (file is not null && file.Length > appOptions.MaxUploadBytes)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }

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
                appOptions.MaxUploadBytes, ct);
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
}

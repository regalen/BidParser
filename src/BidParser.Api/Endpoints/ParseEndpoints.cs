using System.Globalization;
using System.Diagnostics;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using Microsoft.Extensions.Primitives;
using BidParser.Api.Options;
using BidParser.Domain.Models;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using BidParser.Infrastructure.Storage;

namespace BidParser.Api.Endpoints;

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
        var parserSlug = form["parser_slug"].FirstOrDefault()?.Trim();
        var fxRateStr = form["fx_rate"].FirstOrDefault()?.Trim();
        var marginStr = form["margin"].FirstOrDefault()?.Trim();
        var imPercentStr = form["im_percent"].FirstOrDefault()?.Trim();
        var crmTemplate = form["crm_template"].FirstOrDefault()?.Trim();

        if (file is null)
        {
            return Results.Json(new ApiError("file is required."), statusCode: 400);
        }

        if (string.IsNullOrEmpty(vendor))
        {
            return Results.Json(new ApiError("vendor is required."), statusCode: 400);
        }

        if (string.IsNullOrEmpty(parserSlug))
        {
            return Results.Json(new ApiError("parser_slug is required."), statusCode: 400);
        }

        // fx_rate, margin, and im_percent are all optional at the wire level. They are
        // passed as nullable decimals to ParseService so that omitting a value doesn't
        // clobber the user's saved default. The writer-side defaults (fxRate=1, margin=0)
        // are applied inside ParseService when null.
        decimal? fxRate = null;
        if (!string.IsNullOrEmpty(fxRateStr))
        {
            if (!decimal.TryParse(fxRateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedFxRate))
            {
                return Results.Json(new ApiError("Invalid fx_rate."), statusCode: 400);
            }
            fxRate = parsedFxRate;
        }

        decimal? margin = null;
        if (!string.IsNullOrEmpty(marginStr))
        {
            if (!decimal.TryParse(marginStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMargin))
            {
                return Results.Json(new ApiError("Invalid margin."), statusCode: 400);
            }
            margin = parsedMargin;
        }

        decimal? imPercent = null;
        if (!string.IsNullOrEmpty(imPercentStr))
        {
            if (!decimal.TryParse(imPercentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedImPercent))
            {
                return Results.Json(new ApiError("Invalid im_percent."), statusCode: 400);
            }
            imPercent = parsedImPercent;
        }

        if (file.Length > appOptions.MaxUploadBytes)
        {
            return Results.Json(new ApiError("File is too large."), statusCode: 413);
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db, ct);
        if (user is null)
        {
            return Results.Json(new ApiError("not_authenticated"), statusCode: 401);
        }

        var displayFilename = Path.GetFileName(file.FileName ?? "quote");
        await using var stream = file.OpenReadStream();

        ParseServiceResult result;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await parseService.ParseAsync(
                user, stream, displayFilename, vendor, parserSlug,
                fxRate, margin, imPercent, crmTemplate, appOptions.MaxUploadBytes, ct);
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
            parserSlug,
            user.Id,
            result.Validation.ComputedTotal,
            result.Validation.QuotedTotal.GetValueOrDefault(),
            result.Validation.Matches,
            stopwatch.ElapsedMilliseconds);

        context.Response.Headers["X-Validation"] = result.Validation.Matches ? "match" : "mismatch";
        context.Response.Headers["X-Currency"] = result.Currency;
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

        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{result.OutputFilename}\"";
        return Results.File(
            new FileStream(result.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}

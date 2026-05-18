using System.Globalization;
using BidParser.Api.Auth;
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
            .AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    private static async Task<IResult> ParseAsync(
        HttpContext context,
        AppDbContext db,
        ParseService parseService,
        AppOptions appOptions,
        CancellationToken ct)
    {
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return Results.Json(new { detail = "File is too large." }, statusCode: 413);
        }
        catch (InvalidDataException)
        {
            return Results.Json(new { detail = "File is too large." }, statusCode: 413);
        }

        var file = form.Files.GetFile("file");
        var vendor = form["vendor"].FirstOrDefault()?.Trim();
        var parserSlug = form["parser_slug"].FirstOrDefault()?.Trim();
        var fxRateStr = form["fx_rate"].FirstOrDefault()?.Trim();
        var marginStr = form["margin"].FirstOrDefault()?.Trim();

        if (file is null)
        {
            return Results.Json(new { detail = "file is required." }, statusCode: 400);
        }

        if (string.IsNullOrEmpty(vendor))
        {
            return Results.Json(new { detail = "vendor is required." }, statusCode: 400);
        }

        if (string.IsNullOrEmpty(parserSlug))
        {
            return Results.Json(new { detail = "parser_slug is required." }, statusCode: 400);
        }

        if (!decimal.TryParse(fxRateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var fxRate))
        {
            return Results.Json(new { detail = "Invalid fx_rate." }, statusCode: 400);
        }

        if (!decimal.TryParse(marginStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var margin))
        {
            return Results.Json(new { detail = "Invalid margin." }, statusCode: 400);
        }

        if (file.Length > appOptions.MaxUploadBytes)
        {
            return Results.Json(new { detail = "File is too large." }, statusCode: 413);
        }

        var user = await EndpointHelpers.CurrentUserAsync(context, db);
        if (user is null)
        {
            return Results.Json(new { detail = "not_authenticated" }, statusCode: 401);
        }

        var displayFilename = Path.GetFileName(file.FileName ?? "quote");
        await using var stream = file.OpenReadStream();

        ParseServiceResult result;
        try
        {
            result = await parseService.ParseAsync(
                user, stream, displayFilename, vendor, parserSlug,
                fxRate, margin, appOptions.MaxUploadBytes, ct);
        }
        catch (ParseValidationException ex)
        {
            return Results.Json(new { detail = ex.Detail }, statusCode: ex.StatusCode);
        }
        catch (UploadTooLargeException)
        {
            return Results.Json(new { detail = "File is too large." }, statusCode: 413);
        }
        catch (ParseError ex)
        {
            return Results.Json(
                new { detail = new { stage = ex.Stage, hint = ex.Hint, message = ex.Message } },
                statusCode: 422);
        }
        catch (Exception ex)
        {
            return Results.Json(
                new { detail = new { stage = "parse", hint = "Could not parse this file.", message = ex.Message } },
                statusCode: 422);
        }

        context.Response.Headers["X-Validation"] = result.Validation.Matches ? "match" : "mismatch";
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

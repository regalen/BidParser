using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class ReportTypesEndpoints
{
    private const int MaxReportTypeLength = 512;

    public static IEndpointRouteBuilder MapReportTypesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/report-types").RequireAuthorization(AuthPolicies.Admin);

        group.MapPut("/{slug}", UpsertReportTypeAsync).AddEndpointFilter<RequireCsrfHeader>();

        return app;
    }

    // Upsert the report-type string for a parser slug. An empty/whitespace value
    // clears the mapping (deletes the row). The list of combinations is read from
    // /api/parsers, which already includes the configured report type per parser.
    private static async Task<IResult> UpsertReportTypeAsync(
        string slug,
        HttpRequest request,
        IParserRegistry registry,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<ReportTypeUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null)
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }

        if (!registry.Parsers.Any(p => p.Slug == slug))
        {
            return Results.Json(new ApiError("Unknown parser."), statusCode: StatusCodes.Status404NotFound);
        }

        var reportType = body.Value.ReportType?.Trim() ?? string.Empty;
        if (reportType.Length > MaxReportTypeLength)
        {
            return EndpointHelpers.ValidationProblem("Report type is too long.");
        }

        var existing = await db.ReportTypeConfigs.SingleOrDefaultAsync(c => c.ParserSlug == slug, ct);

        if (reportType.Length == 0)
        {
            if (existing is not null)
            {
                db.ReportTypeConfigs.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            logger.LogInformation("Admin cleared report type for {ParserSlug}", slug);
            return Results.Ok(new OkResponse());
        }

        if (existing is null)
        {
            db.ReportTypeConfigs.Add(new ReportTypeConfig { ParserSlug = slug, ReportType = reportType });
        }
        else
        {
            existing.ReportType = reportType;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Admin set report type for {ParserSlug}", slug);
        return Results.Ok(new OkResponse());
    }
}

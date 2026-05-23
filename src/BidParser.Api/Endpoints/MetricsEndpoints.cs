using System.Globalization;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/export", ExportAsync);
    }

    private static async Task<IResult> SummaryAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? vendor,
        [FromQuery] int? userId,
        [FromQuery] string? parserSlug,
        AppDbContext db,
        IParserRegistry parserRegistry,
        CancellationToken cancellationToken)
    {
        var toDate = string.IsNullOrEmpty(to) ? DateTime.Today : DateTime.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fromDate = string.IsNullOrEmpty(from) ? toDate.AddDays(-30) : DateTime.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Convert to UTC range
        var fromUtc = fromDate.ToUniversalTime();
        var toUtc = toDate.AddDays(1).ToUniversalTime(); // Inclusive up to end of day

        var query = db.ParseMetrics.Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtc);

        if (!string.IsNullOrEmpty(vendor)) query = query.Where(m => m.Vendor == vendor);
        if (userId.HasValue) query = query.Where(m => m.UserId == userId.Value);
        if (!string.IsNullOrEmpty(parserSlug)) query = query.Where(m => m.ParserSlug == parserSlug);

        // 1. KPIs
        var totalParses = await query.CountAsync(cancellationToken);
        var activeUsers = await query.Select(m => m.UserUsername).Distinct().CountAsync(cancellationToken);
        var activeVendors = await query.Select(m => m.Vendor).Distinct().CountAsync(cancellationToken);
        var mismatchCount = await query.CountAsync(m => !m.TotalsMatch, cancellationToken);
        var mismatchRate = totalParses == 0 ? "0" : ((decimal)mismatchCount / totalParses).ToString("F4", CultureInfo.InvariantCulture);

        var kpis = new MetricsKpis(totalParses, activeUsers, activeVendors, mismatchRate);

        // 2. By User
        var byUserDb = await query
            .GroupBy(m => new { m.UserId, m.UserUsername, m.UserName })
            .Select(g => new { g.Key.UserId, g.Key.UserUsername, g.Key.UserName, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        var byUser = byUserDb.Select(x => new MetricsByUser(x.UserId, x.UserUsername, x.UserName, x.Count)).ToList();

        // 3. By Vendor
        var byVendorDb = await query
            .GroupBy(m => m.Vendor)
            .Select(g => new { Vendor = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        var byVendor = byVendorDb.Select(x => new MetricsByVendor(x.Vendor, x.Count)).ToList();

        // 4. By Parser
        var byParserDb = await query
            .GroupBy(m => m.ParserSlug)
            .Select(g => new { Slug = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byParser = byParserDb
            .Select(x => new MetricsByParser(
                x.Slug,
                parserRegistry.Parsers.FirstOrDefault(p => p.Slug == x.Slug)?.DisplayName ?? x.Slug,
                x.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        var timeSeriesQuery = db.Database.SqlQuery<TimeSeriesRow>(
            $@"
            SELECT date(created_at, 'localtime') as Date, COUNT(*) as Count
            FROM parse_metrics
            WHERE created_at >= {fromUtc} AND created_at < {toUtc}
              AND ({vendor} IS NULL OR vendor = {vendor})
              AND ({userId} IS NULL OR user_id = {userId})
              AND ({parserSlug} IS NULL OR parser_slug = {parserSlug})
            GROUP BY date(created_at, 'localtime')
            ORDER BY date(created_at, 'localtime') ASC
            ");

        var timeSeriesDb = await timeSeriesQuery.ToListAsync(cancellationToken);
        var timeSeries = timeSeriesDb.Select(ts => new MetricsTimeSeries(ts.Date, ts.Count)).ToList();

        return Results.Ok(new MetricsSummaryResponse(
            new MetricsDateRange(fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd")),
            kpis,
            byUser,
            byVendor,
            byParser,
            timeSeries
        ));
    }

    private static async Task<IResult> ExportAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? vendor,
        [FromQuery] int? userId,
        [FromQuery] string? parserSlug,
        AppDbContext db,
        IParserRegistry parserRegistry,
        CancellationToken cancellationToken)
    {
        var toDate = string.IsNullOrEmpty(to) ? DateTime.Today : DateTime.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fromDate = string.IsNullOrEmpty(from) ? toDate.AddDays(-30) : DateTime.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var fromUtc = fromDate.ToUniversalTime();
        var toUtc = toDate.AddDays(1).ToUniversalTime();

        var query = db.ParseMetrics.Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtc);

        if (!string.IsNullOrEmpty(vendor)) query = query.Where(m => m.Vendor == vendor);
        if (userId.HasValue) query = query.Where(m => m.UserId == userId.Value);
        if (!string.IsNullOrEmpty(parserSlug)) query = query.Where(m => m.ParserSlug == parserSlug);

        var rows = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(cancellationToken);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Utilisation");

        // Header
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "User";
        ws.Cell(1, 3).Value = "Username";
        ws.Cell(1, 4).Value = "Vendor";
        ws.Cell(1, 5).Value = "Parser";
        ws.Cell(1, 6).Value = "Source Filename";
        ws.Cell(1, 7).Value = "Currency";
        ws.Cell(1, 8).Value = "Quoted Total";
        ws.Cell(1, 9).Value = "Computed Total";
        ws.Cell(1, 10).Value = "Totals Match";
        ws.Cell(1, 11).Value = "FX Rate";
        ws.Cell(1, 12).Value = "Margin";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            var localDate = row.CreatedAt.ToLocalTime();
            ws.Cell(rowIndex, 1).Value = localDate;
            ws.Cell(rowIndex, 2).Value = row.UserName ?? "";
            ws.Cell(rowIndex, 3).Value = row.UserUsername;
            ws.Cell(rowIndex, 4).Value = row.Vendor;
            
            var parserDisplay = parserRegistry.Parsers.FirstOrDefault(p => p.Slug == row.ParserSlug)?.DisplayName ?? row.ParserSlug;
            ws.Cell(rowIndex, 5).Value = parserDisplay;
            
            ws.Cell(rowIndex, 6).Value = row.SourceFilename;
            ws.Cell(rowIndex, 7).Value = row.Currency;
            
            if (row.QuotedTotal.HasValue) ws.Cell(rowIndex, 8).Value = row.QuotedTotal.Value;
            ws.Cell(rowIndex, 9).Value = row.ComputedTotal;
            ws.Cell(rowIndex, 10).Value = row.TotalsMatch;
            ws.Cell(rowIndex, 11).Value = row.FxRate;
            ws.Cell(rowIndex, 12).Value = row.Margin;

            rowIndex++;
        }

        // Format
        ws.Column(1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
        ws.Column(8).Style.NumberFormat.Format = "#,##0.00";
        ws.Column(9).Style.NumberFormat.Format = "#,##0.00";
        ws.Column(11).Style.NumberFormat.Format = "0.0000";
        ws.Column(12).Style.NumberFormat.Format = "0.00";
        ws.Columns().AdjustToContents();

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var fromStr = fromDate.ToString("yyyy-MM-dd");
        var toStr = toDate.ToString("yyyy-MM-dd");

        return Results.File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"utilisation_{fromStr}_{toStr}.xlsx");
    }

    public class TimeSeriesRow
    {
        public required string Date { get; set; }
        public int Count { get; set; }
    }
}

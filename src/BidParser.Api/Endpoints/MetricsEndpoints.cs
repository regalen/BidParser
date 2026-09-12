using System.Globalization;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Endpoints;

/// <summary>
/// Admin analytics under /api/metrics (Admin policy), computed from the ParseMetric ledger: GET /summary
/// (KPIs + breakdowns by user/vendor/parser + a time series over a date range) and GET /export (XLSX).
/// Date filters are yyyy-MM-dd; the range defaults to the last 30 days.
/// </summary>
public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/export", ExportAsync);
    }

    private static async Task<IResult> SummaryAsync(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? vendor,
        [FromQuery] int? userId,
        [FromQuery] string? parserSlug,
        [FromQuery] string? importType,
        AppDbContext db,
        IParserRegistry parserRegistry,
        CancellationToken cancellationToken)
    {
        var errorResult = BidParser.Api.Common.DateRangeParser.Parse(range, from, to, out var parsedRange);
        if (errorResult != null) return errorResult;

        var query = db.ParseMetrics.AsQueryable();

        if (!parsedRange.IsAll)
        {
            query = query.Where(m => m.CreatedAt >= parsedRange.FromUtc && m.CreatedAt < parsedRange.ToUtc);
        }

        if (!string.IsNullOrEmpty(vendor)) query = query.Where(m => m.Vendor == vendor);
        if (userId.HasValue) query = query.Where(m => m.UserId == userId.Value);
        if (!string.IsNullOrEmpty(parserSlug)) query = query.Where(m => m.ParserSlug == parserSlug);
        if (importType == "auto") query = query.Where(m => m.ImportType == ImportType.Auto);
        else if (importType == "manual") query = query.Where(m => m.ImportType == ImportType.Manual);

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

        // 5. By Import Type
        var byImportTypeDb = await query
            .GroupBy(m => m.ImportType)
            .Select(g => new { ImportType = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byImportType = byImportTypeDb
            .Select(x => new MetricsByImportType(
                x.ImportType.HasValue ? x.ImportType.Value.ToString().ToLowerInvariant() : null,
                x.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        var localTzId = TimeZoneInfo.Local.Id;
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(localTzId, out var sqlTz)) {
            sqlTz = localTzId;
        }

        var timeSeries = new List<MetricsTimeSeries>();

        if (parsedRange.IsAll)
        {
            var timeSeriesDb = await query
                .GroupBy(m => new {
                    Year = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(m.CreatedAt, "UTC"), sqlTz).Year,
                    Month = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(m.CreatedAt, "UTC"), sqlTz).Month
                })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(cancellationToken);

            timeSeries = timeSeriesDb
                .Select(x => new MetricsTimeSeries(new DateTime(x.Year, x.Month, 1).ToString("yyyy-MM-dd"), x.Count))
                .OrderBy(ts => ts.Date)
                .ToList();
        }
        else
        {
            var timeSeriesDb = await query
                .GroupBy(m => new {
                    Year = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(m.CreatedAt, "UTC"), sqlTz).Year,
                    Month = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(m.CreatedAt, "UTC"), sqlTz).Month,
                    Day = EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(m.CreatedAt, "UTC"), sqlTz).Day
                })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
                .ToListAsync(cancellationToken);

            timeSeries = timeSeriesDb
                .Select(x => new MetricsTimeSeries(new DateTime(x.Year, x.Month, x.Day).ToString("yyyy-MM-dd"), x.Count))
                .OrderBy(ts => ts.Date)
                .ToList();
        }

        var mode = parsedRange.IsAll ? "all" : "bounded";
        var granularity = parsedRange.IsAll ? "month" : "day";

        return Results.Ok(new MetricsSummaryResponse(
            new MetricsDateRange(mode, parsedRange.FromStr, parsedRange.ToStr),
            granularity,
            kpis,
            byUser,
            byVendor,
            byParser,
            byImportType,
            timeSeries
        ));
    }

    private static async Task<IResult> ExportAsync(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? vendor,
        [FromQuery] int? userId,
        [FromQuery] string? parserSlug,
        [FromQuery] string? importType,
        IExportEnvironment exportEnvironment,
        IParserRegistry parserRegistry,
        CancellationToken cancellationToken)
    {
        var errorResult = BidParser.Api.Common.DateRangeParser.Parse(range, from, to, out var parsedRange);
        if (errorResult != null) return errorResult;

        await using var exportDb = exportEnvironment.CreateExportContext();
        var query = exportDb.ParseMetrics.AsNoTracking().AsQueryable();

        if (!parsedRange.IsAll)
        {
            query = query.Where(m => m.CreatedAt >= parsedRange.FromUtc && m.CreatedAt < parsedRange.ToUtc);
        }

        if (!string.IsNullOrEmpty(vendor)) query = query.Where(m => m.Vendor == vendor);
        if (userId.HasValue) query = query.Where(m => m.UserId == userId.Value);
        if (!string.IsNullOrEmpty(parserSlug)) query = query.Where(m => m.ParserSlug == parserSlug);
        if (importType == "auto") query = query.Where(m => m.ImportType == ImportType.Auto);
        else if (importType == "manual") query = query.Where(m => m.ImportType == ImportType.Manual);

        var parserNames = parserRegistry.Parsers.ToDictionary(parser => parser.Slug, parser => parser.DisplayName, StringComparer.Ordinal);
        var stream = exportEnvironment.CreateTempStream();

        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                WriteMetricsStyles(workbookPart);

                var sheetRelationshipIds = new List<string>();
                OpenXmlWriter? worksheetWriter = null;
                var sheetNumber = 0;
                var rowCount = 0;

                void StartSheet()
                {
                    worksheetWriter?.WriteEndElement();
                    worksheetWriter?.WriteEndElement();
                    worksheetWriter?.Dispose();

                    sheetNumber++;
                    var relationshipId = $"rIdMetrics{sheetNumber}";
                    sheetRelationshipIds.Add(relationshipId);
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>(relationshipId);
                    worksheetWriter = OpenXmlWriter.Create(worksheetPart);
                    worksheetWriter.WriteStartElement(new Worksheet());
                    worksheetWriter.WriteStartElement(new SheetData());
                    worksheetWriter.WriteStartElement(new Row());
                    foreach (var header in new[]
                    {
                        "Date", "User", "Username", "Vendor", "Parser", "Import Type", "Source Filename",
                        "Currency", "Quoted Total", "Computed Total", "Totals Match", "FX Rate", "Margin"
                    })
                    {
                        WriteString(worksheetWriter, header);
                    }
                    worksheetWriter.WriteEndElement();
                    rowCount = 1;
                }

                StartSheet();

                await foreach (var metric in query
                    .OrderByDescending(row => row.CreatedAt)
                    .ThenByDescending(row => row.Id)
                    .AsAsyncEnumerable()
                    .WithCancellation(cancellationToken))
                {
                    if (rowCount >= exportEnvironment.WorksheetRowLimit)
                    {
                        StartSheet();
                    }

                    worksheetWriter!.WriteStartElement(new Row());
                    WriteDate(worksheetWriter, TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(metric.CreatedAt, DateTimeKind.Utc), TimeZoneInfo.Local));
                    WriteString(worksheetWriter, metric.UserName);
                    WriteString(worksheetWriter, metric.UserUsername);
                    WriteString(worksheetWriter, metric.Vendor);
                    WriteString(worksheetWriter, parserNames.GetValueOrDefault(metric.ParserSlug, metric.ParserSlug));
                    WriteString(worksheetWriter, metric.ImportType?.ToString() ?? "Unknown");
                    WriteString(worksheetWriter, metric.SourceFilename);
                    WriteString(worksheetWriter, metric.Currency);
                    WriteNumber(worksheetWriter, metric.QuotedTotal, 2);
                    WriteNumber(worksheetWriter, metric.ComputedTotal, 2);
                    WriteBoolean(worksheetWriter, metric.TotalsMatch);
                    WriteNumber(worksheetWriter, metric.FxRate, 3);
                    WriteNumber(worksheetWriter, metric.Margin, 4);
                    worksheetWriter.WriteEndElement();
                    rowCount++;
                }

                worksheetWriter?.WriteEndElement();
                worksheetWriter?.WriteEndElement();
                worksheetWriter?.Dispose();

                using var workbookWriter = OpenXmlWriter.Create(workbookPart);
                workbookWriter.WriteStartElement(new Workbook());
                workbookWriter.WriteStartElement(new Sheets());
                for (var index = 0; index < sheetRelationshipIds.Count; index++)
                {
                    workbookWriter.WriteElement(new Sheet
                    {
                        Name = index == 0 ? "Utilisation" : $"Utilisation {index + 1}",
                        SheetId = (uint)(index + 1),
                        Id = sheetRelationshipIds[index]
                    });
                }
                workbookWriter.WriteEndElement();
                workbookWriter.WriteEndElement();
            }

            stream.Position = 0;
            var filename = parsedRange.IsAll
                ? "utilisation_all.xlsx"
                : $"utilisation_{parsedRange.FromStr}_{parsedRange.ToStr}.xlsx";
            return Results.File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static void WriteMetricsStyles(WorkbookPart workbookPart)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>("rIdStyles");
        using var writer = OpenXmlWriter.Create(stylesPart);
        writer.WriteStartElement(new Stylesheet());
        writer.WriteStartElement(new NumberingFormats());
        writer.WriteElement(new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd hh:mm:ss" });
        writer.WriteElement(new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.00" });
        writer.WriteElement(new NumberingFormat { NumberFormatId = 166, FormatCode = "0.0000" });
        writer.WriteElement(new NumberingFormat { NumberFormatId = 167, FormatCode = "0.00" });
        writer.WriteEndElement();
        writer.WriteStartElement(new Fonts());
        writer.WriteElement(new Font());
        writer.WriteEndElement();
        writer.WriteStartElement(new Fills());
        writer.WriteElement(new Fill());
        writer.WriteEndElement();
        writer.WriteStartElement(new Borders());
        writer.WriteElement(new Border());
        writer.WriteEndElement();
        writer.WriteStartElement(new CellStyleFormats());
        writer.WriteElement(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 });
        writer.WriteEndElement();
        writer.WriteStartElement(new CellFormats());
        writer.WriteElement(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 });
        foreach (var numberFormatId in new uint[] { 164, 165, 166, 167 })
        {
            writer.WriteElement(new CellFormat
            {
                NumberFormatId = numberFormatId,
                FontId = 0,
                FillId = 0,
                BorderId = 0,
                FormatId = 0,
                ApplyNumberFormat = true
            });
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteString(OpenXmlWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteElement(new Cell());
            return;
        }

        writer.WriteStartElement(new Cell { DataType = CellValues.InlineString });
        writer.WriteElement(new InlineString(new Text(value)));
        writer.WriteEndElement();
    }

    private static void WriteNumber(OpenXmlWriter writer, decimal? value, uint styleIndex)
    {
        if (!value.HasValue)
        {
            writer.WriteElement(new Cell());
            return;
        }

        writer.WriteStartElement(new Cell { DataType = CellValues.Number, StyleIndex = styleIndex });
        writer.WriteElement(new CellValue(value.Value.ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private static void WriteBoolean(OpenXmlWriter writer, bool value)
    {
        writer.WriteStartElement(new Cell { DataType = CellValues.Boolean });
        writer.WriteElement(new CellValue(value ? "1" : "0"));
        writer.WriteEndElement();
    }

    private static void WriteDate(OpenXmlWriter writer, DateTime value)
    {
        writer.WriteStartElement(new Cell { DataType = CellValues.Number, StyleIndex = 1 });
        writer.WriteElement(new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }
}

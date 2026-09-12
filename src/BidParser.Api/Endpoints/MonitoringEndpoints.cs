namespace BidParser.Api.Endpoints;

using System.Globalization;
using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using BidParser.Output;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public interface IExportEnvironment
{
    int WorksheetRowLimit { get; }
    Stream CreateTempStream();
    AppDbContext CreateExportContext();
}

public sealed class DefaultExportEnvironment : IExportEnvironment
{
    private readonly BidParser.Api.Options.AppOptions _options;
    public DefaultExportEnvironment(BidParser.Api.Options.AppOptions options)
    {
        _options = options;
    }

    public int WorksheetRowLimit => 1_048_576;
    public Stream CreateTempStream()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
        return new FileStream(tempFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
    }
    public AppDbContext CreateExportContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(_options.ConnectionString);
        return new AppDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// Admin monitoring under /api/monitoring (Admin policy): GET /runs is the unified runs view, merging
/// successful/mismatched ParseJobs (kind "job") with genuine FailedParseJobs (kind "failure"); it excludes
/// ValidationMismatch failure rows so each mismatch appears once (via its job). Plus per-run downloads:
/// /jobs/{id}/source|output and /failures/{id}/source (input only). Filters: status/vendor/user/parser/from/to.
/// </summary>
public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitoring").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/runs", ListRunsAsync);
        group.MapGet("/runs/export", ExportRunsAsync);
        group.MapGet("/failures/{id:int}/source", GetSourceAsync);
        group.MapGet("/jobs/{id:int}/source", GetJobSourceAsync);
        group.MapGet("/jobs/{id:int}/output", GetJobOutputAsync);
    }

    private sealed class UnifiedRunItem
    {
        public string Kind { get; set; } = "";
        public int Id { get; set; }
        public string Status { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int? UserId { get; set; }
        public string UserUsername { get; set; } = "";
        public string? UserName { get; set; }
        public string Vendor { get; set; } = "";
        public string ParserSlug { get; set; } = "";
        public string CrmTemplate { get; set; } = "";
        public string SourceFilename { get; set; } = "";
        public string? BidNumber { get; set; }
        public string? BidRevision { get; set; }
        public string SourcePath { get; set; } = "";
        public string? OutputPath { get; set; }
        public decimal FxRate { get; set; }
        public decimal Margin { get; set; }
        public decimal? ComputedTotal { get; set; }
        public decimal? QuotedTotal { get; set; }
        public bool? TotalsMatch { get; set; }
        public bool? SplitBySolutionId { get; set; }
        public ImportType? ImportType { get; set; }
        public string? Stage { get; set; }
        public string? Hint { get; set; }
        public string? Message { get; set; }
        public string? ErrorDetail { get; set; }
    }

    private static IQueryable<UnifiedRunItem> BuildUnifiedQuery(
        AppDbContext db,
        BidParser.Api.Common.ParsedDateRange parsedRange,
        string? vendor,
        int? userId,
        string? parserSlug,
        string? status,
        string? importType)
    {
        var includeJobs = status is null or "success" or "validationMismatch";
        var includeFailures = status is null or "magicByteMismatch" or "parserError" or "unhandledException";

        var jobs = db.ParseJobs.AsNoTracking().Select(j => new UnifiedRunItem
        {
            Kind = "job",
            Id = j.Id,
            Status = j.TotalsMatch ? "success" : "validationMismatch",
            Category = "", // Blank for jobs
            CreatedAt = j.CreatedAt,
            UserId = j.UserId,
            UserUsername = j.User != null ? j.User.Username : "",
            UserName = j.User != null ? j.User.Name : null,
            Vendor = j.Vendor,
            ParserSlug = j.ParserSlug,
            CrmTemplate = j.CrmTemplate,
            SourceFilename = j.SourceFilename,
            BidNumber = j.BidNumber,
            BidRevision = j.BidRevision,
            SourcePath = j.SourcePath,
            OutputPath = j.OutputPath,
            FxRate = j.FxRate,
            Margin = j.Margin,
            ComputedTotal = j.ComputedTotal,
            QuotedTotal = j.QuotedTotal,
            TotalsMatch = j.TotalsMatch,
            SplitBySolutionId = j.SplitBySolutionId,
            ImportType = j.ImportType,
            Stage = null,
            Hint = null,
            Message = null,
            ErrorDetail = null
        });

        if (!includeJobs) jobs = jobs.Where(j => false);

        var failures = db.FailedParseJobs.AsNoTracking()
            .Where(f => f.Category != FailureCategory.ValidationMismatch)
            .Select(f => new UnifiedRunItem
            {
                Kind = "failure",
                Id = f.Id,
                // Keep these conditionals inside the projection: a helper method is not SQL-translatable.
                Status = f.Category == FailureCategory.MagicByteMismatch ? "magicByteMismatch" :
                         f.Category == FailureCategory.ParserError ? "parserError" :
                         f.Category == FailureCategory.UnhandledException ? "unhandledException" :
                         "unknown",
                Category = f.Category == FailureCategory.MagicByteMismatch ? "magicByteMismatch" :
                         f.Category == FailureCategory.ParserError ? "parserError" :
                         f.Category == FailureCategory.UnhandledException ? "unhandledException" :
                         "unknown",
                CreatedAt = f.CreatedAt,
                UserId = f.UserId,
                UserUsername = f.UserUsername,
                UserName = f.UserName,
                Vendor = f.Vendor,
                ParserSlug = f.ParserSlug,
                CrmTemplate = "",
                SourceFilename = f.SourceFilename,
                BidNumber = null,
                BidRevision = null,
                SourcePath = f.SourcePath,
                OutputPath = null,
                FxRate = f.FxRate,
                Margin = f.Margin,
                ComputedTotal = f.ComputedTotal,
                QuotedTotal = f.QuotedTotal,
                TotalsMatch = null,
                SplitBySolutionId = null,
                ImportType = f.ImportType,
                Stage = f.Stage,
                Hint = f.Hint,
                Message = f.Message,
                ErrorDetail = f.ErrorDetail
            });

        if (!includeFailures) failures = failures.Where(f => false);

        var query = jobs.Concat(failures);

        if (!parsedRange.IsAll)
        {
            query = query.Where(q => q.CreatedAt >= parsedRange.FromUtc && q.CreatedAt < parsedRange.ToUtc);
        }

        if (!string.IsNullOrEmpty(vendor)) query = query.Where(q => q.Vendor == vendor);
        if (userId.HasValue) query = query.Where(q => q.UserId == userId.Value);
        if (!string.IsNullOrEmpty(parserSlug)) query = query.Where(q => q.ParserSlug == parserSlug);
        if (!string.IsNullOrEmpty(status)) query = query.Where(q => q.Status == status);

        if (importType == "auto") query = query.Where(q => q.ImportType == ImportType.Auto);
        else if (importType == "manual") query = query.Where(q => q.ImportType == ImportType.Manual);

        return query.OrderByDescending(q => q.CreatedAt)
                    .ThenByDescending(q => q.Kind)
                    .ThenByDescending(q => q.Id);
    }

    private static async Task<IResult> ExportRunsAsync(
        IExportEnvironment env,
        CancellationToken cancellationToken,
        [FromQuery] string? range = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? vendor = null,
        [FromQuery] int? userId = null,
        [FromQuery] string? parserSlug = null,
        [FromQuery] string? status = null,
        [FromQuery] string? importType = null)
    {
        var errorResult = BidParser.Api.Common.DateRangeParser.Parse(range, from, to, out var parsedRange);
        if (errorResult != null) return errorResult;

        await using var exportDb = env.CreateExportContext();

        var query = BuildUnifiedQuery(exportDb, parsedRange, vendor, userId, parserSlug, status, importType);

        var stream = env.CreateTempStream();

        try
        {
            using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();

                var stylesPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorkbookStylesPart>("rIdStyles");
                using (var stylesWriter = DocumentFormat.OpenXml.OpenXmlWriter.Create(stylesPart))
                {
                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Stylesheet());

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.NumberingFormats());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd hh:mm:ss" });
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Fonts());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Font());
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Fills());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Fill());
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Borders());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Border());
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.CellStyleFormats());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 });
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.CellFormats());
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 }); // index 0
                    stylesWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellFormat { NumberFormatId = 164, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true }); // index 1
                    stylesWriter.WriteEndElement();

                    stylesWriter.WriteEndElement();
                }

                var sheetCount = 0;
                var rowCount = 0;
                DocumentFormat.OpenXml.OpenXmlWriter? writer = null;
                var sheetIds = new List<string>();

                void StartNewSheet()
                {
                    writer?.WriteEndElement(); // SheetData
                    writer?.WriteEndElement(); // Worksheet
                    writer?.Dispose();

                    sheetCount++;
                    var rId = $"rId{sheetCount}";
                    sheetIds.Add(rId);

                    var worksheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>(rId);
                    writer = DocumentFormat.OpenXml.OpenXmlWriter.Create(worksheetPart);
                    writer.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Worksheet());
                    writer.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.SheetData());

                    // Write header
                    writer.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Row());
                    string[] headers = {
                        "kind", "id", "status", "failure_category", "created_at", "user_id", "user_username", "user_name",
                        "vendor", "parser_slug", "crm_template", "source_filename", "bid_number", "bid_revision",
                        "source_path", "output_path", "fx_rate", "margin", "computed_total", "quoted_total",
                        "totals_match", "split_by_solution_id", "import_type", "stage", "hint", "message", "error_detail",
                        "source_available", "output_available"
                    };

                    foreach (var header in headers)
                    {
                        writer.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                        writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(header)));
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); // Row

                    rowCount = 1; // header takes 1 row
                }

                void WriteCellString(string? value)
                {
                    writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                    if (value != null)
                        writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value)));
                    writer.WriteEndElement();
                }

                void WriteCellNumber(decimal? value)
                {
                    if (value.HasValue)
                    {
                        writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.Number });
                        writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.Value.ToString(CultureInfo.InvariantCulture)));
                        writer.WriteEndElement();
                    }
                    else
                    {
                        writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                        writer.WriteEndElement();
                    }
                }

                void WriteCellBool(bool? value)
                {
                    if (value.HasValue)
                    {
                        writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.Boolean });
                        writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.Value ? "1" : "0"));
                        writer.WriteEndElement();
                    }
                    else
                    {
                        writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                        writer.WriteEndElement();
                    }
                }

                void WriteCellDate(DateTime? value)
                {
                    if (value.HasValue)
                    {
                        writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.Number, StyleIndex = 1 });
                        writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.Value.ToOADate().ToString(CultureInfo.InvariantCulture)));
                        writer.WriteEndElement();
                    }
                    else
                    {
                        WriteCellString(null);
                    }
                }

                StartNewSheet();

                await foreach (var run in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
                {
                    if (rowCount >= env.WorksheetRowLimit)
                    {
                        StartNewSheet();
                    }

                    writer!.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Row());
                    WriteCellString(run.Kind);
                    WriteCellNumber(run.Id);
                    WriteCellString(run.Status);
                    WriteCellString(run.Kind == "failure" ? run.Category : "");
                    WriteCellDate(TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(run.CreatedAt, DateTimeKind.Utc), TimeZoneInfo.Local));
                    WriteCellNumber(run.UserId);
                    WriteCellString(run.UserUsername);
                    WriteCellString(run.UserName);
                    WriteCellString(run.Vendor);
                    WriteCellString(run.ParserSlug);
                    WriteCellString(run.CrmTemplate);
                    WriteCellString(run.SourceFilename);
                    WriteCellString(run.BidNumber);
                    WriteCellString(run.BidRevision);
                    WriteCellString(run.SourcePath);
                    WriteCellString(run.OutputPath);
                    WriteCellNumber(run.FxRate);
                    WriteCellNumber(run.Margin);
                    WriteCellNumber(run.ComputedTotal);
                    WriteCellNumber(run.QuotedTotal);
                    WriteCellBool(run.TotalsMatch);
                    WriteCellBool(run.SplitBySolutionId);
                    WriteCellString(ImportTypeToWire(run.ImportType));
                    WriteCellString(run.Stage);
                    WriteCellString(run.Hint);
                    WriteCellString(run.Message);
                    WriteCellString(run.ErrorDetail);
                    WriteCellBool(File.Exists(run.SourcePath));
                    WriteCellBool(run.OutputPath != null && File.Exists(run.OutputPath));
                    writer.WriteEndElement();

                    rowCount++;
                }

                writer?.WriteEndElement();
                writer?.WriteEndElement();
                writer?.Dispose();

                using (var workbookWriter = DocumentFormat.OpenXml.OpenXmlWriter.Create(workbookPart))
                {
                    workbookWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Workbook());
                    workbookWriter.WriteStartElement(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                    for (int i = 0; i < sheetIds.Count; i++)
                    {
                        var name = i == 0 ? "Parser Runs" : $"Parser Runs {i + 1}";
                        workbookWriter.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name = name, SheetId = (uint)(i + 1), Id = sheetIds[i] });
                    }
                    workbookWriter.WriteEndElement();
                    workbookWriter.WriteEndElement();
                }
            }

            stream.Position = 0;
            string filename = parsedRange.IsAll
                ? "parser_runs_all.xlsx"
                : $"parser_runs_{parsedRange.FromStr}_{parsedRange.ToStr}.xlsx";

            return Results.File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileDownloadName: filename);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static async Task<IResult> ListRunsAsync(
        AppDbContext db,
        IParserRegistry registry,
        CancellationToken cancellationToken,
        [FromQuery] string? range = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? vendor = null,
        [FromQuery] int? userId = null,
        [FromQuery] string? parserSlug = null,
        [FromQuery] string? status = null,
        [FromQuery] string? importType = null,
        [FromQuery] int limit = 25,
        [FromQuery] int offset = 0)
    {
        var errorResult = BidParser.Api.Common.DateRangeParser.Parse(range, from, to, out var parsedRange);
        if (errorResult != null) return errorResult;

        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var query = BuildUnifiedQuery(db, parsedRange, vendor, userId, parserSlug, status, importType);

        var total = await query.CountAsync(cancellationToken);

        var page = await query.Skip(offset).Take(limit).ToListAsync(cancellationToken);

        string Display(string slug) =>
            registry.Parsers.FirstOrDefault(p => p.Slug == slug)?.DisplayName ?? slug;

        var items = page.Select(p => new MonitoringRunItem(
            p.Kind,
            p.Id,
            p.Status,
            DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
            p.UserId,
            p.UserUsername,
            p.UserName,
            p.Vendor,
            p.ParserSlug,
            Display(p.ParserSlug),
            ImportTypeToWire(p.ImportType),
            p.SourceFilename,
            File.Exists(p.SourcePath),
            p.OutputPath != null && File.Exists(p.OutputPath),
            p.ComputedTotal?.ToString("F2", CultureInfo.InvariantCulture),
            p.QuotedTotal?.ToString("F2", CultureInfo.InvariantCulture),
            p.Stage,
            p.Hint,
            p.Message,
            p.ErrorDetail)).ToList();

        return Results.Ok(new MonitoringRunsResponse(total, items));
    }

    private static async Task<IResult> GetSourceAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var failure = await db.FailedParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (failure is null || !File.Exists(failure.SourcePath))
        {
            return Results.NotFound();
        }

        return FileResult(failure.SourcePath, failure.SourceFilename);
    }

    private static async Task<IResult> GetJobSourceAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.ParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null || !File.Exists(job.SourcePath))
        {
            return Results.NotFound();
        }

        return FileResult(job.SourcePath, job.SourceFilename);
    }

    private static async Task<IResult> GetJobOutputAsync(
        int id,
        AppDbContext db,
        IParserRegistry registry,
        CancellationToken cancellationToken)
    {
        var job = await db.ParseJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null || !File.Exists(job.OutputPath))
        {
            return Results.NotFound();
        }

        var style = registry.Parsers.FirstOrDefault(p => p.Slug == job.ParserSlug)?.OutputNameStyle
            ?? OutputNameStyle.BidScoped;
        var downloadName = job.SplitBySolutionId
            ? OutputNaming.OutputArchiveFilename(
                job.SourceFilename, job.CrmTemplate, job.BidNumber, job.BidRevision, style)
            : OutputNaming.OutputFilename(
                job.SourceFilename, job.CrmTemplate, job.BidNumber, job.BidRevision, style);
        return Results.File(
            new FileStream(job.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            job.SplitBySolutionId
                ? "application/zip"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: downloadName);
    }

    private static IResult FileResult(string path, string downloadName)
    {
        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.File(stream, contentType, fileDownloadName: downloadName);
    }

    private static string? ImportTypeToWire(ImportType? importType) => importType switch
    {
        ImportType.Auto => "auto",
        ImportType.Manual => "manual",
        _ => null
    };
}

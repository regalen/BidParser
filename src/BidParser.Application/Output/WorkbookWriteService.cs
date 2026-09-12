using System.IO.Compression;
using BidParser.Application.Parsing;
using BidParser.Domain.Constants;
using BidParser.Output;

namespace BidParser.Application.Output;

public sealed record WorkbookWriteRequest(
    ParsedQuote Quote,
    string DestinationPath,
    string? CrmTemplate = null,
    decimal FxRate = 1m,
    decimal Margin = 0m,
    decimal? ImPercent = null,
    decimal? OnCostPercent = null,
    bool SplitBySolutionId = false);

public sealed record WorkbookWriteResult(
    string OutputPath,
    string OutputFilename,
    string CrmTemplate,
    int WorkbookCount);

/// <summary>
/// What a write would produce, resolved before a destination exists. Local hosts need the
/// canonical filename to seed a save dialog; the web host names its own storage path.
/// </summary>
public sealed record WorkbookWritePlan(
    string CrmTemplate,
    string SuggestedFilename,
    bool IsArchive);

/// <summary>Validates output options and writes a workbook or split archive using shared rules.</summary>
public sealed class WorkbookWriteService
{
    /// <summary>
    /// Resolves and validates the template/split combination and returns the canonical output
    /// filename, without writing anything.
    /// </summary>
    public WorkbookWritePlan Describe(ParsedQuote quote, string? crmTemplate, bool splitBySolutionId)
    {
        var template = ResolveTemplate(quote, crmTemplate, splitBySolutionId);
        return new WorkbookWritePlan(
            template,
            OutputFilenameFor(quote, template, splitBySolutionId),
            splitBySolutionId);
    }

    /// <summary>
    /// Writes the workbook to a staging file in the destination directory, then moves it into
    /// place — a failed or cancelled write never leaves a half-written workbook at the
    /// destination. With <paramref name="overwrite"/> false an existing destination is reported
    /// as <see cref="ParseInputErrorKind.DestinationExists"/> so the caller can ask the user.
    /// </summary>
    public async Task<WorkbookWriteResult> SaveAsync(
        WorkbookWriteRequest request,
        bool overwrite,
        CancellationToken ct = default)
    {
        var destination = Path.GetFullPath(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ParseInputException(
                ParseInputErrorKind.InvalidDestination,
                "Choose a folder to save the workbook into.");

        Directory.CreateDirectory(directory);
        EnsureAbsent(destination, overwrite);

        // The staging file keeps the destination's extension: ClosedXML refuses to save a
        // workbook to a path that is not .xlsx.
        var stagingPath = Path.Combine(
            directory, $"bidparser-{Guid.NewGuid():N}{Path.GetExtension(destination)}");
        try
        {
            var written = await WriteAsync(request with { DestinationPath = stagingPath }, ct);
            ct.ThrowIfCancellationRequested();

            // The staging file is closed by now, and the destination may have appeared while we
            // were writing — re-check before replacing it.
            EnsureAbsent(destination, overwrite);
            File.Move(stagingPath, destination, overwrite: true);

            return written with { OutputPath = destination };
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    /// <summary>
    /// Returns <paramref name="path"/> when it is free, otherwise the first "&lt;name&gt; (n)"
    /// variant that is — the "Save a copy" answer to an overwrite prompt.
    /// </summary>
    public static string UniqueDestination(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var copy = 2; copy < 1000; copy++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({copy}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    public async Task<WorkbookWriteResult> WriteAsync(
        WorkbookWriteRequest request,
        CancellationToken ct = default)
    {
        var parser = request.Quote.Parser;
        var template = ResolveTemplate(request.Quote, request.CrmTemplate, request.SplitBySolutionId);

        if (template == CrmTemplates.PercentOffWithUplift && request.ImPercent is null)
        {
            throw new ParseInputException(ParseInputErrorKind.MissingRequiredOption, "IM% is required for OneConfig (XLSX).");
        }

        ct.ThrowIfCancellationRequested();
        var options = new CrmWriterOptions(
            VendorName: parser.OutputVendorName.ToUpperInvariant(),
            Margin: request.Margin,
            FxRate: request.FxRate,
            Currency: request.Quote.Result.Metadata.Currency,
            ImPercent: request.ImPercent,
            OnCost: parser.SupportsOnCost ? request.OnCostPercent : null,
            TermAsComment: parser.TermRendersAsComment);

        var filename = OutputFilenameFor(request.Quote, template, request.SplitBySolutionId);

        if (!request.SplitBySolutionId)
        {
            CrmWriter.Write(request.Quote.Result.LineItems, request.DestinationPath, template, options);
            ct.ThrowIfCancellationRequested();
            return new WorkbookWriteResult(request.DestinationPath, filename, template, WorkbookCount: 1);
        }

        var groups = SolutionOutputSplitter.Split(request.Quote.Result.LineItems);
        var stagingDirectory = Directory.CreateTempSubdirectory("bidparser-split-");
        try
        {
            await using var archiveStream = new FileStream(
                request.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var entryName = OutputNaming.OutputFilename(
                    request.Quote.SourceFilename,
                    template,
                    group.SolutionId,
                    request.Quote.Result.Metadata.BidNumber,
                    request.Quote.Result.Metadata.BidRevision,
                    parser.OutputNameStyle);
                var stagedPath = Path.Combine(stagingDirectory.FullName, entryName);
                CrmWriter.Write(group.Items, stagedPath, template, options);

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var workbookStream = new FileStream(
                    stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await workbookStream.CopyToAsync(entryStream, ct);
            }
        }
        finally
        {
            try { stagingDirectory.Delete(recursive: true); } catch { }
        }

        return new WorkbookWriteResult(request.DestinationPath, filename, template, groups.Count);
    }

    private static string ResolveTemplate(ParsedQuote quote, string? crmTemplate, bool splitBySolutionId)
    {
        var parser = quote.Parser;
        var template = string.IsNullOrWhiteSpace(crmTemplate) ? parser.CrmTemplate : crmTemplate;

        if (!parser.AvailableTemplates.Contains(template))
        {
            throw new ParseInputException(ParseInputErrorKind.UnsupportedTemplate, "Unknown CRM template for this parser.");
        }
        if (!CrmWriter.IsSupported(template))
        {
            throw new ParseInputException(ParseInputErrorKind.UnsupportedTemplate, "Unsupported CRM template.");
        }
        if (splitBySolutionId && !parser.SupportsSolutionIdSplit)
        {
            throw new ParseInputException(ParseInputErrorKind.UnsupportedSplit, "This file type does not support splitting by Solution ID.");
        }

        return template;
    }

    private static string OutputFilenameFor(ParsedQuote quote, string template, bool splitBySolutionId)
        => splitBySolutionId
            ? OutputNaming.OutputArchiveFilename(
                quote.SourceFilename,
                template,
                quote.Result.Metadata.BidNumber,
                quote.Result.Metadata.BidRevision,
                quote.Parser.OutputNameStyle)
            : OutputNaming.OutputFilename(
                quote.SourceFilename,
                template,
                quote.Result.Metadata.BidNumber,
                quote.Result.Metadata.BidRevision,
                quote.Parser.OutputNameStyle);

    private static void EnsureAbsent(string destination, bool overwrite)
    {
        if (!overwrite && File.Exists(destination))
        {
            throw new ParseInputException(
                ParseInputErrorKind.DestinationExists,
                $"{Path.GetFileName(destination)} already exists.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A staging file we cannot remove is not worth failing a successful save over.
        }
    }
}

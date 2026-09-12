using System.Text.RegularExpressions;
using BidParser.Domain.Constants;

namespace BidParser.Output;

/// <summary>Builds standard workbook and Solution-ID archive download filenames.</summary>
public static partial class OutputNaming
{
    private const string NoSolutionToken = "NoSolutionID";

    /// <summary>Returns the legacy "&lt;basename&gt;_&lt;FileToken&gt;.xlsx" filename.</summary>
    public static string OutputFilename(string sourceFilename, string crmTemplate)
    {
        return OutputFilename(sourceFilename, crmTemplate, bidNumber: null, bidRevision: null, OutputNameStyle.BidScoped);
    }

    /// <summary>
    /// Returns "&lt;BidNumber&gt;_&lt;BidRevision&gt;_&lt;FileToken&gt;.xlsx" when both bid fields
    /// are available, otherwise falls back to the source filename stem.
    /// </summary>
    public static string OutputFilename(
        string sourceFilename,
        string crmTemplate,
        string? bidNumber,
        string? bidRevision)
    {
        return OutputFilename(sourceFilename, crmTemplate, bidNumber, bidRevision, OutputNameStyle.BidScoped);
    }

    /// <summary>
    /// Returns the whole-quote workbook filename formatted per <paramref name="style"/>.
    /// </summary>
    public static string OutputFilename(
        string sourceFilename,
        string crmTemplate,
        string? bidNumber,
        string? bidRevision,
        OutputNameStyle style)
    {
        return $"{OutputStem(sourceFilename, bidNumber, bidRevision, style)}_{CrmTemplates.FileToken(crmTemplate)}.xlsx";
    }

    /// <summary>Returns the workbook filename for one Solution ID within a split archive.</summary>
    public static string OutputFilename(string sourceFilename, string crmTemplate, string? solutionId)
    {
        return OutputFilename(sourceFilename, crmTemplate, solutionId, bidNumber: null, bidRevision: null, OutputNameStyle.BidScoped);
    }

    /// <summary>Returns the workbook filename for one Solution ID within a split archive.</summary>
    public static string OutputFilename(
        string sourceFilename,
        string crmTemplate,
        string? solutionId,
        string? bidNumber,
        string? bidRevision)
    {
        return OutputFilename(sourceFilename, crmTemplate, solutionId, bidNumber, bidRevision, OutputNameStyle.BidScoped);
    }

    /// <summary>Returns the workbook filename for one Solution ID within a split archive formatted per <paramref name="style"/>.</summary>
    public static string OutputFilename(
        string sourceFilename,
        string crmTemplate,
        string? solutionId,
        string? bidNumber,
        string? bidRevision,
        OutputNameStyle style)
    {
        var solutionToken = Sanitise(solutionId) ?? NoSolutionToken;
        if (style == OutputNameStyle.SolutionScoped)
        {
            return $"{solutionToken}_{CrmTemplates.FileToken(crmTemplate)}.xlsx";
        }

        return $"{OutputStem(sourceFilename, bidNumber, bidRevision, OutputNameStyle.BidScoped)}_{solutionToken}_{CrmTemplates.FileToken(crmTemplate)}.xlsx";
    }

    /// <summary>Returns the ZIP filename for a Solution-ID-split output.</summary>
    public static string OutputArchiveFilename(string sourceFilename, string crmTemplate)
    {
        return OutputArchiveFilename(sourceFilename, crmTemplate, bidNumber: null, bidRevision: null, OutputNameStyle.BidScoped);
    }

    /// <summary>Returns the ZIP filename for a Solution-ID-split output.</summary>
    public static string OutputArchiveFilename(
        string sourceFilename,
        string crmTemplate,
        string? bidNumber,
        string? bidRevision)
    {
        return OutputArchiveFilename(sourceFilename, crmTemplate, bidNumber, bidRevision, OutputNameStyle.BidScoped);
    }

    /// <summary>Returns the ZIP filename for a Solution-ID-split output formatted per <paramref name="style"/>.</summary>
    public static string OutputArchiveFilename(
        string sourceFilename,
        string crmTemplate,
        string? bidNumber,
        string? bidRevision,
        OutputNameStyle style)
    {
        return $"{OutputStem(sourceFilename, bidNumber, bidRevision, style)}_{CrmTemplates.FileToken(crmTemplate)}.zip";
    }

    private static string OutputStem(string sourceFilename, string? bidNumber, string? bidRevision, OutputNameStyle style)
    {
        if (style == OutputNameStyle.SolutionScoped)
        {
            return Sanitise(bidNumber) ?? Path.GetFileNameWithoutExtension(sourceFilename);
        }

        var numberToken = Sanitise(bidNumber);
        var revisionToken = Sanitise(bidRevision);
        return numberToken is not null && revisionToken is not null
            ? $"{numberToken}_{revisionToken}"
            : Path.GetFileNameWithoutExtension(sourceFilename);
    }

    /// <summary>
    /// Strips anything that has no business in a filename or ZIP entry name. Bid metadata and
    /// Solution IDs reach here straight from source documents, so they are never trusted.
    /// Maps runs of whitespace to a single '_' before stripping invalid characters.
    /// </summary>
    private static string? Sanitise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withUnderscores = WhitespacePattern().Replace(value.Trim(), "_");
        var sanitised = UnsafeFilenameChars().Replace(withUnderscores, string.Empty);
        return sanitised.Length == 0 ? null : sanitised;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFilenameChars();
}

using System.Text.RegularExpressions;

namespace BidParser.Parsing.Cleaning;

/// <summary>
/// Normalises the bid identity stored separately from legacy quote metadata.
/// <para>
/// Extraction is <b>best-effort and never throws</b>. The bid fields are display-and-search only,
/// so an unreadable header must not cost the user a workbook that is otherwise correct — a missing
/// value degrades to <c>null</c>, which every downstream layer already handles (nullable columns,
/// nullable API fields, an em dash in Recent Uploads, null-guarded search).
/// </para>
/// </summary>
public static partial class BidMetadataCleaner
{
    public const string DefaultRevision = "1";

    /// <summary>
    /// Cleans both values. The number is the load-bearing half: without it there is nothing worth
    /// displaying, so both come back null. A number with an unreadable revision keeps the number
    /// and falls back to <see cref="DefaultRevision"/> — every revision anchor is newer and less
    /// proven than its number anchor, and a bid number with an assumed revision is far more useful
    /// than no bid number at all.
    /// </summary>
    public static (string? BidNumber, string? BidRevision) Clean(string? bidNumber, string? bidRevision)
    {
        var number = TextCleaner.Clean(bidNumber);
        if (number.Length == 0)
        {
            return (null, null);
        }

        var revision = NormalizeRevision(bidRevision);
        return (number, revision.Length > 0 ? revision : DefaultRevision);
    }

    /// <summary>
    /// Cleans a bid number for a format documented as carrying no source revision, storing
    /// <see cref="DefaultRevision"/>. Behaviourally the same as passing a null revision to
    /// <see cref="Clean"/> — it exists so a call site distinguishes "this format has no revision"
    /// from "we looked for one and it may be missing".
    /// </summary>
    public static (string? BidNumber, string? BidRevision) CleanRevisionless(string? bidNumber)
    {
        return Clean(bidNumber, DefaultRevision);
    }

    /// <summary>
    /// Recovers a display-only bid identity from a source filename when a supplier export omits
    /// or damages its header. This is deliberately conservative: it recognises the structured
    /// identifiers used by supported supplier formats, then falls back to a long numeric token.
    /// </summary>
    public static (string? BidNumber, string? BidRevision) FromFilename(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var number = FilenameBid().Match(stem).Value;
        var revision = FilenameRevision().Match(stem).Groups[1].Value;
        return Clean(number, revision);
    }

    /// <summary>
    /// Strips one optional leading <c>v</c> / <c>v.</c>. Revisions are never parsed numerically —
    /// Zebra's <c>2.0</c> must stay <c>2.0</c>, not become <c>2</c>.
    /// </summary>
    public static string NormalizeRevision(string? value)
    {
        var revision = TextCleaner.Clean(value);
        return OptionalPrefix().Replace(revision, string.Empty).Trim();
    }

    [GeneratedRegex(@"^v\s*\.?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex OptionalPrefix();

    [GeneratedRegex(@"(?:XQ-\d+|BR[A-Z]+\d+|PE\d+|CH\d+|\d{7,})", RegexOptions.IgnoreCase)]
    private static partial Regex FilenameBid();

    [GeneratedRegex(@"(?:^|[_-])V(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex FilenameRevision();
}

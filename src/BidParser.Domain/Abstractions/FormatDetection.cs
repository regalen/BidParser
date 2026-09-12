namespace BidParser.Domain.Abstractions;

/// <summary>
/// Shared Detect()-based resolver with two consumers: the wrong-file-type suggestion flow and
/// the per-vendor Auto option. See AGENTS.md "Format detection is a soft hint, not routing".
/// </summary>
public static class FormatDetection
{
    /// <summary>
    /// Minimum Detect() score to accept a match (auto-detect and wrong-type suggestion).
    /// </summary>
    public const double MinConfidence = 0.7;

    /// <summary>
    /// Highest-scoring parser for the same vendor whose AcceptedMimes includes acceptedMime and
    /// whose Detect(path) score is at least MinConfidence, or null.
    /// Strict '>' keeps the earlier registry entry on ties (deterministic; ties are prevented by
    /// design since siblings return 0.0 on a rival's anchor).
    /// </summary>
    public static IParser? Resolve(
        IEnumerable<IParser> parsers,
        string vendor,
        string acceptedMime,
        string path,
        IParser? exclude = null)
    {
        IParser? best = null;
        var bestScore = 0.0;

        foreach (var candidate in parsers)
        {
            if (candidate.Slug == exclude?.Slug
                || !string.Equals(candidate.Vendor, vendor, StringComparison.OrdinalIgnoreCase)
                || !candidate.AcceptedMimes.Any(m => string.Equals(m, acceptedMime, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var score = candidate.Detect(path);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return bestScore >= MinConfidence ? best : null;
    }
}

namespace BidParser.Domain.Models;

/// <summary>
/// Per-parse caller options. Parsers that accept none simply ignore this
/// (see the default <see cref="Abstractions.IParser.Parse(string, ParseOptions)"/>).
/// </summary>
public sealed record ParseOptions
{
    /// <summary>
    /// When true, emit the source's full item/SKU tree unfiltered. Default false. Honoured only by
    /// the Dell CTO parser; the Dell APOS parser emits every source SKU either way.
    /// <para>
    /// /api/parse pins this to false: the Dell CTO SKU filtering is unconditional and there is no
    /// user-facing opt-out. The full-tree path is retained, and reachable only from
    /// <c>Parse(path, options)</c>, so re-exposing it stays a one-line change.
    /// </para>
    /// </summary>
    public bool IncludeSubComponentDetail { get; init; }
}

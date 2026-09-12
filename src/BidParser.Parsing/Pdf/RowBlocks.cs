namespace BidParser.Parsing.Pdf;

/// <summary>
/// Resolves which anchor row each continuation row belongs to, for tables whose anchor row is
/// <em>vertically centred</em> on its block of wrapped text.
///
/// Zebra Price Concession lays out a multi-line description this way, so a three-line description
/// puts one line ABOVE the anchor row and one BELOW it. Continuation lines therefore cannot be
/// classified by direction — "the row after an anchor continues it" drops the
/// leading line onto the previous item, and "a large gap means the next item" drops the trailing one
/// onto the next.
///
/// On one page, the boundary between two anchor rows is the largest consecutive midline gap between
/// them. Lines in a block are one line-pitch apart, while Zebra adds inter-block padding.
/// A page-break window keeps the distance-based rule, because a legitimate block can straddle pages.
/// </summary>
public sealed class RowBlocks
{
    /// <summary>
    /// Hysteresis, in points, favouring the previous anchor — so a trailing line that happens to
    /// end a page is not stolen by an equidistant anchor at the top of the next one.
    /// </summary>
    private const double CrossPageBias = 0.5;

    private readonly IReadOnlyList<PdfRow> _rows;
    private readonly IReadOnlyList<int> _anchorIndexes;
    private readonly double _lineHeight;

    /// <param name="rows">All table rows, in reading order.</param>
    /// <param name="anchorIndexes">Indexes into <paramref name="rows"/> of the rows that start a block, ascending.</param>
    public RowBlocks(IReadOnlyList<PdfRow> rows, IReadOnlyList<int> anchorIndexes)
    {
        _rows = rows;
        _anchorIndexes = anchorIndexes;
        _lineHeight = EstimateLineHeight(rows);
    }

    /// <summary>
    /// The anchor row that the row at <paramref name="rowIndex"/> belongs to, or <c>null</c> only
    /// when there is no anchor at all. A row outside the outermost anchors has just one candidate,
    /// so it goes to that one whatever the distance: the table is bounded by its header and stop
    /// token, and a stray row inside those bounds is part of the block it sits next to.
    /// </summary>
    public int? OwnerOf(int rowIndex)
    {
        var previous = -1;
        var next = -1;

        foreach (var anchor in _anchorIndexes)
        {
            if (anchor < rowIndex)
            {
                previous = anchor;
            }
            else if (anchor > rowIndex)
            {
                next = anchor;
                break;
            }
        }

        if (previous < 0 && next < 0) return null;
        if (previous < 0) return next;
        if (next < 0) return previous;

        if (SpansOnePage(previous, next))
        {
            return rowIndex < LargestGapIndex(previous, next) ? previous : next;
        }

        var previousDistance = DistanceTo(rowIndex, previous);
        var nextDistance = DistanceTo(rowIndex, next);

        if (nextDistance < previousDistance - CrossPageBias) return next;

        return previous >= 0 ? previous : next;
    }

    private bool SpansOnePage(int first, int last)
    {
        var page = _rows[first].PageIndex;
        return _rows.Skip(first).Take(last - first + 1).All(row => row.PageIndex == page);
    }

    /// <summary>Returns the row after the first largest consecutive midline gap.</summary>
    private int LargestGapIndex(int first, int last)
    {
        var cut = first + 1;
        var largestGap = double.NegativeInfinity;
        for (var index = first + 1; index <= last; index++)
        {
            var gap = _rows[index].Midline - _rows[index - 1].Midline;
            if (gap > largestGap)
            {
                largestGap = gap;
                cut = index;
            }
        }

        return cut;
    }

    private double DistanceTo(int rowIndex, int anchorIndex)
    {
        if (anchorIndex < 0) return double.PositiveInfinity;

        var row = _rows[rowIndex];
        var anchor = _rows[anchorIndex];

        if (row.PageIndex == anchor.PageIndex) return Math.Abs(row.Midline - anchor.Midline);

        // A block split by a page break keeps its lines consecutive in reading order, so only the
        // immediately adjacent row can still be part of it — one line-height away.
        return Math.Abs(rowIndex - anchorIndex) == 1 ? _lineHeight : double.PositiveInfinity;
    }

    /// <summary>Median gap between consecutive rows on the same page — the table's line height.</summary>
    private static double EstimateLineHeight(IReadOnlyList<PdfRow> rows)
    {
        var gaps = rows
            .Zip(rows.Skip(1), (first, second) => (first, second))
            .Where(pair => pair.first.PageIndex == pair.second.PageIndex)
            .Select(pair => pair.second.Midline - pair.first.Midline)
            .Where(gap => gap > 0)
            .Order()
            .ToList();

        return gaps.Count > 0 ? gaps[gaps.Count / 2] : 12.0;
    }
}

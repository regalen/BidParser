using System.Globalization;
using BidParser.Domain.Models;

namespace BidParser.Output;

/// <summary>
/// One output workbook's worth of lines: every line sharing a Solution ID, already renumbered.
/// <see cref="SolutionId"/> is null for lines the source never attributed to a solution.
/// </summary>
public sealed record SolutionGroup(string? SolutionId, IReadOnlyList<LineItem> Items);

/// <summary>
/// Splits a parse result into one group per Solution ID so the caller can emit a workbook each.
/// Generic over formats — it reads <see cref="LineItem.SolutionId"/> only, and never inspects the
/// vendor or parser slug. Parsers opt in by declaring <c>IParser.SupportsSolutionIdSplit</c>.
/// </summary>
public static class SolutionOutputSplitter
{
    /// <summary>
    /// Groups by Solution ID in first-appearance order and renumbers each group's line
    /// sequence from 1 while preserving document order and parent/child structure.
    /// Always returns at least one group, so a split always yields at least one workbook.
    /// </summary>
    public static IReadOnlyList<SolutionGroup> Split(IReadOnlyList<LineItem> items)
    {
        if (items.Count == 0)
        {
            // A quote with no lines still produces one (empty) workbook, matching the
            // unsplit path and keeping "split ⇒ at least one archive entry" true.
            return [new SolutionGroup(null, [])];
        }

        var groups = new List<(string? SolutionId, List<LineItem> Items)>();

        foreach (var item in items)
        {
            var groupIndex = groups.FindIndex(group =>
                string.Equals(group.SolutionId, item.SolutionId, StringComparison.Ordinal));
            if (groupIndex < 0)
            {
                groups.Add((item.SolutionId, []));
                groupIndex = groups.Count - 1;
            }

            groups[groupIndex].Items.Add(item);
        }

        return groups
            .Select(group => new SolutionGroup(group.SolutionId, Renumber(group.Items)))
            .ToList();
    }

    private static IReadOnlyList<LineItem> Renumber(IReadOnlyList<LineItem> items)
    {
        var parent = 0;
        var child = 0;
        var renumbered = new List<LineItem>(items.Count);

        foreach (var item in items)
        {
            string lineSequence;
            if (item.LineSequence is null || !item.LineSequence.Contains('.'))
            {
                parent++;
                child = 0;
                lineSequence = parent.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if (parent == 0)
                {
                    parent = 1;
                }
                child++;
                lineSequence = $"{parent}.{child:D2}";
            }

            renumbered.Add(item with { LineSequence = lineSequence });
        }

        return renumbered;
    }
}

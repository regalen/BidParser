using System.Globalization;
using System.Text.RegularExpressions;
using BidParser.Parsing.Cleaning;

namespace BidParser.Parsing.Pdf;

public static partial class PdfTableHelpers
{
    public static string WordStreamText(IEnumerable<PdfWord> words)
    {
        return string.Join(' ', words.Select(word => word.Text));
    }

    public static int? FindSequence(IReadOnlyList<PdfWord> words, IReadOnlyList<string> sequence, int startIndex = 0)
    {
        for (var i = startIndex; i <= words.Count - sequence.Count; i++)
        {
            var matches = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (!string.Equals(words[i + j].Text, sequence[j], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return i;
            }
        }

        return null;
    }

    public static int? FindProductCodeHeader(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count - 1; i++)
        {
            var first = words[i];
            var second = words[i + 1];
            if (first.Text == "Product"
                && second.Text == "Code"
                && first.PageIndex == second.PageIndex
                && Math.Abs(first.Top - second.Top) <= 3
                && second.X0 > first.X0)
            {
                return i;
            }
        }

        return null;
    }

    public static int? FindRenewalHeader(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text == "No")
            {
                return i;
            }
        }

        return null;
    }

    public static IReadOnlyList<PdfRow> RowsBetween(
        IEnumerable<PdfWord> words,
        double startTop,
        int startPage,
        IReadOnlyDictionary<string, (double Left, double Right)> columns,
        string stopToken = "TOTAL:")
    {
        var bodyWords = words
            .Where(word => word.PageIndex > startPage || (word.PageIndex == startPage && word.Top > startTop))
            .OrderBy(word => word.PageIndex)
            .ThenBy(word => word.Top)
            .ThenBy(word => word.X0)
            .ToList();

        var stopIndex = bodyWords.FindIndex(word => word.Text == stopToken);
        if (stopIndex >= 0)
        {
            bodyWords = bodyWords[..stopIndex];
        }

        var rows = new List<List<PdfWord>>();
        foreach (var word in bodyWords)
        {
            var row = rows.FirstOrDefault(existing =>
                existing[0].PageIndex == word.PageIndex
                && Math.Abs(existing[0].Top - word.Top) <= 3.5);

            if (row is null)
            {
                rows.Add([word]);
            }
            else
            {
                row.Add(word);
            }
        }

        return rows
            .OrderBy(row => row[0].PageIndex)
            .ThenBy(row => row[0].Top)
            .Select(row =>
            {
                var cells = columns.ToDictionary(
                    pair => pair.Key,
                    pair => TextCleaner.Clean(string.Join(' ', row
                        .Where(word => word.X0 >= pair.Value.Left && word.X0 < pair.Value.Right)
                        .OrderBy(word => word.X0)
                        .Select(word => word.Text))));
                return new PdfRow(row[0].PageIndex, row[0].Top, cells);
            })
            .ToList();
    }

    public static decimal? TotalFromWords(IReadOnlyList<PdfWord> words, int startIndex = 0)
    {
        for (var i = startIndex; i < words.Count; i++)
        {
            if (words[i].Text != "TOTAL:")
            {
                continue;
            }

            var tail = string.Join(' ', words.Skip(i + 1).Take(8).Select(word => word.Text));
            var match = TotalPattern().Match(tail);
            if (match.Success)
            {
                return DecimalCleaner.Parse(match.Value);
            }
        }

        return null;
    }

    public static Dictionary<string, string> RawDict(params (string Key, string? Value)[] values)
    {
        return values.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);
    }

    public static IReadOnlyDictionary<string, (double Left, double Right)> ColumnRanges(
        IReadOnlyList<(string Name, double X0)> headers,
        double pageWidth)
    {
        var ordered = headers.OrderBy(header => header.X0).ToList();
        return ordered
            .Select((header, index) =>
            {
                var right = index + 1 < ordered.Count ? ordered[index + 1].X0 : pageWidth;
                return (header.Name, Range: (header.X0, right));
            })
            .ToDictionary(pair => pair.Name, pair => pair.Range);
    }

    /// <summary>
    /// Pre-fuses each "USD" token with its nearby numeric amount into a single synthetic
    /// word anchored at the amount's coordinates. This ensures that amounts which wrap
    /// onto the line below their "USD" prefix still land in the correct price column when
    /// bucketed, and that DecimalCleaner never sees a bare "USD" token.
    /// </summary>
    public static IReadOnlyList<PdfWord> FuseCurrencyTokens(IReadOnlyList<PdfWord> words)
    {
        const int lookAhead = 6;
        var consumed = new bool[words.Count];
        var result = new List<PdfWord>(words.Count);

        for (var i = 0; i < words.Count; i++)
        {
            if (consumed[i])
            {
                continue;
            }

            var current = words[i];
            if (current.Text == "USD")
            {
                var matchIndex = -1;
                for (var j = i + 1; j < words.Count && j <= i + lookAhead; j++)
                {
                    if (consumed[j])
                    {
                        continue;
                    }

                    var candidate = words[j];
                    if (candidate.PageIndex == current.PageIndex
                        && candidate.Top >= current.Top - 3.5
                        && candidate.Top - current.Top <= 15.0
                        && CurrencyAmount().IsMatch(candidate.Text))
                    {
                        matchIndex = j;
                        break;
                    }
                }

                if (matchIndex >= 0)
                {
                    var amount = words[matchIndex];
                    result.Add(new PdfWord(
                        $"USD {amount.Text}",
                        amount.X0, amount.X1,
                        amount.Top, amount.Bottom,
                        amount.PageIndex, amount.PageWidth));
                    consumed[matchIndex] = true;
                    continue;
                }
            }

            result.Add(current);
        }

        return result;
    }

    [GeneratedRegex(@"(?:USD\s*)?[$]?\s*[-+]?\d[\d,]*(?:\.\d+)?")]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"^\d[\d,]*(?:\.\d+)?$")]
    private static partial Regex CurrencyAmount();
}

using System.Text.Json;
using System.Text.Json.Serialization;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class PdfCharacterisationTests
{
    [Fact]
    public void PdfPig_word_coordinates_match_pdfplumber_anchor_snapshot()
    {
        var root = FindRepoRoot();
        var snapshotPath = Path.Combine(root, "tests", "BidParser.Parsing.Tests", "Fixtures", "pdfplumber-word-snapshot.json");
        var snapshot = JsonSerializer.Deserialize<List<SnapshotWindow>>(
            File.ReadAllText(snapshotPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

        var cache = new Dictionary<string, IReadOnlyList<PdfWord>>();
        foreach (var window in snapshot)
        {
            if (!cache.TryGetValue(window.File, out var actualWords))
            {
                actualWords = PdfWordCollector.CollectWords(Path.Combine(root, window.File));
                cache[window.File] = actualWords;
            }

            foreach (var expected in window.Words)
            {
                var candidate = actualWords
                    .Where(word => word.PageIndex == expected.PageIndex && word.Text == expected.Text)
                    .OrderBy(word => CoordinateDistance(word, expected))
                    .FirstOrDefault();

                candidate.Should().NotBeNull($"'{window.File}' / '{window.Label}' should contain '{expected.Text}'");
                candidate!.X0.Should().BeApproximately(expected.X0, 1.0, $"{window.File} {window.Label} {expected.Text} x0");
                candidate.X1.Should().BeApproximately(expected.X1, 1.0, $"{window.File} {window.Label} {expected.Text} x1");
                candidate.Top.Should().BeApproximately(expected.Top, 1.0, $"{window.File} {window.Label} {expected.Text} top");
                candidate.Bottom.Should().BeApproximately(expected.Bottom, 1.0, $"{window.File} {window.Label} {expected.Text} bottom");
                candidate.PageWidth.Should().BeApproximately(expected.PageWidth, 1.0, $"{window.File} {window.Label} {expected.Text} page width");
            }
        }
    }

    private static double CoordinateDistance(PdfWord actual, SnapshotWord expected)
    {
        return Math.Abs(actual.X0 - expected.X0)
            + Math.Abs(actual.X1 - expected.X1)
            + Math.Abs(actual.Top - expected.Top)
            + Math.Abs(actual.Bottom - expected.Bottom);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record SnapshotWindow(
        string File,
        string Label,
        IReadOnlyList<SnapshotWord> Words);

    private sealed record SnapshotWord(
        string Text,
        double X0,
        double X1,
        double Top,
        double Bottom,
        [property: JsonPropertyName("page_index")] int PageIndex,
        [property: JsonPropertyName("page_width")] double PageWidth);
}

using System.Text.Json;
using System.Text.Json.Serialization;
using BidParser.Parsing.Pdf;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class PdfCharacterisationTests
{
    [Fact]
    public void PdfPig_collector_handles_real_whitespace_separated_footer_tokens()
    {
        var root = TestSample.Root;
        const string file = "XQ-9100008-Hardware-Sample.pdf";
        var words = PdfWordCollector.CollectWords(Path.Combine(root, "samples", "inputs", file));
        var pageTenWords = words.Where(word => word.PageIndex == 9).ToList();
        var footerIndex = pageTenWords.FindIndex(word => word.Text == "Page");

        footerIndex.Should().BeGreaterThanOrEqualTo(0);
        var footerTokens = pageTenWords.Skip(footerIndex).Take(7).ToList();
        footerTokens.Select(word => word.Text.Trim()).Should().Equal("Page", "", "10", "", "of", "", "12");

        var pageWord = footerTokens[0];
        pageWord.LineY.Should().NotBeNull("collapsed Type 3 pages should use stable baseline geometry");
        pageWord.Letters.Should().NotBeNullOrEmpty();
        pageWord.Letters!
            .Zip(pageWord.Letters.Skip(1), (left, right) => right.X0 - left.X1)
            .Should().OnlyContain(gap => gap <= 0.01, "baseline extents should not create a false internal gap");

        var withoutFooters = PdfTableHelpers.RemovePageFooters(pageTenWords);
        withoutFooters.Should().NotContain(word => footerTokens.Contains(word));

        words.Single(word => word.PageIndex == 2 && word.Text == "Page").LineY.Should().NotBeNull(
            "explicit whitespace words must not dilute the collapsed-box signal on a Type 3 page");

        var zebraWords = PdfWordCollector.CollectWords(
            Path.Combine(root, "samples", "inputs", "Zebra_PC_97000001_V2.0.pdf"));
        zebraWords.Should().OnlyContain(word => word.LineY == null,
            "normal Zebra geometry should continue using tight boxes");
    }

    [Fact]
    public void PdfPig_word_horizontal_and_baseline_coordinates_match_pdfplumber_anchor_snapshot()
    {
        var root = TestSample.Root;
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
                candidate.LineY.Should().NotBeNull($"{window.File} {window.Label} should use baseline geometry");
                candidate.LineY!.Value.Should().BeApproximately(
                    expected.Bottom, 1.0, $"{window.File} {window.Label} {expected.Text} baseline");
                candidate.PageWidth.Should().BeApproximately(expected.PageWidth, 1.0, $"{window.File} {window.Label} {expected.Text} page width");
            }
        }
    }

    private static double CoordinateDistance(PdfWord actual, SnapshotWord expected)
    {
        return Math.Abs(actual.X0 - expected.X0)
            + Math.Abs(actual.X1 - expected.X1)
            + Math.Abs(actual.LineY.GetValueOrDefault((actual.Top + actual.Bottom) / 2.0)
                - ((expected.Top + expected.Bottom) / 2.0));
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

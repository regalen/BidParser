using BidParser.Domain.Abstractions;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

// Verifies that each parser's Detect() signature uniquely identifies its own format
// among the sibling formats of the same vendor + MIME — the candidate set used to
// suggest the correct file type when a user picks the wrong one.
public sealed class WrongFileTypeDetectionTests
{
    private const double Threshold = 0.7;

    [Theory]
    // Nutanix XLSX trio
    [InlineData("XQ-4076249.xlsx", "nutanix_software_only_xlsx")]
    [InlineData("XQ-4108785.xlsx", "nutanix_hardware_only_xlsx")]
    [InlineData("XQ-4176792.xlsx", "nutanix_renewal_xlsx")]
    // Nutanix PDF trio
    [InlineData("XQ-4076249.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-4157308.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-4128926.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-4166696.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-4029825.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-4108785.pdf", "nutanix_hardware_only_pdf")]
    // HP XLSX trio
    [InlineData("Deals20260518T034809_HPI.xlsx", "hp_bid_xlsx")]
    [InlineData("Deals20260518T043243_HPI.xlsx", "hp_bid_xlsx")]
    [InlineData("translate_quote_47500427_v25_all.xlsx", "hp_global_bid_xlsx")]
    [InlineData("55648855.xlsx", "hp_oneconfig_xlsx")]
    public void Detect_uniquely_identifies_format_among_vendor_siblings(string inputName, string expectedSlug)
    {
        var root = FindRepoRoot();
        var parsers = new ParserRegistry().Parsers;
        var expected = parsers.Single(p => p.Slug == expectedSlug);
        var path = Path.Combine(root, "samples", "inputs", inputName);

        // The correct parser recognises the file.
        expected.Detect(path).Should().BeGreaterThanOrEqualTo(Threshold,
            "the matching parser should recognise {0}", inputName);

        // No sibling of the same vendor + MIME crosses the confidence threshold.
        var siblings = parsers.Where(p =>
            p.Slug != expectedSlug
            && p.Vendor == expected.Vendor
            && p.AcceptedMime == expected.AcceptedMime);

        foreach (var sibling in siblings)
        {
            sibling.Detect(path).Should().BeLessThan(Threshold,
                "sibling {0} should not claim {1}", sibling.Slug, inputName);
        }
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
}
